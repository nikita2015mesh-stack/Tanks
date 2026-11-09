using System;
using UnityEngine;

namespace MadMachines
{
    public enum SuspensionGeometry
    {
        Vertical,
        ParallelWishbone
    }

    [Serializable]
    public sealed class VehicleWheel
    {
        [SerializeField] private string id;
        [SerializeField] private Vector3 mount;
        [SerializeField, Min(0.01f)] private float radius = 0.43f;
        [SerializeField, Min(0.01f)] private float travel = 0.65f;
        [SerializeField, Min(0f)] private float supportWeight = 1f;
        [SerializeField, Min(0f)] private float springMultiplier = 1f;
        [SerializeField, Min(0f)] private float damperMultiplier = 1f;
        [SerializeField, Min(0f)] private float gripMultiplier = 1f;
        [SerializeField, Range(-60f, 60f)] private float steeringAngle;
        [SerializeField, Min(0f)] private float driveWeight = 1f;
        [SerializeField, Min(0f)] private float brakeWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float handbrakeGripMultiplier = 1f;
        [SerializeField] private SuspensionGeometry geometry;
        [SerializeField, Min(0.05f)] private float armLength = 0.8f;

        public string Id => id;
        public Vector3 Mount => mount;
        public float Radius => Mathf.Max(0.01f, radius);
        public float Travel => Mathf.Max(0.01f, travel);
        public float SupportWeight => Mathf.Max(0f, supportWeight);
        public float SpringMultiplier => Mathf.Max(0f, springMultiplier);
        public float DamperMultiplier => Mathf.Max(0f, damperMultiplier);
        public float GripMultiplier => Mathf.Max(0f, gripMultiplier);
        public float SteeringAngle => Mathf.Clamp(steeringAngle, -60f, 60f);
        public float DriveWeight => Mathf.Max(0f, driveWeight);
        public float BrakeWeight => Mathf.Max(0f, brakeWeight);
        public float HandbrakeGripMultiplier => Mathf.Clamp01(handbrakeGripMultiplier);
        public SuspensionGeometry Geometry => geometry;
        public float ArmLength => Mathf.Max(armLength, Travel * 0.5f + 0.05f);

        // The two equal parallel arms keep the upright vertical. Their hinge axes run along Z.
        // At both ends of travel the hub has Mount.x; halfway through it moves slightly outward.
        public Vector3 ArmPivot
        {
            get
            {
                float halfTravel = Travel * 0.5f;
                float reach = Mathf.Sqrt(ArmLength * ArmLength - halfTravel * halfTravel);
                return mount - Vector3.right * Side * reach - Vector3.up * halfTravel;
            }
        }

        private float Side => mount.x < 0f ? -1f : 1f;

        public Vector3 GetPosition(float extension)
        {
            extension = Mathf.Clamp(extension, 0f, Travel);
            if (geometry == SuspensionGeometry.Vertical)
            {
                return mount - Vector3.up * extension;
            }

            float height = Travel * 0.5f - extension;
            float reach = Mathf.Sqrt(Mathf.Max(0f, ArmLength * ArmLength - height * height));
            return ArmPivot + Vector3.right * Side * reach + Vector3.up * height;
        }

        public void EnableWishbone()
        {
            geometry = SuspensionGeometry.ParallelWishbone;
            armLength = ArmLength;
        }

        public static VehicleWheel CreateDefault(int index, float radius, float travel)
        {
            return new VehicleWheel
            {
                id = "wheel_" + index,
                mount = new Vector3(index % 2 == 0 ? -1.05f : 1.05f, 0f, 1.25f - index / 2 * 2.5f),
                radius = radius,
                travel = travel,
                steeringAngle = index < 2 ? 28f : 0f,
                handbrakeGripMultiplier = index < 2 ? 1f : 0.18f
            };
        }
    }

    public struct VehicleWheelState
    {
        public float Extension { get; internal set; }
        public float Steering { get; internal set; }
        public float Rotation { get; internal set; }
        public float AngularSpeed { get; internal set; }
        public bool Grounded { get; internal set; }
    }
}
