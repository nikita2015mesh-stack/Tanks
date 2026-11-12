using MadMachines.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadMachines
{
    public sealed class FrontEnd : MonoBehaviour
    {
        private enum Page { Main, Garage }
        private enum Dialog { None, GarageChanges, Quit }
        private static readonly string[] LocalMapScenes = { GameFlow.AsphaltTrackScene, GameFlow.DirtTrackScene, GameFlow.ShootingRangeScene };
        private static readonly string[] LocalMapNames = { GameFlow.AsphaltTrackLocation, GameFlow.DirtTrackLocation, GameFlow.ShootingRangeLocation };
        private const string LocalMapKey = "MadMachines.Main.LocalMap.v1";
        private static readonly string[] DriveNames = { "Полный", "Передний", "Задний" };
        [SerializeField] private VehicleDefinition vehicle;
        [SerializeField] private VehicleDefinition[] vehicles;
        private readonly ServerSessionQuery query = new ServerSessionQuery();
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private Page page;
        private Dialog dialog;
        private VehicleDefinition draftVehicle;
        private VehicleDrive draftDrive;
        private int localMap;
        private bool rotate = true;
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
        private Camera previewCamera;
        private bool Dirty => draftVehicle != GameFlow.CurrentVehicle || draftDrive != GameFlow.SelectedDrive;

        private void Awake()
        {
            GameFlow.RegisterVehicles(vehicle, vehicles);
            string previousMap = PlayerPrefs.GetString(LocalMapKey, GameFlow.AsphaltTrackScene);
            if (previousMap == "ShootingRange_01") previousMap = GameFlow.ShootingRangeScene;
            localMap = Mathf.Max(0, System.Array.IndexOf(LocalMapScenes, previousMap));
            address = PlayerPrefs.GetString("MadMachines.Server.Address.v1", "127.0.0.1");
            port = PlayerPrefs.GetString("MadMachines.Server.Port.v1", "7777");
            CreateStage();
            BuildPreview(GameFlow.CurrentVehicle);
            if (GameFlow.ConsumeGarageReturn()) OpenGarage();
        }

        private void CreateStage()
        {
            previewCamera = new GameObject("Garage camera").AddComponent<Camera>();
            previewCamera.transform.SetParent(transform);
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.035f, 0.05f, 0.07f);
            previewCamera.fieldOfView = 42;
            previewCamera.rect = new Rect(0.43f, 0, 0.57f, 1);
            previewCamera.gameObject.AddComponent<AudioListener>();
            Light light = new GameObject("Garage key light").AddComponent<Light>();
            light.transform.SetParent(transform);
            light.type = LightType.Directional;
            light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);
            RenderSettings.fog = false;
        }

        private void BuildPreview(VehicleDefinition definition)
        {
            if (preview != null)
            {
                preview.gameObject.SetActive(false);
                Destroy(preview.gameObject);
            }
            preview = null;
            if (definition == null || definition.Prefab == null) return;
            preview = new GameObject("Current vehicle preview").transform;
            preview.SetParent(transform);
            Transform source = definition.Prefab.transform;
            // Copy geometry only; no garage physics, weapons or input components.
            foreach (MeshFilter mesh in source.GetComponentsInChildren<MeshFilter>(true))
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
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            float distance = Mathf.Max(8, bounds.size.magnitude * 1.4f);
            previewCamera.transform.position = bounds.center + new Vector3(0.65f, 0.35f, -0.8f).normalized * distance;
            previewCamera.transform.LookAt(bounds.center);
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
            if (preview != null && rotate && !GameFlow.Loading) preview.Rotate(Vector3.up, 8f * Time.unscaledDeltaTime, Space.World);
            if (GameFlow.Loading || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (dialog != Dialog.None) dialog = Dialog.None;
            else if (settings.IsOpen) settings.Close();
            else if (page == Page.Garage) LeaveGarage();
            else dialog = Dialog.Quit;
        }

        private void OnGUI()
        {
            if (GameFlow.OverlayVisible) return;
            heading ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = true };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 17, fixedHeight = 39 };
            float scale = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.enabled = !GameFlow.Loading && dialog == Dialog.None;
            GUI.Box(new Rect(24, 24, 510, 672), "");
            GUILayout.BeginArea(new Rect(44, 38, 470, 642));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("БЕЗУМНЫЕ МАШИНЫ", heading);
            if (settings.IsOpen) settings.Draw();
            else if (page == Page.Garage) DrawGarage();
            else DrawMain();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (preview != null)
            {
                GUILayout.BeginArea(new Rect(570, 620, 650, 75));
                rotate = GUILayout.Toggle(rotate, "Вращать машину");
                if (GUILayout.Button("ПОВЕРНУТЬ", GUILayout.Width(160), GUILayout.Height(30))) preview.Rotate(Vector3.up, 30, Space.World);
                GUILayout.EndArea();
            }
            GUI.enabled = !GameFlow.Loading;
            if (dialog != Dialog.None) DrawDialog();
            GUI.enabled = true;
            GUI.matrix = previous;
        }

        private void DrawMain()
        {
            VehicleDefinition current = GameFlow.CurrentVehicle;
            GUILayout.Label(current != null ? current.DisplayName : "Нет доступной машины", label);
            GUILayout.Label("Привод: " + DriveNames[(int)GameFlow.SelectedDrive - 1], label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ГАРАЖ", button)) OpenGarage();
            if (GUILayout.Button("НАСТРОЙКИ", button)) settings.Open();
            GUILayout.EndHorizontal();
            DrawNotice();
            GUILayout.Space(8);
            DrawOnline();
            GUILayout.Space(8);
            if (GUILayout.Button("ВЫХОД", button)) dialog = Dialog.Quit;
        }

        private void DrawNotice()
        {
            if (string.IsNullOrEmpty(GameFlow.Notice)) return;
            GUILayout.Label(GameFlow.Notice, label);
            if (GUILayout.Button("ЗАКРЫТЬ СООБЩЕНИЕ")) GameFlow.ClearNotice();
        }

        private void DrawOnline()
        {
            bool interactable = GUI.enabled;
            GUILayout.Label("Адрес сервера и игровой порт", label);
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, 45, GUILayout.Height(30));
            port = GUILayout.TextField(port, 5, GUILayout.Width(80), GUILayout.Height(30));
            GUILayout.EndHorizontal();
            if (GUILayout.Button(query.Pending ? "ОТМЕНИТЬ ПРОВЕРКУ" : "ПРОВЕРИТЬ", button))
            {
                if (query.Pending) query.Cancel();
                else
                {
                    queriedAddress = address;
                    ushort.TryParse(port, out queriedPort);
                    infoExpires = Time.realtimeSinceStartupAsDouble + 15;
                    query.Begin(address, port);
                }
            }
            GUILayout.Label(query.Status ?? "Проверьте доступный режим перед запуском.", label);
            ServerSessionInfo info = query.Result;
            bool fresh = info != null && Time.realtimeSinceStartupAsDouble < infoExpires && address == queriedAddress && port == queriedPort.ToString();
            if (info != null && fresh)
            {
                if (info.Supported) GUILayout.Label(info.Summary, label);
                bool correctVehicle = GameFlow.CurrentVehicle != null && GameFlow.CurrentVehicle.Id == info.vehicleId;
                if (!correctVehicle) GUILayout.Label("Сервер допускает машину: " + (info.vehicleName ?? "не сообщена"), label);
                GUI.enabled = interactable && info.Supported && info.joinable && correctVehicle;
                if (GUILayout.Button("ИГРАТЬ", button))
                {
                    PlayerPrefs.SetString("MadMachines.Server.Address.v1", address);
                    PlayerPrefs.SetString("MadMachines.Server.Port.v1", port);
                    PlayerPrefs.Save();
                    GameFlow.StartOnline(info.Scene, queriedAddress, queriedPort);
                }
            }
            else if (info != null) GUILayout.Label("Обновите сведения для выбранного адреса.", label);
            GUI.enabled = interactable;
        }

        private void OpenGarage()
        {
            query.Cancel();
            draftVehicle = GameFlow.CurrentVehicle;
            draftDrive = GameFlow.SelectedDrive;
            page = Page.Garage;
            scroll = Vector2.zero;
        }

        private void DrawGarage()
        {
            bool interactable = GUI.enabled;
            GUILayout.Label("ГАРАЖ", heading);
            DrawNotice();
            foreach (VehicleDefinition definition in GameFlow.Vehicles)
            {
                if (GUILayout.Toggle(draftVehicle == definition, definition.DisplayName, button) && draftVehicle != definition)
                {
                    draftVehicle = definition;
                    draftDrive = GameFlow.ReadDrive(definition);
                    BuildPreview(definition);
                }
            }
            GUILayout.Label("ПРИВОД", label);
            draftDrive = (VehicleDrive)(GUILayout.Toolbar((int)draftDrive - 1, DriveNames, button) + 1);
            GUILayout.Label(Dirty ? "Есть неприменённые изменения" : "Текущая конфигурация сохранена", label);
            GUI.enabled = interactable && draftVehicle != null && Dirty;
            if (GUILayout.Button("ПРИМЕНИТЬ", button)) ApplyGarage();
            GUI.enabled = interactable;
            if (GUILayout.Button("СБРОСИТЬ НАСТРОЙКИ", button) && draftVehicle != null)
                draftDrive = draftVehicle.Prefab.DriveMode == VehicleDrive.Configured ? VehicleDrive.All : draftVehicle.Prefab.DriveMode;
            GUILayout.Space(8);
            DrawTestDrive();
            if (GUILayout.Button("НА ГЛАВНЫЙ", button)) LeaveGarage();
        }

        private void DrawTestDrive()
        {
            bool interactable = GUI.enabled;
            GUILayout.Label("ПРОБНЫЙ ЗАЕЗД", label);
            localMap = GUILayout.SelectionGrid(localMap, LocalMapNames, 1, button);
            GUI.enabled = interactable && draftVehicle != null;
            if (GUILayout.Button(Dirty ? "ПРИМЕНИТЬ И ВЫЕХАТЬ" : "ВЫЕХАТЬ", button))
            {
                if (Dirty) ApplyGarage();
                string scene = LocalMapScenes[localMap];
                PlayerPrefs.SetString(LocalMapKey, scene);
                PlayerPrefs.Save();
                GameFlow.StartLocal(scene, ChallengeMode.Free, fromGarage: true);
            }
            GUI.enabled = interactable;
        }

        private void ApplyGarage() => GameFlow.ApplyVehicle(draftVehicle, draftDrive);

        private void LeaveGarage()
        {
            if (Dirty) dialog = Dialog.GarageChanges;
            else CloseGarage();
        }

        private void CloseGarage()
        {
            page = Page.Main;
            dialog = Dialog.None;
            BuildPreview(GameFlow.CurrentVehicle);
        }

        private void DrawDialog()
        {
            GUI.Box(new Rect(320, 220, 640, 285), "");
            GUILayout.BeginArea(new Rect(345, 240, 590, 245));
            GUILayout.Label(dialog == Dialog.Quit ? "Выйти из игры?" : "Применить изменения машины?", heading);
            if (dialog == Dialog.GarageChanges)
            {
                if (GUILayout.Button("ПРИМЕНИТЬ И ВЕРНУТЬСЯ", button)) { ApplyGarage(); CloseGarage(); }
                if (GUILayout.Button("НЕ ПРИМЕНЯТЬ", button)) CloseGarage();
                if (GUILayout.Button("ОСТАТЬСЯ", button)) dialog = Dialog.None;
            }
            else
            {
                if (GUILayout.Button("ВЫЙТИ", button)) Quit();
                if (GUILayout.Button("ОСТАТЬСЯ", button)) dialog = Dialog.None;
            }
            GUILayout.EndArea();
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
