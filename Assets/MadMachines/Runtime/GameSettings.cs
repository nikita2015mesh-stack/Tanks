using UnityEngine;

namespace MadMachines
{
    public static class GameSettings
    {
        public static float MouseSensitivity => Mathf.Clamp(PlayerPrefs.GetFloat("MadMachines.Settings.Mouse.v1", 1f), 0.25f, 2.5f);
        public static bool InvertMouse => PlayerPrefs.GetInt("MadMachines.Settings.InvertMouse.v1", 0) != 0;

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
    }

    // Shared by the main screen, local pause and network menu.
    public sealed class GameSettingsPanel
    {
        public bool IsOpen { get; private set; }
        private float sensitivity;
        private bool invert;

        public void Open()
        {
            sensitivity = GameSettings.MouseSensitivity;
            invert = GameSettings.InvertMouse;
            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        public void Draw()
        {
            GUILayout.Label("НАСТРОЙКИ УПРАВЛЕНИЯ");
            GUILayout.Label($"Чувствительность мыши: {sensitivity:0.00}×");
            sensitivity = GUILayout.HorizontalSlider(sensitivity, 0.25f, 2.5f);
            invert = GUILayout.Toggle(invert, "Инверсия мыши по вертикали");
            GUILayout.Space(12);
            GUILayout.Label("WASD — движение · Пробел — ручник\nМышь — наведение · ЛКМ — огонь\n1/2/3 — оружие (одиночная игра)\nC — вид камеры (одиночная игра)\nR — восстановление (одиночная игра)\nEsc — игровое меню");
            if (GUILayout.Button("ПРИМЕНИТЬ", GUILayout.Height(40)))
            {
                GameSettings.Save(sensitivity, invert);
                Close();
            }
            if (GUILayout.Button("ПО УМОЛЧАНИЮ", GUILayout.Height(40)))
            {
                sensitivity = 1;
                invert = false;
            }
            if (GUILayout.Button("ОТМЕНА", GUILayout.Height(40))) Close();
        }
    }
}
