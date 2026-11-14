using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MadMachines.Networking
{
    // Created exclusively by a client session. No input, camera or IMGUI component exists on the server.
    public sealed class NetworkClientView : MonoBehaviour
    {
        private enum CameraComparison { Smoothed, Direct, Stationary }

        private NetworkSandboxSession session;
        private NetworkVehicle localVehicle;
        private NetworkPhysicsState localState;
        private int? localSlot;
        private NetworkVehicle aimedVehicle;
        private Camera view;
        private string address;
        private string port;
        private float yaw;
        private float pitch = 12f;
        private bool aiming;
        private float aimBlend;
        private Vector3 chasePosition;
        private Collider[] hullColliders;
        private uint cameraRecoveryCount;
        private double resumeCommandsAt;
        private double fireUntil;
        private uint sequence;
        private VehicleCommand movement = new VehicleCommand { Handbrake = true };
        private bool predictionEnabled = true;
        private bool hasFocus = true;
        private GUIStyle statusStyle;
        private GUIStyle healthBarStyle;
        private bool captureWhenReady;
        private bool captureOnNextVehicle = true;
        private bool waitForFireRelease = true;
        private CameraComparison cameraComparison;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly CombatHud combatHud = new CombatHud();
        public bool ReadyForPlay => localVehicle != null && localVehicle.IsSpawned;
        public string CameraComparisonName => cameraComparison switch
        {
            CameraComparison.Direct => "Прямое слежение",
            CameraComparison.Stationary => "Фиксированная позиция",
            _ => "Обычное сглаживание"
        };

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
            if (session.HasResults)
            {
                if (Cursor.lockState != CursorLockMode.None) ReleaseCursor();
                if (localState != null) localState.StopPrediction();
                if (localVehicle != null) localVehicle.PresentThrottle(0f);
                return;
            }
            NetworkVehicle current = FindLocalVehicle();
            if (!ReferenceEquals(current, localVehicle))
            {
                if (localState != null) localState.StopPrediction();
                localVehicle = current;
                localState = current != null ? current.GetComponent<NetworkPhysicsState>() : null;
                cameraRecoveryCount = localState != null ? localState.PresentedRecoveryCount : 0;
                aimedVehicle = null;
                fireUntil = 0;
                movement = new VehicleCommand { Handbrake = true };
                cameraComparison = CameraComparison.Smoothed;
                aiming = false;
                aimBlend = 0f;
                view.fieldOfView = 65f;
                hullColliders = current != null ? current.GetComponentsInChildren<Collider>() : null;
                if (current != null)
                {
                    localSlot = current.Slot;
                    yaw = current.transform.eulerAngles.y;
                    pitch = 12f;
                    sequence = 0;
                    resumeCommandsAt = 0;
                    captureWhenReady = captureOnNextVehicle && !settings.IsOpen;
                    MoveCamera(true);
                }
                else
                {
                    captureOnNextVehicle = !session.MatchGame || Cursor.lockState == CursorLockMode.Locked;
                    ReleaseCursor();
                    if (!session.MatchGame) ResetCamera();
                }
            }
            if (GameFlow.Loading) return;
            Keyboard keyboard = Keyboard.current;
            if (!ReadyForPlay)
            {
                if (Cursor.lockState != CursorLockMode.None) ReleaseCursor();
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && settings.IsOpen) settings.Close();
                return;
            }
            if (captureWhenReady && hasFocus)
            {
                captureWhenReady = false;
                CaptureCursor();
            }
            Mouse mouse = Mouse.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
            {
                cameraComparison = (CameraComparison)(((int)cameraComparison + 1) % 3);
                Debug.Log($"[MM NET SMOOTH] Камера: {CameraComparisonName}. F8 переключает режим сравнения.");
            }
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
            {
                predictionEnabled = !predictionEnabled;
                if (predictionEnabled) localState.RetryPrediction();
                else localState.StopPrediction();
                Debug.Log($"[MM NET PREDICT] Предсказание своей машины {(predictionEnabled ? "включено" : "выключено")}.");
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (settings.IsOpen) settings.Close();
                else if (Cursor.lockState == CursorLockMode.Locked) ReleaseCursor();
                else CaptureCursor();
            }
            bool controlling = hasFocus && Cursor.lockState == CursorLockMode.Locked;
            if (controlling && keyboard != null && keyboard.cKey.wasPressedThisFrame) aiming = !aiming;
            if (controlling && session.MatchGame && keyboard != null && keyboard.rKey.wasPressedThisFrame)
                localVehicle.RequestRecovery();
            if (controlling && mouse != null)
            {
                Vector2 delta = GameSettings.AdjustLook(mouse.delta.ReadValue());
                yaw = Mathf.Repeat(yaw + delta.x * 0.12f, 360f);
                pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -40f, 25f);
                if (!mouse.leftButton.isPressed) waitForFireRelease = false;
                if (!waitForFireRelease && mouse.leftButton.wasPressedThisFrame) fireUntil = Time.realtimeSinceStartupAsDouble + 0.1;
            }
            movement = new VehicleCommand
            {
                Throttle = controlling && keyboard != null ? (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f) : 0f,
                Steering = controlling && keyboard != null ? (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f) : 0f,
                Handbrake = !controlling || (keyboard != null && keyboard.spaceKey.isPressed)
            };
            localVehicle.PresentThrottle(movement.Throttle);
        }

        private void FixedUpdate()
        {
            if (!ReadyForPlay || !localVehicle.IsLocalController || !localVehicle.isActiveAndEnabled || GameFlow.Loading || session.HasResults ||
                session.Manager.ShutdownInProgress || Time.realtimeSinceStartupAsDouble < resumeCommandsAt) return;
            bool controlling = hasFocus && Cursor.lockState == CursorLockMode.Locked;
            VehicleCommand step = controlling ? movement : new VehicleCommand { Handbrake = true };
            Mouse mouse = Mouse.current;
            NetworkVehicleCommand command = new NetworkVehicleCommand
            {
                Sequence = ++sequence,
                Throttle = step.Throttle,
                Steering = step.Steering,
                Handbrake = step.Handbrake,
                Aim = AimDirection(),
                Fire = controlling && !waitForFireRelease && ((mouse != null && mouse.leftButton.isPressed) || Time.realtimeSinceStartupAsDouble < fireUntil)
            };
            localVehicle.SendCommand(command);
            if (predictionEnabled) localState.Predict(command, session.gameObject.scene);
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
            localState.StopPrediction();
            resumeCommandsAt = Time.realtimeSinceStartupAsDouble + 0.6;
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
            if (localVehicle == null || view == null) return;
            uint recovered = localState.PresentedRecoveryCount;
            MoveCamera(recovered != cameraRecoveryCount);
            cameraRecoveryCount = recovered;
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
            aimedVehicle = session.HasTeams && !session.MatchGame && targetBody != null ? targetBody.GetComponent<NetworkVehicle>() : null;
            Vector3 aim = target - localVehicle.MuzzlePosition;
            return aim.sqrMagnitude > 0.001f ? aim.normalized : direction;
        }

        private void MoveCamera(bool snap)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (!snap && cameraComparison == CameraComparison.Stationary && !aiming && aimBlend == 0f)
            {
                view.transform.rotation = rotation;
                return;
            }
            Vector3 pivot = localVehicle.transform.position + Vector3.up * 1.6f;
            Vector3 offset = rotation * new Vector3(0f, 0f, -9f);
            float distance = offset.magnitude;
            foreach (RaycastHit hit in Physics.SphereCastAll(pivot, 0.25f, offset.normalized, distance, NetworkVehicle.HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != localVehicle.Body) distance = Mathf.Min(distance, Mathf.Max(0.3f, hit.distance - 0.15f));
            }
            Vector3 position = pivot + offset.normalized * distance;
            chasePosition = snap || cameraComparison == CameraComparison.Direct ? position :
                Vector3.Lerp(chasePosition, position, 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
            aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, Time.unscaledDeltaTime / VehicleCamera.AimTransitionSeconds);
            float blend = Mathf.SmoothStep(0f, 1f, aimBlend);
            view.fieldOfView = Mathf.Lerp(65f, VehicleCamera.DefaultAimFieldOfView, blend);
            view.transform.position = chasePosition;
            if (blend > 0f)
            {
                Vector3 aimPosition = VehicleCamera.GetAimPosition(localVehicle.MountPosition, rotation * Vector3.forward, hullColliders);
                view.transform.position = VehicleCamera.ClearHull(Vector3.Lerp(chasePosition, aimPosition, blend), hullColliders);
            }
            view.transform.rotation = rotation;
        }

        private void OnGUI()
        {
            if (session == null || GameFlow.Loading) return;
            if (ReadyForPlay && cameraComparison != CameraComparison.Smoothed)
                GUI.Label(new Rect(Mathf.Max(12f, Screen.width - 460f), 12f, 450f, 28f),
                    $"Камера: {CameraComparisonName} · F8 — сравнение");
            if (GameFlow.OnlineLaunchPending)
            {
                DrawDeathmatch();
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
                    if (session.HasTeams && !session.MatchGame) GUILayout.Label(TeamName(localVehicle));
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
            if (ReadyForPlay && Cursor.lockState == CursorLockMode.Locked)
                DrawCombatHud(null);
        }

        private void DrawDeathmatch()
        {
            ServerSessionInfo info = session.SessionInfo;
            bool finished = session.HasResults;
            if (!finished && ReadyForPlay && Cursor.lockState == CursorLockMode.Locked)
            {
                DrawCombatHud(info);
                return;
            }
            float scale = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            Rect panel = finished ? new Rect((Screen.width / scale - 420f) / 2f, 130f, 420f, 460f) : new Rect(16f, 16f, 420f, 690f);
            GUILayout.BeginArea(panel);
            GUILayout.BeginVertical(GUI.skin.box);
            if (!finished && settings.IsOpen) settings.Draw();
            else
            {
                GUILayout.Label(finished ? "ИТОГИ МАТЧА" : $"{session.LocationName} · Deathmatch");
                if (finished) GUILayout.Label(info.result);
                else if (info?.standings != null)
                {
                    int seconds = session.MatchSecondsLeft;
                    GUILayout.Label($"До конца матча  {seconds / 60:00}:{seconds % 60:00}");
                    if (session.Manager.IsConnectedClient && localSlot.HasValue)
                    {
                        DeathmatchStanding self = System.Array.Find(info.standings, row => row.slot == localSlot.Value);
                        if (self != null) DrawHealthBar(self.health, info.DeathmatchMaxHealth);
                    }
                }
                if (info?.standings != null) DrawStandings(info.standings, finished);
                if (!finished)
                {
                    GUILayout.Label(ReadyForPlay ? "МЕНЮ СЕССИИ · БОЙ ПРОДОЛЖАЕТСЯ" :
                        session.Manager.IsConnectedClient ? "Ожидание машины…" : session.Status);
                    if (ReadyForPlay && GUILayout.Button("ПРОДОЛЖИТЬ", GUILayout.Height(43))) CaptureCursor();
                    if (GUILayout.Button("НАСТРОЙКИ", GUILayout.Height(43))) settings.Open();
                }
                if (GUILayout.Button("НА ГЛАВНЫЙ", GUILayout.Height(43))) session.LeaveToMain();
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void DrawCombatHud(ServerSessionInfo info)
        {
            if (Event.current.type != EventType.Repaint) return;
            DeathmatchStanding self = info?.standings != null && localSlot.HasValue
                ? System.Array.Find(info.standings, row => row.slot == localSlot.Value) : null;
            CombatHud.Frame frame = new CombatHud.Frame
            {
                Camera = view,
                Hull = localVehicle.transform,
                Aiming = aiming,
                Speed = localVehicle.Speed,
                Health = self?.health ?? 0,
                MaxHealth = self != null ? info.DeathmatchMaxHealth : 0,
                Weapon = localVehicle.Weapon.Label,
                Ammunition = localVehicle.Ammunition,
                Reload = localVehicle.Reload,
                Muzzle = localVehicle.MuzzlePosition,
                ShotDirection = localVehicle.ShotDirection,
                Velocity = localVehicle.PresentedVelocity
            };
            combatHud.Draw(frame, session.MatchGame ? DrawMatchStatus : null, DrawAimedTeam);
        }

        private void DrawMatchStatus(float width, float height)
        {
            ServerSessionInfo info = session.SessionInfo;
            int seconds = session.MatchSecondsLeft;
            int count = info?.standings?.Length ?? 0;
            combatHud.DrawModePanel($"{session.LocationName} · Deathmatch", count + 2);
            combatHud.DrawModeLine(0, $"До конца матча  {seconds / 60:00}:{seconds % 60:00}");
            for (int i = 0; i < count; i++)
                combatHud.DrawModeLine(i + 1, FormatStanding(info.standings[i], false));
            combatHud.DrawModeLine(count + 1, "C — вид  ·  R — восстановление");
        }

        private void DrawHealthBar(int health, int maxHealth)
        {
            Rect bar = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;

            healthBarStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            health = Mathf.Clamp(health, 0, maxHealth);
            float fraction = (float)health / maxHealth;
            Color previous = GUI.color;
            GUI.color = new Color(0.08f, 0.11f, 0.14f, 0.96f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.63f, 0.17f, 0.15f), new Color(0.14f, 0.47f, 0.25f), fraction);
            if (health > 0)
                GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * fraction, bar.height - 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(bar, $"ХП  {health} / {maxHealth}", healthBarStyle);
            GUI.color = previous;
        }

        private void DrawStandings(DeathmatchStanding[] standings, bool finished)
        {
            foreach (DeathmatchStanding row in standings)
                GUILayout.Label(FormatStanding(row, finished));
        }

        private string FormatStanding(DeathmatchStanding row, bool finished)
        {
            string status = row.status switch
            {
                DeathmatchPlayerStatus.Left => "Вышел",
                DeathmatchPlayerStatus.Disconnected => "Нет связи",
                DeathmatchPlayerStatus.Respawning => "Возрождается",
                _ => "В бою"
            };
            string suffix = finished && row.status != DeathmatchPlayerStatus.Left ? string.Empty : $" · {status}";
            string name = row.slot == localSlot ? $"{row.Name} (вы)" : row.Name;
            return $"{name} · Уничтожения: {row.kills} · Смерти: {row.deaths}{suffix}";
        }

        private void DrawAimedTeam(float width, float height)
        {
            if (aimedVehicle == null || !aimedVehicle.IsSpawned) return;
            bool sameTeam = NetworkSandboxSession.TeamForSlot(aimedVehicle.Slot) == NetworkSandboxSession.TeamForSlot(localVehicle.Slot);
            GUI.Box(new Rect(width * 0.5f - 45f, height * 0.5f + 24f, 90f, 22f), sameTeam ? "Союзник" : "Противник");
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
            if (localState != null) localState.StopPrediction();
            ReleaseCursor();
            if (view != null) Destroy(view.gameObject);
        }
    }
}
