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

        private void OnGUI()
        {
            if (GameFlow.Loading) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, wordWrap = true };
                title.normal.textColor = new Color(0.4f, 1f, 0.75f);
                text = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true };
                small = new GUIStyle(text) { fontSize = 15 };
                button = new GUIStyle(GUI.skin.button) { fontSize = 19, fixedHeight = 43 };
            }
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            if (input.Menu || input.Paused || rules.Phase == SessionPhase.Results)
            {
                GUI.color = new Color(0.025f, 0.06f, 0.1f, 0.96f);
                GUI.DrawTexture(new Rect(30, 30, 510, height - 60), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUILayout.BeginArea(new Rect(55, 48, 460, height - 90));
                GUILayout.Label(PlaygroundText.Title, title);
                GUILayout.Label(string.IsNullOrEmpty(locationName) ? PlaygroundText.Subtitle : locationName, small);
                GUILayout.Space(14);
                if (rules.Phase == SessionPhase.Results)
                {
                    GUILayout.Label(PlaygroundText.ModeName(rules.Mode), title);
                    GUILayout.Label(FormatResult(rules.Result), title);
                    GUILayout.Label(rules.ResultDetail, text);
                    GUILayout.Space(10);
                    GUILayout.Label("ЛОКАЛЬНЫЙ ТОП-5", small);
                    var records = LocalRecords.Read(rules.Mode, rules.TrackId);
                    for (int i = 0; i < records.Count; i++)
                        GUILayout.Label($"{i + 1}.   {FormatResult(records[i])}", small);
                    if (GUILayout.Button("ПОВТОРИТЬ", button)) input.Begin(rules.Mode);
                    if (GUILayout.Button("В ГАРАЖ", button)) input.OpenMenu();
                }
                else if (input.Paused)
                {
                    GUILayout.Label("ПАУЗА", title);
                    if (GUILayout.Button("ПРОДОЛЖИТЬ", button)) input.Resume();
                    if (GUILayout.Button("НАЧАТЬ ЗАНОВО", button)) input.Begin(rules.Mode);
                    if (GUILayout.Button("В ГАРАЖ", button)) input.OpenMenu();
                }
                else
                {
                    if (Application.CanStreamedLevelBeLoaded(GameFlow.FrontEndScene) && GUILayout.Button("ОБЩИЙ ГАРАЖ", button)) input.OpenMenu();
                    GUILayout.Label("Один безумный багги. Три испытания.", text);
                    if (vehicle.DriveMode != VehicleDrive.Configured)
                    {
                        GUILayout.Label("ПРИВОД", small);
                        int selectedDrive = GUILayout.Toolbar((int)vehicle.DriveMode - 1, DriveNames);
                        if (selectedDrive >= 0) vehicle.DriveMode = (VehicleDrive)(selectedDrive + 1);
                    }
                    GUILayout.Space(12);
                    if (GUILayout.Button(PlaygroundText.ModeName(ChallengeMode.Free), button)) input.Begin(ChallengeMode.Free);
                    if (GUILayout.Button(PlaygroundText.ModeName(ChallengeMode.Race), button)) input.Begin(ChallengeMode.Race);
                    if (GUILayout.Button(PlaygroundText.ModeName(ChallengeMode.Shooting), button)) input.Begin(ChallengeMode.Shooting);
                    if (GUILayout.Button(PlaygroundText.ModeName(ChallengeMode.Combat), button)) input.Begin(ChallengeMode.Combat);
                    GUILayout.Space(14);
                    GUILayout.Label("Гонка — проезжайте зелёные ворота по порядку. Восстановление добавляет 3 с.\nСтрелковый полигон — 60 с на красные цели. Движущиеся цели дают вдвое больше очков.\nБоевая трасса — каждая оставшаяся цель добавляет 8 с, а каждый промах — 0,25 с.", small);
                    GUILayout.Space(14);
                    GUILayout.Label("WASD — езда  •  Пробел — занос\nМышь — прицел  •  ЛКМ — огонь  •  1/2/3 — оружие\nC — смена вида  •  R — восстановление  •  Esc — пауза", text);
                }
                GUILayout.EndArea();
                return;
            }
            GUI.Box(new Rect(20, 20, 360, 138), "");
            GUI.Label(new Rect(35, 28, 340, 35), PlaygroundText.ModeName(rules.Mode), text);
            string progress = rules.Mode == ChallengeMode.Shooting
                ? $"Осталось {PlaygroundText.Seconds(rules.Remaining, "0.0")}   •   {FormatResult(rules.Score)}"
                : $"{PlaygroundText.Seconds(rules.Elapsed)}   •   Ворота {Mathf.Min(rules.NextCheckpoint + 1, rules.CheckpointCount)}/{rules.CheckpointCount}";
            GUI.Label(new Rect(35, 61, 340, 30), progress, text);
            GUI.Label(new Rect(35, 94, 340, 50), $"{vehicle.Speed:0} км/ч   •   {weapons.Current.Label}", text);
            GUI.Box(new Rect(20, height - 78, 355, 58), "");
            GUI.Label(new Rect(35, height - 71, 335, 25), weapons.Reload > 0f ? "ПЕРЕЗАРЯДКА" : "МОЖНО СТРЕЛЯТЬ", small);
            GUI.Label(new Rect(185, height - 71, 180, 25), input.AimMode ? "C — ПРИЦЕЛЬНЫЙ ВИД" : "C — ТРЕТЬЕ ЛИЦО", small);
            GUI.color = new Color(0.3f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(35, height - 40, 320 * (1f - weapons.Reload), 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(width - 375, height - 60, 355, 50), "1 — пулемёт  2 — пушка  3 — граната\nR — восстановление  •  Пробел — занос  •  Esc — пауза", small);
            GUI.color = Time.time - rules.LastHitTime < 0.2f ? Color.red : Color.white;
            GUI.DrawTexture(new Rect(width / 2 - 13, height / 2, 9, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(width / 2 + 5, height / 2, 9, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(width / 2, height / 2 - 13, 2, 9), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(width / 2, height / 2 + 5, 2, 9), Texture2D.whiteTexture);
            GUI.color = Color.white;
            // Ballistic impact marker: first intersection of sampled trajectory, not a hitscan shot.
            Vector3 position = weapons.MuzzlePosition;
            Vector3 velocity = weapons.AimDirection * weapons.Current.Speed + vehicle.Body.linearVelocity;
            for (int i = 0; i < 100; i++)
            {
                Vector3 next = position + velocity * 0.08f + (weapons.Current.Gravity ? Physics.gravity * 0.0032f : Vector3.zero);
                if (Physics.Linecast(position, next, out RaycastHit hit, 1, QueryTriggerInteraction.Ignore))
                {
                    Vector3 screen = Camera.main.WorldToScreenPoint(hit.point);
                    if (screen.z > 0f)
                    {
                        GUI.color = new Color(1f, 0.75f, 0.2f);
                        GUI.Label(new Rect(screen.x / scale - 10, (Screen.height - screen.y) / scale - 13, 30, 30), "○", text);
                        GUI.color = Color.white;
                    }
                    break;
                }
                position = next;
                if (weapons.Current.Gravity) velocity += Physics.gravity * 0.08f;
            }
            if (rules.Phase == SessionPhase.Countdown)
                GUI.Label(new Rect(width / 2 - 125, height / 2 - 140, 250, 60), "ДО СТАРТА " + Mathf.CeilToInt(rules.Countdown), title);
        }

        private string FormatResult(float result)
        {
            return rules.Mode == ChallengeMode.Shooting ? $"{result:0} очк." : PlaygroundText.Seconds(result);
        }
    }
}
