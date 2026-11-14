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
        private const float PreviewDegreesPerPixel = 0.4f;
        private static readonly string[] DriveNames = { "Полный", "Передний", "Задний" };
        private static readonly Rect PreviewControlsArea = new Rect(570, 620, 650, 75);
        [SerializeField] private VehicleDefinition vehicle;
        [SerializeField] private VehicleDefinition[] vehicles;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private Page page;
        private Dialog dialog;
        private VehicleDefinition draftVehicle;
        private VehicleDrive draftDrive;
        private int localMap;
        private bool rotate = true;
        private string address;
        private string port;
        private bool invalidEndpoint;
        private GUIStyle heading;
        private GUIStyle label;
        private GUIStyle button;
        private Vector2 scroll;
        private Transform preview;
        private Camera previewCamera;
        private Vector3 previewCenter;
        private Mouse previewDragMouse;
        private static float GuiScale => Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
        private bool Dirty => draftVehicle != GameFlow.CurrentVehicle || draftDrive != GameFlow.SelectedDrive;

        private void Awake()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            enabled = false;
            MatchLobbyServer.StartDedicated();
#else
            GameFlow.RegisterVehicles(vehicle, vehicles);
            string previousMap = PlayerPrefs.GetString(LocalMapKey, GameFlow.AsphaltTrackScene);
            if (previousMap == "ShootingRange_01") previousMap = GameFlow.ShootingRangeScene;
            localMap = Mathf.Max(0, System.Array.IndexOf(LocalMapScenes, previousMap));
            address = PlayerPrefs.GetString("MadMachines.Server.Address.v1", "127.0.0.1");
            port = PlayerPrefs.GetString("MadMachines.Server.Port.v1", "7777");
            invalidEndpoint = !GameFlow.Lobby.Configure(address, port);
            CreateStage();
            BuildPreview(GameFlow.CurrentVehicle);
            if (GameFlow.ConsumeGarageReturn()) OpenGarage();
