using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    public sealed class Projectile : MonoBehaviour
    {
        private ProjectileDefinition definition;
        private VehiclePhysics owner;
        private Collider projectileCollider;
        private Rigidbody body;
        private float expires;
        private float penetration;
        private bool detonated;
        private Vector3 previous;
        private Vector3 incomingVelocity;
        private int shotId;
        private int sweepLayers;
        private Action<Collider, Vector3> finish;
        private Transform model;
        private LineRenderer tracerLine;
        private Vector3 launchPoint;
        private List<Collider> penetratedPlates;

        public static Projectile Launch(ProjectileDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId,
            bool tracer = false)
        {
            GameObject shot = new GameObject(definition.Label + " projectile") { layer = 2 };
            SceneManager.MoveGameObjectToScene(shot, owner.gameObject.scene);
            shot.transform.position = position;
            shot.transform.localScale = Vector3.one * definition.Size * 2f;
            shot.AddComponent<SphereCollider>().radius = 0.5f;
            shot.AddComponent<Rigidbody>();
            AddTrail(shot, definition);
            Projectile projectile = shot.AddComponent<Projectile>();
            projectile.Initialize(definition, direction, owner, shotId);
            projectile.model = ProjectileVisual.Attach(shot, definition.Appearance);
            ProjectileVisual.PointAlong(projectile.model, projectile.body.linearVelocity);
            if (tracer && !Application.isBatchMode) projectile.CreateTracer();
            return projectile;
        }

        public void Initialize(ProjectileDefinition ammunition, Vector3 direction, VehiclePhysics shooter, int id,
            int impactLayers = 1, Action<Collider, Vector3> onFinished = null)
        {
            definition = ammunition;
            owner = shooter;
            shotId = id;
            finish = onFinished;
            sweepLayers = impactLayers;
            detonated = false;
            penetration = definition.Penetration;
            body = GetComponent<Rigidbody>();
            projectileCollider = GetComponent<Collider>();
            body.mass = definition.Mass;
            body.useGravity = definition.Gravity;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = definition.LaunchVelocity(direction, owner.Body.linearVelocity);
            incomingVelocity = body.linearVelocity;
            if (definition.TimedBounce)
            {
                PhysicsMaterial bounce = new PhysicsMaterial("Rubber grenade") { bounciness = 0.65f, bounceCombine = PhysicsMaterialCombine.Maximum };
                projectileCollider.material = bounce;
            }
            foreach (Collider collider in owner.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(projectileCollider, collider);
            expires = Time.time + definition.Lifetime;
            previous = body.position;
            launchPoint = previous;
        }

        public static void AddTrail(GameObject shot, ProjectileDefinition definition)
        {
            if (Application.isBatchMode || !definition.IsShell) return;
            DrivingEffects effects = DrivingEffects.In(shot.scene);
            TrailRenderer trail = shot.AddComponent<TrailRenderer>();
            trail.sharedMaterial = effects.WakeMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.minVertexDistance = 0.08f;

            bool penetrator = definition.Appearance == ProjectileAppearance.Penetrator;
            Color tail = penetrator ? new Color(0.67f, 0.77f, 0.8f) : new Color(0.68f, 0.71f, 0.72f);
            trail.startColor = new Color(tail.r, tail.g, tail.b, 0.52f);
            trail.widthCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
            trail.widthMultiplier = penetrator ? 0.16f : 0.28f;
            trail.time = penetrator ? 0.12f : 0.20f;
            tail.a = 0f;
            trail.endColor = tail;
        }

        private void CreateTracer()
        {
            tracerLine = gameObject.AddComponent<LineRenderer>();
            tracerLine.sharedMaterial = DrivingEffects.In(gameObject.scene).TracerMaterial;
            tracerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracerLine.receiveShadows = false;
            tracerLine.startColor = new Color(1f, 0.26f, 0.06f, 0.04f);
            tracerLine.endColor = new Color(1f, 0.78f, 0.38f, 1f);
            tracerLine.startWidth = 0.026f;
            tracerLine.endWidth = 0.092f;
            tracerLine.useWorldSpace = true;
            tracerLine.positionCount = 2;
            tracerLine.SetPosition(0, launchPoint);
            tracerLine.SetPosition(1, launchPoint);
        }

        private void LateUpdate()
        {
            if (model != null) ProjectileVisual.PointAlong(model, body.linearVelocity);
            if (tracerLine != null)
            {
                Vector3 position = transform.position;
                Vector3 direction = body.linearVelocity.normalized;
                float length = Mathf.Clamp(Vector3.Dot(position - launchPoint, direction), 0f, 3.2f);
                tracerLine.SetPosition(0, position - direction * length);
                tracerLine.SetPosition(1, position);
            }
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
                ResolveImpact(hit.collider, hit.point, hit.normal, body.linearVelocity);
                return;
            }
            previous = body.position;
            incomingVelocity = body.linearVelocity + definition.Acceleration * Time.fixedDeltaTime;
        }

        private bool Sweep(Vector3 delta, out RaycastHit nearest)
        {
            if (finish == null && penetratedPlates == null)
                return Physics.SphereCast(previous, definition.Size, delta.normalized, out nearest,
                    delta.magnitude, sweepLayers, QueryTriggerInteraction.Ignore);

            nearest = default;
            float distance = float.PositiveInfinity;
            Rigidbody ownerBody = owner != null ? owner.Body : null;
            foreach (RaycastHit hit in Physics.SphereCastAll(previous, definition.Size, delta.normalized,
                delta.magnitude, sweepLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == body || (ownerBody != null && hit.rigidbody == ownerBody) || hit.distance >= distance ||
                    (penetratedPlates != null && penetratedPlates.Contains(hit.collider))) continue;
                nearest = hit;
                distance = hit.distance;
            }
            return nearest.collider != null;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isActiveAndEnabled && !detonated && !definition.TimedBounce)
            {
                if (collision.contactCount == 0)
                {
                    Detonate(collision.collider);
                    return;
                }
                ContactPoint contact = collision.GetContact(0);
                ResolveImpact(collision.collider, contact.point, contact.normal, incomingVelocity);
            }
        }

        private void ResolveImpact(Collider direct, Vector3 point, Vector3 normal, Vector3 incoming)
        {
            if (penetratedPlates != null && penetratedPlates.Contains(direct)) return;
            ArmorPlate plate = direct.GetComponent<ArmorPlate>();
            if (plate == null)
            {
                Detonate(direct, point);
                return;
            }

            if (Vector3.Dot(normal, incoming) > 0f) normal = -normal;
            float alignment = Mathf.Clamp01(-Vector3.Dot(incoming.normalized, normal));
            ArmorImpact result = plate.Resolve(penetration, alignment, out float resistance);
            plate.Report(definition.Label, result, alignment, owner);
            PlaygroundEffects.ShowArmorImpact(gameObject.scene, point, normal, result);
            if (result == ArmorImpact.Stopped)
            {
                body.position = point + normal * (definition.Size + 0.02f);
                Detonate(direct, point, armorContact: true);
                return;
            }

            if (result == ArmorImpact.Penetrated)
            {
                penetration -= resistance;
                penetratedPlates ??= new List<Collider>();
                penetratedPlates.Add(direct);
                Physics.IgnoreCollision(projectileCollider, direct);
                body.linearVelocity = incoming * Mathf.Lerp(0.55f, 0.86f, penetration / definition.Penetration);
            }
            else
            {
                penetration *= 0.5f;
                body.linearVelocity = Vector3.Reflect(incoming, normal) * 0.56f;
            }

            // Continue from the entry side. A penetrator can now cross this collider.
            body.position = point + normal * (definition.Size + 0.02f);
            previous = body.position;
            incomingVelocity = body.linearVelocity + definition.Acceleration * Time.fixedDeltaTime;
        }

        private void Detonate(Collider direct, Vector3? impactPoint = null, bool armorContact = false)
        {
            if (detonated) return;
            detonated = true;
            Vector3 position = body.position;
            Target directTarget = null;
            if (direct != null)
            {
                directTarget = direct.GetComponentInParent<Target>();
                if (directTarget != null) directTarget.Hit(definition.Damage, owner, shotId);
            }
            if (definition.BlastRadius > 0f)
            {
                HashSet<Target> damaged = new HashSet<Target>();
                HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();
                if (directTarget != null) damaged.Add(directTarget);
                foreach (Collider collider in Physics.OverlapSphere(position, definition.BlastRadius, ~0, QueryTriggerInteraction.Ignore))
                {
                    Rigidbody affected = collider.attachedRigidbody;
                    Target target = collider.GetComponentInParent<Target>();
                    if ((affected == null || affected == body) && target == null) continue;
                    Vector3 closest = collider.ClosestPoint(position);
                    Vector3 ray = closest - position;
                    if (ray.magnitude > 0.15f && Physics.Raycast(position + ray.normalized * 0.08f, ray.normalized, out RaycastHit obstruction, ray.magnitude - 0.1f, 1, QueryTriggerInteraction.Ignore) && obstruction.collider != collider) continue;
                    if (affected != null && affected != body && pushed.Add(affected))
                        affected.AddExplosionForce(definition.BlastImpulse, position, definition.BlastRadius, 1.5f, ForceMode.Impulse);
                    if (target != null && damaged.Add(target)) target.Hit(definition.Damage, owner, shotId);
                }
            }
            Vector3 effectPoint = impactPoint ?? position;
            if (finish != null) finish(direct, effectPoint);
            else
            {
                if (!armorContact || definition.BlastRadius > 0f)
                    PlaygroundEffects.ShowImpact(gameObject.scene, definition, effectPoint, direct != null);
                Destroy(gameObject);
            }
        }
    }
}
