using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    internal sealed class CombatHud
    {
        private static readonly Color Panel = new Color(0.04f, 0.09f, 0.12f, 0.94f);
        private static readonly Color Accent = new Color(0.42f, 0.96f, 0.77f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.43f);
        private static readonly Color Outline = new Color(0.02f, 0.04f, 0.05f, 0.9f);
        private GUIStyle text;
        private GUIStyle small;
        private GUIStyle title;
        private GUIStyle centered;
        private string mapScene;
        private Texture2D map;
        private Rect mapBounds;

        internal struct Frame
        {
            public Camera Camera;
            public Transform Hull;
            public bool Aiming;
            public bool Hit;
            public float Speed;
            public int Health;
            public int MaxHealth;
            public string Weapon;
            public ProjectileDefinition Ammunition;
            public float Reload;
            public string WeaponStatus;
            public string VehicleStatus;
            public Vector3 Muzzle;
            public Vector3 ShotDirection;
            public Vector3 Velocity;
        }

        internal void Draw(Frame frame, Action<float, float> drawMode, Action<float, float> drawWeaponOverlay = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            PrepareStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.color = Color.white;
            try
            {
                drawMode?.Invoke(width, height);
                DrawVehicle(new Rect(20f, height - 100f, 184f, 80f), frame);
                DrawWeapon(new Rect(216f, height - 100f, 264f, 80f), frame);
                if (GameSettings.ShowMinimap) DrawMinimap(new Rect(width - 204f, height - 216f, 184f, 196f), frame.Hull);
                DrawSight(new Vector2(width * 0.5f, height * 0.5f), scale, frame);
                drawWeaponOverlay?.Invoke(width, height);
            }
            finally
            {
                GUI.matrix = previous;
                GUI.color = previousColor;
            }
        }

        private void PrepareStyles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            small = new GUIStyle(text) { fontSize = 14 };
            title = new GUIStyle(text) { fontSize = 20, fontStyle = FontStyle.Bold };
            centered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        }

        internal void DrawModePanel(string heading, int rows)
        {
            Fill(new Rect(20f, 20f, 416f, 41f + rows * 23f), Panel);
            GUI.Label(new Rect(32f, 25f, 392f, 29f), heading, title);
        }

        internal void DrawModeLine(int row, string value)
        {
            GUI.Label(new Rect(32f, 56f + row * 23f, 392f, 23f), value, small);
        }

        private void DrawVehicle(Rect bounds, Frame frame)
        {
            Fill(bounds, Panel);
            GUI.Label(new Rect(bounds.x + 12f, bounds.y + 4f, 160f, 30f), $"{frame.Speed:0} км/ч", title);
            Rect status = new Rect(bounds.x + 12f, bounds.y + 36f, 160f, 26f);
            if (frame.MaxHealth > 0)
            {
                GUI.Label(status, $"ХП  {frame.Health} / {frame.MaxHealth}", small);
                float fraction = (float)frame.Health / frame.MaxHealth;
                Color health = Color.Lerp(new Color(0.95f, 0.32f, 0.3f), Accent, fraction);
                Meter(new Rect(status.x, bounds.yMax - 14f, status.width, 5f), fraction, health);
            }
            else if (!string.IsNullOrEmpty(frame.VehicleStatus)) GUI.Label(status, frame.VehicleStatus, small);
        }

        private void DrawWeapon(Rect bounds, Frame frame)
        {
            Fill(bounds, Panel);
            GUI.Label(new Rect(bounds.x + 12f, bounds.y + 3f, bounds.width - 24f, 25f), frame.Weapon, text);
            string ammunition = frame.Ammunition.Label == frame.Weapon ? "" : frame.Ammunition.Label;
            if (!string.IsNullOrEmpty(ammunition))
                GUI.Label(new Rect(bounds.x + 12f, bounds.y + 25f, bounds.width - 24f, 20f), ammunition, small);
            string status = frame.WeaponStatus ?? (frame.Reload > 0f ? "Перезарядка" : "Готово к выстрелу");
            GUI.Label(new Rect(bounds.x + 12f, bounds.y + 44f, bounds.width - 24f, 22f), status, small);
            Meter(new Rect(bounds.x + 12f, bounds.yMax - 10f, bounds.width - 24f, 4f),
                1f - frame.Reload, frame.Reload > 0f ? Gold : Accent);
        }

        private void DrawSight(Vector2 center, float scale, Frame frame)
        {
            Color reticle = frame.Hit ? Gold : Color.white;
            float length = frame.Aiming ? 37f : 14f;
            Stroke(center + Vector2.left * length, center + Vector2.left * 6f, reticle);
            Stroke(center + Vector2.right * 6f, center + Vector2.right * length, reticle);
            Stroke(center + Vector2.up * 6f, center + Vector2.up * 14f, reticle);
            Stroke(center + Vector2.down * 6f, center + Vector2.down * 14f, reticle);
            if (!frame.Aiming || frame.Camera == null) return;

            Vector2 hullCenter = center + new Vector2(-140f, 45f);
            Fill(new Rect(hullCenter.x - 33f, hullCenter.y - 35f, 66f, 66f), Panel);
            Stroke(hullCenter + Vector2.up * 25f, hullCenter + Vector2.up * 18f, Color.white);
            float relativeYaw = Mathf.DeltaAngle(frame.Camera.transform.eulerAngles.y, frame.Hull.eulerAngles.y);
            DrawHull(hullCenter, relativeYaw, 15f);
            GUI.Label(new Rect(hullCenter.x - 38f, hullCenter.y + 31f, 76f, 24f), "Корпус", centered);
            if (GameSettings.ShowBallistics && frame.Ammunition.IsShell) DrawBallistics(center, scale, frame);
        }

        private void DrawBallistics(Vector2 center, float scale, Frame frame)
        {
            Vector3 velocity = frame.Ammunition.LaunchVelocity(frame.ShotDirection, frame.Velocity);
            Vector2 lastMark = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            foreach (int distance in BallisticSight.Distances)
            {
                if (!BallisticSight.TryGetMark(frame.Camera, frame.Muzzle, velocity, frame.Ammunition, distance, out Vector3 mark))
                    continue;
                Vector2 point = new Vector2(mark.x, Screen.height - mark.y) / scale;
                if (point.x < 100f || point.x > 2f * center.x - 120f ||
                    point.y < 162f || point.y > 2f * center.y - 135f || (point - lastMark).sqrMagnitude < 18f * 18f)
                    continue;
                bool closeToReticle = (point - center).sqrMagnitude < 24f * 24f;
                if (!closeToReticle) Stroke(point + Vector2.left * 13f, point + Vector2.right * 13f, Gold);
                float offset = closeToReticle ? 44f : 17f;
                Rect label = new Rect(point.x + offset, point.y - 11f, 62f, 23f);
                Fill(label, Outline);
                GUI.Label(label, $"{distance} м", centered);
                lastMark = point;
            }
            Rect speed = new Rect(center.x + 104f, center.y + 14f, 156f, 26f);
            Fill(speed, Panel);
            GUI.Label(speed, $"Нач. скорость  {frame.Ammunition.Speed:0} м/с", centered);
        }

        private void DrawMinimap(Rect bounds, Transform hull)
        {
            Scene scene = hull.gameObject.scene;
            if (mapScene != scene.name)
            {
                mapScene = scene.name;
                mapBounds = mapScene switch
                {
                    "Location_AsphaltTrack" => new Rect(-180f, -180f, 360f, 360f),
                    "Location_DirtTrack" => new Rect(-210f, -210f, 420f, 420f),
                    "Location_ShootingRange" => new Rect(-552f, -70f, 1104f, 1104f),
                    "Map_01_ThreeRoads" => new Rect(-325f, -325f, 650f, 650f),
                    _ => default
                };
                map = mapBounds.width > 0f ? Resources.Load<Texture2D>("HudMaps/" + mapScene) : null;
            }
            if (map == null) return;

            Rect picture = new Rect(bounds.x + 5f, bounds.y + 5f, bounds.width - 10f, bounds.width - 10f);
            Fill(bounds, Panel);
            GUI.DrawTexture(picture, map);
            Vector3 position = hull.position;
            float x = Mathf.Clamp(Mathf.InverseLerp(mapBounds.xMin, mapBounds.xMax, position.x), 0.055f, 0.945f);
            float y = Mathf.Clamp(Mathf.InverseLerp(mapBounds.yMin, mapBounds.yMax, position.z), 0.055f, 0.945f);
            DrawHull(new Vector2(picture.x + x * picture.width, picture.yMax - y * picture.height), hull.eulerAngles.y, 8f);
            Rect north = new Rect(picture.x + 4f, picture.y + 4f, 23f, 23f);
            Fill(north, Outline);
            GUI.Label(north, "С", centered);
            GUI.Label(new Rect(bounds.xMax - 76f, picture.yMax, 70f, 20f), $"{mapBounds.width:0} м", centered);
        }

        private static void DrawHull(Vector2 center, float yaw, float size)
        {
            Vector2 ahead = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), -Mathf.Cos(yaw * Mathf.Deg2Rad));
            Vector2 right = new Vector2(-ahead.y, ahead.x);
            Vector2 front = center + ahead * size;
            Vector2 back = center - ahead * size * 0.55f;
            Vector2 left = back - right * size * 0.65f;
            right = back + right * size * 0.65f;
            Stroke(left, front, Accent, 2.5f);
            Stroke(front, right, Accent, 2.5f);
            Stroke(right, left, Accent, 2.5f);
        }

        private static void Meter(Rect area, float fraction, Color color)
        {
            Fill(area, Outline);
            area.width *= Mathf.Clamp01(fraction);
            Fill(area, color);
        }

        private static void Stroke(Vector2 from, Vector2 to, Color color, float thickness = 2f)
        {
            Matrix4x4 previous = GUI.matrix;
            Vector2 delta = to - from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            Fill(new Rect(from.x - 1f, from.y - thickness * 0.5f - 1f, delta.magnitude + 2f, thickness + 2f), Outline);
            Fill(new Rect(from.x, from.y - thickness * 0.5f, delta.magnitude, thickness), color);
            GUI.matrix = previous;
        }

        private static void Fill(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
