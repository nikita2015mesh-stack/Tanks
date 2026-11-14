using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    // Set the size before NetworkPhysicsState caches the prefab scale.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class NetworkBallisticProjectile : NetworkBehaviour
    {
        [SerializeField] private WeaponDefinition definition;
        private SphereCollider projectileCollider;
        private Projectile simulation;
        private NetworkSandboxSession match;
        private int shooterSlot;
        private Transform model;

        public WeaponDefinition Definition => definition;
        private ProjectileDefinition Ammunition => definition.GetProjectile();

        public void Configure(WeaponDefinition weapon) => definition = weapon;

        private void Awake()
        {
            transform.localScale = Vector3.one * Ammunition.Size * 2f;
            projectileCollider = GetComponent<SphereCollider>();
            projectileCollider.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            // Clients only display the replicated projectile.
            projectileCollider.enabled = IsServer;
            if (IsClient && !IsServer)
            {
                model = ProjectileVisual.Attach(gameObject, Ammunition.Appearance);
                Projectile.AddTrail(gameObject, Ammunition);
            }
        }

        internal void ApplyPresentation(Vector3 velocity) => ProjectileVisual.PointAlong(model, velocity);

        public override void OnNetworkDespawn()
        {
            projectileCollider.enabled = false;
            if (simulation != null) simulation.enabled = false;
            if (model != null)
            {
                model.gameObject.SetActive(false);
                Destroy(model.gameObject);
                model = null;
            }
        }

        public void Launch(NetworkVehicle shooter, VehiclePhysics vehicle, Vector3 direction, int shotId, NetworkSandboxSession deathmatch)
        {
            match = deathmatch;
            shooterSlot = shooter.Slot;
            simulation = gameObject.AddComponent<Projectile>();
            simulation.Initialize(Ammunition, direction, vehicle, shotId, NetworkVehicle.HitLayers, Finish);
            Debug.Log($"[MM NET] Spawned projectile {NetworkObjectId} from vehicle {shooter.NetworkObjectId}.");
        }

        private void Finish(Collider target, Vector3 point)
        {
            if (!IsServer || !IsSpawned || NetworkManager.ShutdownInProgress) return;
            NetworkVehicle vehicle = target != null ? target.GetComponentInParent<NetworkVehicle>() : null;
            if (vehicle != null)
            {
                vehicle.RegisterHit();
                Debug.Log($"[MM NET] Projectile {NetworkObjectId} hit vehicle {vehicle.NetworkObjectId}. Total hits: {vehicle.Hits}.");
                if (match != null) match.ApplyDeathmatchHit(shooterSlot, vehicle, Ammunition.Damage);
            }
            else if (target != null) Debug.Log($"[MM NET] Projectile {NetworkObjectId} collided with {target.name}.");
            else Debug.Log($"[MM NET] Projectile {NetworkObjectId} expired.");
            ShowImpactRpc(point, target != null);
            NetworkObject.Despawn(true);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ShowImpactRpc(Vector3 point, bool hit)
        {
            if (IsClient && !IsServer && !NetworkManager.ShutdownInProgress)
                PlaygroundEffects.ShowImpact(gameObject.scene, Ammunition, point, hit);
        }
    }
}
