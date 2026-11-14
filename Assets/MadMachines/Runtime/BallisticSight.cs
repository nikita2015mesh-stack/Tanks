using UnityEngine;

namespace MadMachines
{
    internal static class BallisticSight
    {
        internal static readonly int[] Distances = { 25, 50, 100 };

        internal static float HorizontalDistance(Vector3 origin, Vector3 target) =>
            Vector3.ProjectOnPlane(target - origin, Vector3.up).magnitude;

        internal static Vector3 PointAtRange(Ray sight, Vector3 muzzle, float distance)
        {
            Vector3 forward = Vector3.ProjectOnPlane(sight.direction, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) return sight.GetPoint(distance);
            Vector3 offset = Vector3.ProjectOnPlane(muzzle - sight.origin, Vector3.up);
            float along = Vector3.Dot(offset, forward.normalized);
            float acrossSquared = Mathf.Max(0f, offset.sqrMagnitude - along * along);
            float ahead = Mathf.Sqrt(Mathf.Max(0f, distance * distance - acrossSquared));
            return sight.GetPoint(Mathf.Max(0.1f, (ahead + along) / forward.magnitude));
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

        internal static bool TryAimAt(Vector3 origin, Vector3 target, Vector3 carrierVelocity,
            ProjectileDefinition ammunition, out Vector3 direction, out float flightTime)
        {
            Vector3 offset = target - origin;
            direction = offset.normalized;
            flightTime = 0f;
            if (!ammunition.IsShell || offset.sqrMagnitude < 0.01f) return true;

            Vector3 gravity = ammunition.Acceleration;
            float speedSquared = ammunition.Speed * ammunition.Speed;
            float lower = 0f;
            // Find the first crossing to favor the flatter trajectory.
            const int samples = 48;
            for (int i = 1; i <= samples; i++)
            {
                float upper = ammunition.Lifetime * i / samples;
                if (SpeedError(upper) > 0f)
                {
                    lower = upper;
                    continue;
                }

                for (int j = 0; j < 12; j++)
                {
                    float middle = (lower + upper) * 0.5f;
                    if (SpeedError(middle) > 0f) lower = middle;
                    else upper = middle;
                }
                direction = RequiredDisplacement(upper).normalized;
                flightTime = upper;
                return true;
            }
            return false;

            Vector3 RequiredDisplacement(float time) => offset - PositionAt(Vector3.zero, carrierVelocity, gravity, time);

            float SpeedError(float time) => RequiredDisplacement(time).sqrMagnitude - speedSquared * time * time;
        }

        private static Vector3 PositionAt(Vector3 origin, Vector3 velocity, Vector3 gravity, float time)
        {
            // Match the gravity applied by the Rigidbody before it advances each step.
            return origin + velocity * time + gravity * (0.5f * time * (time + Time.fixedDeltaTime));
        }
    }
}
