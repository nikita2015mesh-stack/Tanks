using System.Collections;
using System.Collections.Generic;
using MadMachines.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    // Owns the current car and navigation. Match state still belongs to the session.
    public sealed class GameFlow : MonoBehaviour
    {
        public const string FrontEndScene = "FrontEnd";
        public const string TestAsphaltScene = "Test_Asphalt_01";
        public const string TestAsphaltScenePath = "Assets/MadMachines/Scenes/" + TestAsphaltScene + ".unity";
        public const string TestAsphaltMapId = "test_asphalt_01";
        public const string TestAsphaltLocation = "Испытательная трасса";
        public static readonly string[] PlayerScenes =
        {
            "Assets/MadMachines/Scenes/FrontEnd.unity",
            "Assets/MadMachines/Scenes/Playground.unity",
            "Assets/MadMachines/Networking/Scenes/NetworkSandbox.unity",
            TestAsphaltScenePath
        };
        private static GameFlow instance;
        private static readonly List<VehicleDefinition> catalog = new List<VehicleDefinition>();
        public static IReadOnlyList<VehicleDefinition> Vehicles => catalog;
        public static VehicleDefinition CurrentVehicle { get; private set; }
        public static bool LocalLaunchPending { get; private set; }
        public static bool OnlineLaunchPending { get; private set; }
        public static ChallengeMode LocalMode { get; private set; }
        public static string ServerAddress { get; private set; }
        public static ushort ServerPort { get; private set; }
        public static bool Loading => instance != null && instance.loading;
        public static bool CancellationRequested => instance != null && instance.cancel;
        public static bool OverlayVisible => Loading && Time.realtimeSinceStartupAsDouble - instance.startedAt >= 0.5;
        public static VehicleDrive SelectedDrive { get; private set; } = VehicleDrive.All;
        public static string Notice { get; private set; }
        private bool loading;
        private bool cancel;
        private bool canCancel;
        private double startedAt;
        private string message;
        private float progress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            instance = null;
            catalog.Clear();
            CurrentVehicle = null;
            LocalLaunchPending = OnlineLaunchPending = false;
            SelectedDrive = VehicleDrive.All;
            ServerAddress = Notice = null;
            ServerPort = 0;
        }

        public static void Initialize()
        {
            if (instance != null) return;
            instance = new GameObject("Game navigation").AddComponent<GameFlow>();
            DontDestroyOnLoad(instance.gameObject);
            SelectedDrive = (VehicleDrive)Mathf.Clamp(PlayerPrefs.GetInt("MadMachines.Garage.Drive.v1", 1), 1, 3);
        }

        public static void RegisterVehicles(VehicleDefinition fallback, VehicleDefinition[] definitions)
        {
            Initialize();
            catalog.Clear();
            AddVehicle(fallback);
            if (definitions != null)
                foreach (VehicleDefinition definition in definitions) AddVehicle(definition);
            string selected = PlayerPrefs.GetString("MadMachines.Garage.Vehicle.v1", fallback != null ? fallback.Id : "");
            CurrentVehicle = catalog.Find(item => item.Id == selected);
            if (CurrentVehicle == null && catalog.Count > 0) CurrentVehicle = catalog[0];
            if (CurrentVehicle != null) SelectedDrive = ReadDrive(CurrentVehicle);
        }

        private static void AddVehicle(VehicleDefinition definition)
        {
            if (definition == null || definition.Prefab == null || string.IsNullOrEmpty(definition.Id) || catalog.Exists(item => item.Id == definition.Id)) return;
            catalog.Add(definition);
        }

        public static VehicleDrive ReadDrive(VehicleDefinition definition)
        {
            int fallback = catalog.Count > 0 && definition == catalog[0]
                ? PlayerPrefs.GetInt("MadMachines.Garage.Drive.v1", 1) : 1;
            return (VehicleDrive)Mathf.Clamp(PlayerPrefs.GetInt("MadMachines.Garage.Drive.v1." + definition.Id, fallback), 1, 3);
        }

        public static void ApplyVehicle(VehicleDefinition definition, VehicleDrive drive)
        {
            if (!catalog.Contains(definition)) return;
            CurrentVehicle = definition;
            SelectedDrive = (VehicleDrive)Mathf.Clamp((int)drive, 1, 3);
            PlayerPrefs.SetString("MadMachines.Garage.Vehicle.v1", definition.Id);
            PlayerPrefs.SetInt("MadMachines.Garage.Drive.v1." + definition.Id, (int)SelectedDrive);
            PlayerPrefs.Save();
        }

        // Compatibility for callers that previously changed only the current drive.
        public static void SelectDrive(VehicleDrive drive) => ApplyVehicle(CurrentVehicle, drive);
        public static void ClearNotice() => Notice = null;

        public static void StartLocal(string scene, ChallengeMode mode)
        {
            Initialize();
            if (Loading || CurrentVehicle == null) return;
            LocalLaunchPending = true;
            OnlineLaunchPending = false;
            LocalMode = mode;
            instance.StartCoroutine(instance.Load(scene, false));
        }

        public static void ConsumeLocalLaunch() => LocalLaunchPending = false;

        public static void StartOnline(string scene, string address, ushort port)
        {
            Initialize();
            if (Loading || CurrentVehicle == null) return;
            LocalLaunchPending = false;
            OnlineLaunchPending = true;
            ServerAddress = address;
            ServerPort = port;
            instance.StartCoroutine(instance.Load(scene, true));
        }

        public static void ReturnToMain()
        {
            Initialize();
            if (Loading) return;
            LocalLaunchPending = OnlineLaunchPending = false;
            instance.StartCoroutine(instance.Load(FrontEndScene, false));
        }

        public static void ReturnToGarage() => ReturnToMain();

        private IEnumerator Load(string scene, bool online)
        {
            loading = true;
            cancel = false;
            canCancel = scene != FrontEndScene;
            startedAt = Time.realtimeSinceStartupAsDouble;
            Notice = null;
            message = "Загружаем карту…";
            progress = 0;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Notice = "Карта отсутствует в этой сборке игры.";
                LocalLaunchPending = OnlineLaunchPending = loading = false;
                yield break;
            }
            AsyncOperation operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            while (!operation.isDone)
            {
                progress = operation.progress;
                yield return null;
            }
            // Scene Start methods initialize local input and the network manager.
            yield return null;
            NetworkSandboxSession session = online ? FindFirstObjectByType<NetworkSandboxSession>() : null;
            if (online && !cancel)
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 12;
                while (!cancel)
                {
                    NetworkClientView client = session != null ? session.GetComponent<NetworkClientView>() : null;
                    if (client != null && client.ReadyForPlay) break;
                    message = session != null ? session.Status : "Не удалось подготовить подключение.";
                    if (session == null || session.ConnectionStopped || Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        Notice = session != null && session.ConnectionStopped ? session.Status : "Не удалось войти за отведённое время. Проверьте доступность сервера.";
                        cancel = true;
                        break;
                    }
                    yield return null;
                }
            }
            if (cancel && scene != FrontEndScene)
            {
                message = "Возвращаемся на главный…";
                if (session != null)
                {
                    session.Disconnect();
                    while (session.Manager != null && (session.Manager.IsListening || session.Manager.ShutdownInProgress)) yield return null;
                }
                LocalLaunchPending = OnlineLaunchPending = false;
                operation = SceneManager.LoadSceneAsync(FrontEndScene, LoadSceneMode.Single);
                while (!operation.isDone) yield return null;
            }
            loading = false;
            cancel = false;
        }

        private void Update()
        {
            if (loading && canCancel && UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) cancel = true;
        }

        private void OnGUI()
        {
            if (!OverlayVisible) return;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -1000;
            GUI.color = new Color(0.025f, 0.04f, 0.06f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true };
            GUI.Label(new Rect(35, 35, Screen.width - 70, 100), message, style);
            if (message == "Загружаем карту…") GUI.Label(new Rect(35, 135, 200, 35), $"{Mathf.Clamp01(progress / 0.9f):P0}", style);
            GUI.enabled = !cancel;
            if (canCancel && GUI.Button(new Rect(35, 185, 240, 45), "ОТМЕНА")) cancel = true;
            GUI.enabled = true;
            GUI.matrix = previous;
        }
    }
}