#endif
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
            previewDragMouse = null;
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
            previewCenter = bounds.center;
            float distance = Mathf.Max(8, bounds.size.magnitude * 1.4f);
            previewCamera.transform.position = previewCenter + new Vector3(0.65f, 0.35f, -0.8f).normalized * distance;
            previewCamera.transform.LookAt(previewCenter);
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
            if (preview == null || GameFlow.Loading) previewDragMouse = null;
            else if (!UpdatePreviewDrag() && rotate) TurnPreview(8f * Time.unscaledDeltaTime);
            if (GameFlow.Loading || Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (dialog != Dialog.None) dialog = Dialog.None;
            else if (settings.IsOpen) settings.Close();
            else if (page == Page.Garage) LeaveGarage();
            else if (GameFlow.Lobby.WantsMatch) GameFlow.Lobby.SetWaiting(false);
            else dialog = Dialog.Quit;
        }

        private bool UpdatePreviewDrag()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed || !Application.isFocused || dialog != Dialog.None)
            {
                previewDragMouse = null;
                return false;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                Vector2 pointer = mouse.position.ReadValue();
                Vector2 guiPointer = new Vector2(pointer.x, Screen.height - pointer.y) / GuiScale;
                bool overPreview = previewCamera.pixelRect.Contains(pointer) && !PreviewControlsArea.Contains(guiPointer);
                previewDragMouse = overPreview ? mouse : null;
                return overPreview;
            }

            if (previewDragMouse != mouse)
            {
                previewDragMouse = null;
                return false;
            }

            Vector2 delta = mouse.delta.ReadValue();
            if (delta != Vector2.zero)
            {
                Transform view = previewCamera.transform;
                // Rotate relative to the screen so the visible surface follows the drag.
                Vector3 axis = view.right * delta.y - view.up * delta.x;
                preview.RotateAround(previewCenter, axis.normalized, delta.magnitude * PreviewDegreesPerPixel / GuiScale);
            }
            return true;
        }

        private void TurnPreview(float degrees) => preview.RotateAround(previewCenter, preview.up, degrees);

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) previewDragMouse = null;
        }

        private void OnGUI()
        {
            if (GameFlow.OverlayVisible) return;
            heading ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = true };
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 17, fixedHeight = 39 };
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * GuiScale);
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
                GUILayout.BeginArea(PreviewControlsArea);
                rotate = GUILayout.Toggle(rotate, "Вращать машину");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("ПОВЕРНУТЬ", GUILayout.Width(160), GUILayout.Height(30))) TurnPreview(30f);
                GUILayout.Label("Зажмите ПКМ и двигайте мышь", label);
                GUILayout.EndHorizontal();
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
            GUILayout.Label("Адрес сервера и игровой порт", label);
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, 45, GUILayout.Height(30));
            port = GUILayout.TextField(port, 5, GUILayout.Width(80), GUILayout.Height(30));
            GUILayout.EndHorizontal();
            bool changed = address != GameFlow.Lobby.Address || port != GameFlow.Lobby.Port.ToString();
            if (changed && GUILayout.Button("СОХРАНИТЬ АДРЕС", button))
            {
                invalidEndpoint = !GameFlow.Lobby.Configure(address, port);
                if (!invalidEndpoint) SaveServerAddress();
            }
            if (invalidEndpoint) GUILayout.Label("Укажите IPv4 и игровой порт от 1 до 65534.", label);

            DrawMatchmaking(!changed);
        }

        private void DrawMatchmaking(bool endpointApplied)
        {
            MatchLobbyClient lobby = GameFlow.Lobby;
            bool connected = endpointApplied && lobby.Connected;
            GUILayout.BeginHorizontal();
            Color color = GUI.contentColor;
            GUI.contentColor = connected ? new Color(0.3f, 0.9f, 0.43f) : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label("●", label, GUILayout.Width(20));
            GUI.contentColor = color;
            GUILayout.Label(connected ? "Сервер подключён" : "Нет связи с сервером", label);
            GUILayout.EndHorizontal();
            GUILayout.Label("Deathmatch", label);
            if (lobby.WantsMatch)
            {
                GUILayout.Label(lobby.Phase == MatchLobbyPhase.Loading ? "Состав собран. Готовим карту…" :
                    $"Ожидаем игроков {lobby.Players}/{lobby.RequiredPlayers}", label);
                if (GUILayout.Button("ОТМЕНИТЬ ОЖИДАНИЕ", button)) lobby.SetWaiting(false);
                return;
            }

            bool interactable = GUI.enabled;
            GUI.enabled = interactable && connected && GameFlow.CurrentVehicle != null;
            if (GUILayout.Button("В БОЙ", button)) lobby.SetWaiting(true);
            GUI.enabled = interactable;
        }

        private void SaveServerAddress()
        {
            PlayerPrefs.SetString("MadMachines.Server.Address.v1", address);
            PlayerPrefs.SetString("MadMachines.Server.Port.v1", port);
            PlayerPrefs.Save();
        }

        private void OpenGarage()
        {
            GameFlow.Lobby.SetWaiting(false);
            draftVehicle = GameFlow.CurrentVehicle;
            draftDrive = GameFlow.SelectedDrive;
            page = Page.Garage;
            scroll = Vector2.zero;
        }

        private void DrawGarage()
        {
            bool interactable = GUI.enabled;
            GUILayout.Label("ГАРАЖ", heading);
            DrawVehicleSelection();
            DrawNotice();
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

        private void DrawVehicleSelection()
        {
            GUILayout.Label("ВЫБОР МАШИНЫ", label);
            if (GameFlow.Vehicles.Count == 0)
            {
                GUILayout.Label("Нет доступных машин", label);
                return;
            }

            Color background = GUI.backgroundColor;
            foreach (VehicleDefinition definition in GameFlow.Vehicles)
            {
                bool selected = definition == draftVehicle;
                GUI.backgroundColor = selected ? new Color(0.42f, 0.9f, 0.64f) : background;
                bool clicked = GUILayout.Button(selected ? $"✓  {definition.DisplayName} — выбрана" : definition.DisplayName, button);
                GUI.backgroundColor = background;
                if (!clicked || selected) continue;

                draftVehicle = definition;
                draftDrive = GameFlow.ReadDrive(definition);
                BuildPreview(definition);
            }
            GUILayout.Space(8);
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
    }
}
