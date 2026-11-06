using UnityEngine;

namespace MadMachines
{
    public sealed class PlaygroundHud : MonoBehaviour
    {
        [SerializeField] private LocalPlayerInput input;
        [SerializeField] private VehiclePhysics vehicle;
        [SerializeField] private WeaponSystem weapons;
        [SerializeField] private ChallengeManager rules;
        private GUIStyle title;
        private GUIStyle text;
        private GUIStyle button;
        private GUIStyle small;
        public void Configure(LocalPlayerInput local, VehiclePhysics car, WeaponSystem gun, ChallengeManager session)
        {
            input = local; vehicle = car; weapons = gun; rules = session;
        }

        private void OnGUI()
        {
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
                GUILayout.Label("MAD MACHINES", title);
                GUILayout.Label("P H Y S I C S   P L A Y G R O U N D", small);
                GUILayout.Space(14);
                if (rules.Phase == SessionPhase.Results)
                {
                    GUILayout.Label($"{rules.Mode} complete", title);
                    GUILayout.Label(rules.Mode == ChallengeMode.Shooting ? $"{rules.Result:0} POINTS" : $"{rules.Result:0.00} SECONDS", title);
                    GUILayout.Label(rules.ResultDetail, text);
                    GUILayout.Space(10);
                    GUILayout.Label("LOCAL TOP 5", small);
                    var records = LocalRecords.Read(rules.Mode);
                    for (int i = 0; i < records.Count; i++) GUILayout.Label($"{i + 1}.   {records[i]:0.00}" + (rules.Mode == ChallengeMode.Shooting ? " pts" : " s"), small);
                    if (GUILayout.Button("RETRY", button)) input.Begin(rules.Mode);
                    if (GUILayout.Button("BACK TO GARAGE", button)) input.OpenMenu();
                }
                else if (input.Paused)
                {
                    GUILayout.Label("PAUSED", title);
                    if (GUILayout.Button("RESUME", button)) input.Resume();
                    if (GUILayout.Button("RESTART", button)) input.Begin(rules.Mode);
                    if (GUILayout.Button("BACK TO GARAGE", button)) input.OpenMenu();
                }
                else
                {
                    GUILayout.Label("One ridiculous buggy. Three ways to prove yourself.", text);
                    GUILayout.Space(12);
                    if (GUILayout.Button("FREE DRIVE  /  explore & experiment", button)) input.Begin(ChallengeMode.Free);
                    if (GUILayout.Button("RACE  /  beat the clock", button)) input.Begin(ChallengeMode.Race);
                    if (GUILayout.Button("TARGET RUSH  /  60 seconds", button)) input.Begin(ChallengeMode.Shooting);
                    if (GUILayout.Button("COMBAT COURSE  /  drive + destroy", button)) input.Begin(ChallengeMode.Combat);
                    GUILayout.Space(14);
                    GUILayout.Label("RACE: pass green gates in order. R adds 3s.\nTARGET RUSH: destroy red targets; moving targets score double.\nCOMBAT: every surviving target adds 8s; missed shots add 0.25s.", small);
                    GUILayout.Space(14);
                    GUILayout.Label("WASD drive  •  SPACE drift\nMouse aim  •  LMB fire  •  1 / 2 / 3 weapon\nR recover  •  ESC pause", text);
                }
                GUILayout.EndArea();
                return;
            }
            GUI.Box(new Rect(20, 20, 360, 138), "");
            GUI.Label(new Rect(35, 28, 340, 35), rules.Mode == ChallengeMode.Free ? "FREE DRIVE" : rules.Mode.ToString().ToUpperInvariant(), text);
            GUI.Label(new Rect(35, 61, 340, 30), rules.Mode == ChallengeMode.Shooting ? $"{rules.Remaining:0.0}s left   •   {rules.Score} pts" : $"{rules.Elapsed:0.00}s   •   Gate {Mathf.Min(rules.NextCheckpoint + 1, rules.CheckpointCount)}/{rules.CheckpointCount}", text);
            GUI.Label(new Rect(35, 94, 340, 50), $"{vehicle.Speed:0} km/h   •   {weapons.Current.Label}", text);
            GUI.Box(new Rect(20, height - 78, 355, 58), "");
            GUI.Label(new Rect(35, height - 71, 335, 25), weapons.Reload > 0f ? "RELOADING" : "READY TO FIRE", small);
            GUI.color = new Color(0.3f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(35, height - 40, 320 * (1f - weapons.Reload), 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(width - 375, height - 60, 355, 50), "1 Machine gun  2 Cannon  3 Wrecker\nR recover  •  SPACE drift  •  ESC menu", small);
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
                GUI.Label(new Rect(width / 2 - 80, height / 2 - 140, 230, 60), "READY " + Mathf.CeilToInt(rules.Countdown), title);
        }
    }
}
