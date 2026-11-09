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
        private GUIStyle statusStyle;

        public void Configure(NetworkSandboxSession value)
        {
            session = value;
            address = value.Address;
            port = value.Port.ToString();
        }

        private void Start()
        {
            GameObject cameraObject = new GameObject("Network client camera");
            cameraObject.transform.SetParent(transform, false);
            view = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            view.nearClipPlane = 0.1f;
            view.farClipPlane = 350f;
            view.fieldOfView = 65f;
            view.backgroundColor = new Color(0.12f, 0.17f, 0.23f);
            view.clearFlags = CameraClearFlags.SolidColor;
            ResetCamera();
        }

        private void ResetCamera()
        {
            view.transform.SetPositionAndRotation(new Vector3(0f, 30f, -35f), Quaternion.Euler(35f, 0f, 0f));
        }

        private void Update()
        {
            NetworkVehicle current = FindLocalVehicle();
            if (!ReferenceEquals(current, localVehicle))
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
                else
                {
                    ReleaseCursor();
                    ResetCamera();
                }
            }
            if (localVehicle == null || !localVehicle.IsSpawned)
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

#if UNITY_EDITOR
        // Explicit user-invoked probe; never runs automatically or ships in a player.
        public void SendInvalidCommandProbe()
        {
            if (localVehicle == null || !localVehicle.IsSpawned) throw new System.InvalidOperationException("Сначала подключите клиент и дождитесь машины.");
            ReleaseCursor();
            NetworkVehicleCommand command = new NetworkVehicleCommand { Aim = Vector3.forward, Handbrake = true };
            command.Sequence = ++sequence;
            command.Throttle = float.NaN;
            localVehicle.SendCommand(command);
            command.Sequence = ++sequence;
            command.Throttle = 0;
            command.Aim = new Vector3(float.PositiveInfinity, 0, 1);
            localVehicle.SendCommand(command);
            command.Sequence = ++sequence;
            command.Aim = Vector3.zero;
            localVehicle.SendCommand(command);
            command.Sequence = ++sequence;
            command.Aim = Vector3.forward;
            command.Steering = 2;
            localVehicle.SendCommand(command);
            command.Sequence = ++sequence;
            command.Steering = 0;
            localVehicle.SendCommand(command);
            localVehicle.SendCommand(command);
            command.Sequence--;
            localVehicle.SendCommand(command);
            for (int i = 0; i < 20; i++)
            {
                command.Sequence = ++sequence;
                localVehicle.SendCommand(command);
            }
            nextCommand = Time.realtimeSinceStartupAsDouble + 0.6;
            Debug.Log("[MM NET] Отправлены неверные числа, повтор, старый номер и пачка команд. Смотрите счётчики сервера; доставка UDP не гарантирована. Ввод возобновится через 0,6 с.");
        }
#endif

        private NetworkVehicle FindLocalVehicle()
        {
            NetworkManager manager = session.Manager;
            if (manager == null || !manager.IsConnectedClient || manager.ShutdownInProgress) return null;
            if (localVehicle != null && localVehicle.IsLocalController) return localVehicle;
            foreach (NetworkObject item in manager.SpawnManager.SpawnedObjectsList)
            {
                if (item.TryGetComponent(out NetworkVehicle vehicle) && vehicle.IsLocalController) return vehicle;
            }
            return null;
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
            statusStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(session.Status, statusStyle);
            string mode = session.Mode == NetworkConnectionMode.Offline ? "Автономно" :
                session.Mode == NetworkConnectionMode.DevelopmentOnline ? "Локальный сервер" : "Удалённый сервер";
            GUILayout.Label($"{mode} · {session.Address}:{session.Port} · протокол {NetworkSessionProtocol.Version}");
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
            GUI.enabled = session.CanDisconnect;
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
