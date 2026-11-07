using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Networking
{
    public sealed class NetworkSandboxSession : MonoBehaviour
    {
        [SerializeField] private GameObject vehiclePrefab;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private bool serverInEditor;
        private readonly Dictionary<ulong, int> slots = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, NetworkObject> players = new Dictionary<ulong, NetworkObject>();
        private NetworkLaunchOptions options;
        private double nextStopCheck;
        private bool quitAfterShutdown;
        private bool applicationQuitting;
        private int exitCode;
        private bool ready;
        private float previousFixedDeltaTime;
        private bool changedFixedDeltaTime;
        private float previousTimeScale;
        private int previousTargetFrameRate;
        private bool previousRunInBackground;

        public NetworkManager Manager { get; private set; }
        public UnityTransport Transport { get; private set; }
        public string Address => options?.Address ?? "127.0.0.1";
        public ushort Port => options?.Port ?? 7777;
        public string Status { get; private set; } = "Отключено";
        public bool IsServerProcess { get; private set; }
        public bool CanConnect => ready && !IsServerProcess && Manager != null && !Manager.IsListening && !Manager.ShutdownInProgress;

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
#endif
#if UNITY_SERVER
            server = true;
#endif
            string[] arguments = Environment.GetCommandLineArgs();
            IsServerProcess = server || Array.IndexOf(arguments, "-mm-server") >= 0;
            try
            {
                options = NetworkLaunchOptions.Parse(arguments, server);
                IsServerProcess = options.Server;
                ValidateConfiguration();
                if (NetworkManager.Singleton != null || GetComponent<NetworkManager>() != null)
                    throw new InvalidOperationException("Уже существует NetworkManager. Откройте только NetworkSandbox и перезапустите Play Mode.");
                previousFixedDeltaTime = Time.fixedDeltaTime;
                previousTimeScale = Time.timeScale;
                previousTargetFrameRate = Application.targetFrameRate;
                previousRunInBackground = Application.runInBackground;
                changedFixedDeltaTime = true;
                Application.runInBackground = true;
                Time.timeScale = 1;
                Time.fixedDeltaTime = 0.02f;

                Transport = gameObject.AddComponent<UnityTransport>();
                Scene sessionScene = gameObject.scene;
                Manager = gameObject.AddComponent<NetworkManager>();
                // NGO moves its root to DontDestroyOnLoad in OnEnable. This sandbox is scene-scoped.
                SceneManager.MoveGameObjectToScene(gameObject, sessionScene);
                Manager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = Transport,
                    ProtocolVersion = 2,
                    TickRate = 30,
                    EnableSceneManagement = false,
                    ConnectionApproval = true,
                    AutoSpawnPlayerPrefabClientSide = false,
                    EnsureNetworkVariableLengthSafety = true,
                    ForceSamePrefabs = true
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
                    foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
                    Transport.SetConnectionData(true, options.Address, options.Port, options.BindAddress);
                    Log($"Запуск выделенного сервера UDP {options.BindAddress}:{Port}; физика 50 Гц, сеть 30 Гц.");
                    if (!string.IsNullOrEmpty(options.StopFile) && File.Exists(options.StopFile))
                        throw new InvalidOperationException("Файл остановки уже существует: " + options.StopFile);
                    if (!Manager.StartServer()) throw new InvalidOperationException("Не удалось запустить сервер; проверьте порт и журнал транспорта.");
                    Log("Сервер готов. Локальный игрок не создаётся.");
                }
                else
                {
                    gameObject.AddComponent<NetworkClientView>().Configure(this);
                    Connect(Address, Port.ToString());
                }
            }
            catch (Exception error)
            {
                ready = false;
                if (Manager != null && Manager.IsListening) Manager.Shutdown();
                Status = "Ошибка запуска: " + error.Message;
                Debug.LogError("[MM NET] " + error);
                if (IsServerProcess) StopServer(1);
                else if (GetComponent<NetworkClientView>() == null)
                    gameObject.AddComponent<NetworkClientView>().Configure(this);
            }
        }

        public void ValidateConfiguration()
        {
            if (transform.parent != null)
                throw new InvalidOperationException("Network session должна быть корневым объектом сцены.");
            if (spawnPoints == null || spawnPoints.Length != 2 || spawnPoints[0] == null || spawnPoints[1] == null || spawnPoints[0] == spawnPoints[1])
                throw new InvalidOperationException("NetworkSandbox: нужны две разные точки появления.");
            foreach (Transform start in spawnPoints)
                if (start.gameObject.scene != gameObject.scene)
                    throw new InvalidOperationException("NetworkSandbox: точки появления должны находиться в сцене сессии.");
            NetworkSandboxValidation.ValidateVehicle(vehiclePrefab, projectilePrefab);
            NetworkSandboxValidation.ValidateProjectile(projectilePrefab);
        }

        public void Connect(string address, string port)
        {
            if (IsServerProcess || !CanConnect) return;
            if (!NetworkLaunchOptions.IsIpv4(address) || address == "0.0.0.0" ||
                !ushort.TryParse(port, out ushort parsed) || parsed == 0)
            {
                Status = "Укажите IPv4 и порт от 1 до 65535.";
                return;
            }
            options.Address = address;
            options.Port = parsed;
            Transport.SetConnectionData(true, address, parsed);
            Status = "Подключение…";
            Log($"Подключение к {Address}:{Port}.");
            try
            {
                if (!Manager.StartClient()) Status = "Не удалось начать подключение. См. журнал.";
            }
            catch (Exception error)
            {
                Status = "Ошибка подключения: " + error.Message;
                Debug.LogError("[MM NET] " + error);
                Manager.Shutdown();
            }
        }

        public void Disconnect()
        {
            if (IsServerProcess || Manager == null) return;
            Status = "Отключено пользователем";
            Manager.Shutdown();
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (!ready || Manager.ShutdownInProgress || slots.Count >= 2 || slots.ContainsKey(request.ClientNetworkId))
            {
                response.Approved = false;
                response.Reason = "На сервере уже два игрока.";
                Log($"Отклонено подключение {request.ClientNetworkId}: {response.Reason}");
                return;
            }
            int slot = slots.ContainsValue(0) ? 1 : 0;
            slots.Add(request.ClientNetworkId, slot);
            response.Approved = true;
            Log($"Одобрено подключение {request.ClientNetworkId}; место {slot + 1}.");
        }

        private void Connected(ulong clientId)
        {
            if (!Manager.IsServer)
            {
                Status = "Подключено";
                Log($"Подключено; локальный игрок {Manager.LocalClientId}.");
                return;
            }
            if (players.ContainsKey(clientId) || !slots.TryGetValue(clientId, out int slot)) return;
            GameObject instance = null;
            try
            {
                Transform start = spawnPoints[slot];
                instance = Instantiate(vehiclePrefab, start.position, start.rotation);
                NetworkObject player = instance.GetComponent<NetworkObject>();
                instance.GetComponent<NetworkVehicle>().InitializeServer(slot);
                player.SpawnAsPlayerObject(clientId, true);
                players.Add(clientId, player);
                Log($"Игрок {clientId} подключён; машина {player.NetworkObjectId}, место {slot + 1}; машин {players.Count}/2.");
            }
            catch (Exception error)
            {
                if (instance != null)
                {
                    NetworkObject failed = instance.GetComponent<NetworkObject>();
                    if (failed != null && failed.IsSpawned) failed.Despawn(true);
                    else Destroy(instance);
                }
                players.Remove(clientId);
                slots.Remove(clientId);
                Debug.LogError($"[MM NET] Не удалось создать машину игрока {clientId}: {error}");
                Manager.DisconnectClient(clientId, "Не удалось создать машину. См. журнал сервера.");
            }
        }

        private void Disconnected(ulong clientId)
        {
            if (IsServerProcess)
            {
                if (players.TryGetValue(clientId, out NetworkObject player) && player != null && player.IsSpawned)
                    player.Despawn(true);
                players.Remove(clientId);
                slots.Remove(clientId);
                Log($"Игрок {clientId} отключён; машина удалена, место освобождено.");
            }
            else
            {
                Status = string.IsNullOrEmpty(Manager.DisconnectReason) ? "Отключено: соединение закрыто или потеряно" : "Отключено: " + Manager.DisconnectReason;
                Log(Status);
            }
        }

        private void TransportFailed()
        {
            Status = "Ошибка транспорта. См. журнал.";
            Debug.LogError("[MM NET] " + Status);
            if (IsServerProcess) StopServer(1);
        }

        private void ServerStopped(bool wasHost)
        {
            players.Clear();
            slots.Clear();
            ready = false;
            Log("Сервер остановлен; сетевые объекты освобождены.");
        }

        private void ClientStopped(bool wasHost)
        {
            if (Status == "Подключено" || Status == "Подключение…") Status = "Отключено";
            Log("Клиентский транспорт остановлен.");
        }

        public void StopServer(int code = 0)
        {
            if (quitAfterShutdown) return;
            Log("Завершение выделенного сервера…");
            exitCode = code;
            quitAfterShutdown = true;
            if (Manager != null) Manager.Shutdown();
        }

        private void Update()
        {
            if (quitAfterShutdown && (Manager == null || (!Manager.IsListening && !Manager.ShutdownInProgress)))
            {
                quitAfterShutdown = false;
                Log("Завершение процесса, код " + exitCode + ".");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(exitCode);
#endif
                return;
            }
            if (!IsServerProcess || options == null || string.IsNullOrEmpty(options.StopFile) || Time.realtimeSinceStartupAsDouble < nextStopCheck) return;
            nextStopCheck = Time.realtimeSinceStartupAsDouble + 1;
            if (File.Exists(options.StopFile)) StopServer();
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
            Log("Приложение закрывается; завершение сетевой сессии.");
            if (Manager != null && Manager.IsListening) Manager.Shutdown();
        }

        private void OnDestroy()
        {
            ready = false;
            if (changedFixedDeltaTime)
            {
                Time.fixedDeltaTime = previousFixedDeltaTime;
                Time.timeScale = previousTimeScale;
                Application.targetFrameRate = previousTargetFrameRate;
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
