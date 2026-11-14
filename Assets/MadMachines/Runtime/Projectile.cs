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
        private Vector3 guidancePoint;
        private bool hasGuidancePoint;
        private TrailRenderer smokeTrail;
        public bool InFlight => !detonated && isActiveAndEnabled;
        public float RemainingLifetimeFraction => InFlight ? Mathf.Clamp01((expires - Time.time) / definition.Lifetime) : 0f;
        public bool IsGuiding { get; private set; }

        public static Projectile Launch(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId)
        {
            GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shot.name = definition.Label + " projectile";
            shot.layer = 2;
            shot.transform.position = position;
            if (definition.Guided)
            {
                shot.transform.rotation = Quaternion.LookRotation(direction);
                shot.GetComponent<SphereCollider>().radius = definition.Size;
                shot.GetComponent<Renderer>().enabled = false;
                AddRocketVisual(shot.transform);
            }
            else
            {
                shot.transform.localScale = Vector3.one * definition.Size * 2f;
                shot.GetComponent<Renderer>().sharedMaterial = PlaygroundEffects.Material(definition.Color);
                AddTrail(shot, definition);
            }
            shot.AddComponent<Rigidbody>();
            Projectile projectile = shot.AddComponent<Projectile>();
            projectile.Initialize(definition, direction, owner, shotId);
            if (definition.Guided) projectile.smokeTrail = AddRocketSmoke(shot.transform);
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
            body.linearVelocity = direction * definition.Speed;
            if (!definition.Guided) body.linearVelocity += owner.Body.linearVelocity;
            IsGuiding = definition.Guided;
            if (IsGuiding) body.interpolation = RigidbodyInterpolation.Interpolate;
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

        public void SetGuidancePoint(Vector3 point)
        {
            guidancePoint = point;
            hasGuidancePoint = true;
        }

        public void StopGuidance()
        {
            IsGuiding = false;
        }

        internal static void AddRocketVisual(Transform root)
        {
            AddRocketPart(root, "Rocket body", PrimitiveType.Capsule, Vector3.zero,
                new Vector3(0.21f, 0.52f, 0.21f), Quaternion.Euler(90f, 0f, 0f), new Color(0.26f, 0.32f, 0.18f));
            AddRocketPart(root, "Nose marking", PrimitiveType.Sphere, new Vector3(0f, 0f, 0.32f),
                new Vector3(0.22f, 0.22f, 0.4f), Quaternion.identity, new Color(0.95f, 0.45f, 0.08f));
            AddRocketPart(root, "Horizontal fins", PrimitiveType.Cube, new Vector3(0f, 0f, -0.29f),
                new Vector3(0.38f, 0.035f, 0.23f), Quaternion.identity, new Color(0.13f, 0.16f, 0.12f));
            AddRocketPart(root, "Vertical fins", PrimitiveType.Cube, new Vector3(0f, 0f, -0.29f),
                new Vector3(0.035f, 0.38f, 0.23f), Quaternion.identity, new Color(0.13f, 0.16f, 0.12f));
        }

        private static void AddRocketPart(Transform root, string label, PrimitiveType shape,
            Vector3 position, Vector3 scale, Quaternion rotation, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = label;
            part.layer = root.gameObject.layer;
            part.transform.SetParent(root, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial = PlaygroundEffects.Material(color);
        }

        private static TrailRenderer AddRocketSmoke(Transform root)
        {
            GameObject smoke = new GameObject("Rocket smoke");
            smoke.transform.SetParent(root, false);
            smoke.transform.localPosition = new Vector3(0f, 0f, -0.48f);
            TrailRenderer trail = smoke.AddComponent<TrailRenderer>();
            trail.sharedMaterial = PlaygroundEffects.Material(Color.white);
            trail.startColor = new Color(1f, 0.94f, 0.76f);
            trail.endColor = new Color(0.48f, 0.49f, 0.47f);
            trail.startWidth = 0.15f;
            trail.endWidth = 0.03f;
            trail.time = 1f;
            return trail;
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
                body.position = definition.Guided ? previous + delta.normalized * hit.distance : hit.point;
                Detonate(hit.collider);
                return;
            }
            previous = body.position;

            if (IsGuiding && hasGuidancePoint)
            {
                Vector3 toAim = guidancePoint - body.position;
                Vector3 velocity = body.linearVelocity;
                if (Vector3.Dot(toAim, velocity) > 0f)
                {
                    float turn = definition.SteeringDegreesPerSecond * Mathf.Deg2Rad * Time.fixedDeltaTime;
                    Vector3 heading = Vector3.RotateTowards(velocity.normalized, toAim.normalized, turn, 0f);
                    body.linearVelocity = heading * velocity.magnitude;
                    body.MoveRotation(Quaternion.LookRotation(heading));
                }
            }
        }

        private bool Sweep(Vector3 delta, out RaycastHit nearest)
        {
            if (finish == null && !definition.Guided)
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
            StopGuidance();
            if (smokeTrail != null)
            {
                smokeTrail.emitting = false;
                smokeTrail.transform.SetParent(null, true);
                Destroy(smokeTrail.gameObject, smokeTrail.time);
            }
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
