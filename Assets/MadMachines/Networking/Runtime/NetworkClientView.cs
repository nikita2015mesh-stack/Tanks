using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadMachines.Networking
{
    // Created exclusively by a client session. No input, camera or IMGUI component exists on the server.
    public sealed class NetworkClientView : MonoBehaviour
    {
        private const double SendInterval = 1.0 / 30.0;
        private NetworkSandboxSession session;
        private NetworkVehicle localVehicle;
        private Camera view;
        private string address;
        private string port;
        private float yaw;
        private float pitch = 12f;
        private double nextCommand;
        private double fireUntil;
        private uint sequence;
        private bool hasFocus = true;

        public void Configure(NetworkSandboxSession value)
        {
            session = value;
            address = value.Address;
            port = value.Port.ToString();
        }

        private void Start()
        {
            GameObject cameraObject = new GameObject("Network client camera");
            view = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 350f;
            view.fieldOfView = 65f;
            view.backgroundColor = new Color(0.12f, 0.17f, 0.23f);
            view.clearFlags = CameraClearFlags.SolidColor;
            view.transform.SetPositionAndRotation(new Vector3(0f, 30f, -35f), Quaternion.Euler(35f, 0f, 0f));
        }

        private void Update()
        {
            NetworkManager manager = session.Manager;
            NetworkVehicle current = manager != null && manager.IsConnectedClient && manager.LocalClient.PlayerObject != null
                ? manager.LocalClient.PlayerObject.GetComponent<NetworkVehicle>() : null;
            if (current != localVehicle)
            {
                localVehicle = current;
                fireUntil = 0;
                if (current != null)
                {
                    yaw = current.transform.eulerAngles.y;
                    pitch = 12f;
                    sequence = 0;
                    nextCommand = 0;
                    if (hasFocus) CaptureCursor();
                    MoveCamera(true);
                }
                else ReleaseCursor();
            }
            if (localVehicle == null)
            {
                if (Cursor.lockState != CursorLockMode.None) ReleaseCursor();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ReleaseCursor();
            bool controlling = hasFocus && Cursor.lockState == CursorLockMode.Locked;
            if (controlling && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw = Mathf.Repeat(yaw + delta.x * 0.12f, 360f);
                pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -40f, 25f);
                if (mouse.leftButton.wasPressedThisFrame) fireUntil = Time.realtimeSinceStartupAsDouble + 0.1;
            }
            if (Time.realtimeSinceStartupAsDouble < nextCommand) return;
            nextCommand = Time.realtimeSinceStartupAsDouble + SendInterval;
            float throttle = controlling && keyboard != null ? (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f) : 0f;
            float steering = controlling && keyboard != null ? (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f) : 0f;
            localVehicle.SendCommand(new NetworkVehicleCommand
            {
                Sequence = ++sequence,
                Throttle = throttle,
                Steering = steering,
                Handbrake = !controlling || (keyboard != null && keyboard.spaceKey.isPressed),
                Aim = AimDirection(),
                Fire = controlling && ((mouse != null && mouse.leftButton.isPressed) || Time.realtimeSinceStartupAsDouble < fireUntil)
            });
        }

        private void LateUpdate()
        {
            if (localVehicle != null && view != null) MoveCamera(false);
        }

        private Vector3 AimDirection()
        {
            Vector3 direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            Vector3 target = view.transform.position + direction * 150f;
            float nearest = 150f;
            foreach (RaycastHit hit in Physics.RaycastAll(view.transform.position, direction, nearest, 1, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == localVehicle.Body || hit.distance >= nearest) continue;
                target = hit.point;
                nearest = hit.distance;
            }
            Vector3 aim = target - localVehicle.MuzzlePosition;
            return aim.sqrMagnitude > 0.001f ? aim.normalized : direction;
        }

        private void MoveCamera(bool snap)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = localVehicle.transform.position + Vector3.up * 1.6f;
            Vector3 offset = rotation * new Vector3(0f, 0f, -9f);
            float distance = offset.magnitude;
            foreach (RaycastHit hit in Physics.SphereCastAll(pivot, 0.25f, offset.normalized, distance, 1, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != localVehicle.Body) distance = Mathf.Min(distance, Mathf.Max(0.3f, hit.distance - 0.15f));
            }
            Vector3 position = pivot + offset.normalized * distance;
            view.transform.position = snap ? position : Vector3.Lerp(view.transform.position, position, 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
            view.transform.rotation = rotation;
        }

        private void OnGUI()
        {
            if (session == null) return;
            GUILayout.BeginArea(new Rect(12f, 12f, 430f, 330f), GUI.skin.box);
            GUILayout.Label("Безумные машины — сетевой полигон");
            GUILayout.Label(session.Status);
            GUILayout.Label($"Сервер: {session.Address}:{session.Port}");
            if (session.Manager != null && session.Manager.IsConnectedClient)
            {
                ulong rtt = session.Transport.GetCurrentRtt(NetworkManager.ServerClientId);
                GUILayout.Label($"Игрок: {session.Manager.LocalClientId}    RTT: {(rtt == 0 ? "ожидание" : rtt + " мс")}");
                if (localVehicle != null)
                    GUILayout.Label($"Машина {localVehicle.Slot + 1}    {localVehicle.Speed:0} км/ч\nВыстрелов: {localVehicle.Shots}    Попаданий по вам: {localVehicle.Hits}");
                if (GUILayout.Button("Вернуться к управлению")) CaptureCursor();
            }
            GUI.enabled = session.CanConnect;
            GUILayout.BeginHorizontal();
            GUILayout.Label("IPv4", GUILayout.Width(35f));
            address = GUILayout.TextField(address, 45, GUILayout.Width(220f));
            port = GUILayout.TextField(port, 5, GUILayout.Width(65f));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Подключиться")) session.Connect(address, port);
            GUI.enabled = !session.CanConnect;
            if (GUILayout.Button("Отключиться")) session.Disconnect();
            GUI.enabled = true;
            GUILayout.Label("WASD — движение · Пробел — ручник\nМышь — наведение · ЛКМ — пушка\nEsc — освободить курсор; сервер продолжает игру.");
            GUILayout.EndArea();
            if (localVehicle != null) GUI.Label(new Rect(Screen.width / 2f - 5f, Screen.height / 2f - 10f, 20f, 20f), "+");
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!focused) ReleaseCursor();
        }

        private static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ReleaseCursor()
        {
            fireUntil = 0;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            ReleaseCursor();
            if (view != null) Destroy(view.gameObject);
        }
    }
}
