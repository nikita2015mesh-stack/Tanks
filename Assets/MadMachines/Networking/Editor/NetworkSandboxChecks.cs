using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MadMachines.Networking.Editor
{
    // Deliberately opt-in: no InitializeOnLoad, no automatic Play Mode and no test-runner dependency.
    public static class NetworkSandboxChecks
    {
        [MenuItem(NetworkSandboxEditor.ChecksMenu + "Run command policy checks")]
        public static void CheckCommandPolicy()
        {
            NetworkVehicleCommand command = Valid(1);
            Require(command.IsValid(), "Допустимая команда");
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1.01f, 1.01f })
            {
                command = Valid(1);
                command.Throttle = bad;
                Require(!command.IsValid(), "Недопустимый газ " + bad);
                command = Valid(1);
                command.Steering = bad;
                Require(!command.IsValid(), "Недопустимое руление " + bad);
            }
            foreach (Vector3 bad in new[] { Vector3.zero, Vector3.one, new Vector3(float.NaN, 0, 1),
                new Vector3(0, float.PositiveInfinity, 1), new Vector3(float.MaxValue, 0, 0) })
            {
                command = Valid(1);
                command.Aim = bad;
                Require(!command.IsValid(), "Недопустимое наведение");
            }

            NetworkCommandGate gate = new NetworkCommandGate();
            Require(gate.Accept(Valid(uint.MaxValue), 1), "Первый номер");
            Require(gate.Accept(Valid(0), 1), "Переполнение uint");
            Require(gate.Evaluate(Valid(0), 1) == CommandRejection.Sequence, "Дубликат");
            Require(gate.Evaluate(Valid(uint.MaxValue), 1) == CommandRejection.Sequence, "Старый номер");
            Require(gate.Evaluate(Valid(0x80000000), 1) == CommandRejection.Sequence, "Неоднозначная половина диапазона");
            gate.Reset();
            for (uint i = 1; i <= 8; i++) Require(gate.Accept(Valid(i), 1), "Запас пачки");
            Require(gate.Evaluate(Valid(9), 1) == CommandRejection.Rate, "Лимит частоты");
            Require(gate.Accept(Valid(9), 1.03), "Лимит не поглощает номер; токен восстанавливается");
            Require(gate.Evaluate(Valid(10), double.NaN) == CommandRejection.Clock, "Неверные часы");
            Require(gate.Evaluate(Valid(10), 0) == CommandRejection.Clock, "Часы не идут назад");

            NetworkCommandBuffer buffer = new NetworkCommandBuffer();
            Require(buffer.Receive(Valid(1), 1, 2, 1, true) == CommandRejection.Sender, "Чужой владелец");
            Require(buffer.Receive(Valid(1), 1, 1, 1, false) == CommandRejection.Inactive, "Неактивный объект");
            command = Valid(1);
            command.Fire = true;
            command.Throttle = 1;
            Require(buffer.Receive(command, 1, 1, 1, true) == CommandRejection.None, "Приём огня");
            Require(buffer.Receive(Valid(2), 1.01, 1, 1, true) == CommandRejection.None, "Следующая команда до физики");
            Require(buffer.Sample(1.02, out command) && command.Fire && command.Throttle == 1 && command.Sequence == 1,
                "Первая команда движения из пачки и сохранённый огонь");
            Require(buffer.Sample(1.04, out command) && !command.Fire && command.Throttle == 0 && command.Sequence == 2,
                "Следующий физический шаг получает вторую команду, огонь не накапливается");
            command = Valid(3);
            command.Throttle = float.NaN;
            Require(buffer.Receive(command, 1.2, 1, 1, true) == CommandRejection.Values, "Неверная команда не обновляет таймер");
            Require(!buffer.Sample(1.27, out command) && command.Handbrake && !command.Fire && command.Throttle == 0, "Тайм-аут тормозит");
            buffer.Reset();
            Require(!buffer.Sample(2, out command), "Сброс не оставляет управление");
            Require(buffer.Receive(Valid(1), 2, 1, 1, true) == CommandRejection.None, "Новый поток после сброса");
            command = Valid(2);
            command.Fire = true;
            buffer.Receive(command, 2.01, 1, 1, true);
            Require(buffer.Sample(2.02, out command) && command.Fire && buffer.Sample(2.04, out command) && command.Fire, "Удержание огня между тиками");
            Require(!buffer.Sample(2.3, out command) && !command.Fire, "Огонь прекращается по тайм-ауту");
            command = Valid(3);
            command.Fire = true;
            buffer.Receive(command, 3, 1, 1, true);
            buffer.Receive(Valid(4), 4, 1, 1, true);
            Require(buffer.Sample(4.01, out command) && !command.Fire, "Старый огонь не оживает после остановки физики");
            buffer.ClearInput();
            Require(!buffer.Sample(4.02, out command), "Disable очищает управление");
            Require(buffer.Receive(Valid(4), 4.03, 1, 1, true) == CommandRejection.Sequence, "Disable не разрешает повтор старого номера");

            buffer.Reset();
            for (uint i = 1; i <= 6; i++) buffer.Receive(Valid(i), 5, 1, 1, true);
            Require(buffer.Sample(5.02, out command) && command.Sequence == 3, "Избыточная пачка не копит задержку");
            Require(buffer.Sample(5.04, out command) && command.Sequence == 4, "После пропуска старых номеров порядок сохраняется");
            buffer.Receive(Valid(7), 5.3, 1, 1, true);
            Require(buffer.Sample(5.31, out command) && command.Sequence == 7, "Просроченные шаги не оживают при новом вводе");

            const float cannonInterval = 1.15f;
            WeaponCooldown cooldown = default;
            Require(cooldown.TryBegin(1, cannonInterval), "Первый выстрел");
            for (int i = 0; i < 100; i++) Require(!cooldown.TryBegin(1.01, cannonInterval), "Спам не сокращает перезарядку");
            Require(!cooldown.TryBegin(2.14, cannonInterval) && cooldown.TryBegin(2.16, cannonInterval), "Серверная перезарядка");
            Require(cooldown.TryBegin(20, cannonInterval) && !cooldown.TryBegin(20, cannonInterval), "Нет залпа накопленных выстрелов после задержки");

            NetworkPoseBuffer poses = new NetworkPoseBuffer();
            Require(!poses.Sample(0, out _), "Пустой буфер позы");
            poses.Push(new NetworkPhysicsPose { Position = Vector3.zero, Rotation = Quaternion.identity, ServerTime = 1 });
            poses.Push(new NetworkPhysicsPose { Position = Vector3.right * 10, Rotation = Quaternion.Euler(0, 90, 0), ServerTime = 2 });
            poses.Push(new NetworkPhysicsPose { Position = Vector3.one * 100, Rotation = Quaternion.identity, ServerTime = 1 });
            Require(poses.Sample(1.5, out NetworkPhysicsPose sample) && Mathf.Abs(sample.Position.x - 5) < 0.001f &&
                Quaternion.Angle(sample.Rotation, Quaternion.Euler(0, 45, 0)) < 0.1f, "Интерполяция, устаревшая поза отброшена");
            Require(poses.Sample(3, out sample) && sample.Position.x == 10, "Нет экстраполяции при нехватке снимков");
            poses.Reset();
            Require(!poses.Sample(4, out sample), "Очистка позы при отключении");

            Require(NetworkLaunchOptions.Parse(new[] { "-mm-port", "12345", "-mm-bind", "127.0.0.1" }, true).Port == 12345, "Порт запуска");
            foreach (string[] arguments in new[] { new[] { "-mm-port", "0" }, new[] { "-mm-port", "65536" },
                new[] { "-mm-port" }, new[] { "-mm-unknown", "value" }, new[] { "-mm-address", "0.0.0.0" } })
            {
                bool rejected = false;
                try { NetworkLaunchOptions.Parse(arguments, false); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Отказ неверных аргументов запуска");
            }
            NetworkDevelopmentSettings settings = new NetworkDevelopmentSettings
            {
                mode = NetworkConnectionMode.RemoteOnline,
                localPort = 7781,
                remoteAddress = "192.0.2.5",
                remotePort = 7782
            };
            NetworkLaunchOptions remote = NetworkLaunchOptions.Parse(Array.Empty<string>(), false, settings);
            Require(remote.Address == settings.remoteAddress && remote.Port == settings.remotePort, "Настройки удалённого подключения");
            NetworkLaunchOptions local = NetworkLaunchOptions.Parse(new[] { "-mm-mode", "development" }, false, settings);
            Require(local.Address == NetworkDevelopmentSettings.LocalAddress && local.Port == settings.localPort, "Локальный режим переопределяет удалённый");
            NetworkLaunchOptions offline = NetworkLaunchOptions.Parse(new[] { "-mm-mode", "offline" }, false, settings);
            Require(offline.Mode == NetworkConnectionMode.Offline, "Режим Offline");

            byte[] handshake = NetworkSessionProtocol.CreatePayload(1, 2);
            Require(NetworkSessionProtocol.CheckCompatibility(handshake, handshake, out _) == null, "Совместимый протокол");
            Require(NetworkSessionProtocol.CheckCompatibility(Array.Empty<byte>(), handshake, out _) != null, "Отсутствующий заголовок");
            Require(NetworkSessionProtocol.CheckCompatibility(NetworkSessionProtocol.CreatePayload(2, 1), handshake, out _) != null, "Разные prefab");
            byte[] rearDrive = NetworkSessionProtocol.CreatePayload(1, 2, "sandbox", VehicleDrive.Rear);
            Require(NetworkSessionProtocol.CheckCompatibility(rearDrive, handshake, out _) == null &&
                NetworkSessionProtocol.ReadDrive(rearDrive) == VehicleDrive.Rear, "Личная конфигурация привода");
            byte[] invalidDrive = (byte[])rearDrive.Clone();
            invalidDrive[12] = 255;
            Require(NetworkSessionProtocol.CheckCompatibility(invalidDrive, handshake, out _) != null, "Отказ неизвестного привода");
            Require(NetworkSessionProtocol.CheckCompatibility(NetworkSessionProtocol.CreatePayload(1, 2, "other-map"), handshake, out _) != null,
                "Отказ другой карты после запроса сессии");
            Require(NetworkSessionProtocol.CheckCompatibility(new byte[] { 77, 77, (byte)NetworkSessionProtocol.Version, 0 }, handshake, out _) != null,
                "Отказ неполного пакета конфигурации");
            byte[] oldHandshake = (byte[])handshake.Clone();
            oldHandshake[2] ^= 1;
            Require(NetworkSessionProtocol.CheckCompatibility(oldHandshake, handshake, out _).StartsWith(NetworkSessionProtocol.RejectionPrefix, StringComparison.Ordinal),
                "Отказ несовместимого протокола с причиной");
            Debug.Log("[MM NET CHECK] Проверки команд, перезарядки, интерполяции, аргументов и протокола завершены. Сетевое соединение и PhysX проверяются отдельно.");
        }

        [MenuItem(NetworkSandboxEditor.ChecksMenu + "Проверить интерполяцию снимков")]
        public static void CheckSnapshotInterpolation()
        {
            // A constant-speed body sampled at 50 Hz and sent at 30 Hz has alternating 20/40 ms gaps.
            // Interpolation must follow physical timestamps, not assume equally spaced send ticks.
            NetworkPoseBuffer buffer = new NetworkPoseBuffer();
            double[] times = { 1.00, 1.02, 1.06, 1.10, 1.12, 1.16, 1.20 };
            for (int i = 0; i < times.Length; i++)
            {
                NetworkPhysicsPose pose = new NetworkPhysicsPose
                {
                    Position = Vector3.right * (float)((times[i] - 1) * 20),
                    Rotation = Quaternion.identity,
                    ServerTime = times[i],
                    PhysicsStep = (ulong)Math.Round((times[i] - 1) / 0.02)
                };
                buffer.Push(pose);
            }
            Require(buffer.Sample(0.9, out NetworkPhysicsPose sample) && sample.Position == Vector3.zero &&
                buffer.Mode == NetworkPoseSampleMode.Buffering, "До первого снимка нет экстраполяции назад");
            for (int frame = 0; frame < 12; frame++)
            {
                double time = 1 + frame / 60.0;
                Require(buffer.Sample(time, out sample) &&
                    Mathf.Abs(sample.Position.x - (float)((time - 1) * 20)) < 0.0001f &&
                    buffer.Mode == NetworkPoseSampleMode.Interpolating,
                    "Равномерное движение между неравномерными физическими снимками, кадр " + frame);
            }
            Require(buffer.Sample(1.25, out sample) && Mathf.Abs(sample.Position.x - 4) < 0.0001f &&
                buffer.Mode == NetworkPoseSampleMode.Holding, "Нехватка данных удерживает последнюю позу");
            buffer.Push(new NetworkPhysicsPose { Position = Vector3.right * 6, Rotation = Quaternion.identity, ServerTime = 1.3 });
            Require(buffer.Sample(1.26, out sample) && Mathf.Abs(sample.Position.x - 5.2f) < 0.0001f &&
                buffer.Mode == NetworkPoseSampleMode.Interpolating, "Движение возобновляется после нехватки данных");
            buffer.Reset();
            Require(buffer.Count == 0 && !buffer.Sample(1.3, out _) && buffer.Mode == NetworkPoseSampleMode.Empty,
                "Новая жизнь машины не использует прежнюю историю");

            NetworkPhysicsPose from = new NetworkPhysicsPose { Rotation = Quaternion.Euler(0, 350, 0), ServerTime = 2 };
            from.Wheels.Add(new Vector3(0.2f, -10, 350));
            NetworkPhysicsPose to = new NetworkPhysicsPose { Rotation = Quaternion.Euler(0, 10, 0), ServerTime = 2.04 };
            to.Wheels.Add(new Vector3(0.4f, 10, 10));
            buffer.Push(from);
            buffer.Push(to);
            Require(buffer.Sample(2.02, out sample) && Quaternion.Angle(sample.Rotation, Quaternion.identity) < 0.01f &&
                Mathf.Abs(sample.Wheels[0].x - 0.3f) < 0.0001f && Mathf.Abs(sample.Wheels[0].y) < 0.0001f &&
                Mathf.Abs(Mathf.DeltaAngle(sample.Wheels[0].z, 0)) < 0.01f, "Кузов и опоры интерполируются вместе через 360 градусов");
            Debug.Log("[MM NET CHECK] Проверки интерполяции снимков завершены. Порядок PlayerLoop, PhysX, сеть и камера требуют отдельной проверки в игре.");
        }

        [MenuItem(NetworkSandboxEditor.ChecksMenu + "Inspect running session")]
        public static void InspectSession()
        {
            NetworkManager manager = NetworkManager.Singleton;
            Require(EditorApplication.isPlaying && manager != null && manager.IsListening, "Нужна запущенная сессия");
            HashSet<ulong> controllers = new HashSet<ulong>();
            HashSet<int> slots = new HashSet<int>();
            int vehicles = 0;
            int projectiles = 0;
            foreach (NetworkObject item in manager.SpawnManager.SpawnedObjectsList)
            {
                Rigidbody body = item.GetComponent<Rigidbody>();
                if (body != null) Require(body.isKinematic != manager.IsServer, "Авторитет Rigidbody " + item.NetworkObjectId);
                NetworkVehicle vehicle = item.GetComponent<NetworkVehicle>();
                if (vehicle != null)
                {
                    vehicles++;
                    Require(controllers.Add(vehicle.ControllerClientId) && slots.Add(vehicle.Slot), "Уникальные управляющий клиент и место");
                    Require(item.OwnerClientId == NetworkManager.ServerClientId && !item.AutoObjectParentSync &&
                        !item.SyncOwnerTransformWhenParented && !item.AllowOwnerToParent, "Сервер владеет машиной, сетевая смена родителя отключена");
                    VehiclePhysics physics = item.GetComponent<VehiclePhysics>();
                    Require(physics.enabled == manager.IsServer && physics.Authority == manager.IsServer, "Авторитет VehiclePhysics");
                    if (manager.IsServer)
                        Require(manager.ConnectedClients.ContainsKey(vehicle.ControllerClientId), "Управляющий клиент подключён");
                    Debug.Log($"[MM NET CHECK] Машина {item.NetworkObjectId}, клиент {vehicle.ControllerClientId}, место {vehicle.Slot + 1}: {vehicle.CommandDiagnostics}.");
                    Debug.Log($"[MM NET SMOOTH] Машина {item.NetworkObjectId}: {item.GetComponent<NetworkPhysicsState>().SnapshotDiagnostics}.");
                }
                if (item.GetComponent<NetworkBallisticProjectile>() != null)
                {
                    projectiles++;
                    Require(item.OwnerClientId == NetworkManager.ServerClientId, "Снарядом владеет сервер");
                    Require(item.GetComponent<Collider>().enabled == manager.IsServer, "Клиентский снаряд без столкновений");
                }
            }
            Require(vehicles <= 2, "Не более двух машин");
            if (manager.IsServer)
            {
                Require(!manager.IsHost && vehicles == manager.ConnectedClients.Count, "Dedicated Server и одна машина на клиента");
                Require(UnityEngine.Object.FindObjectsByType<Camera>().Length == 0 &&
                    UnityEngine.Object.FindObjectsByType<AudioListener>().Length == 0 &&
                    UnityEngine.Object.FindObjectsByType<NetworkClientView>().Length == 0, "Сервер без камеры, звука и HUD");
            }
            Debug.Log($"[MM NET CHECK] Текущий снимок согласован: машин {vehicles}, снарядов {projectiles}. Для сценария двух клиентов ожидаются две машины.");
        }

        [MenuItem(NetworkSandboxEditor.ChecksMenu + "Send invalid commands from client")]
        public static void SendInvalidCommands()
        {
            NetworkClientView view = UnityEngine.Object.FindAnyObjectByType<NetworkClientView>();
            Require(EditorApplication.isPlaying && view != null, "Нужен подключённый Editor-клиент");
            view.SendInvalidCommandProbe();
        }

        [MenuItem(NetworkSandboxEditor.ChecksMenu + "Request client teleport")]
        public static void RequestClientTeleport()
        {
            NetworkManager manager = NetworkManager.Singleton;
            Require(EditorApplication.isPlaying && manager != null && manager.IsConnectedClient && !manager.IsServer, "Нужен подключённый Editor-клиент");
            foreach (NetworkObject item in manager.SpawnManager.SpawnedObjectsList)
            {
                if (!item.TryGetComponent(out NetworkVehicle vehicle) || !vehicle.IsLocalController) continue;
                item.GetComponent<NetworkPhysicsState>().RequestClientPoseProbe();
                Debug.Log("[MM NET CHECK] Ожидается ошибка прав записи NGO; локальная подмена позы должна исчезнуть на следующем кадре. Сервер и второй клиент не должны изменить положение машины.");
                return;
            }
            throw new InvalidOperationException("[MM NET CHECK] Нужна машина Editor-клиента.");
        }

        private static NetworkVehicleCommand Valid(uint sequence) => new NetworkVehicleCommand { Sequence = sequence, Aim = Vector3.forward };

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[MM NET CHECK] " + message);
        }
    }
}
