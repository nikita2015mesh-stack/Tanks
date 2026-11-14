using UnityEngine;

namespace MadMachines
{
    public sealed class PlaygroundHud : MonoBehaviour
    {
        private static readonly string[] DriveNames = { "Полный", "Передний", "Задний" };

        [SerializeField] private LocalPlayerInput input;
        [SerializeField] private VehiclePhysics vehicle;
        [SerializeField] private WeaponSystem weapons;
        [SerializeField] private ChallengeManager rules;
        [SerializeField] private string locationName;
        private GUIStyle title;
        private GUIStyle text;
        private GUIStyle button;
        private GUIStyle small;
        private Texture2D rocketFuelMask;
        private Texture2D unguidedRocketIcon;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly CombatHud combatHud = new CombatHud();
        private bool confirmExit;

        private void Awake()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (TextMesh label in GetComponentsInChildren<TextMesh>())
            {
                label.font = font;
                label.GetComponent<Renderer>().sharedMaterial = font.material;
            }
        }

        public void Configure(LocalPlayerInput local, VehiclePhysics car, WeaponSystem gun, ChallengeManager session)
        {
            input = local; vehicle = car; weapons = gun; rules = session;
        }

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null ||
                !UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) return;
            // Input reads the flag from the preceding frame, regardless of Update order.
            if (settings.IsOpen) settings.Close();
            else if (confirmExit) confirmExit = false;
        }

        private void OnDestroy()
        {
            if (rocketFuelMask != null) Destroy(rocketFuelMask);
            if (unguidedRocketIcon != null) Destroy(unguidedRocketIcon);
        }

        private void OnGUI()
        {
            input.MenuOverlayOpen = settings.IsOpen || confirmExit;
            if (GameFlow.Loading) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, wordWrap = true };
                title.normal.textColor = new Color(0.4f, 1f, 0.75f);
                text = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true };
                small = new GUIStyle(text) { fontSize = 15 };
                button = new GUIStyle(GUI.skin.button) { fontSize = 19, fixedHeight = 43 };
            }
            if (input.Menu || input.Paused || rules.Phase == SessionPhase.Results)
            {
                float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
                float height = Screen.height / scale;
                Matrix4x4 previous = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                GUI.color = new Color(0.025f, 0.06f, 0.1f, 0.96f);
                GUI.DrawTexture(new Rect(30, 30, 510, height - 60), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUILayout.BeginArea(new Rect(55, 48, 460, height - 90));
                GUILayout.Label(PlaygroundText.Title, title);
                GUILayout.Label(string.IsNullOrEmpty(locationName) ? PlaygroundText.Subtitle : locationName, small);
                GUILayout.Space(14);
                bool hasMain = Application.CanStreamedLevelBeLoaded(GameFlow.FrontEndScene);
                string returnLabel = !hasMain ? "В МЕНЮ" : GameFlow.GarageTestActive ? "В ГАРАЖ" : "НА ГЛАВНЫЙ";
                if (settings.IsOpen) settings.Draw();
                else if (confirmExit)
                {
                    GUILayout.Label("Завершить заезд? Текущая попытка не будет записана.", text);
                    if (GUILayout.Button("ЗАВЕРШИТЬ И ВЕРНУТЬСЯ", button)) input.OpenMenu();
                    if (GUILayout.Button("ОСТАТЬСЯ", button)) confirmExit = false;
                }
                else if (rules.Phase == SessionPhase.Results)
                {
                    GUILayout.Label(PlaygroundText.ModeName(rules.Mode), title);
                    GUILayout.Label(FormatResult(rules.Result), title);
                    GUILayout.Label(rules.ResultDetail, text);
                    var records = LocalRecords.Read(rules.Mode, rules.TrackId);
                    if (records.Count > 0) GUILayout.Label("Лучший результат: " + FormatResult(records[0]), text);
                    if (GUILayout.Button(returnLabel, button)) input.OpenMenu();
                    if (!hasMain && GUILayout.Button("ПОВТОРИТЬ", button)) input.Begin(rules.Mode);
                }
                else if (input.Paused)
                {
                    GUILayout.Label("ПАУЗА", title);
                    if (GUILayout.Button("ПРОДОЛЖИТЬ", button)) input.Resume();
                    if (GUILayout.Button("НАСТРОЙКИ", button)) settings.Open();
                    if (GUILayout.Button(returnLabel, button))
                    {
                        if (rules.Mode != ChallengeMode.Free) confirmExit = true;
                        else input.OpenMenu();
                    }
                    if (!hasMain && GUILayout.Button("НАЧАТЬ ЗАНОВО", button)) input.Begin(rules.Mode);
                }
                else if (hasMain)
                {
                    GUILayout.Label("Пробный заезд можно запустить из гаража.", text);
                    if (GUILayout.Button("НА ГЛАВНЫЙ", button)) input.OpenMenu();
                }
                else
                {
                    // Isolated legacy/debug builds without FrontEnd keep their existing launch path.
                    if (vehicle.DriveMode != VehicleDrive.Configured)
                    {
                        int selectedDrive = GUILayout.Toolbar((int)vehicle.DriveMode - 1, DriveNames);
                        if (selectedDrive >= 0) vehicle.DriveMode = (VehicleDrive)(selectedDrive + 1);
                    }
                    foreach (ChallengeMode choice in new[] { ChallengeMode.Free, ChallengeMode.Race, ChallengeMode.Shooting, ChallengeMode.Combat })
                        if (GUILayout.Button(PlaygroundText.ModeName(choice), button)) input.Begin(choice);
                }
                input.MenuOverlayOpen = settings.IsOpen || confirmExit;
                GUILayout.EndArea();
                GUI.matrix = previous;
                return;
            }
            if (Event.current.type != EventType.Repaint) return;
            CombatHud.Frame frame = new CombatHud.Frame
            {
                Camera = input.SightCamera,
                Hull = vehicle.transform,
                Aiming = input.AimMode,
                Hit = Time.time - rules.LastHitTime < 0.2f,
                Speed = vehicle.Speed,
                Weapon = weapons.Current.Label,
                Ammunition = weapons.CurrentProjectile,
                Reload = weapons.Reload,
                WeaponStatus = weapons.GuidedRocketSelected && weapons.RocketInFlight ? "Ракета в полёте" : null,
                VehicleStatus = vehicle.ParkingBrake ? (vehicle.IsHeld ? "Стояночный тормоз" : "Торможение") :
                    vehicle.IsHeld ? "Автоудержание" : null,
                Muzzle = weapons.MuzzlePosition,
                ShotDirection = weapons.AimDirection,
                Velocity = vehicle.Body.linearVelocity,
                ShowRangeMarks = input.Range != null,
                SightUnreachable = input.RangeSolutionUnavailable,
                ZeroDistance = input.AimMode && weapons.CurrentProjectile.IsShell ? input.Range?.ZeroDistance : null,
                FlightTime = input.RangeFlightTime,
                RangefinderNotice = input.RangefinderNotice
            };
            combatHud.Draw(frame, DrawMode, DrawWeaponOverlay);
        }

        private void DrawMode(float width, float height)
        {
            combatHud.DrawModePanel(string.IsNullOrEmpty(locationName) ? PlaygroundText.ModeName(rules.Mode) : locationName,
                rules.Mode == ChallengeMode.Free ? 2 : 3);
            int row = 0;
            if (rules.Mode != ChallengeMode.Free)
            {
                string progress = rules.Mode == ChallengeMode.Shooting
                    ? $"Осталось {PlaygroundText.Seconds(rules.Remaining, "0.0")}   •   {FormatResult(rules.Score)}"
                    : $"{PlaygroundText.Seconds(rules.Elapsed)}   •   Ворота {Mathf.Min(rules.NextCheckpoint + 1, rules.CheckpointCount)}/{rules.CheckpointCount}";
                combatHud.DrawModeLine(row++, progress);
            }
            string weaponKeys = weapons.HasGuidedRocket
                ? "1 — пулемёт  2 — пушка  3 — гранатомёт  4 — ПТУР"
                : "1 — пулемёт  2 — пушка  3 — гранатомёт";
            if (weapons.HasUnguidedRocket) weaponKeys += "  5 — НУРС";
            if (!weapons.HasMultipleWeapons) weaponKeys = "ЛКМ — выстрел";
            combatHud.DrawModeLine(row++, weaponKeys);
            string ammunitionKey = weapons.Current.AmmunitionCount > 1 ? "  ·  B — боеприпас" : "";
            combatHud.DrawModeLine(row, "C — вид  ·  R — восстановление  ·  P — стоянка" + ammunitionKey);
            if (input.Range != null) DrawRange(input.Range, input.SightCamera, width, height);
            if (rules.Phase == SessionPhase.Countdown)
                GUI.Label(new Rect(width / 2 - 125, height / 2 - 140, 250, 60), "ДО СТАРТА " + Mathf.CeilToInt(rules.Countdown), title);
        }

        private void DrawWeaponOverlay(float width, float height)
        {
            if (weapons.GuidedRocketSelected)
            {
                DrawRocketFuel(width * 0.5f, height * 0.5f);
                if (weapons.IsGuidingRocket)
                {
                    GUI.color = new Color(1f, 0.75f, 0.2f);
                    GUI.Label(new Rect(width * 0.5f - 38f, height * 0.5f + 17f, 100f, 25f), "Наведение", small);
                    GUI.color = Color.white;
                }
            }
            else if (weapons.UnguidedRocketSelected) DrawUnguidedRocketAmmo(width * 0.5f, height * 0.5f);
        }

        private void DrawUnguidedRocketAmmo(float centerX, float centerY)
        {
            int loaded = weapons.UnguidedRocketsLoaded;
            if (loaded == 0 || Event.current.type != EventType.Repaint) return;
            if (unguidedRocketIcon == null) unguidedRocketIcon = CreateUnguidedRocketIcon();

            GUI.color = Color.white;
            for (int i = 0; i < loaded; i++)
                GUI.DrawTexture(new Rect(centerX - 48f + i * 21f, centerY + 24f, 18f, 31f), unguidedRocketIcon);
        }

        private static Texture2D CreateUnguidedRocketIcon()
        {
            const int width = 48;
            const int height = 84;
            Color32[] pixels = new Color32[width * height];
            Color outline = new Color(0.07f, 0.09f, 0.11f);
            Color metal = new Color(0.63f, 0.73f, 0.8f);
            Color amber = new Color(1f, 0.76f, 0.34f);
            Color fins = new Color(0.36f, 0.48f, 0.56f);
            for (int y = 0; y < height; y++)
            {
                if (y < 4 || y > 79) continue;
                // Pixel rows start at the exhaust. The fins spread outward toward the bottom.
                float halfWidth = y < 23 ? Mathf.Lerp(21f, 11f, Mathf.InverseLerp(8f, 23f, y))
                    : y > 59 ? Mathf.Lerp(11f, 0.5f, Mathf.InverseLerp(59f, 80f, y)) : 11f;
                for (int x = 0; x < width; x++)
                {
                    float offset = x + 0.5f - width * 0.5f;
                    float edge = halfWidth - Mathf.Abs(offset);
                    if (edge < -0.5f) continue;
                    Color surface = y >= 57 || (y >= 47 && y <= 51) ? amber
                        : y < 23 && Mathf.Abs(offset) > 9f ? fins : metal;
                    float bevel = Mathf.Lerp(1.18f, 0.65f, Mathf.InverseLerp(-8f, 9f, offset));
                    Color pixel = Color.Lerp(outline, surface * bevel, Mathf.Clamp01(edge - 1f));
                    pixel.a = Mathf.Clamp01(edge + 0.5f);
                    pixels[y * width + x] = pixel;
                }
            }

            Texture2D icon = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Unguided rocket ammo icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            icon.SetPixels32(pixels);
            icon.Apply(false, true);
            return icon;
        }

        private void DrawRocketFuel(float centerX, float centerY)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (rocketFuelMask == null) rocketFuelMask = CreateRocketFuelMask();

            Rect arc = new Rect(centerX - 72f, centerY - 24f, 54f, 70f);
            GUI.color = new Color(0.14f, 0.18f, 0.2f, 0.94f);
            GUI.DrawTexture(arc, rocketFuelMask);

            float fuel = weapons.RocketFuel;
            if (fuel > 0f)
            {
                Color red = new Color(1f, 0.24f, 0.2f);
                Color yellow = new Color(1f, 0.8f, 0.16f);
                Color green = new Color(0.35f, 1f, 0.46f);
                GUI.color = fuel < 0.5f ? Color.Lerp(red, yellow, fuel * 2f) : Color.Lerp(yellow, green, fuel * 2f - 1f);
                Rect fill = new Rect(arc.x, arc.yMax - arc.height * fuel, arc.width, arc.height * fuel);
                GUI.DrawTextureWithTexCoords(fill, rocketFuelMask, new Rect(0f, 0f, 1f, fuel));
            }
            GUI.color = Color.white;
        }

        private static Texture2D CreateRocketFuelMask()
        {
            const int width = 108;
            const int height = 140;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                // Texture rows run from bottom to top. The arc bends toward the reticle at the bottom.
                float down = 1f - (y + 0.5f) / height;
                float center = (0.14f + 0.72f * down * down) * width;
                float halfWidth = Mathf.Lerp(0.105f, 0.13f, down) * width;
                for (int x = 0; x < width; x++)
                {
                    float coverage = Mathf.Clamp01(halfWidth + 0.5f - Mathf.Abs(x + 0.5f - center));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Rocket fuel arc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            mask.SetPixels32(pixels);
            mask.Apply(false, true);
            return mask;
        }

        private void DrawRange(ShootingRange range, Camera view, float width, float height)
        {
            if (view == null || Event.current.type != EventType.Repaint) return;
            if (range.RecoveredAtBoundary)
            {
                GUI.Box(new Rect(width * 0.5f - 144f, 28f, 288f, 34f), "");
                GUI.Label(new Rect(width * 0.5f - 132f, 33f, 272f, 25f), "Возврат на рубеж с края полигона", small);
            }
            if (range.LastImpact != null)
            {
                GUI.Box(new Rect(width - 344, 20, 324, 74), "");
                GUI.Label(new Rect(width - 330, 26, 300, 64), range.LastImpact, small);
            }
            ArmorPlate plate = range.ViewedPlate(view);
            if (plate != null)
            {
                GUI.Box(new Rect(width - 344, 104, 324, 58), "");
                GUI.Label(new Rect(width - 330, 110, 300, 47),
                    $"Стенд {plate.Distance} м · {plate.Thickness} ед. · {plate.Tilt}°\nT — толщина   ·   Y — наклон", small);
            }
            if (rules.Mode == ChallengeMode.Free)
                GUI.Label(new Rect(width - 344, 166f, 324, 26), "F — сброс мишеней", small);
        }

        private string FormatResult(float result)
        {
            return rules.Mode == ChallengeMode.Shooting ? $"{result:0} очк." : PlaygroundText.Seconds(result);
        }
    }
}
