using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Networking
{
    public sealed class NetworkSandboxSession : MonoBehaviour
    {
        private const double RetryDelay = 2;
        private const string LegacyServerFullReason = "На сервере уже два игрока.";
        private const string TeamServerFullReason = "На сервере нет свободных мест.";
        private const string SpawnBlockedReason = "Все доступные точки появления заняты машинами.";
        private const string MatchUnavailableReason = "Участие в матче завершено. Подайте новую заявку на главном экране.";
        private const string MatchConnectionClosingReason = "Игровое соединение ещё закрывается.";
        private const string LeaveMatchMessage = "MadMachines.LeaveMatch.v1";
        public const string ServerReadyMarker = "[MM NET] SERVER_READY";

        [SerializeField] private GameObject vehiclePrefab;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private Transform[] opposingTeamSpawnPoints;
        [SerializeField] private bool serverInEditor;
        [SerializeField] private string mapId = "sandbox";
        [SerializeField] private string locationName = "Сетевой полигон";
        [SerializeField, Min(350f)] private float viewDistance = 350f;
        private readonly Dictionary<ulong, int> slots = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, NetworkObject> players = new Dictionary<ulong, NetworkObject>();
        private readonly Dictionary<ulong, VehicleDrive> configurations = new Dictionary<ulong, VehicleDrive>();
        private readonly Dictionary<ulong, Guid> matchTickets = new Dictionary<ulong, Guid>();
        private readonly HashSet<ulong> matchReadyClients = new HashSet<ulong>();
        private MatchLobbyServer matchLobby;
        private DeathmatchRound deathmatch;
        private bool leaving;
        private double nextSessionUpdate;
        private const string SessionMessage = "MadMachines.Session.v2";
        public ServerSessionInfo SessionInfo { get; private set; }
        private NetworkLaunchOptions options;
        private double nextStopCheck;
        private bool quitAfterShutdown;
        private bool applicationQuitting;
        private int exitCode;
        private bool ready;
        private bool wantsConnection;
        private bool joined;
        private int earlyRejections;
        private double nextAttempt = double.PositiveInfinity;
        private double estimatedMatchEnd;
        private float previousFixedDeltaTime;
        private bool changedFixedDeltaTime;
        private float previousTimeScale;
        private int previousTargetFrameRate;
        private bool previousRunInBackground;

        public NetworkManager Manager { get; private set; }
        public UnityTransport Transport { get; private set; }
        public string Address => options?.Address ?? NetworkDevelopmentSettings.LocalAddress;
        public ushort Port => options?.Port ?? NetworkDevelopmentSettings.DefaultPort;
        public NetworkConnectionMode Mode => options?.Mode ?? NetworkConnectionMode.Offline;
        public string Status { get; private set; } = "Отключено";
        public bool IsServerProcess { get; private set; }
        public string LocationName => locationName;
        public bool HasTeams => opposingTeamSpawnPoints != null && opposingTeamSpawnPoints.Length > 0;
        internal bool MatchGame => matchLobby != null || (!IsServerProcess && GameFlow.MatchTicket != Guid.Empty);
        public float ViewDistance => viewDistance;
        private int Capacity => HasTeams ? spawnPoints.Length + opposingTeamSpawnPoints.Length : spawnPoints.Length;
        internal bool CanFight(double now) => deathmatch != null && deathmatch.CanFight(now);
        internal bool ResultsExpired(double now) => deathmatch != null && deathmatch.ResultsExpired(now);
        internal bool HasResults => !string.IsNullOrEmpty(SessionInfo?.result);
        internal int MatchSecondsLeft => Mathf.Max(0, Mathf.CeilToInt((float)(estimatedMatchEnd - Time.realtimeSinceStartupAsDouble)));
        public bool ConnectionStopped => !ready || (!wantsConnection && (Manager == null || !Manager.IsConnectedClient));
        public bool CanConnect => !leaving && ready && !IsServerProcess && Manager != null && !Manager.IsListening && !Manager.ShutdownInProgress;
        public bool CanDisconnect => ready && !IsServerProcess && Manager != null && (wantsConnection || Manager.IsListening);

        public void Configure(GameObject vehicle, GameObject projectile, Transform[] starts)
        {
            vehiclePrefab = vehicle;
            projectilePrefab = projectile;
            spawnPoints = starts;
        }

        private void Start()
        {
            bool server = false;
#if UNITY_EDITOR
            server = serverInEditor;
#elif UNITY_SERVER
            server = true;
#endif
            string[] arguments = Environment.GetCommandLineArgs();
            IsServerProcess = server;
            try
            {
                options = NetworkLaunchOptions.Parse(arguments, server, NetworkDevelopmentSettings.Load());
                if (!options.Server && GameFlow.OnlineLaunchPending)
                {
                    options.Address = GameFlow.ServerAddress;
                    options.Port = GameFlow.ServerPort;
                    options.Mode = NetworkConnectionMode.RemoteOnline;
                }
                IsServerProcess = options.Server;
                if (IsServerProcess) matchLobby = MatchLobbyServer.Instance;
                ValidateConfiguration();
                if (NetworkManager.Singleton != null || GetComponent<NetworkManager>() != null)
                    throw new InvalidOperationException("A NetworkManager already exists. Open only one network scene and restart Play Mode.");
                previousFixedDeltaTime = Time.fixedDeltaTime;
                previousTimeScale = Time.timeScale;
                if (IsServerProcess) previousTargetFrameRate = Application.targetFrameRate;
                previousRunInBackground = Application.runInBackground;
                changedFixedDeltaTime = true;
                Application.runInBackground = true;
                Time.timeScale = 1;
                Time.fixedDeltaTime = 0.02f;

                Transport = gameObject.AddComponent<UnityTransport>();
                Transport.ConnectTimeoutMS = 500;
                Transport.MaxConnectAttempts = 4;
                Transport.DisconnectTimeoutMS = 6000;
                Scene sessionScene = gameObject.scene;
                Manager = gameObject.AddComponent<NetworkManager>();
                // NGO moves its root to DontDestroyOnLoad in OnEnable. The session belongs to the loaded map.
                SceneManager.MoveGameObjectToScene(gameObject, sessionScene);
                byte[] payload = NetworkSessionProtocol.CreatePayload(vehiclePrefab.GetComponent<NetworkObject>().PrefabIdHash,
                    projectilePrefab.GetComponent<NetworkObject>().PrefabIdHash, mapId,
                    GameFlow.OnlineLaunchPending ? GameFlow.SelectedDrive : VehicleDrive.Configured);
                if (!IsServerProcess && GameFlow.MatchTicket != Guid.Empty)
                {
                    byte[] ticket = GameFlow.MatchTicket.ToByteArray();
                    int size = payload.Length;
                    Array.Resize(ref payload, size + ticket.Length);
                    ticket.CopyTo(payload, size);
                }
                Manager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = Transport,
                    NetworkTopology = NetworkTopologyTypes.ClientServer,
                    ProtocolVersion = NetworkSessionProtocol.NgoHandshakeVersion,
                    ConnectionData = payload,
                    TickRate = 30,
                    ClientConnectionBufferTimeout = 8,
                    EnableSceneManagement = false,
                    ConnectionApproval = true,
                    AutoSpawnPlayerPrefabClientSide = false,
                    EnsureNetworkVariableLengthSafety = true,
                    ForceSamePrefabs = false // Approval verifies prefab IDs and can return a useful error.
                };
                Manager.AddNetworkPrefab(vehiclePrefab);
                Manager.AddNetworkPrefab(projectilePrefab);
                Manager.ConnectionApprovalCallback = Approve;
                Manager.OnClientConnectedCallback += Connected;
                Manager.OnClientDisconnectCallback += Disconnected;
                Manager.OnTransportFailure += TransportFailed;
                Manager.OnServerStopped += ServerStopped;
                Manager.OnClientStopped += ClientStopped;
                ready = true;

                if (IsServerProcess)
                {
                    Application.targetFrameRate = 60;
                    // The scene contains no camera/HUD. Server never creates either component.
                    foreach (Light light in FindObjectsByType<Light>()) light.enabled = false;
                    Transport.SetConnectionData(true, options.Address, options.Port, options.BindAddress);
                    Log($"Starting dedicated server on UDP {options.BindAddress}:{Port} (physics 50 Hz, network 30 Hz).");
                    if (!string.IsNullOrEmpty(options.StopFile) && File.Exists(options.StopFile))
                        throw new InvalidOperationException("The stop file already exists: " + options.StopFile);
                    if (!Manager.StartServer()) throw new InvalidOperationException("Could not start the server. Check the port and transport log.");
                    if (matchLobby != null) Manager.CustomMessagingManager.RegisterNamedMessageHandler(LeaveMatchMessage, ReceiveLeaveMatch);
                    Debug.Log($"{ServerReadyMarker} UDP {options.BindAddress}:{Port}, protocol {NetworkSessionProtocol.Version}, map {mapId}.");
                    Log("Server ready. No local player was spawned.");
                }
                else
                {
                    gameObject.AddComponent<NetworkClientView>().Configure(this);
                    if (Mode == NetworkConnectionMode.Offline)
                    {
                        Status = "Автоподключение выключено. Пробный заезд можно запустить из гаража.";
                    }
                    else
                    {
                        wantsConnection = true;
                        TryConnect();
                    }
                }
            }
            catch (Exception error)
            {
                ready = false;
                if (Manager != null && Manager.IsListening) Manager.Shutdown();
                Status = "Ошибка запуска. Подробности в журнале.";
                Debug.LogError("[MM NET] Startup failed: " + error);
                if (IsServerProcess)
                {
                    if (matchLobby == null) StopServer(1);
                }
                else if (GetComponent<NetworkClientView>() == null && (NetworkManager.Singleton == null || Manager == NetworkManager.Singleton))
                    gameObject.AddComponent<NetworkClientView>().Configure(this);
            }
        }

        public void ValidateConfiguration()
        {
            if (transform.parent != null)
                throw new InvalidOperationException("The network session must be a root scene object.");
            if (string.IsNullOrWhiteSpace(mapId))
                throw new InvalidOperationException("The network session requires a map ID for connection approval.");
            if (spawnPoints == null || (HasTeams ? spawnPoints.Length != opposingTeamSpawnPoints.Length : spawnPoints.Length != 2))
                throw new InvalidOperationException("The session needs two individual starts or an equal number of starts for both teams.");
            var starts = new HashSet<Transform>();
            for (int slot = 0; slot < Capacity; slot++)
            {
                Transform start = StartForSlot(slot);
                if (start == null || start.gameObject.scene != gameObject.scene || !starts.Add(start))
                    throw new InvalidOperationException("Every spawn point must be distinct and belong to the session scene.");
            }
            NetworkSandboxValidation.ValidateVehicle(vehiclePrefab, projectilePrefab);
            NetworkSandboxValidation.ValidateProjectile(projectilePrefab);
            if (vehiclePrefab.GetComponent<NetworkObject>().PrefabIdHash == projectilePrefab.GetComponent<NetworkObject>().PrefabIdHash)
                throw new InvalidOperationException("The vehicle and projectile prefabs have the same network ID.");
        }

        // Team slots alternate between spawn groups and keep their existing replicated slot ID.
        public static int TeamForSlot(int slot) => slot % 2;

        private Transform StartForSlot(int slot)
        {
            if (!HasTeams) return spawnPoints[slot];
            return TeamForSlot(slot) == 0 ? spawnPoints[slot / 2] : opposingTeamSpawnPoints[slot / 2];
        }

        private int FindAvailableSlot()
        {
            if (!HasTeams) return slots.ContainsValue(0) ? 1 : 0;

            int firstTeamCount = 0;
            foreach (int slot in slots.Values)
                if (TeamForSlot(slot) == 0) firstTeamCount++;
            int preferredTeam = firstTeamCount <= slots.Count - firstTeamCount ? 0 : 1;

            for (int offset = 0; offset < 2; offset++)
            {
                for (int slot = (preferredTeam + offset) % 2; slot < Capacity; slot += 2)
                {
                    if (slots.ContainsValue(slot)) continue;
                    Transform start = StartForSlot(slot);
                    if (!StartBlocked(start)) return slot;
                }
            }
            return -1;
        }

        private static bool StartBlocked(Transform start)
        {
            foreach (Collider overlap in Physics.OverlapBox(start.position, new Vector3(3f, 2.5f, 4f), start.rotation,
                NetworkVehicle.HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (overlap.attachedRigidbody != null && overlap.attachedRigidbody.GetComponent<NetworkVehicle>() != null)
                    return true;
            }
            return false;
        }

        public void Connect(string address, string port)
        {
            if (!CanConnect) return;
            if (!NetworkLaunchOptions.IsClientAddress(address) || !ushort.TryParse(port, out ushort parsed) || parsed == 0)
            {
                StopConnecting("Укажите IPv4 и порт от 1 до 65535.");
                return;
            }
            options.Address = System.Net.IPAddress.Parse(address).ToString();
            options.Port = parsed;
            options.Mode = Address == NetworkDevelopmentSettings.LocalAddress ? NetworkConnectionMode.DevelopmentOnline : NetworkConnectionMode.RemoteOnline;
            earlyRejections = 0;
            wantsConnection = true;
            TryConnect();
        }

        private void TryConnect()
        {
            nextAttempt = double.PositiveInfinity;
            joined = false;
            if (!MatchGame) SessionInfo = null;
            Status = "Подключение…";
            Log($"Подключение к {Address}:{Port}.");
            try
            {
                Transport.SetConnectionData(true, Address, Port);
                if (!Manager.StartClient()) ScheduleRetry("Не удалось начать подключение. См. журнал.");
                else Manager.CustomMessagingManager.RegisterNamedMessageHandler(SessionMessage, ReceiveSessionInfo);
            }
            catch (Exception error)
            {
                Debug.LogError("[MM NET] " + error);
                if (Manager.IsListening) Manager.Shutdown(true);
                ScheduleRetry("Ошибка подключения. См. журнал.");
            }
        }

        public void Disconnect()
        {
            if (!CanDisconnect) return;
            SessionInfo = null;
            StopConnecting("Отключено пользователем.");
            if (Manager.IsListening) Manager.Shutdown();
        }

        private void ScheduleRetry(string reason)
        {
            if (!wantsConnection) return;
            Status = "Ожидание сервера. " + reason;
            nextAttempt = Time.realtimeSinceStartupAsDouble + RetryDelay;
        }

        private void StopConnecting(string reason)
        {
            wantsConnection = false;
            nextAttempt = double.PositiveInfinity;
            Status = reason;
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            byte[] payload = request.Payload;
            Guid ticket = Guid.Empty;
            if (matchLobby != null)
            {
                int size = Manager.NetworkConfig.ConnectionData.Length;
                if (payload != null && payload.Length == size + 16)
                {
                    ticket = new Guid(payload.AsSpan(size));
                    Array.Resize(ref payload, size);
                }
                if (!matchLobby.CanAdmit(ticket) || (deathmatch != null && !deathmatch.CanFight(Time.realtimeSinceStartupAsDouble)))
                {
                    response.Reason = MatchUnavailableReason;
                    return;
                }
                if (matchTickets.ContainsValue(ticket))
                {
                    response.Reason = MatchConnectionClosingReason;
                    return;
                }
            }
            string incompatibility = NetworkSessionProtocol.CheckCompatibility(payload, Manager.NetworkConfig.ConnectionData, out string diagnostic);
            if (incompatibility != null)
            {
                response.Approved = false;
                response.Reason = incompatibility;
                Log($"Rejected client {request.ClientNetworkId}: {diagnostic}");
                return;
            }
            if (!ready || Manager.ShutdownInProgress || slots.Count >= Capacity || slots.ContainsKey(request.ClientNetworkId))
            {
                response.Approved = false;
                response.Reason = HasTeams ? TeamServerFullReason : LegacyServerFullReason;
                Log($"Rejected client {request.ClientNetworkId}: no player slot available.");
                return;
            }
            DeathmatchRound.Participant returning = deathmatch?.Find(ticket);
            int slot = returning != null ? returning.Standing.slot : FindAvailableSlot();
            if (slot < 0)
            {
                response.Approved = false;
                response.Reason = SpawnBlockedReason;
                return;
            }
            slots.Add(request.ClientNetworkId, slot);
            configurations[request.ClientNetworkId] = returning != null ? returning.Drive : NetworkSessionProtocol.ReadDrive(payload);
            if (matchLobby != null) matchTickets.Add(request.ClientNetworkId, ticket);
            response.Approved = true;
            Log($"Approved client {request.ClientNetworkId}, slot {slot + 1}.");
        }

        private void Connected(ulong clientId)
        {
            if (!Manager.IsServer)
            {
                if (clientId != Manager.LocalClientId) return;
                joined = true;
                earlyRejections = 0;
                Status = "Подключено";
                Log($"Подключено; локальный клиент {Manager.LocalClientId}.");
                return;
            }
            if (matchLobby == null)
            {
                SpawnVehicle(clientId);
                return;
            }
            if (!matchTickets.TryGetValue(clientId, out Guid ticket) || !matchLobby.CanAdmit(ticket))
            {
                Manager.DisconnectClient(clientId, MatchUnavailableReason);
                return;
            }
            if (!matchLobby.Running)
            {
                matchReadyClients.Add(clientId);
                if (matchReadyClients.Count < MatchLobbyServer.RequiredPlayers) return;
                foreach (ulong participant in new List<ulong>(matchReadyClients)) SpawnVehicle(participant);
                if (players.Count == MatchLobbyServer.RequiredPlayers)
                {
                    deathmatch = new DeathmatchRound(Time.realtimeSinceStartupAsDouble);
                    foreach (var player in players)
                        deathmatch.Add(matchTickets[player.Key], player.Key, slots[player.Key], configurations[player.Key], player.Value);
                    players.Clear();
                    matchReadyClients.Clear();
                    matchLobby.GameplayStarted(this);
                    nextSessionUpdate = 0;
                }
                return;
            }
            DeathmatchRound.Participant returning = deathmatch?.Find(ticket);
            if (returning == null || deathmatch.Finished)
            {
                Manager.DisconnectClient(clientId, MatchUnavailableReason);
                return;
            }
            returning.ClientId = clientId;
            returning.Standing.status = returning.Vehicle != null ? DeathmatchPlayerStatus.Playing : DeathmatchPlayerStatus.Respawning;
            if (returning.Vehicle != null) returning.Vehicle.GetComponent<NetworkVehicle>().SetController(clientId);
            nextSessionUpdate = 0;
        }

        private void SpawnVehicle(ulong clientId)
        {
            if (players.ContainsKey(clientId) || !slots.TryGetValue(clientId, out int slot)) return;
            NetworkObject player = SpawnVehicleAt(clientId, slot, configurations[clientId], StartForSlot(slot));
            if (player != null)
            {
                players.Add(clientId, player);
                return;
            }
            slots.Remove(clientId);
            configurations.Remove(clientId);
            if (matchTickets.Remove(clientId, out Guid ticket)) matchLobby.GameplayConnectionLost(ticket);
            Manager.DisconnectClient(clientId, "Не удалось создать машину. См. журнал сервера.");
        }

        private NetworkObject SpawnVehicleAt(ulong clientId, int slot, VehicleDrive drive, Transform start)
        {
            GameObject instance = null;
            try
            {
                instance = Instantiate(vehiclePrefab, start.position, start.rotation);
                if (matchLobby != null) SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
                NetworkObject player = instance.GetComponent<NetworkObject>();
                instance.GetComponent<NetworkVehicle>().InitializeServer(slot, clientId, matchLobby != null ? this : null);
                // Approval accepts only configured/all/front/rear, never arbitrary forces.
                if (drive != VehicleDrive.Configured) instance.GetComponent<VehiclePhysics>().DriveMode = drive;
                player.Spawn(destroyWithScene: true);
                if (!player.IsSpawned) throw new InvalidOperationException("Netcode did not spawn the vehicle.");
                Log($"Client {clientId} joined with vehicle {player.NetworkObjectId}, slot {slot + 1}.");
                return player;
            }
            catch (Exception error)
            {
                if (instance != null)
                {
                    NetworkObject failed = instance.GetComponent<NetworkObject>();
                    if (failed != null && failed.IsSpawned) failed.Despawn(true);
                    else Destroy(instance);
                }
                Debug.LogError($"[MM NET] Could not spawn a vehicle for client {clientId}: {error}");
                return null;
            }
        }

        internal void ApplyDeathmatchHit(int shooterSlot, NetworkVehicle vehicle, float damage)
        {
            DeathmatchRound.Participant target = deathmatch?.Find(vehicle.Slot);
            if (target == null || target.Vehicle != vehicle.NetworkObject) return;
            deathmatch.Hit(target, shooterSlot, damage, Time.realtimeSinceStartupAsDouble);
            if (target.Health <= 0)
            {
                NetworkObject destroyed = target.Vehicle;
                target.Vehicle = null;
                destroyed.Despawn(true);
            }
            nextSessionUpdate = 0;
        }

        private void AdvanceDeathmatch(double now)
        {
            if (deathmatch.Finished) return;
            deathmatch.Advance(now);
            if (deathmatch.Finished)
            {
                nextSessionUpdate = 0;
                return;
            }
            foreach (DeathmatchRound.Participant participant in deathmatch.Participants)
            {
                if (!participant.ClientId.HasValue || participant.Vehicle != null || now < participant.RespawnAt) continue;
                Transform start = FindRespawnPoint();
                if (start == null) continue;
                ulong clientId = participant.ClientId.Value;
                NetworkObject vehicle = SpawnVehicleAt(clientId, participant.Standing.slot, participant.Drive, start);
                if (vehicle == null)
                {
                    participant.RespawnAt = now + DeathmatchRound.RespawnDelay;
                    continue;
                }
                participant.Vehicle = vehicle;
                participant.Health = DeathmatchRound.MaxHealth;
                participant.Standing.health = DeathmatchRound.MaxHealth;
                participant.Standing.status = DeathmatchPlayerStatus.Playing;
                // Include the new chassis in the occupancy check for the next player this frame.
                Physics.SyncTransforms();
                nextSessionUpdate = 0;
            }
        }

        private Transform FindRespawnPoint()
        {
            Transform selected = null;
            int free = 0;
            for (int index = 0; index < Capacity; index++)
            {
                Transform start = StartForSlot(index);
                if (!StartBlocked(start) && UnityEngine.Random.Range(0, ++free) == 0) selected = start;
            }
            return selected;
        }

        private void ReceiveLeaveMatch(ulong sender, FastBufferReader reader)
        {
            if (matchTickets.TryGetValue(sender, out Guid ticket)) matchLobby.PlayerLeft(ticket);
        }

        internal void PlayerLeft(Guid ticket)
        {
            DeathmatchRound.Participant participant = deathmatch?.Find(ticket);
            if (participant == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            deathmatch.Advance(now);
            deathmatch.PlayerLeft(participant, now);
            if (participant.Vehicle != null)
            {
                participant.Vehicle.Despawn(true);
                participant.Vehicle = null;
            }
            if (participant.ClientId.HasValue) Manager.DisconnectClient(participant.ClientId.Value);
            participant.ClientId = null;
            nextSessionUpdate = 0;
        }

        private void Disconnected(ulong clientId)
        {
            if (IsServerProcess)
            {
                if (deathmatch != null && matchTickets.Remove(clientId, out Guid departedTicket))
                {
                    DeathmatchRound.Participant participant = deathmatch.Find(departedTicket);
                    participant.ClientId = null;
                    if (!deathmatch.Finished) participant.Standing.status = DeathmatchPlayerStatus.Disconnected;
                    if (participant.Vehicle != null) participant.Vehicle.GetComponent<NetworkVehicle>().SetController(NetworkManager.ServerClientId);
                    slots.Remove(clientId);
                    configurations.Remove(clientId);
                    nextSessionUpdate = 0;
                    return;
                }
                players.TryGetValue(clientId, out NetworkObject player);
                players.Remove(clientId);
                slots.Remove(clientId);
                configurations.Remove(clientId);
                if (matchTickets.Remove(clientId, out Guid ticket)) matchLobby.GameplayConnectionLost(ticket);
                matchReadyClients.Remove(clientId);
                if (player != null && player.IsSpawned) player.Despawn(true);
                Log($"Client {clientId} disconnected. Vehicles: {players.Count}/{Capacity}.");
            }
            else if (wantsConnection && !applicationQuitting)
            {
                if (!MatchGame) SessionInfo = null;
                string reason = Manager.DisconnectReason ?? string.Empty;
                Log($"Отключение клиента: {reason}");
                if (reason.StartsWith(NetworkSessionProtocol.RejectionPrefix, StringComparison.Ordinal))
                {
                    StopConnecting(reason.Substring(NetworkSessionProtocol.RejectionPrefix.Length));
                    return;
                }
                if (MatchGame && reason == MatchUnavailableReason)
                {
                    StopConnecting(reason);
                    return;
                }

                // Older builds can be rejected by the NGO config hash before approval can send a reason.
                NetworkTransport.DisconnectEvents disconnect = Manager.DisconnectEvent;
                bool waitingForSlot = reason == LegacyServerFullReason || reason == TeamServerFullReason || reason == SpawnBlockedReason ||
                    (MatchGame && reason == MatchConnectionClosingReason);
                if (!joined && (disconnect == NetworkTransport.DisconnectEvents.ClosedByRemote ||
                    disconnect == NetworkTransport.DisconnectEvents.ProtocolError) &&
                    !waitingForSlot)
                {
                    if (++earlyRejections >= 2)
                    {
                        StopConnecting("Сервер отклоняет подключение. Проверьте версию сборок и журнал сервера, затем нажмите «Подключиться».");
                        return;
                    }
                }
                else earlyRejections = 0;
                string detail = joined ? "Связь потеряна." : "Не удалось подключиться. См. журнал.";
                ScheduleRetry(waitingForSlot ? reason : detail);
            }
        }

        private void TransportFailed()
        {
            Debug.LogError("[MM NET] Network transport failed.");
            if (IsServerProcess) StopServer(1);
            else ScheduleRetry("Ошибка транспорта. См. журнал.");
        }

        private void ServerStopped(bool wasHost)
        {
            players.Clear();
            slots.Clear();
            configurations.Clear();
            matchTickets.Clear();
            matchReadyClients.Clear();
            deathmatch = null;
            ready = false;
            Log("Server stopped. Network objects were released.");
        }

        private void ClientStopped(bool wasHost)
        {
            if (!applicationQuitting && double.IsPositiveInfinity(nextAttempt)) ScheduleRetry("Связь закрыта.");
            Log("Клиентский транспорт остановлен.");
        }

        public void StopServer(int code = 0)
        {
            if (quitAfterShutdown) return;
            Log("Shutting down the dedicated server.");
            exitCode = code;
            quitAfterShutdown = true;
            if (Manager != null) Manager.Shutdown();
        }

        public void LeaveToGarage() => LeaveToMain();

        public void LeaveToMain()
        {
            if (leaving || IsServerProcess) return;
            leaving = true;
            StartCoroutine(Leave());
        }

        private System.Collections.IEnumerator Leave(string notice = null)
        {
            StopConnecting("Возвращение на главный…");
            if (MatchGame && Manager != null && Manager.IsConnectedClient && !Manager.ShutdownInProgress)
            {
                using (var writer = new FastBufferWriter(1, Allocator.Temp))
                {
                    writer.WriteValueSafe((byte)0);
                    Manager.CustomMessagingManager.SendNamedMessage(LeaveMatchMessage, NetworkManager.ServerClientId, writer);
                }
                GameFlow.Lobby.SetWaiting(false);
                double deadline = Time.realtimeSinceStartupAsDouble + 1;
                while (Manager.IsConnectedClient && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            }
            if (Manager != null && Manager.IsListening) Manager.Shutdown();
            while (Manager != null && (Manager.IsListening || Manager.ShutdownInProgress)) yield return null;
            GameFlow.ReturnToMain(notice);
        }

        private ServerSessionInfo DescribeSession()
        {
            var info = new ServerSessionInfo
            {
                location = locationName,
                mode = MatchGame ? ServerSessionInfo.DeathmatchMode : HasTeams ? ServerSessionInfo.TeamSkirmishMode : ServerSessionInfo.FreeMode,
                players = deathmatch?.Participants.Count ?? slots.Count,
                capacity = MatchGame ? MatchLobbyServer.RequiredPlayers : Capacity
            };
            deathmatch?.Describe(info, Time.realtimeSinceStartupAsDouble);
            return info;
        }

        private void ReceiveSessionInfo(ulong sender, FastBufferReader reader)
        {
            if (IsServerProcess || sender != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out FixedString4096Bytes json);
            SessionInfo = JsonUtility.FromJson<ServerSessionInfo>(json.ToString());
            if (SessionInfo?.standings != null) estimatedMatchEnd = Time.realtimeSinceStartupAsDouble + SessionInfo.secondsLeft;
            if (HasResults) StopConnecting("Матч завершён.");
        }

        private void Update()
        {
            if (deathmatch != null && Manager.IsListening && !Manager.ShutdownInProgress)
                AdvanceDeathmatch(Time.realtimeSinceStartupAsDouble);
            if (IsServerProcess && Manager != null && Manager.IsListening && !Manager.ShutdownInProgress &&
                Manager.ConnectedClientsIds.Count > 0 && Time.realtimeSinceStartupAsDouble >= nextSessionUpdate)
            {
                nextSessionUpdate = Time.realtimeSinceStartupAsDouble + 1;
                SessionInfo = DescribeSession();
                using var writer = new FastBufferWriter(4096, Allocator.Temp);
                writer.WriteValueSafe(new FixedString4096Bytes(JsonUtility.ToJson(SessionInfo)));
                Manager.CustomMessagingManager.SendNamedMessageToAll(SessionMessage, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            if (quitAfterShutdown && (Manager == null || (!Manager.IsListening && !Manager.ShutdownInProgress)))
            {
                quitAfterShutdown = false;
                Log($"Exiting with code {exitCode}.");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(exitCode);
#endif
                return;
            }
            if (!IsServerProcess)
            {
                if (MatchGame && wantsConnection && !Manager.IsConnectedClient && estimatedMatchEnd > 0 &&
                    Time.realtimeSinceStartupAsDouble >= estimatedMatchEnd + RetryDelay)
                    StopConnecting("Время матча истекло. Связь с сервером потеряна.");
                if (MatchGame && !GameFlow.Loading && !leaving && !HasResults && !wantsConnection)
                {
                    leaving = true;
                    StartCoroutine(Leave(Status));
                    return;
                }
                if (wantsConnection && CanConnect && Time.realtimeSinceStartupAsDouble >= nextAttempt) TryConnect();
                return;
            }
            if (matchLobby != null || options == null || string.IsNullOrEmpty(options.StopFile) || Time.realtimeSinceStartupAsDouble < nextStopCheck) return;
            nextStopCheck = Time.realtimeSinceStartupAsDouble + 1;
            if (File.Exists(options.StopFile)) StopServer();
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
            wantsConnection = false;
            Log("Application is quitting. Closing the network session.");
            if (Manager != null && Manager.IsListening) Manager.Shutdown();
        }

        private void OnDestroy()
        {
            ready = false;
            wantsConnection = false;
            if (changedFixedDeltaTime)
            {
                Time.fixedDeltaTime = previousFixedDeltaTime;
                Time.timeScale = previousTimeScale;
                if (IsServerProcess) Application.targetFrameRate = previousTargetFrameRate;
                Application.runInBackground = previousRunInBackground;
            }
            if (Manager == null) return;
            if (!applicationQuitting && Manager.IsListening) Manager.Shutdown();
            Manager.OnClientConnectedCallback -= Connected;
            Manager.OnClientDisconnectCallback -= Disconnected;
            Manager.OnTransportFailure -= TransportFailed;
            Manager.OnServerStopped -= ServerStopped;
            Manager.OnClientStopped -= ClientStopped;
            Manager.ConnectionApprovalCallback = null;
        }

        private static void Log(string message) => Debug.Log("[MM NET] " + message);
    }
}
