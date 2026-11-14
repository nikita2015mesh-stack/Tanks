using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    internal sealed class VehicleFeedback : MonoBehaviour
    {
        private const float TrackSpacing = 0.38f;
        private const float MaximumTrackGap = 2.5f;
        private VehiclePhysics vehicle;
        private WheelTrace[] wheels;
        private AudioSource engine;
        private float nextCollision;
        private float throttle;
        private float engineLoad;
        private bool hasPose;
        private Vector3 lastPosition;

        private struct WheelTrace
        {
            public LooseGround Surface;
            public Vector3 Point;
            public Vector3 Normal;
            public Vector3 Across;
            public float Distance;
            public float NextDust;
        }

        internal void Bind(VehiclePhysics owner)
        {
            vehicle = owner;
            engine = gameObject.AddComponent<AudioSource>();
            DrivingEffects.ConfigureSource(engine, 45f);
            engine.clip = DrivingEffects.EngineClip;
            engine.loop = true;
            engine.volume = 0f;
        }

        internal void PresentControls(float input)
        {
            throttle = Mathf.Abs(input);
        }

        private void LateUpdate()
        {
            if (vehicle == null || !vehicle.isActiveAndEnabled || !vehicle.Authority) return;
            PresentControls(vehicle.Throttle);
            Rigidbody body = vehicle.Body;
            PresentWheels(null, body.linearVelocity, body.angularVelocity);
        }

        internal void PresentWheels(VehicleWheelState[] renderedWheels, Vector3 velocity, Vector3 angularVelocity)
        {
            if (vehicle == null || Time.deltaTime <= 0f) return;
            float speed = velocity.magnitude;
            UpdateEngine(speed);
            if (wheels == null || wheels.Length != vehicle.WheelCount) wheels = new WheelTrace[vehicle.WheelCount];
            if (speed < 0.65f)
            {
                if (hasPose) ResetTracks();
                return;
            }

            bool moving = !hasPose || (transform.position - lastPosition).sqrMagnitude > 0.00001f;
            if (hasPose && (transform.position - lastPosition).sqrMagnitude > MaximumTrackGap * MaximumTrackGap) ResetTracks();
            lastPosition = transform.position;
            hasPose = true;
            if (!moving) return;
            PhysicsScene physics = gameObject.scene.GetPhysicsScene();
            Matrix4x4 pose = transform.localToWorldMatrix;
            Vector3 up = transform.up;
            for (int i = 0; i < wheels.Length; i++)
            {
                VehicleWheelState wheel = renderedWheels == null ? vehicle.GetWheelVisualState(i) : renderedWheels[i];
                ref WheelTrace trace = ref wheels[i];
                if (!vehicle.FindVisibleContact(physics, pose, up, i, wheel.Extension, out RaycastHit hit) ||
                    !hit.collider.TryGetComponent(out LooseGround ground) || !ground.isActiveAndEnabled)
                {
                    trace = default;
                    continue;
                }

                Vector3 heading = Quaternion.AngleAxis(wheel.Steering, up) * transform.forward;
                Vector3 sideways = Vector3.Cross(hit.normal, heading).normalized;
                Vector3 movement = Vector3.ProjectOnPlane(velocity + Vector3.Cross(angularVelocity, hit.point - transform.position), hit.normal);
                float slip = Mathf.Abs(Vector3.Dot(movement, sideways));
                float width = vehicle.GetWheelDefinition(i).Radius * 0.58f;
                LayTrack(ref trace, hit, ground, width, slip);
                if (Time.time >= trace.NextDust && movement.sqrMagnitude > 2.25f)
                {
                    float intensity = Mathf.Clamp01(movement.magnitude / 22f + slip / 6f);
                    trace.NextDust = Time.time + Mathf.Lerp(0.18f, 0.05f, intensity);
                    DrivingEffects.In(gameObject.scene).WheelDust(hit.point, hit.normal, movement, ground.Dust, intensity);
                }
            }
        }

        private void LayTrack(ref WheelTrace trace, RaycastHit hit, LooseGround ground, float width, float slip)
        {
            Vector3 delta = hit.point - trace.Point;
            float distance = delta.magnitude;
            if (trace.Surface != ground || distance > MaximumTrackGap || Vector3.Dot(trace.Normal, hit.normal) < 0.7f)
            {
                trace = new WheelTrace { Surface = ground, Point = hit.point, Normal = hit.normal, NextDust = trace.NextDust };
                return;
            }
            if (distance < TrackSpacing) return;

            Vector3 across = Vector3.Cross(hit.normal, delta).normalized * (width * 0.5f);
            if (across.sqrMagnitude < 0.0001f) return;
            if (trace.Across.sqrMagnitude < 0.0001f) trace.Across = across;
            // Split the strip at a sharp reversal rather than twisting a quad over itself.
            if (Vector3.Dot(across, trace.Across) > 0f)
            {
                Color color = ground.Track;
                color.a *= Mathf.Lerp(0.46f, 0.78f, Mathf.Clamp01(slip / 6f));
                DrivingEffects.In(gameObject.scene).AddTrack(trace.Point + trace.Normal * 0.04f, trace.Across,
                    hit.point + hit.normal * 0.04f, across, trace.Distance, distance, color);
            }
            trace.Point = hit.point;
            trace.Normal = hit.normal;
            trace.Across = across;
            trace.Distance = Mathf.Repeat(trace.Distance + distance, DrivingEffects.TreadRepeat);
        }

        private void UpdateEngine(float speed)
        {
            if (engine.clip == null) return;
            float requestedLoad = Mathf.Clamp01(speed / 38f + throttle * 0.28f);
            engineLoad = Mathf.Lerp(engineLoad, requestedLoad, 1f - Mathf.Exp(-Time.deltaTime * 5f));
            engine.pitch = Mathf.Lerp(0.79f, 2.05f, engineLoad);
            engine.volume = Mathf.Lerp(0.19f, 0.5f, engineLoad);
            if (!engine.isPlaying) engine.Play();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (isActiveAndEnabled && TryGetCollision(collision, ref nextCollision, out Vector3 point, out float strength))
                DrivingEffects.In(gameObject.scene).ChassisImpact(point, strength);
        }

        internal static bool TryGetCollision(Collision collision, ref float nextCollision, out Vector3 point, out float strength)
        {
            point = default;
            strength = 0f;
            if (Time.time < nextCollision || collision.contactCount == 0 || collision.collider.GetComponent<Projectile>() != null)
                return false;
            ContactPoint contact = collision.GetContact(0);
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (speed < 3.5f) return false;
            point = contact.point;
            strength = Mathf.InverseLerp(3.5f, 16f, speed);
            nextCollision = Time.time + 0.18f;
            return true;
        }

        internal void ResetMotion()
        {
            throttle = engineLoad = 0f;
            nextCollision = 0f;
            ResetTracks();
        }

        private void ResetTracks()
        {
            hasPose = false;
            if (wheels != null) System.Array.Clear(wheels, 0, wheels.Length);
        }

        private void OnDisable()
        {
            ResetMotion();
            if (engine != null) engine.Stop();
        }

        private void Update()
        {
            if (engine != null && Time.timeScale == 0f) engine.volume = 0f;
        }
    }
}
