using MadMachines.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadMachines
{
    public sealed class FrontEnd : MonoBehaviour
    {
        private enum Page { Main, Garage, Statistics }
        private enum Dialog { None, GarageChanges, Quit }
        private static readonly string[] DriveNames = { "Полный", "Передний", "Задний" };
        [SerializeField] private VehicleDefinition vehicle;
        [SerializeField] private VehicleDefinition[] vehicles;
        private readonly ServerSessionQuery query = new ServerSessionQuery();
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private Page page;
        private Dialog dialog;
        private VehicleDefinition draftVehicle;
        private VehicleDrive draftDrive;
        private bool online;
        private bool rotate = true;
        private ChallengeMode mode;
        private ChallengeMode statisticsMode = ChallengeMode.Race;
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
            mode = (ChallengeMode)Mathf.Clamp(PlayerPrefs.GetInt("MadMachines.Garage.Mode.v1", 0), 0, 3);
            online = PlayerPrefs.GetInt("MadMachines.Main.Online.v1", 0) != 0;
            address = PlayerPrefs.GetString("MadMachines.Server.Address.v1", "127.0.0.1");
            port = PlayerPrefs.GetString("MadMachines.Server.Port.v1", "7777");
            CreateStage();
            BuildPreview(GameFlow.CurrentVehicle);
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
            else if (page == Page.Statistics) page = Page.Main;
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
            else if (page == Page.Statistics) DrawStatistics();
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
            if (GUILayout.Button("СТАТИСТИКА", button)) page = Page.Statistics;
            if (GUILayout.Button("НАСТРОЙКИ", button)) settings.Open();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(GameFlow.Notice))
            {
                GUILayout.Label(GameFlow.Notice, label);
                if (GUILayout.Button("ЗАКРЫТЬ СООБЩЕНИЕ")) GameFlow.ClearNotice();
            }
            GUILayout.Space(8);
            online = GUILayout.Toolbar(online ? 1 : 0, new[] { "ОДИНОЧНАЯ", "ПО СЕТИ" }, button) == 1;
            if (online) DrawOnline();
            else DrawLocal();
            GUILayout.Space(8);
            if (GUILayout.Button("ВЫХОД", button)) dialog = Dialog.Quit;
        }

        private void DrawLocal()
        {
            bool interactable = GUI.enabled;
            GUILayout.Label("КАРТА", label);
            GUILayout.Label("Полигон", label);
            GUILayout.Label("РЕЖИМ", label);
            foreach (ChallengeMode choice in new[] { ChallengeMode.Free, ChallengeMode.Race, ChallengeMode.Shooting, ChallengeMode.Combat })
                if (GUILayout.Toggle(mode == choice, PlaygroundText.ModeName(choice), button)) mode = choice;
            string rules = mode switch
            {
                ChallengeMode.Race => "Пройдите ворота по порядку. Меньшее время лучше. Восстановление: +3 с.",
                ChallengeMode.Shooting => "60 с на цели: 100 очков за обычную, 200 за движущуюся. Больше очков лучше.",
                ChallengeMode.Combat => "Ворота и цели. Оставшаяся цель: +8 с, промах: +0,25 с, восстановление: +3 с.",
                _ => "Исследуйте карту, опробуйте машину и оружие без ограничения времени."
            };
            GUILayout.Label(rules, label);
            GUI.enabled = interactable && GameFlow.CurrentVehicle != null;
            if (GUILayout.Button("ИГРАТЬ", button))
            {
                SaveLaunch();
                GameFlow.StartLocal("Playground", mode);
            }
            GUI.enabled = interactable;
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
                    SaveLaunch();
                    GameFlow.StartOnline(info.Scene, queriedAddress, queriedPort);
                }
            }
            else if (info != null) GUILayout.Label("Обновите сведения для выбранного адреса.", label);
            GUI.enabled = interactable;
        }

        private void SaveLaunch()
        {
            PlayerPrefs.SetInt("MadMachines.Main.Online.v1", online ? 1 : 0);
            PlayerPrefs.SetInt("MadMachines.Garage.Mode.v1", (int)mode);
            PlayerPrefs.SetString("MadMachines.Server.Address.v1", address);
            PlayerPrefs.SetString("MadMachines.Server.Port.v1", port);
            PlayerPrefs.Save();
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
            if (GUILayout.Button("НА ГЛАВНЫЙ", button)) LeaveGarage();
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

        private void DrawStatistics()
        {
            GUILayout.Label("СТАТИСТИКА ИГРОКА", heading);
            GUILayout.Label("Сохранённые личные результаты", label);
            GUILayout.Label("Полигон", label);
            foreach (ChallengeMode choice in new[] { ChallengeMode.Race, ChallengeMode.Shooting, ChallengeMode.Combat })
                if (GUILayout.Toggle(statisticsMode == choice, PlaygroundText.ModeName(choice), button)) statisticsMode = choice;
            var records = LocalRecords.Read(statisticsMode);
            if (records.Count == 0) GUILayout.Label("Результатов пока нет", label);
            for (int i = 0; i < records.Count; i++)
                GUILayout.Label($"{i + 1}. " + (statisticsMode == ChallengeMode.Shooting ? $"{records[i]:0} очк." : PlaygroundText.Seconds(records[i])), label);
            GUILayout.Label("Локальный топ-5. Сетевые результаты здесь не учитываются.", label);
            if (GUILayout.Button("НА ГЛАВНЫЙ", button)) page = Page.Main;
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
