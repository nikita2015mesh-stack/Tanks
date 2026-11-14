using UnityEngine;

namespace MadMachines
{
    internal static class BallisticSight
    {
        internal static readonly int[] Distances = { 25, 50, 100 };

        internal static Vector3 ZeroDirection(Vector3 view, ProjectileDefinition ammunition, float distance)
        {
            float gravity = -ammunition.Acceleration.y;
            Vector3 horizontal = Vector3.ProjectOnPlane(view, Vector3.up);
            float speedSquared = ammunition.Speed * ammunition.Speed;
            if (gravity <= 0f || horizontal.sqrMagnitude < 0.001f) return view;

            float sightHeight = distance * view.y / horizontal.magnitude;
            float angle = Mathf.Atan2(view.y, horizontal.magnitude);
            for (int i = 0; i < 2; i++)
            {
                float time = distance / (ammunition.Speed * Mathf.Max(0.15f, Mathf.Cos(angle)));
                float height = sightHeight + 0.5f * gravity * time * Time.fixedDeltaTime;
                float discriminant = speedSquared * speedSquared - gravity * (gravity * distance * distance + 2f * height * speedSquared);
                if (discriminant < 0f) return view;
                angle = Mathf.Atan((speedSquared - Mathf.Sqrt(discriminant)) / (gravity * distance));
            }
            return horizontal.normalized * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
        }

        internal static bool TryGetMark(Camera view, Vector3 origin, Vector3 velocity, ProjectileDefinition ammunition,
            float distance, out Vector3 screenPoint)
        {
            screenPoint = default;
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            float speed = Vector3.Dot(velocity, forward);
            if (speed <= 0f) return false;
            float time = distance / speed;
            if (time > ammunition.Lifetime) return false;
            Vector3 position = PositionAt(origin, velocity, ammunition.Acceleration, time);
            screenPoint = view.WorldToScreenPoint(position);
            return screenPoint.z > 0f;
        }

        internal static bool PredictImpact(PhysicsScene scene, Vector3 origin, Vector3 velocity, ProjectileDefinition ammunition,
            out RaycastHit hit)
        {
            const float sampleInterval = 0.06f;
            Vector3 position = origin;
            for (float elapsed = 0f; elapsed < ammunition.Lifetime;)
            {
                elapsed = Mathf.Min(elapsed + sampleInterval, ammunition.Lifetime);
                Vector3 next = PositionAt(origin, velocity, ammunition.Acceleration, elapsed);
                Vector3 step = next - position;
                float length = step.magnitude;
                if (length > 0.0001f && scene.SphereCast(position, ammunition.Size, step / length, out hit,
                    length, 1, QueryTriggerInteraction.Ignore))
                    return true;
                position = next;
            }
            hit = default;
            return false;
        }

        private static Vector3 PositionAt(Vector3 origin, Vector3 velocity, Vector3 gravity, float time)
        {
            // Match the gravity applied by the Rigidbody before it advances each step.
            return origin + velocity * time + gravity * (0.5f * time * (time + Time.fixedDeltaTime));
        }
    }
}
