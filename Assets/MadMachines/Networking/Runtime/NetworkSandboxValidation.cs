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
                throw new InvalidOperationException("NetworkVehicle is missing its network adapter, physics component, or collider.");
            vehicle.ValidateConfiguration(projectile);
        }

        public static void ValidateProjectile(GameObject prefab)
        {
            ValidateReplication(prefab);
            NetworkBallisticProjectile projectile = prefab.GetComponent<NetworkBallisticProjectile>();
            if (projectile == null || !projectile.enabled || prefab.GetComponent<SphereCollider>() == null)
                throw new InvalidOperationException("NetworkProjectile is missing its network adapter or sphere collider.");
        }

        private static void ValidateReplication(GameObject prefab)
        {
            if (prefab == null || !prefab.activeSelf || prefab.GetComponent<NetworkObject>() == null || prefab.GetComponent<Rigidbody>() == null)
                throw new InvalidOperationException("A network prefab is missing, inactive, or has no NetworkObject or Rigidbody. Create NetworkSandbox from the Networking menu.");
            if (prefab.GetComponent<NetworkObject>().PrefabIdHash == 0)
                throw new InvalidOperationException(prefab.name + ": missing network prefab ID. Check the prefab import and save it in Unity.");
            NetworkPhysicsState replication = prefab.GetComponent<NetworkPhysicsState>();
            if (replication == null || !replication.enabled || prefab.GetComponentInChildren<NetworkTransform>(true) != null ||
                prefab.GetComponentInChildren<NetworkRigidbody>(true) != null)
                throw new InvalidOperationException(prefab.name + ": expected NetworkPhysicsState without NetworkTransform or NetworkRigidbody. Run Upgrade sandbox replication.");
            if (prefab.GetComponentsInChildren<NetworkObject>(true).Length != 1)
                throw new InvalidOperationException(prefab.name + ": nested NetworkObjects are not supported.");
            if (prefab.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                throw new InvalidOperationException(prefab.name + ": expected exactly one Rigidbody on the root.");
        }
    }
}
