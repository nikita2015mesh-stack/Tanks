using MadMachines.Networking;
using UnityEngine;

namespace MadMachines
{
    public sealed class FrontEnd : MonoBehaviour
    {
        private static readonly string[] DriveNames = { "Полный", "Передний", "Задний" };
        private static readonly string[] MapNames = { "Карьер", "Полигон" };
        [SerializeField] private VehicleDefinition vehicle;
        private readonly ServerSessionQuery query = new ServerSessionQuery();
        private bool garage;
        private bool online;
        private int map;
        private ChallengeMode mode;
        private string address;
        private string port;
        private string queriedAddress;
        private ushort queriedPort;
        private double infoExpires;
        private GUIStyle heading;
        private GUIStyle label;
        private GUIStyle button;
        private Vector2 scroll;
        private Transform preview;

        private void Awake()
        {
            GameFlow.Initialize();
            garage = GameFlow.ShowGarage;
            map = Mathf.Clamp(PlayerPrefs.GetInt("MadMachines.Garage.Map.v1", 0), 0, 1);
            mode = (ChallengeMode)Mathf.Clamp(PlayerPrefs.GetInt("MadMachines.Garage.Mode.v1", 0), 0, 3);
            address = PlayerPrefs.GetString("MadMachines.Server.Address.v1", "127.0.0.1");
            port = PlayerPrefs.GetString("MadMachines.Server.Port.v1", "7777");
            CreateGarage();
        }

        private void CreateGarage()
        {
            Camera camera = new GameObject("Garage camera").AddComponent<Camera>();
            camera.transform.SetParent(transform);
            camera.transform.SetPositionAndRotation(new Vector3(8, 4, -9), Quaternion.Euler(16, -38, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.07f);
            camera.fieldOfView = 42;
            camera.rect = new Rect(0.42f, 0, 0.58f, 1);
            camera.gameObject.AddComponent<AudioListener>();
            Light light = new GameObject("Garage key light").AddComponent<Light>();
            light.transform.SetParent(transform);
            light.type = LightType.Directional;
            light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);
            RenderSettings.fog = false;
            if (vehicle == null || vehicle.Prefab == null) return;
            // Copy geometry only: the garage has no simulated car, weapons or player input.
            preview = new GameObject("Garage vehicle preview").transform;
            preview.SetParent(transform);
            Transform source = vehicle.Prefab.transform;
            foreach (MeshFilter mesh in source.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || !VisibleInPrefab(mesh.transform, source)) continue;
                GameObject part = new GameObject(mesh.name);
                part.transform.SetParent(preview, false);
                part.transform.localPosition = source.InverseTransformPoint(mesh.transform.position);
                part.transform.localRotation = Quaternion.Inverse(source.rotation) * mesh.transform.rotation;
                part.transform.localScale = mesh.transform.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            // Keep the vehicle to the right of the menu.
            preview.position = new Vector3(2, 0, 0);
        }

        private static bool VisibleInPrefab(Transform part, Transform root)
        {
            for (Transform current = part; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) break;
            }
            return true;
        }

        private void Update()
        {
            query.Poll();
            if (preview != null) preview.Rotate(Vector3.up, 8f * Time.unscaledDeltaTime, Space.World);
        }

        private void OnGUI()
        {
            if (GameFlow.Loading) return;
            heading ??= new GUIStyle(GUI.skin.label) { fontSize = 31, fontStyle = FontStyle.Bold, wordWrap = true };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 42 };
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.Box(new Rect(24, 24, 500, 672), "");
            GUILayout.BeginArea(new Rect(44, 40, 460, 640));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("БЕЗУМНЫЕ МАШИНЫ", heading);
            GUILayout.Space(14);
            if (!garage)
            {
                GUILayout.Label("Подготовьте машину и отправляйтесь на трассу.", label);
                if (GUILayout.Button("В ГАРАЖ", button)) garage = true;
                if (GUILayout.Button("ВЫХОД", button)) Quit();
            }
            else
            {
                GUILayout.Label("ГАРАЖ", heading);
                GUILayout.Label(vehicle != null ? vehicle.DisplayName : "Машина не назначена", label);
                GUILayout.Label("ПРИВОД", label);
                int drive = GUILayout.Toolbar((int)GameFlow.SelectedDrive - 1, DriveNames, button);
                if (drive != (int)GameFlow.SelectedDrive - 1) GameFlow.SelectDrive((VehicleDrive)(drive + 1));
                GUILayout.Space(14);
                bool selectedOnline = GUILayout.Toolbar(online ? 1 : 0, new[] { "ОДИНОЧНАЯ ИГРА", "ПО СЕТИ" }, button) == 1;
                online = selectedOnline;
                GUILayout.Space(10);
                if (online) DrawOnline();
                else DrawLocal();
                GUILayout.Space(14);
                if (GUILayout.Button("ГЛАВНОЕ МЕНЮ", button)) garage = false;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void DrawLocal()
        {
            GUILayout.Label("КАРТА", label);
            map = GUILayout.Toolbar(map, MapNames, button);
            GUILayout.Label("РЕЖИМ", label);
            foreach (ChallengeMode choice in new[] { ChallengeMode.Free, ChallengeMode.Race, ChallengeMode.Shooting, ChallengeMode.Combat })
            {
                if (GUILayout.Toggle(mode == choice, PlaygroundText.ModeName(choice), button)) mode = choice;
            }
            GUI.enabled = vehicle != null;
            if (GUILayout.Button("ВЫЕХАТЬ", button))
            {
                PlayerPrefs.SetInt("MadMachines.Garage.Map.v1", map);
                PlayerPrefs.SetInt("MadMachines.Garage.Mode.v1", (int)mode);
                PlayerPrefs.Save();
                GameFlow.StartLocal(map == 0 ? "Quarry" : "Playground", mode);
            }
            GUI.enabled = true;
        }

        private void DrawOnline()
        {
            GUILayout.Label("Адрес сервера и игровой порт", label);
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, 45, GUILayout.Height(30));
            port = GUILayout.TextField(port, 5, GUILayout.Width(80), GUILayout.Height(30));
            GUILayout.EndHorizontal();
            if (GUILayout.Button(query.Pending ? "ОБНОВИТЬ ЗАПРОС" : "НАЙТИ СЕССИЮ", button))
            {
                queriedAddress = address;
                ushort.TryParse(port, out queriedPort);
                infoExpires = Time.realtimeSinceStartupAsDouble + 15;
                query.Begin(address, port);
            }
            GUILayout.Label(query.Status ?? "Сервер сообщит карту, режим и доступность входа.", label);
            ServerSessionInfo info = query.Result;
            if (info == null) return;
            if (info.Supported) GUILayout.Label(info.Summary, label);
            bool fresh = Time.realtimeSinceStartupAsDouble < infoExpires && address == queriedAddress && port == queriedPort.ToString();
            if (!fresh) GUILayout.Label("Обновите сведения о сессии.", label);
            GUI.enabled = fresh && info.Supported && info.joinable;
            if (GUILayout.Button("ПРИСОЕДИНИТЬСЯ", button))
            {
                PlayerPrefs.SetString("MadMachines.Server.Address.v1", address);
                PlayerPrefs.SetString("MadMachines.Server.Port.v1", port);
                PlayerPrefs.Save();
                GameFlow.StartOnline(info.Scene, queriedAddress, queriedPort);
            }
            GUI.enabled = true;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnDestroy() => query.Dispose();
    }
}
