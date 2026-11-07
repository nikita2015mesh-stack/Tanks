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
        [MenuItem("Tools/Mad Machines/Network/Checks/Run command policy checks")]
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
            Require(buffer.Sample(1.02, out command) && command.Fire && command.Throttle == 0, "Огонь сохранён, движение последнее");
            Require(buffer.Sample(1.04, out command) && !command.Fire, "Огонь не накапливается");
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

            NetworkShotCooldown cooldown = new NetworkShotCooldown();
            Require(cooldown.TryBegin(1), "Первый выстрел");
            for (int i = 0; i < 100; i++) Require(!cooldown.TryBegin(1.01), "Спам не сокращает перезарядку");
            Require(!cooldown.TryBegin(2.14) && cooldown.TryBegin(2.16), "Серверная перезарядка");
            Require(cooldown.TryBegin(20) && !cooldown.TryBegin(20), "Нет залпа накопленных выстрелов после задержки");

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
            Debug.Log("[MM NET CHECK] Проверки команд, перезарядки, интерполяции и аргументов завершены. RPC, PhysX и соединение здесь не проверяются.");
        }

        [MenuItem("Tools/Mad Machines/Network/Checks/Inspect running session")]
        public static void InspectSession()
        {
            NetworkManager manager = NetworkManager.Singleton;
            Require(EditorApplication.isPlaying && manager != null && manager.IsListening, "Нужна запущенная сессия");
            HashSet<ulong> owners = new HashSet<ulong>();
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
                    Require(owners.Add(item.OwnerClientId) && slots.Add(vehicle.Slot), "Уникальные владелец и место");
                    Require(item.IsPlayerObject, "Машина назначена PlayerObject");
                    VehiclePhysics physics = item.GetComponent<VehiclePhysics>();
                    Require(physics.enabled == manager.IsServer && physics.Authority == manager.IsServer, "Авторитет VehiclePhysics");
                    if (manager.IsServer)
                        Require(manager.ConnectedClients.TryGetValue(item.OwnerClientId, out NetworkClient client) && client.PlayerObject == item, "Реестр участников NGO");
                    Debug.Log($"[MM NET CHECK] Машина {item.NetworkObjectId}, владелец {item.OwnerClientId}, место {vehicle.Slot + 1}: {vehicle.CommandDiagnostics}.");
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
                Require(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length == 0 &&
                    UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0 &&
                    UnityEngine.Object.FindObjectsByType<NetworkClientView>(FindObjectsSortMode.None).Length == 0, "Сервер без камеры, звука и HUD");
            }
            Debug.Log($"[MM NET CHECK] Текущий снимок согласован: машин {vehicles}, снарядов {projectiles}. Для сценария двух клиентов ожидаются две машины.");
        }

        [MenuItem("Tools/Mad Machines/Network/Checks/Send invalid commands from client")]
        public static void SendInvalidCommands()
        {
            NetworkClientView view = UnityEngine.Object.FindFirstObjectByType<NetworkClientView>();
            Require(EditorApplication.isPlaying && view != null, "Нужен подключённый Editor-клиент");
            view.SendInvalidCommandProbe();
        }

        [MenuItem("Tools/Mad Machines/Network/Checks/Request client teleport")]
        public static void RequestClientTeleport()
        {
            NetworkManager manager = NetworkManager.Singleton;
            Require(EditorApplication.isPlaying && manager != null && manager.IsConnectedClient && !manager.IsServer && manager.LocalClient.PlayerObject != null, "Нужна машина Editor-клиента");
            NetworkObject player = manager.LocalClient.PlayerObject;
            player.GetComponent<NetworkPhysicsState>().RequestClientPoseProbe();
            Debug.Log("[MM NET CHECK] Ожидается ошибка прав записи NGO; локальная подмена позы должна исчезнуть на следующем кадре. Сервер и второй клиент не должны изменить положение машины.");
        }

        private static NetworkVehicleCommand Valid(uint sequence) => new NetworkVehicleCommand { Sequence = sequence, Aim = Vector3.forward };

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[MM NET CHECK] " + message);
        }
    }
}
