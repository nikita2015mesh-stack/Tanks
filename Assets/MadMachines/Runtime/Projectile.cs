using System.Collections.Generic;
using UnityEngine;

namespace MadMachines
{
    public sealed class Projectile : MonoBehaviour
    {
        private WeaponDefinition definition;
        private VehiclePhysics owner;
        private Rigidbody body;
        private float expires;
        private bool detonated;
        private Vector3 previous;
        private int shotId;

        public static Projectile Launch(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId)
        {
            GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.name = definition.Label + " projectile";
            shot.layer = 2;
            shot.transform.position = position;
            shot.transform.localScale = Vector3.one * definition.Size * 2f;
            shot.GetComponent<Renderer>().sharedMaterial = PlaygroundEffects.Material(definition.Color);
            Rigidbody body = shot.AddComponent<Rigidbody>();
            body.mass = definition.Mass;
            body.useGravity = definition.Gravity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = direction * definition.Speed + owner.Body.linearVelocity;
            if (definition.TimedBounce)
            {
                PhysicsMaterial bounce = new PhysicsMaterial("Rubber grenade") { bounciness = 0.65f, bounceCombine = PhysicsMaterialCombine.Maximum };
                shot.GetComponent<Collider>().material = bounce;
            }
            foreach (Collider collider in owner.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(shot.GetComponent<Collider>(), collider);
            TrailRenderer trail = shot.AddComponent<TrailRenderer>();
            trail.sharedMaterial = PlaygroundEffects.Material(definition.Color);
            trail.startWidth = definition.Size;
            trail.endWidth = 0f;
            trail.time = definition.Gravity ? 0.35f : 0.12f;
            Projectile projectile = shot.AddComponent<Projectile>();
            projectile.definition = definition;
            projectile.owner = owner;
            projectile.body = body;
            projectile.expires = Time.time + definition.Lifetime;
            projectile.previous = position;
            projectile.shotId = shotId;
            return projectile;
        }

        private void FixedUpdate()
        {
            if (Time.time >= expires)
            {
                Detonate(null);
                return;
            }
            // Swept query supplements PhysX CCD for fast rounds and thin targets.
            Vector3 delta = body.position - previous;
            if (!definition.TimedBounce && delta.sqrMagnitude > 0.0001f && Physics.SphereCast(previous, definition.Size, delta.normalized, out RaycastHit hit, delta.magnitude, 1, QueryTriggerInteraction.Ignore))
            {
                body.position = hit.point;
                Detonate(hit.collider);
            }
            previous = body.position;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!definition.TimedBounce) Detonate(collision.collider);
        }

        private void Detonate(Collider direct)
        {
            if (detonated) return;
            detonated = true;
            Vector3 position = transform.position;
            HashSet<Target> damaged = new HashSet<Target>();
            HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();
            if (direct != null)
            {
                Target target = direct.GetComponentInParent<Target>();
                if (target != null)
                {
                    target.Hit(definition.Damage, owner, shotId);
                    damaged.Add(target);
                }
            }
            if (definition.Radius > 0f)
            {
                foreach (Collider collider in Physics.OverlapSphere(position, definition.Radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    Vector3 closest = collider.ClosestPoint(position);
                    Vector3 ray = closest - position;
                    if (ray.magnitude > 0.15f && Physics.Raycast(position + ray.normalized * 0.08f, ray.normalized, out RaycastHit obstruction, ray.magnitude - 0.1f, 1, QueryTriggerInteraction.Ignore) && obstruction.collider != collider) continue;
                    Rigidbody affected = collider.attachedRigidbody;
                    if (affected != null && affected != body && pushed.Add(affected))
                        affected.AddExplosionForce(definition.Impulse, position, definition.Radius, 1.5f, ForceMode.Impulse);
                    Target target = collider.GetComponentInParent<Target>();
                    if (target != null && damaged.Add(target)) target.Hit(definition.Damage, owner, shotId);
                }
            }
            PlaygroundEffects.Flash(position, definition.Color, Mathf.Max(0.4f, definition.Radius * 1.5f), 0.35f);
            Destroy(gameObject);
        }
    }
}
