using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class NetworkBallisticProjectile : NetworkBehaviour
    {
        public const float Radius = 0.15f;
        private const float Lifetime = 6f;
        private const float ImpactImpulse = 3200f;
        [SerializeField] private Material impactMaterial;
        private Rigidbody body;
        private Collider projectileCollider;
        private Rigidbody ownerBody;
        private Vector3 previous;
        private Vector3 flightVelocity;
        private double expires;
        private bool finished;

        public void Configure(Material material) => impactMaterial = material;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            projectileCollider = GetComponent<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            // Client projectiles are only interpolated render objects, never impact authorities.
            projectileCollider.enabled = IsServer;
            previous = transform.position;
            expires = Time.realtimeSinceStartupAsDouble + Lifetime;
        }

        public void Launch(NetworkVehicle owner, Vector3 velocity)
        {
            if (!IsServer || !IsSpawned) return;
            ownerBody = owner.Body;
            foreach (Collider collider in owner.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(projectileCollider, collider);
            body.linearVelocity = velocity;
            flightVelocity = velocity;
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || finished) return;
            if (Time.realtimeSinceStartupAsDouble >= expires)
            {
                finished = true;
                NetworkObject.Despawn(true);
                return;
            }
            // CCD plus a swept sphere prevents fast shots skipping thin static colliders.
            Vector3 delta = body.position - previous;
            if (delta.sqrMagnitude > 0.000001f)
            {
                RaycastHit nearest = default;
                bool found = false;
                foreach (RaycastHit hit in Physics.SphereCastAll(previous, Radius, delta.normalized, delta.magnitude, 1, QueryTriggerInteraction.Ignore))
                {
                    if ((ownerBody != null && hit.rigidbody == ownerBody) || hit.rigidbody == body) continue;
                    if (!found || hit.distance < nearest.distance)
                    {
                        nearest = hit;
                        found = true;
                    }
                }
                if (found)
                {
                    Impact(nearest.collider, nearest.point);
                    return;
                }
            }
            previous = body.position;
            flightVelocity = body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || !IsSpawned || finished || (ownerBody != null && collision.rigidbody == ownerBody)) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
            Impact(collision.collider, point);
        }

        private void Impact(Collider target, Vector3 point)
        {
            if (finished) return;
            finished = true;
            Rigidbody affected = target.attachedRigidbody;
            if (affected != null && !affected.isKinematic)
                affected.AddForceAtPosition(flightVelocity.normalized * ImpactImpulse, point, ForceMode.Impulse);
            NetworkVehicle vehicle = target.GetComponentInParent<NetworkVehicle>();
            if (vehicle != null)
            {
                vehicle.RegisterHit();
                Debug.Log($"[MM NET] Снаряд {NetworkObjectId}: попадание в машину {vehicle.NetworkObjectId}; всего попаданий {vehicle.Hits}.");
            }
            ShowImpactRpc(point);
            NetworkObject.Despawn(true);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        private void ShowImpactRpc(Vector3 point)
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Network impact presentation";
            flash.layer = 2;
            flash.GetComponent<Collider>().enabled = false;
            Destroy(flash.GetComponent<Collider>());
            flash.transform.position = point;
            flash.transform.localScale = Vector3.one * 0.8f;
            flash.GetComponent<Renderer>().sharedMaterial = impactMaterial;
            Destroy(flash, 0.25f);
        }
    }
}
