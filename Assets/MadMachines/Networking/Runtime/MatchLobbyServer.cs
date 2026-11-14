using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Networking
{
    internal enum MatchLobbyPhase : byte { Waiting, Loading, Running, Closing, Failed }

    internal sealed class MatchLobbyServer : MonoBehaviour
    {
        public const byte Protocol = 1;
        public const int RequiredPlayers = 2;
        private const int MaxConnections = 32;
        public static MatchLobbyServer Instance { get; private set; }

        private sealed class Peer
        {
            public NetworkConnection Connection;
            public bool Compatible;
            public bool Waiting;
            public bool Failed;
            public Guid Ticket;
        }

        private readonly List<Peer> peers = new List<Peer>();
        private readonly HashSet<Guid> tickets = new HashSet<Guid>();
        private NetworkDriver driver;
        private NetworkPipeline pipeline;
        private NetworkSandboxSession session;
        private NetworkLaunchOptions options;
        private MatchLobbyPhase phase;
        private bool loadingMap;
        private double preparationDeadline;
        private double nextStatus;
        private double nextStopCheck;

        public static void StartDedicated()
        {
            if (Instance != null) return;
            Instance = new GameObject("Match lobby server").AddComponent<MatchLobbyServer>();
            DontDestroyOnLoad(Instance.gameObject);
        }

        private void Start()
        {
            try
            {
                options = NetworkLaunchOptions.Parse(Environment.GetCommandLineArgs(), true);
                if (options.Port == ushort.MaxValue || !NetworkEndpoint.TryParse(options.BindAddress,
                    (ushort)(options.Port + 1), out NetworkEndpoint endpoint, NetworkFamily.Ipv4))
                    throw new ArgumentException("Укажите IPv4 и игровой порт до 65534.");
                if (!string.IsNullOrEmpty(options.StopFile) && File.Exists(options.StopFile))
                    throw new IOException("Файл остановки уже существует: " + options.StopFile);
                if (!Application.CanStreamedLevelBeLoaded(GameFlow.ThreeRoadsScene))
                    throw new InvalidOperationException("Карта «Три дороги» отсутствует в сборке сервера.");
                driver = CreateDriver();
                pipeline = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                if (driver.Bind(endpoint) != 0 || driver.Listen() != 0)
                    throw new IOException($"Не удалось открыть UDP {options.BindAddress}:{options.Port + 1}.");
                Application.runInBackground = true;
                Application.targetFrameRate = 60;
                Log($"LOBBY_READY UDP {options.BindAddress}:{options.Port + 1}. Ожидание заявок 0/{RequiredPlayers}.");
            }
            catch (Exception error)
            {
                enabled = false;
                Debug.LogError("[MM NET] Ошибка запуска очереди: " + error);
                Application.Quit(1);
            }
        }

        internal static NetworkDriver CreateDriver()
        {
            var settings = new NetworkSettings(Allocator.Temp);
            try
            {
                settings.WithNetworkConfigParameters(connectTimeoutMS: 500, maxConnectAttempts: 4, disconnectTimeoutMS: 5000);
                return NetworkDriver.Create(settings);
            }
            finally
            {
                settings.Dispose();
            }
        }

        public bool CanAdmit(Guid ticket) => (phase == MatchLobbyPhase.Loading || phase == MatchLobbyPhase.Running) && tickets.Contains(ticket);
        public bool Running => phase == MatchLobbyPhase.Running;

        public void GameplayStarted(NetworkSandboxSession activeSession)
        {
            if (phase != MatchLobbyPhase.Loading) return;
            session = activeSession;
            phase = MatchLobbyPhase.Running;
            Log($"Сессия началась. Игроков {RequiredPlayers}/{RequiredPlayers}.");
        }

        public void GameplayConnectionLost(Guid ticket)
        {
            if (phase != MatchLobbyPhase.Loading) return;
            Peer peer = peers.Find(candidate => candidate.Ticket == ticket);
            if (peer == null) return;
            peer.Ticket = Guid.Empty;
            peer.Waiting = false;
            peer.Failed = true;
            EndRound("Участник потерял игровое соединение при загрузке.", requeue: true);
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextStopCheck)
            {
                nextStopCheck = now + 1;
                if (!string.IsNullOrEmpty(options.StopFile) && File.Exists(options.StopFile))
                {
                    Log("Остановка сервера.");
                    if (session != null) session.StopServer();
                    else Application.Quit();
                    enabled = false;
                    return;
                }
            }

            driver.ScheduleUpdate().Complete();
            NetworkConnection incoming;
            while ((incoming = driver.Accept()).IsCreated)
            {
                if (peers.Count < MaxConnections) peers.Add(new Peer { Connection = incoming });
                else incoming.Disconnect(driver);
            }

            for (int i = peers.Count - 1; i >= 0; i--)
            {
                Peer peer = peers[i];
                NetworkEvent.Type type;
                while ((type = peer.Connection.PopEvent(driver, out var packet)) != NetworkEvent.Type.Empty)
                {
                    if (type == NetworkEvent.Type.Disconnect)
                    {
                        peers.RemoveAt(i);
                        if (peer.Compatible) Log("Клиент отключился от очереди.");
                        if (peer.Ticket != Guid.Empty && phase == MatchLobbyPhase.Loading) EndRound("Участник потерял связь до старта.", requeue: true);
                        break;
                    }
                    if (type != NetworkEvent.Type.Data || (packet.Length != 2 && packet.Length != 18) || packet.ReadByte() != Protocol) continue;
                    if (!peer.Compatible)
                    {
                        peer.Compatible = true;
                        Log("Клиент подключился к очереди.");
                    }
                    bool waiting = packet.ReadByte() == 1;
                    if (packet.Length == 18)
                    {
                        var ticketBytes = new byte[16];
                        packet.ReadBytes(ticketBytes.AsSpan());
                        var ticket = new Guid(ticketBytes);
                        if (!tickets.Contains(ticket)) continue;
                        if (!waiting)
                        {
                            PlayerLeft(ticket);
                            continue;
                        }
                        if (phase == MatchLobbyPhase.Running && peer.Ticket == Guid.Empty && !peers.Exists(other => other.Ticket == ticket))
                            peer.Ticket = ticket;
                        if (peer.Ticket != ticket) continue;
                    }
                    if (peer.Failed)
                    {
                        if (!waiting) peer.Failed = false;
                        continue;
                    }
                    if (peer.Waiting != waiting) ChangeIntent(peer, waiting);
                }
            }

            int waitingCount = 0;
            foreach (Peer peer in peers)
                if (peer.Waiting && peer.Ticket == Guid.Empty) waitingCount++;
            if (phase == MatchLobbyPhase.Waiting && waitingCount >= RequiredPlayers) StartCoroutine(LoadMap());
            if (phase == MatchLobbyPhase.Loading && now >= preparationDeadline) EndRound("Истекло время подготовки карты.");
            if (phase == MatchLobbyPhase.Running && (tickets.Count == 0 || (session != null && session.ResultsExpired(now))))
                EndRound("Сессия завершена.");

            if (now < nextStatus) return;
            nextStatus = now + 0.25;
            foreach (Peer peer in peers)
            {
                if (!peer.Compatible || driver.BeginSend(pipeline, peer.Connection, out var writer) != 0) continue;
                bool reserved = peer.Ticket != Guid.Empty;
                writer.WriteByte(Protocol);
                MatchLobbyPhase peerPhase = peer.Failed ? MatchLobbyPhase.Failed : reserved ? phase : MatchLobbyPhase.Waiting;
                writer.WriteByte((byte)peerPhase);
                writer.WriteByte((byte)(reserved ? RequiredPlayers : Mathf.Min(waitingCount, RequiredPlayers)));
                writer.WriteByte(RequiredPlayers);
                if (reserved && session != null && session.Manager != null && session.Manager.IsListening)
                    foreach (byte value in peer.Ticket.ToByteArray()) writer.WriteByte(value);
                driver.EndSend(writer);
            }
        }

        private void ChangeIntent(Peer peer, bool waiting)
        {
            peer.Waiting = waiting;
            Log(waiting ? "Клиент подал заявку." : "Клиент снял заявку.");
            if (waiting || peer.Ticket == Guid.Empty) return;
            PlayerLeft(peer.Ticket);
        }

        public void PlayerLeft(Guid ticket)
        {
            if (!tickets.Remove(ticket)) return;
            Peer peer = peers.Find(candidate => candidate.Ticket == ticket);
            if (peer != null)
            {
                peer.Ticket = Guid.Empty;
                peer.Waiting = false;
            }
            if (phase == MatchLobbyPhase.Loading) EndRound("Участник отменил запуск.", requeue: true);
            else if (phase == MatchLobbyPhase.Running && session != null) session.PlayerLeft(ticket);
        }

        private IEnumerator LoadMap()
        {
            phase = MatchLobbyPhase.Loading;
            preparationDeadline = Time.realtimeSinceStartupAsDouble + 30;
            foreach (Peer peer in peers)
            {
                if (!peer.Waiting) continue;
                peer.Ticket = Guid.NewGuid();
                tickets.Add(peer.Ticket);
                if (tickets.Count == RequiredPlayers) break;
            }
            Log($"Состав собран {RequiredPlayers}/{RequiredPlayers}. Загружаем «Три дороги».");
            loadingMap = true;
            yield return SceneManager.LoadSceneAsync(GameFlow.ThreeRoadsScene, LoadSceneMode.Additive);
            loadingMap = false;
            Scene scene = SceneManager.GetSceneByName(GameFlow.ThreeRoadsScene);
            SceneManager.SetActiveScene(scene);
            yield return null;
            session = FindAnyObjectByType<NetworkSandboxSession>();
            if (phase == MatchLobbyPhase.Loading && (session == null || session.Manager == null || !session.Manager.IsListening))
                EndRound("Игровой UDP-порт не открылся.");
        }

        private void EndRound(string reason, bool requeue = false)
        {
            Log(reason);
            bool preparationFailed = phase == MatchLobbyPhase.Loading && !requeue;
            phase = MatchLobbyPhase.Closing;
            tickets.Clear();
            foreach (Peer peer in peers)
            {
                if (peer.Ticket == Guid.Empty) continue;
                peer.Ticket = Guid.Empty;
                peer.Waiting = requeue;
                peer.Failed = preparationFailed;
            }
            StartCoroutine(UnloadMap());
        }

        private IEnumerator UnloadMap()
        {
            while (loadingMap) yield return null;
            yield return null;
            NetworkSandboxSession active = FindAnyObjectByType<NetworkSandboxSession>();
            if (active != null && active.Manager != null)
            {
                if (active.Manager.IsListening) active.Manager.Shutdown();
                while (active.Manager.IsListening || active.Manager.ShutdownInProgress) yield return null;
            }
            Scene map = SceneManager.GetSceneByName(GameFlow.ThreeRoadsScene);
            if (map.isLoaded) yield return SceneManager.UnloadSceneAsync(map);
            session = null;
            phase = MatchLobbyPhase.Waiting;
            Log("Очередь готова принять следующий состав.");
        }

        private void OnDestroy()
        {
            if (driver.IsCreated) driver.Dispose();
            if (Instance == this) Instance = null;
        }

        private static void Log(string message) => Debug.Log("[MM NET] " + message);
    }
}
