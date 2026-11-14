using UnityEngine;

namespace MadMachines.Networking
{
    internal sealed class DeathmatchBot
    {
        private const float EngagementDistance = 32f;
        private const float SightDistance = 85f;
        private const float ReverseTime = 1.4f;
        private double nextDecision;
        private double reverseUntil;
        private double nextShot;
        private bool hasFiringSolution;
        private float stuckTime;
        private bool moving;
        private NetworkVehicleCommand command;

        internal bool TryGetCommand(NetworkVehicle self, NetworkVehicle opponent, double now, out NetworkVehicleCommand result)
        {
            result = default;
            if (opponent == null)
            {
                nextDecision = 0;
                reverseUntil = 0;
                stuckTime = 0;
                moving = false;
                hasFiringSolution = false;
                return false;
            }
            if (now >= nextDecision)
            {
                const float decisionInterval = 0.15f;
                nextDecision = now + decisionInterval;
                stuckTime = moving && self.Body.linearVelocity.sqrMagnitude < 0.5f ? stuckTime + decisionInterval : 0f;
                if (stuckTime > 1.8f && now >= reverseUntil)
                {
                    reverseUntil = now + ReverseTime;
                    stuckTime = 0f;
                }
                command = Decide(self, opponent, now < reverseUntil, out bool canFire);
                if (canFire && !hasFiringSolution) nextShot = System.Math.Max(nextShot, now + 2.25);
                hasFiringSolution = canFire;
                moving = command.Throttle > 0f;
            }
            result = command;
            if (hasFiringSolution && now >= nextShot)
            {
                nextShot = now + Random.Range(3.5f, 5f);
                result.Aim = Quaternion.Euler(Random.Range(-1.6f, 1.6f), Random.Range(-2.2f, 2.2f), 0f) * result.Aim;
                result.Fire = true;
            }
            return result.Aim.sqrMagnitude > 0f;
        }

        private static NetworkVehicleCommand Decide(NetworkVehicle self, NetworkVehicle opponent, bool reversing, out bool canFire)
        {
            Vector3 target = opponent.Body.worldCenterOfMass;
            Vector3 offset = target - self.transform.position;
            Vector3 planar = Vector3.ProjectOnPlane(offset, Vector3.up);
            float distance = planar.magnitude;
            float heading = Vector3.SignedAngle(Vector3.ProjectOnPlane(self.transform.forward, Vector3.up), planar, Vector3.up);
            bool visible = distance <= SightDistance && HasLineOfSight(self, opponent, target);
            bool hold = visible && distance <= EngagementDistance;
            canFire = visible && !reversing && distance <= EngagementDistance + 10f &&
                Vector3.Dot(self.transform.up, Vector3.up) > 0.5f;
            float steering = Mathf.Clamp(heading / 45f, -1f, 1f);
            ProjectileDefinition ammunition = self.Ammunition;
            Vector3 aim = target - self.MuzzlePosition;
            float flightTime = aim.magnitude / Mathf.Max(1f, ammunition.Speed);
            aim += (opponent.Body.linearVelocity * 0.55f - self.Body.linearVelocity) * flightTime;
            aim -= ammunition.Acceleration * (0.5f * flightTime * flightTime);
            return new NetworkVehicleCommand
            {
                Throttle = hold ? 0f : reversing ? -0.45f : Mathf.Lerp(0.28f, 0.65f, 1f - Mathf.Abs(steering)),
                Steering = hold ? 0f : reversing ? (heading < 0f ? 0.85f : -0.85f) : steering,
                Handbrake = hold,
                Aim = aim.sqrMagnitude > 0.01f ? aim.normalized : self.transform.forward
            };
        }

        private static bool HasLineOfSight(NetworkVehicle self, NetworkVehicle opponent, Vector3 target)
        {
            Vector3 origin = self.MuzzlePosition;
            Vector3 offset = target - origin;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude,
                NetworkVehicle.HitLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != self.Body && hit.rigidbody != opponent.Body) return false;
            }
            return true;
        }
    }
}
