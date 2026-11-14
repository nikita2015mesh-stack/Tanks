using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    public static class GameSettings
    {
#if !UNITY_SERVER || UNITY_EDITOR
        private const int FrontEndFrameRate = 30;
        private const int GameplayFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeFrameRate()
        {
            if (Application.isBatchMode) return;

            SceneManager.sceneLoaded -= ApplySceneFrameRate;
            SceneManager.sceneLoaded += ApplySceneFrameRate;
            Application.targetFrameRate = GameplayFrameRate;
        }

        private static void ApplySceneFrameRate(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;

            Application.targetFrameRate = scene.name == GameFlow.FrontEndScene ? FrontEndFrameRate : GameplayFrameRate;
        }
#endif

        public static float MouseSensitivity => Mathf.Clamp(PlayerPrefs.GetFloat("MadMachines.Settings.Mouse.v1", 1f), 0.25f, 2.5f);
        public static bool InvertMouse => PlayerPrefs.GetInt("MadMachines.Settings.InvertMouse.v1", 0) != 0;
        internal static bool ShowMinimap { get; private set; } = true;
        internal static bool ShowBallistics { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadHudSettings()
        {
            ShowMinimap = PlayerPrefs.GetInt("MadMachines.Settings.Minimap.v1", 1) != 0;
            ShowBallistics = PlayerPrefs.GetInt("MadMachines.Settings.BallisticMarks.v1", 1) != 0;
        }

        public static Vector2 AdjustLook(Vector2 delta)
        {
            return new Vector2(delta.x, InvertMouse ? -delta.y : delta.y) * MouseSensitivity;
        }

        public static void Save(float sensitivity, bool invert)
        {
            PlayerPrefs.SetFloat("MadMachines.Settings.Mouse.v1", Mathf.Clamp(sensitivity, 0.25f, 2.5f));
            PlayerPrefs.SetInt("MadMachines.Settings.InvertMouse.v1", invert ? 1 : 0);
            PlayerPrefs.Save();
        }

        internal static void SaveHudSettings(bool minimap, bool ballistics)
        {
            ShowMinimap = minimap;
            ShowBallistics = ballistics;
            PlayerPrefs.SetInt("MadMachines.Settings.Minimap.v1", minimap ? 1 : 0);
            PlayerPrefs.SetInt("MadMachines.Settings.BallisticMarks.v1", ballistics ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Shared by the main screen, local pause and network menu.
    public sealed class GameSettingsPanel
    {
        public bool IsOpen { get; private set; }
        private float sensitivity;
        private bool invert;
        private bool minimap;
        private bool ballistics;

        public void Open()
        {
            sensitivity = GameSettings.MouseSensitivity;
            invert = GameSettings.InvertMouse;
            minimap = GameSettings.ShowMinimap;
            ballistics = GameSettings.ShowBallistics;
            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        public void Draw()
        {
            GUILayout.Label("УПРАВЛЕНИЕ И HUD");
            GUILayout.Label($"Чувствительность мыши: {sensitivity:0.00}×");
            sensitivity = GUILayout.HorizontalSlider(sensitivity, 0.25f, 2.5f);
            invert = GUILayout.Toggle(invert, "Инверсия мыши по вертикали");
            minimap = GUILayout.Toggle(minimap, "Миникарта");
            ballistics = GUILayout.Toggle(ballistics, "Баллистическая разметка и скорость снаряда");
            GUILayout.Space(12);
            GUILayout.Label("WASD — движение · Пробел — ручник\nМышь — наведение · ЛКМ — огонь\n1/2/3 — оружие (одиночная игра)\nC — вид камеры\nR — восстановление (в Deathmatch почти с места)\nEsc — игровое меню");
            if (GUILayout.Button("ПРИМЕНИТЬ", GUILayout.Height(40)))
            {
                GameSettings.Save(sensitivity, invert);
                GameSettings.SaveHudSettings(minimap, ballistics);
                Close();
            }
            if (GUILayout.Button("ПО УМОЛЧАНИЮ", GUILayout.Height(40)))
            {
                sensitivity = 1;
                invert = false;
                minimap = ballistics = true;
            }
            if (GUILayout.Button("ОТМЕНА", GUILayout.Height(40))) Close();
        }
    }
}
