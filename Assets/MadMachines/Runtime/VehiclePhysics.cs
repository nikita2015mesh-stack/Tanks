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
        // Retained as defaults for scenes created before configurable wheels.
        [SerializeField] private float suspensionLength = 0.65f;
        [SerializeField] private float wheelRadius = 0.43f;
        [SerializeField, HideInInspector] private Transform[] wheels;
        [SerializeField] private VehicleWheel[] wheelDefinitions;
        private Rigidbody body;
        private VehicleWheelState[] wheelStates;
        private VehicleWheelState[] previousStates;
        private VehicleCommand command;

        public bool Authority { get; set; } = true;
        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();
        public float Speed => Body.linearVelocity.magnitude * 3.6f;
        public int GroundedWheels { get; private set; }
        public int WheelCount => wheelDefinitions != null && wheelDefinitions.Length > 0 ? wheelDefinitions.Length : 4;

        private void Awake()
        {
            Body.centerOfMass = new Vector3(0f, -0.35f, 0f);
            InitializeWheels();
            ResetWheelStates();
            if (GetComponentInChildren<VehicleVisuals>(true) == null && wheels != null && wheels.Length > 0)
            {
                gameObject.AddComponent<VehicleVisuals>().Configure(this, null, wheels);
            }
        }

        public void Configure(Transform[] visualWheels = null)
        {
            wheels = visualWheels;
            InitializeWheels();
            ResetWheelStates();
            if (visualWheels != null)
            {
                VehicleVisuals visuals = GetComponentInChildren<VehicleVisuals>(true);
                if (visuals == null)
                {
                    visuals = gameObject.AddComponent<VehicleVisuals>();
                }
                visuals.Configure(this, null, visualWheels);
            }
            Body.mass = 850f;
            Body.centerOfMass = new Vector3(0f, -0.35f, 0f);
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.angularDamping = 0.9f;
        }

        public void InitializeWheels()
        {
            if (wheelDefinitions == null || wheelDefinitions.Length == 0)
            {
                wheelDefinitions = new VehicleWheel[4];
            }
            for (int i = 0; i < wheelDefinitions.Length; i++)
            {
                if (wheelDefinitions[i] == null)
                {
                    wheelDefinitions[i] = VehicleWheel.CreateDefault(i, wheelRadius, suspensionLength);
                }
            }
            if (wheelStates == null || wheelStates.Length != WheelCount)
            {
                wheelStates = new VehicleWheelState[WheelCount];
                previousStates = new VehicleWheelState[WheelCount];
                ResetWheelStates();
            }
        }

        public VehicleWheel GetWheelDefinition(int index)
        {
            if (wheelDefinitions == null || wheelDefinitions.Length == 0 || wheelDefinitions[index] == null)
            {
                return VehicleWheel.CreateDefault(index, wheelRadius, suspensionLength);
            }
            return wheelDefinitions[index];
        }

        public Vector3 GetWheelPosition(int index)
        {
            VehicleWheel wheel = GetWheelDefinition(index);
            return wheel.GetPosition(wheelStates == null ? wheel.Travel : wheelStates[index].Extension);
        }

        public float GetWheelSteering(int index) => wheelStates == null ? 0f : wheelStates[index].Steering;

        public VehicleWheelState GetWheelVisualState(int index)
        {
            if (wheelStates == null || wheelStates.Length != WheelCount)
            {
                return new VehicleWheelState { Extension = GetWheelDefinition(index).Travel };
            }
            VehicleWheelState current = wheelStates[index];
            if (Body.interpolation != RigidbodyInterpolation.Interpolate)
            {
                return current;
            }
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            VehicleWheelState previous = previousStates[index];
            current.Extension = Mathf.Lerp(previous.Extension, current.Extension, alpha);
            current.Steering = Mathf.Lerp(previous.Steering, current.Steering, alpha);
            current.Rotation = Mathf.Lerp(previous.Rotation, current.Rotation, alpha);
            return current;
        }

        public void Submit(VehicleCommand value)
        {
            command = value;
        }

        private void FixedUpdate()
        {
            if (!Authority)
            {
                return;
            }
            InitializeWheels();
            float supportTotal = 0f;
            float driveTotal = 0f;
            float brakeTotal = 0f;
            foreach (VehicleWheel wheel in wheelDefinitions)
            {
                supportTotal += wheel.SupportWeight;
                driveTotal += wheel.DriveWeight;
                brakeTotal += wheel.BrakeWeight;
            }

            GroundedWheels = 0;
            for (int i = 0; i < WheelCount; i++)
            {
                VehicleWheel wheel = wheelDefinitions[i];
                VehicleWheelState previous = wheelStates[i];
                previous.Rotation = Mathf.Repeat(previous.Rotation, 360f);
                previousStates[i] = previous;
                VehicleWheelState state = previous;
                state.Steering = command.Steering * wheel.SteeringAngle;
                state.Grounded = FindContact(wheel, previous.Extension, out RaycastHit hit, out float extension);
                state.Extension = extension;
                if (state.Grounded)
                {
                    GroundedWheels++;
                    Vector3 origin = transform.TransformPoint(wheel.Mount);
                    Vector3 velocity = Body.GetPointVelocity(origin);
                    float share = supportTotal > 0f ? wheel.SupportWeight / supportTotal : 0f;
                    // Keep the original four-wheel tuning, but preserve total stiffness and grip as count changes.
                    float stiffness = spring * wheel.SpringMultiplier * share * 4f;
                    float damping = damper * wheel.DamperMultiplier * share * 4f;
                    float force = Mathf.Clamp((wheel.Travel - extension) * stiffness
                        - Vector3.Dot(velocity, transform.up) * damping, 0f, stiffness * wheel.Travel * 2f);
                    Body.AddForceAtPosition(transform.up * force, origin);
                    Quaternion steering = Quaternion.AngleAxis(state.Steering, transform.up);
                    Vector3 forward = Vector3.ProjectOnPlane(steering * transform.forward, hit.normal).normalized;
                    Vector3 side = Vector3.Cross(hit.normal, forward);
                    float surfaceGrip = hit.collider.GetComponent<SurfaceGrip>()?.Multiplier ?? 1f;
                    float lateralGrip = grip * wheel.GripMultiplier * surfaceGrip
                        * (command.Handbrake ? wheel.HandbrakeGripMultiplier : 1f);
                    Body.AddForceAtPosition(-side * Vector3.Dot(velocity, side) * lateralGrip * Body.mass * share, hit.point);
                    float forwardSpeed = Vector3.Dot(velocity, forward);
                    float driveShare = driveTotal > 0f ? wheel.DriveWeight / driveTotal : 0f;
                    float drive = command.Throttle * engineForce * driveShare
                        * Mathf.Clamp01(1f - Mathf.Abs(forwardSpeed) / 32f);
                    if (command.Handbrake && brakeTotal > 0f)
                    {
                        drive -= forwardSpeed * Body.mass * 1.5f * wheel.BrakeWeight / brakeTotal * 4f;
                    }
                    Body.AddForceAtPosition(forward * drive, hit.point);
                    Body.AddForceAtPosition(-forward * forwardSpeed * 45f * share * 4f, hit.point);
                    // Rolling presentation only; tire slip and wheel inertia are not simulated yet.
                    state.AngularSpeed = forwardSpeed / wheel.Radius * Mathf.Rad2Deg;
                }
                state.Rotation = previous.Rotation + state.AngularSpeed * Time.fixedDeltaTime;
                wheelStates[i] = state;
            }
            Body.AddForce(-Body.linearVelocity * Body.linearVelocity.magnitude * 0.7f);
        }

        private bool FindContact(VehicleWheel wheel, float previousExtension, out RaycastHit hit, out float extension)
        {
            extension = Mathf.Clamp(previousExtension, 0f, wheel.Travel);
            hit = default;
            int iterations = wheel.Geometry == SuspensionGeometry.ParallelWishbone ? 3 : 1;
            for (int i = 0; i < iterations; i++)
            {
                Vector3 localOrigin = wheel.GetPosition(extension);
                localOrigin.y = wheel.Mount.y;
                // Start above the fully compressed hub so the sphere also detects a landing
                // whose surface is already closer than one wheel radius to the mount.
                float castOffset = wheel.Radius;
                Vector3 origin = transform.TransformPoint(localOrigin) + transform.up * castOffset;
                if (!Physics.SphereCast(origin, wheel.Radius, -transform.up, out hit,
                    wheel.Travel + castOffset, 1, QueryTriggerInteraction.Ignore))
                {
                    extension = wheel.Travel;
                    return false;
                }
                // SphereCast measures hub travel; its radius already accounts for the slope normal.
                float nextExtension = Mathf.Clamp(hit.distance - castOffset, 0f, wheel.Travel);
                if (Mathf.Abs(nextExtension - extension) < 0.001f)
                {
                    extension = nextExtension;
                    break;
                }
                extension = nextExtension;
            }
            return true;
        }

        private void ResetWheelStates()
        {
            GroundedWheels = 0;
            for (int i = 0; i < WheelCount; i++)
            {
                VehicleWheelState state = new VehicleWheelState { Extension = GetWheelDefinition(i).Travel };
                wheelStates[i] = state;
                previousStates[i] = state;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < WheelCount; i++)
            {
                VehicleWheel wheel = GetWheelDefinition(i);
                Gizmos.color = Color.yellow;
                Vector3 previous = wheel.GetPosition(0f);
                for (int step = 1; step <= 12; step++)
                {
                    Vector3 next = wheel.GetPosition(wheel.Travel * step / 12f);
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(wheel.GetPosition(wheel.Travel), wheel.Radius);
                if (wheel.Geometry == SuspensionGeometry.ParallelWishbone)
                {
                    Gizmos.DrawLine(wheel.ArmPivot, wheel.GetPosition(wheel.Travel));
                }
            }
            Gizmos.matrix = previousMatrix;
        }

        public void Recover(Vector3 position, Quaternion rotation)
        {
            Body.position = position;
            Body.rotation = rotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            command = default;
            InitializeWheels();
            ResetWheelStates();
        }
    }
}
