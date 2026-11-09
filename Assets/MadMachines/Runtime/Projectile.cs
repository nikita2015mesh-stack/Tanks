using System;
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
        private int sweepLayers;
        private Action<Collider, Vector3> finish;

        public static Projectile Launch(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId)
        {
            GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.name = definition.Label + " projectile";
            shot.layer = 2;
            shot.transform.position = position;
            shot.transform.localScale = Vector3.one * definition.Size * 2f;
            shot.GetComponent<Renderer>().sharedMaterial = PlaygroundEffects.Material(definition.Color);
            shot.AddComponent<Rigidbody>();
            AddTrail(shot, definition);
            Projectile projectile = shot.AddComponent<Projectile>();
            projectile.Initialize(definition, direction, owner, shotId);
            return projectile;
        }

        public void Initialize(WeaponDefinition weapon, Vector3 direction, VehiclePhysics shooter, int id,
            int impactLayers = 1, Action<Collider, Vector3> onFinished = null)
        {
            definition = weapon;
            owner = shooter;
            shotId = id;
            finish = onFinished;
            sweepLayers = impactLayers;
            detonated = false;
            body = GetComponent<Rigidbody>();
            body.mass = definition.Mass;
            body.useGravity = definition.Gravity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = direction * definition.Speed + owner.Body.linearVelocity;
            if (definition.TimedBounce)
            {
                PhysicsMaterial bounce = new PhysicsMaterial("Rubber grenade") { bounciness = 0.65f, bounceCombine = PhysicsMaterialCombine.Maximum };
                GetComponent<Collider>().material = bounce;
            }
            Collider shotCollider = GetComponent<Collider>();
            foreach (Collider collider in owner.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(shotCollider, collider);
            expires = Time.time + definition.Lifetime;
            previous = body.position;
        }

        public static void AddTrail(GameObject shot, WeaponDefinition definition)
        {
            TrailRenderer trail = shot.AddComponent<TrailRenderer>();
            trail.sharedMaterial = PlaygroundEffects.Material(definition.Color);
            trail.startWidth = definition.Size;
            trail.endWidth = 0f;
            trail.time = definition.Gravity ? 0.35f : 0.12f;
        }

        private void FixedUpdate()
        {
            if (detonated) return;
            if (Time.time >= expires)
            {
                Detonate(null);
                return;
            }
            // Swept query supplements PhysX CCD for fast rounds and thin targets.
            Vector3 delta = body.position - previous;
            if (!definition.TimedBounce && delta.sqrMagnitude > 0.0001f && Sweep(delta, out RaycastHit hit))
            {
                body.position = hit.point;
                Detonate(hit.collider);
                return;
            }
            previous = body.position;
        }

        private bool Sweep(Vector3 delta, out RaycastHit nearest)
        {
            if (finish == null)
                return Physics.SphereCast(previous, definition.Size, delta.normalized, out nearest,
                    delta.magnitude, sweepLayers, QueryTriggerInteraction.Ignore);

            nearest = default;
            float distance = float.PositiveInfinity;
            Rigidbody ownerBody = owner != null ? owner.Body : null;
            foreach (RaycastHit hit in Physics.SphereCastAll(previous, definition.Size, delta.normalized,
                delta.magnitude, sweepLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == body || (ownerBody != null && hit.rigidbody == ownerBody) || hit.distance >= distance) continue;
                nearest = hit;
                distance = hit.distance;
            }
            return nearest.collider != null;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isActiveAndEnabled && !definition.TimedBounce) Detonate(collision.collider);
        }

        private void Detonate(Collider direct)
        {
            if (detonated) return;
            detonated = true;
            Vector3 position = body.position;
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
                    Rigidbody affected = collider.attachedRigidbody;
                    Target target = collider.GetComponentInParent<Target>();
                    if ((affected == null || affected == body) && target == null) continue;
                    Vector3 closest = collider.ClosestPoint(position);
                    Vector3 ray = closest - position;
                    if (ray.magnitude > 0.15f && Physics.Raycast(position + ray.normalized * 0.08f, ray.normalized, out RaycastHit obstruction, ray.magnitude - 0.1f, 1, QueryTriggerInteraction.Ignore) && obstruction.collider != collider) continue;
                    if (affected != null && affected != body && pushed.Add(affected))
                        affected.AddExplosionForce(definition.Impulse, position, definition.Radius, 1.5f, ForceMode.Impulse);
                    if (target != null && damaged.Add(target)) target.Hit(definition.Damage, owner, shotId);
                }
            }
            if (finish != null) finish(direct, position);
            else
            {
                PlaygroundEffects.Flash(position, definition.Color, Mathf.Max(0.4f, definition.Radius * 1.5f), 0.35f);
                Destroy(gameObject);
            }
        }
    }
}
