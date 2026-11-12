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
        private NetworkVehicle aimedVehicle;
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
        private bool captureWhenReady;
        private bool waitForFireRelease = true;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        public bool ReadyForPlay => localVehicle != null && localVehicle.IsSpawned;

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
            view.farClipPlane = session.ViewDistance;
            view.fieldOfView = 65f;
            view.backgroundColor = RenderSettings.fog ? RenderSettings.fogColor : new Color(0.12f, 0.17f, 0.23f);
            view.clearFlags = RenderSettings.fog && RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
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
                aimedVehicle = null;
                fireUntil = 0;
                if (current != null)
                {
                    yaw = current.transform.eulerAngles.y;
                    pitch = 12f;
                    sequence = 0;
                    nextCommand = 0;
                    captureWhenReady = true;
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

            if (GameFlow.Loading) return;
            if (captureWhenReady && hasFocus)
            {
                captureWhenReady = false;
                CaptureCursor();
            }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (settings.IsOpen) settings.Close();
                else if (Cursor.lockState == CursorLockMode.Locked) ReleaseCursor();
                else CaptureCursor();
            }
            bool controlling = hasFocus && Cursor.lockState == CursorLockMode.Locked;
            if (controlling && mouse != null)
            {
                Vector2 delta = GameSettings.AdjustLook(mouse.delta.ReadValue());
                yaw = Mathf.Repeat(yaw + delta.x * 0.12f, 360f);
                pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -40f, 25f);
                if (!mouse.leftButton.isPressed) waitForFireRelease = false;
                if (!waitForFireRelease && mouse.leftButton.wasPressedThisFrame) fireUntil = Time.realtimeSinceStartupAsDouble + 0.1;
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
                Fire = controlling && !waitForFireRelease && ((mouse != null && mouse.leftButton.isPressed) || Time.realtimeSinceStartupAsDouble < fireUntil)
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
            Rigidbody targetBody = null;
            float nearest = 150f;
            foreach (RaycastHit hit in Physics.RaycastAll(view.transform.position, direction, nearest, NetworkVehicle.HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == localVehicle.Body || hit.distance >= nearest) continue;
                target = hit.point;
                nearest = hit.distance;
                targetBody = hit.rigidbody;
            }
            aimedVehicle = session.HasTeams && targetBody != null ? targetBody.GetComponent<NetworkVehicle>() : null;
            Vector3 aim = target - localVehicle.MuzzlePosition;
            return aim.sqrMagnitude > 0.001f ? aim.normalized : direction;
        }

        private void MoveCamera(bool snap)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = localVehicle.transform.position + Vector3.up * 1.6f;
            Vector3 offset = rotation * new Vector3(0f, 0f, -9f);
            float distance = offset.magnitude;
            foreach (RaycastHit hit in Physics.SphereCastAll(pivot, 0.25f, offset.normalized, distance, NetworkVehicle.HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != localVehicle.Body) distance = Mathf.Min(distance, Mathf.Max(0.3f, hit.distance - 0.15f));
            }
            Vector3 position = pivot + offset.normalized * distance;
            view.transform.position = snap ? position : Vector3.Lerp(view.transform.position, position, 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
            view.transform.rotation = rotation;
        }

        private void OnGUI()
        {
            if (session == null || GameFlow.Loading) return;
            if (GameFlow.OnlineLaunchPending)
            {
                DrawGameMenu();
                return;
            }
            GUILayout.BeginArea(new Rect(12f, 12f, 430f, 430f), GUI.skin.box);
            GUILayout.Label($"Безумные машины — {session.LocationName}");
            statusStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(session.Status, statusStyle);
            if (session.SessionInfo != null) GUILayout.Label(session.SessionInfo.Summary, statusStyle);
            string mode = session.Mode == NetworkConnectionMode.Offline ? "Автономно" :
                session.Mode == NetworkConnectionMode.DevelopmentOnline ? "Локальный сервер" : "Удалённый сервер";
            GUILayout.Label($"{mode} · {session.Address}:{session.Port} · протокол {NetworkSessionProtocol.Version}");
            if (session.Manager != null && session.Manager.IsConnectedClient)
            {
                ulong rtt = session.Transport.GetCurrentRtt(NetworkManager.ServerClientId);
                GUILayout.Label($"Игрок: {session.Manager.LocalClientId}    RTT: {(rtt == 0 ? "ожидание" : rtt + " мс")}");
                if (localVehicle != null)
                {
                    GUILayout.Label($"{localVehicle.DisplayName} · Машина {localVehicle.Slot + 1}    {localVehicle.Speed:0} км/ч\nВыстрелов: {localVehicle.Shots}    Попаданий по вам: {localVehicle.Hits}");
                    if (session.HasTeams) GUILayout.Label(TeamName(localVehicle));
                }
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
            if (Application.CanStreamedLevelBeLoaded(GameFlow.FrontEndScene) && GUILayout.Button("НА ГЛАВНЫЙ")) session.LeaveToMain();
            GUILayout.Label("WASD — движение · Пробел — ручник\nМышь — наведение · ЛКМ — пушка\nEsc — освободить курсор; сервер продолжает игру.");
            GUILayout.EndArea();
            if (localVehicle != null) DrawCrosshair();
        }

        private void DrawGameMenu()
        {
            if (ReadyForPlay && Cursor.lockState == CursorLockMode.Locked)
            {
                string team = session.HasTeams ? $"\n{TeamName(localVehicle)} · Попаданий по вам: {localVehicle.Hits}" : string.Empty;
                GUI.Box(new Rect(16, 16, 330, 75), $"{session.LocationName} · {ServerSessionInfo.ModeName(session.HasTeams)}\n{localVehicle.Speed:0} км/ч · Пушка{team}");
                DrawCrosshair();
                return;
            }
            float scale = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(30, 30, 480, 630), GUI.skin.box);
            if (settings.IsOpen) settings.Draw();
            else
            {
                GUILayout.Label("МЕНЮ СЕССИИ · ИГРА ПРОДОЛЖАЕТСЯ");
                GUILayout.Label(session.Status);
                if (session.SessionInfo != null) GUILayout.Label(session.SessionInfo.Summary);
                if (ReadyForPlay && GUILayout.Button("ПРОДОЛЖИТЬ", GUILayout.Height(43))) CaptureCursor();
                if (GUILayout.Button("НАСТРОЙКИ", GUILayout.Height(43))) settings.Open();
                if (GUILayout.Button("НА ГЛАВНЫЙ", GUILayout.Height(43))) session.LeaveToMain();
                GUILayout.Label("Esc — закрыть меню. Выход на главный завершает ваше участие; другие игроки продолжают ездить.");
            }
            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void DrawCrosshair()
        {
            float x = Screen.width / 2f;
            float y = Screen.height / 2f;
            GUI.Label(new Rect(x - 5f, y - 10f, 20f, 20f), "+");
            if (Cursor.lockState != CursorLockMode.Locked || aimedVehicle == null || !aimedVehicle.IsSpawned) return;
            bool sameTeam = NetworkSandboxSession.TeamForSlot(aimedVehicle.Slot) == NetworkSandboxSession.TeamForSlot(localVehicle.Slot);
            GUI.Box(new Rect(x - 45f, y + 12f, 90f, 22f), sameTeam ? "Союзник" : "Противник");
        }

        private static string TeamName(NetworkVehicle vehicle) =>
            NetworkSandboxSession.TeamForSlot(vehicle.Slot) == 0 ? "Синие" : "Оранжевые";

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!focused) ReleaseCursor();
        }

        private void CaptureCursor()
        {
            waitForFireRelease = true;
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
