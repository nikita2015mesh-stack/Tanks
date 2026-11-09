using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace MadMachines.Networking
{
    // Shared by startup and editor validation. No mutation and no scene generation.
    public static class NetworkSandboxValidation
    {
        public static void ValidateVehicle(GameObject prefab, GameObject projectile)
        {
            ValidateReplication(prefab);
            NetworkVehicle vehicle = prefab.GetComponent<NetworkVehicle>();
            if (vehicle == null || !vehicle.enabled || prefab.GetComponent<VehiclePhysics>() == null || prefab.GetComponentInChildren<Collider>() == null)
                throw new InvalidOperationException("NetworkVehicle: нет адаптера, физики или коллайдера.");
            vehicle.ValidateConfiguration(projectile);
        }

        public static void ValidateProjectile(GameObject prefab)
        {
            ValidateReplication(prefab);
            NetworkBallisticProjectile projectile = prefab.GetComponent<NetworkBallisticProjectile>();
            if (projectile == null || !projectile.enabled || prefab.GetComponent<SphereCollider>() == null)
                throw new InvalidOperationException("NetworkProjectile: нет адаптера или сферического коллайдера.");
        }

        private static void ValidateReplication(GameObject prefab)
        {
            if (prefab == null || !prefab.activeSelf || prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<Rigidbody>() == null)
                throw new InvalidOperationException("Сетевой prefab отсутствует, выключен или не содержит NetworkObject/Rigidbody. Создайте NetworkSandbox через меню Network.");
            if (prefab.GetComponent<NetworkObject>().PrefabIdHash == 0)
                throw new InvalidOperationException(prefab.name + ": отсутствует сетевой идентификатор prefab. Проверьте импорт и сохранение prefab в Unity.");
            NetworkPhysicsState replication = prefab.GetComponent<NetworkPhysicsState>();
            if (replication == null || !replication.enabled || prefab.GetComponentInChildren<NetworkTransform>(true) != null ||
                prefab.GetComponentInChildren<NetworkRigidbody>(true) != null)
                throw new InvalidOperationException(prefab.name + ": нужен NetworkPhysicsState без NetworkTransform/NetworkRigidbody. Выполните Upgrade sandbox replication.");
            if (prefab.GetComponentsInChildren<NetworkObject>(true).Length != 1)
                throw new InvalidOperationException(prefab.name + ": вложенные NetworkObject не поддерживаются.");
            if (prefab.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                throw new InvalidOperationException(prefab.name + ": допускается только один корневой Rigidbody.");
        }
    }
}
