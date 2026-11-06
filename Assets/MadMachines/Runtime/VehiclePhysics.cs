using UnityEngine;

namespace MadMachines
{
    public struct VehicleCommand
    {
        public float Throttle;
        public float Steering;
        public bool Handbrake;
    }

    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehiclePhysics : MonoBehaviour
    {
        [SerializeField] private float engineForce = 10500f;
        [SerializeField] private float spring = 34000f;
        [SerializeField] private float damper = 4800f;
        [SerializeField] private float grip = 7f;
        [SerializeField] private float suspensionLength = 0.65f;
        [SerializeField] private float wheelRadius = 0.43f;
        // Legacy scene references; new geometry is bound by VehicleVisuals.
        [SerializeField, HideInInspector] private Transform[] wheels;
        private readonly Vector3[] mounts = { new Vector3(-1.05f, 0f, 1.25f), new Vector3(1.05f, 0f, 1.25f), new Vector3(-1.05f, 0f, -1.25f), new Vector3(1.05f, 0f, -1.25f) };
        private Rigidbody body;
        private readonly Vector3[] wheelPositions = new Vector3[4];
        private readonly float[] wheelSteering = new float[4];
        private VehicleCommand command;
        public bool Authority { get; set; } = true;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        public float Speed => Body.linearVelocity.magnitude * 3.6f;
        public int GroundedWheels { get; private set; }

        private void Awake()
        {
            Body.centerOfMass = new Vector3(0f, -0.35f, 0f);
            for (int i = 0; i < mounts.Length; i++)
                wheelPositions[i] = mounts[i] - Vector3.up * suspensionLength;
            // Compatibility with scenes that still serialize the original wheel meshes.
            if (GetComponentInChildren<VehicleVisuals>(true) == null && wheels != null && wheels.Length > 0)
                gameObject.AddComponent<VehicleVisuals>().Configure(this, null, wheels);
        }

        public void Configure(Transform[] visualWheels = null)
        {
            wheels = visualWheels;
            if (visualWheels != null)
            {
                VehicleVisuals visuals = GetComponentInChildren<VehicleVisuals>(true);
                if (visuals == null) visuals = gameObject.AddComponent<VehicleVisuals>();
                visuals.Configure(this, null, visualWheels);
            }
            Body.mass = 850f;
            Body.centerOfMass = new Vector3(0f, -0.35f, 0f);
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.angularDamping = 0.9f;
        }

        public Vector3 GetWheelPosition(int index) => wheelPositions[index];
        public float GetWheelSteering(int index) => wheelSteering[index];

        public void Submit(VehicleCommand value)
        {
            command = value;
        }

        private void FixedUpdate()
        {
            if (!Authority) return;
            GroundedWheels = 0;
            for (int i = 0; i < mounts.Length; i++)
            {
                Vector3 origin = transform.TransformPoint(mounts[i]);
                float extension = suspensionLength;
                if (Physics.Raycast(origin, -transform.up, out RaycastHit hit, suspensionLength + wheelRadius, 1, QueryTriggerInteraction.Ignore))
                {
                    GroundedWheels++;
                    extension = Mathf.Max(0f, hit.distance - wheelRadius);
                    Vector3 velocity = Body.GetPointVelocity(origin);
                    float force = Mathf.Clamp((suspensionLength - extension) * spring - Vector3.Dot(velocity, transform.up) * damper, 0f, spring * suspensionLength * 2f);
                    Body.AddForceAtPosition(transform.up * force, origin);
                    Quaternion steering = Quaternion.AngleAxis(i < 2 ? command.Steering * 28f : 0f, transform.up);
                    Vector3 forward = Vector3.ProjectOnPlane(steering * transform.forward, hit.normal).normalized;
                    Vector3 side = Vector3.Cross(hit.normal, forward);
                    float surfaceGrip = hit.collider.GetComponent<SurfaceGrip>()?.Multiplier ?? 1f;
                    float lateralGrip = grip * surfaceGrip * (command.Handbrake && i >= 2 ? 0.18f : 1f);
                    Body.AddForceAtPosition(-side * Vector3.Dot(velocity, side) * lateralGrip * Body.mass / 4f, hit.point);
                    float forwardSpeed = Vector3.Dot(velocity, forward);
                    float drive = command.Throttle * engineForce / 4f * Mathf.Clamp01(1f - Mathf.Abs(forwardSpeed) / 32f);
                    if (command.Handbrake) drive -= forwardSpeed * Body.mass * 1.5f;
                    Body.AddForceAtPosition(forward * drive, hit.point);
                    Body.AddForceAtPosition(-forward * forwardSpeed * 45f, hit.point);
                }
                wheelPositions[i] = mounts[i] - Vector3.up * extension;
                wheelSteering[i] = i < 2 ? command.Steering * 28f : 0f;
            }
            Body.AddForce(-Body.linearVelocity * Body.linearVelocity.magnitude * 0.7f);
        }

        public void Recover(Vector3 position, Quaternion rotation)
        {
            Body.position = position;
            Body.rotation = rotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            command = default;
        }
    }
}
