using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    // Owns navigation and the local selection, never the authoritative match state.
    public sealed class GameFlow : MonoBehaviour
    {
        public const string FrontEndScene = "FrontEnd";
        public static readonly string[] PlayerScenes =
        {
            "Assets/MadMachines/Scenes/FrontEnd.unity",
            "Assets/MadMachines/Scenes/Playground.unity",
            "Assets/MadMachines/Scenes/Quarry.unity",
            "Assets/MadMachines/Scenes/QuarryOnline.unity",
            "Assets/MadMachines/Networking/Scenes/NetworkSandbox.unity"
        };
        private static GameFlow instance;
        public static bool LocalLaunchPending { get; private set; }
        public static bool OnlineLaunchPending { get; private set; }
        public static ChallengeMode LocalMode { get; private set; }
        public static string ServerAddress { get; private set; }
        public static ushort ServerPort { get; private set; }
        public static bool ShowGarage { get; private set; }
        public static bool Loading => instance != null && instance.loading;
        public static VehicleDrive SelectedDrive { get; private set; } = VehicleDrive.All;
        private bool loading;
        private string message;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            instance = null;
            LocalLaunchPending = OnlineLaunchPending = ShowGarage = false;
            SelectedDrive = VehicleDrive.All;
            ServerAddress = null;
            ServerPort = 0;
        }

        public static void Initialize()
        {
            if (instance != null) return;
            instance = new GameObject("Game navigation").AddComponent<GameFlow>();
            DontDestroyOnLoad(instance.gameObject);
            SelectedDrive = (VehicleDrive)Mathf.Clamp(PlayerPrefs.GetInt("MadMachines.Garage.Drive.v1", 1), 1, 3);
        }

        public static void SelectDrive(VehicleDrive drive)
        {
            SelectedDrive = drive;
            PlayerPrefs.SetInt("MadMachines.Garage.Drive.v1", (int)drive);
            PlayerPrefs.Save();
        }

        public static void StartLocal(string scene, ChallengeMode mode)
        {
            Initialize();
            if (Loading) return;
            LocalLaunchPending = true;
            OnlineLaunchPending = false;
            LocalMode = mode;
            instance.StartCoroutine(instance.Load(scene));
        }

        public static void ConsumeLocalLaunch() => LocalLaunchPending = false;

        public static void StartOnline(string scene, string address, ushort port)
        {
            Initialize();
            if (Loading) return;
            LocalLaunchPending = false;
            OnlineLaunchPending = true;
            ServerAddress = address;
            ServerPort = port;
            instance.StartCoroutine(instance.Load(scene));
        }

        public static void ReturnToGarage()
        {
            Initialize();
            if (Loading) return;
            LocalLaunchPending = OnlineLaunchPending = false;
            ShowGarage = true;
            instance.StartCoroutine(instance.Load(FrontEndScene));
        }

        private IEnumerator Load(string scene)
        {
            loading = true;
            message = "Загрузка…";
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                LocalLaunchPending = OnlineLaunchPending = false;
                message = "Карта отсутствует в сборке. Вернитесь в основную сборку игры.";
                yield break;
            }
            AsyncOperation operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            while (!operation.isDone) yield return null;
            loading = false;
        }

        private void OnGUI()
        {
            if (!loading) return;
            GUI.depth = -1000;
            GUI.color = new Color(0.025f, 0.04f, 0.06f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(35, 35, Screen.width - 70, 80), message);
            if (message != "Загрузка…" && GUI.Button(new Rect(35, 125, 240, 45), "Закрыть сообщение")) loading = false;
        }
    }
}
