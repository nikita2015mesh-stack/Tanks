using MadMachines.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadMachines
{
    public sealed class FrontEnd : MonoBehaviour
    {
        private enum Page { Main, Garage, Modules }
        private enum Dialog { None, GarageChanges, Quit }
        private enum Module { None, Weapon, Turret, Engine, RunningGear }
        private static readonly string[] LocalMapScenes = { GameFlow.AsphaltTrackScene, GameFlow.DirtTrackScene, GameFlow.ShootingRangeScene };
        private static readonly string[] LocalMapNames = { GameFlow.AsphaltTrackLocation, GameFlow.DirtTrackLocation, GameFlow.ShootingRangeLocation };
        private const string LocalMapKey = "MadMachines.Main.LocalMap.v1";
        private const float PreviewDegreesPerPixel = 0.4f;
        private const int ModuleIconColumns = 6;
        private const int ModuleIconRows = 5;
        private const int ModuleOptionsPerRow = 3;
        private static readonly string[] DriveNames = { "Полный", "Передний", "Задний" };
        private static readonly Module[] GarageModules = { Module.Weapon, Module.Turret, Module.Engine, Module.RunningGear };
        private static readonly GarageWeapon[] GarageWeapons =
        {
            GarageWeapon.Factory, GarageWeapon.Cannon, GarageWeapon.HeavyCannon, GarageWeapon.Machinegun,
            GarageWeapon.GrenadeLauncher, GarageWeapon.GuidedRocket, GarageWeapon.UnguidedRocket
        };
        // Order follows the rows of GarageModuleIcons.png from top left.
        private static readonly string[] ModuleIcons =
        {
            "Weapon/Factory", "Weapon/Cannon", "Weapon/HeavyCannon", "Weapon/Machinegun", "Weapon/GrenadeLauncher", "Weapon/GuidedRocket",
            "Weapon/UnguidedRocket", "Weapon/Factory/bastion-6x6", "Weapon/Factory/t34", "Turret/t100", "Turret/t100/scout", "Turret/t100/assault",
            "Turret/bastion-6x6", "Turret/bastion-6x6/wedge", "Turret/bastion-6x6/casemate", "Turret/t34", "Turret/t34/cast", "Turret/t34/hex",
            "Engine/Standard", "Engine/Economy", "Engine/Sport", "Wheels/Standard", "Wheels/Road", "Wheels/OffRoad",
            "Tracks/Standard", "Tracks/Road", "Tracks/OffRoad"
        };
        private static readonly Rect PreviewControlsArea = new Rect(570, 620, 650, 75);
        [SerializeField] private VehicleDefinition vehicle;
        [SerializeField] private VehicleDefinition[] vehicles;
        [SerializeField] private VehicleDefinition arsenalVehicle;
        [SerializeField] private WeaponDefinition heavyCannon;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private Page page;
        private Dialog dialog;
        private VehicleDefinition draftVehicle;
        private VehicleDrive draftDrive;
        private GarageLoadout draftLoadout;
        private Module armedModule;
        private Module openModule;
        private double moduleClickTime;
        private int localMap;
        private bool rotate = true;
        private string address;
        private string port;
        private bool invalidEndpoint;
        private GUIStyle heading;
        private GUIStyle label;
        private GUIStyle button;
        private GUIStyle moduleButton;
        private GUIStyle moduleCheckmark;
        private Texture2D moduleIcons;
        private string moduleHint;
        private Vector2 scroll;
        private Transform preview;
        private Camera previewCamera;
        private Vector3 previewCenter;
        private Mouse previewDragMouse;
        private static float GuiScale => Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
        private bool Dirty => draftVehicle != GameFlow.CurrentVehicle || draftDrive != GameFlow.SelectedDrive ||
            !draftLoadout.Matches(GameFlow.SelectedLoadout);

        private void Awake()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            enabled = false;
            MatchLobbyServer.StartDedicated();
#else
            GameFlow.RegisterVehicles(vehicle, vehicles);
            WeaponSystem arsenal = arsenalVehicle != null && arsenalVehicle.Prefab != null
                ? arsenalVehicle.Prefab.GetComponent<WeaponSystem>() : null;
            GameFlow.RegisterGarageArsenal(arsenal, heavyCannon);
            string previousMap = PlayerPrefs.GetString(LocalMapKey, GameFlow.AsphaltTrackScene);
            if (previousMap == "ShootingRange_01") previousMap = GameFlow.ShootingRangeScene;
            localMap = Mathf.Max(0, System.Array.IndexOf(LocalMapScenes, previousMap));
            address = PlayerPrefs.GetString("MadMachines.Server.Address.v1", "127.0.0.1");
            port = PlayerPrefs.GetString("MadMachines.Server.Port.v1", "7777");
            invalidEndpoint = !GameFlow.Lobby.Configure(address, port);
            moduleIcons = Resources.Load<Texture2D>("GarageModuleIcons");
            CreateStage();
            BuildPreview(GameFlow.CurrentVehicle, GameFlow.SelectedLoadout);
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

        private void BuildPreview(VehicleDefinition definition, GarageLoadout loadout)
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
            GameObject staging = null;
            VehicleDefinition.TurretOption turret = definition.FindTurret(loadout.TurretId);
            bool customWeapon = loadout.Weapon != GarageWeapon.Factory && GameFlow.GarageArsenal != null;
            if (turret.Mesh != null || customWeapon)
            {
                // Resolve the same mount as the test drive on an inactive copy.
                staging = new GameObject("Garage preview staging");
                staging.SetActive(false);
                source = Instantiate(definition.Prefab.gameObject, staging.transform, false).transform;
                if (source.TryGetComponent(out WeaponSystem weapons))
                {
                    weapons.ApplyGarageTurret(turret);
                    if (customWeapon) weapons.InstallGarageWeapon(loadout.Weapon, GameFlow.GarageArsenal, heavyCannon);
                }
            }
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
            if (staging != null) Destroy(staging);
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
            else if (page == Page.Modules) BackFromModules();
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
            moduleButton ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(12, 10, 4, 4)
            };
            moduleCheckmark ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.52f, 0.95f, 0.69f) }
            };
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * GuiScale);
            GUI.enabled = !GameFlow.Loading && dialog == Dialog.None;
            GUI.Box(new Rect(24, 24, 510, 672), "");
            GUILayout.BeginArea(new Rect(44, 38, 470, 642));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("БЕЗУМНЫЕ МАШИНЫ", heading);
            if (settings.IsOpen) settings.Draw();
            else if (page == Page.Garage) DrawGarage();
            else if (page == Page.Modules) DrawModules();
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
            string drive = current != null && current.Prefab.TrackedDrive ? "Гусеничный" : DriveNames[(int)GameFlow.SelectedDrive - 1];
            GUILayout.Label("Привод: " + drive, label);
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
            draftLoadout = GameFlow.SelectedLoadout;
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
            bool tracked = draftVehicle != null && draftVehicle.Prefab.TrackedDrive;
            if (tracked) GUILayout.Label("Гусеничный", label);
            else draftDrive = (VehicleDrive)(GUILayout.Toolbar((int)draftDrive - 1, DriveNames, button) + 1);
            GUILayout.Label(Dirty ? "Есть неприменённые изменения" : "Текущая конфигурация сохранена", label);
            GUI.enabled = interactable && draftVehicle != null && Dirty;
            if (GUILayout.Button("ПРИМЕНИТЬ", button)) ApplyGarage();
            GUI.enabled = interactable;
            if (GUILayout.Button("СБРОСИТЬ НАСТРОЙКИ", button) && draftVehicle != null)
            {
                bool previewChanged = draftLoadout.Weapon != GarageWeapon.Factory || !string.IsNullOrEmpty(draftLoadout.TurretId);
                draftDrive = draftVehicle.Prefab.DriveMode == VehicleDrive.Configured ? VehicleDrive.All : draftVehicle.Prefab.DriveMode;
                draftLoadout = default;
                if (previewChanged) BuildPreview(draftVehicle, draftLoadout);
            }
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
                bool clicked = GUILayout.Button(selected ? $"✓  {definition.DisplayName}  ›  Модули" : definition.DisplayName, button);
                GUI.backgroundColor = background;
                if (!clicked) continue;
                if (selected)
                {
                    ShowModules();
                    continue;
                }

                draftVehicle = definition;
                draftDrive = GameFlow.ReadDrive(definition);
                draftLoadout = GameFlow.ReadLoadout(definition);
                BuildPreview(definition, draftLoadout);
            }
            GUILayout.Space(8);
            if (draftVehicle != null && GUILayout.Button("МОДУЛИ", button)) ShowModules();
        }

        private void ShowModules()
        {
            page = Page.Modules;
            openModule = armedModule = Module.None;
            scroll = Vector2.zero;
        }

        private void BackFromModules()
        {
            if (openModule != Module.None)
            {
                openModule = armedModule = Module.None;
            }
            else
            {
                page = Page.Garage;
                armedModule = Module.None;
            }
            scroll = Vector2.zero;
        }

        private void DrawModules()
        {
            GUILayout.Label(draftVehicle.DisplayName + " · Модули", heading);
            moduleHint = null;
            if (openModule == Module.None) DrawModuleDiagram();
            else DrawModuleOptions();
            GUILayout.Label(moduleHint ?? "", label, GUILayout.Height(42f));

            GUILayout.Space(8);
            bool interactable = GUI.enabled;
            GUI.enabled = interactable && Dirty;
            if (GUILayout.Button("ПРИМЕНИТЬ", button)) ApplyGarage();
            GUI.enabled = interactable;
            if (GUILayout.Button(openModule == Module.None ? "В ГАРАЖ" : "НАЗАД К МОДУЛЯМ", button)) BackFromModules();
        }

        private void DrawModuleDiagram()
        {
            GUILayout.Label("Дважды нажми на модуль, чтобы открыть его варианты.", label);
            const float cardHeight = 70f;
            const float cardStep = 78f;
            Rect diagram = GUILayoutUtility.GetRect(0f, GarageModules.Length * cardStep, GUILayout.ExpandWidth(true));
            float split = diagram.x + diagram.width * 0.47f;
            float center = diagram.y + diagram.height * 0.5f;
            float cards = split + 24f;
            GUI.Box(new Rect(diagram.x, center - 38f, split - diagram.x - 18f, 76f), draftVehicle.DisplayName);

            if (Event.current.type == EventType.Repaint)
            {
                Color previous = GUI.color;
                GUI.color = new Color(0.52f, 0.65f, 0.71f);
                GUI.DrawTexture(new Rect(split, diagram.y + cardStep / 2f, 1f, (GarageModules.Length - 1) * cardStep), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(split - 18f, center, 18f, 1f), Texture2D.whiteTexture);
                for (int i = 0; i < GarageModules.Length; i++)
                    GUI.DrawTexture(new Rect(split, diagram.y + (i + 0.5f) * cardStep, 24f, 1f), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            for (int i = 0; i < GarageModules.Length; i++)
            {
                Module module = GarageModules[i];
                float cardY = diagram.y + (i + 0.5f) * cardStep - cardHeight / 2f;
                Rect card = new Rect(cards, cardY, diagram.xMax - cards, cardHeight);
                bool clicked = DrawModuleCard(card, CurrentModuleIcon(module), ModuleName(module) + " · " + ModuleValue(module),
                    armedModule == module, true);
                if (!clicked) continue;

                double now = Time.unscaledTimeAsDouble;
                if (armedModule == module && now - moduleClickTime <= 0.6) OpenModule(module);
                armedModule = module;
                moduleClickTime = now;
            }

            bool enabled = GUI.enabled;
            GUI.enabled = enabled && armedModule != Module.None;
            if (GUILayout.Button("ОТКРЫТЬ МОДУЛЬ", button)) OpenModule(armedModule);
            GUI.enabled = enabled;
        }

        private void OpenModule(Module module)
        {
            openModule = module;
            scroll = Vector2.zero;
        }

        private string ModuleName(Module module) => module switch
        {
            Module.Weapon => "Орудие",
            Module.Turret => "Башня",
            Module.Engine => "Двигатель",
            Module.RunningGear => draftVehicle.Prefab.TrackedDrive ? "Гусеницы" : "Колёса",
            _ => ""
        };

        private string ModuleValue(Module module) => module switch
        {
            Module.Weapon => WeaponName(draftLoadout.Weapon),
            Module.Turret => draftVehicle.FindTurret(draftLoadout.TurretId).DisplayName ?? "Штатная башня",
            Module.Engine => EngineDescription(draftLoadout.Engine),
            Module.RunningGear => RunningGearDescription(draftLoadout.RunningGear),
            _ => ""
        };

        private string CurrentModuleIcon(Module module) => module switch
        {
            Module.Weapon => WeaponIcon(draftLoadout.Weapon),
            Module.Turret => TurretIcon(draftLoadout.TurretId),
            Module.Engine => "Engine/" + draftLoadout.Engine,
            Module.RunningGear => RunningGearIcon(draftLoadout.RunningGear),
            _ => ""
        };

        private string WeaponIcon(GarageWeapon weapon)
        {
            if (weapon != GarageWeapon.Factory) return "Weapon/" + weapon;
            string vehicleIcon = "Weapon/Factory/" + draftVehicle.Id;
            return System.Array.IndexOf(ModuleIcons, vehicleIcon) >= 0 ? vehicleIcon : "Weapon/Factory";
        }

        private string TurretIcon(string optionId)
        {
            string stock = "Turret/" + draftVehicle.Id;
            return string.IsNullOrEmpty(optionId) ? stock : stock + "/" + optionId;
        }

        private string RunningGearIcon(GarageRunningGear gear) =>
            (draftVehicle.Prefab.TrackedDrive ? "Tracks/" : "Wheels/") + gear;

        private void DrawModuleOptions()
        {
            GUILayout.Label(ModuleName(openModule), label);
            int count = 0;
            switch (openModule)
            {
                case Module.Weapon:
                    foreach (GarageWeapon weapon in GarageWeapons)
                    {
                        if (weapon != GarageWeapon.Factory &&
                            (GameFlow.GarageArsenal == null || GameFlow.GarageArsenal.GarageWeaponDefinition(weapon, heavyCannon) == null)) continue;
                        if (DrawModuleOption(ref count, WeaponIcon(weapon), WeaponName(weapon), weapon == draftLoadout.Weapon) &&
                            weapon != draftLoadout.Weapon)
                        {
                            draftLoadout.Weapon = weapon;
                            BuildPreview(draftVehicle, draftLoadout);
                        }
                    }
                    break;
                case Module.Turret:
                    DrawTurretOption(ref count, null, "Штатная башня");
                    foreach (VehicleDefinition.TurretOption turret in draftVehicle.Turrets)
                    {
                        if (turret.Mesh != null) DrawTurretOption(ref count, turret.Id, turret.DisplayName);
                    }
                    break;
                case Module.Engine:
                    DrawEngineOption(ref count, GarageEngine.Economy);
                    DrawEngineOption(ref count, GarageEngine.Standard);
                    DrawEngineOption(ref count, GarageEngine.Sport);
                    break;
                case Module.RunningGear:
                    DrawRunningGearOption(ref count, GarageRunningGear.Standard);
                    DrawRunningGearOption(ref count, GarageRunningGear.Road);
                    DrawRunningGearOption(ref count, GarageRunningGear.OffRoad);
                    break;
            }
            if (count % ModuleOptionsPerRow != 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawTurretOption(ref int count, string optionId, string displayName)
        {
            bool selected = optionId == draftLoadout.TurretId;
            if (DrawModuleOption(ref count, TurretIcon(optionId), displayName, selected) && !selected)
            {
                draftLoadout.TurretId = optionId;
                BuildPreview(draftVehicle, draftLoadout);
            }
        }

        private void DrawEngineOption(ref int count, GarageEngine engine)
        {
            if (DrawModuleOption(ref count, "Engine/" + engine, EngineDescription(engine), engine == draftLoadout.Engine))
                draftLoadout.Engine = engine;
        }

        private void DrawRunningGearOption(ref int count, GarageRunningGear gear)
        {
            if (DrawModuleOption(ref count, RunningGearIcon(gear), RunningGearDescription(gear), gear == draftLoadout.RunningGear))
                draftLoadout.RunningGear = gear;
        }

        private bool DrawModuleOption(ref int count, string icon, string description, bool selected)
        {
            if (count % ModuleOptionsPerRow == 0) GUILayout.BeginHorizontal();
            Rect rect = GUILayoutUtility.GetRect(140f, 96f, moduleButton);
            bool clicked = DrawModuleCard(rect, icon, description, selected, selected);
            count++;
            if (count % ModuleOptionsPerRow == 0) GUILayout.EndHorizontal();
            return clicked;
        }

        private bool DrawModuleCard(Rect rect, string icon, string description, bool highlighted, bool checkmark)
        {
            int index = moduleIcons != null ? System.Array.IndexOf(ModuleIcons, icon) : -1;
            bool hovered = GUI.enabled && rect.Contains(Event.current.mousePosition);
            if (hovered) moduleHint = description;
            Color previous = GUI.backgroundColor;
            if (highlighted) GUI.backgroundColor = new Color(0.42f, 0.9f, 0.64f);
            bool clicked = GUI.Button(rect, index >= 0 ? GUIContent.none : new GUIContent(description), moduleButton);
            GUI.backgroundColor = previous;
            if (Event.current.type == EventType.Repaint && index >= 0)
            {
                const float iconAspect = 1.5f;
                float width = Mathf.Min(rect.width - 16f, (rect.height - 8f) * iconAspect);
                float height = width / iconAspect;
                Rect image = new Rect(rect.center.x - width / 2f, rect.center.y - height / 2f, width, height);
                int row = index / ModuleIconColumns;
                Rect uv = new Rect((index % ModuleIconColumns) / (float)ModuleIconColumns,
                    1f - (row + 1f) / ModuleIconRows,
                    1f / ModuleIconColumns, 1f / ModuleIconRows);
                GUI.DrawTextureWithTexCoords(image, moduleIcons, uv);
            }
            if (checkmark)
                GUI.Label(new Rect(rect.xMax - 28f, rect.yMax - 28f, 24f, 24f), "✓", moduleCheckmark);
            return clicked;
        }

        private static string WeaponName(GarageWeapon weapon) => weapon switch
        {
            GarageWeapon.Cannon => "Баллистическая пушка",
            GarageWeapon.HeavyCannon => "Тяжёлая фугасная пушка",
            GarageWeapon.Machinegun => "Пулемёт",
            GarageWeapon.GrenadeLauncher => "Гранатомёт",
            GarageWeapon.GuidedRocket => "ПТУР",
            GarageWeapon.UnguidedRocket => "НУРСы",
            _ => "Штатный комплект"
        };

        private static string EngineDescription(GarageEngine engine) => engine switch
        {
            GarageEngine.Economy => "Экономичный · меньше тяга и скорость",
            GarageEngine.Sport => "Форсированный · выше тяга и скорость",
            _ => "Штатный · обычная тяга"
        };

        private static string RunningGearDescription(GarageRunningGear gear) => gear switch
        {
            GarageRunningGear.Road => "Дорожные · лучше на твёрдом покрытии",
            GarageRunningGear.OffRoad => "Вездеходные · лучше на рыхлом грунте",
            _ => "Штатные · универсальные"
        };

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

        private void ApplyGarage() => GameFlow.ApplyVehicle(draftVehicle, draftDrive, draftLoadout);

        private void LeaveGarage()
        {
            if (Dirty) dialog = Dialog.GarageChanges;
            else CloseGarage();
        }

        private void CloseGarage()
        {
            page = Page.Main;
            dialog = Dialog.None;
            BuildPreview(GameFlow.CurrentVehicle, GameFlow.SelectedLoadout);
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
