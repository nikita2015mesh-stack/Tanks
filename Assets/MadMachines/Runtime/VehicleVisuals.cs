using UnityEngine;

namespace MadMachines
{
    // Presentation bindings follow the configured wheel order; body geometry is independent.
    public sealed class VehicleVisuals : MonoBehaviour
    {
        [SerializeField] private VehiclePhysics vehicle;
        [SerializeField] private Transform bodyRoot;
        [SerializeField] private Transform[] wheelAnchors;
        [SerializeField] private WheelSuspensionVisual[] wheelSuspensions;

        public VehiclePhysics Vehicle => vehicle;
        public int WheelAnchorCount => wheelAnchors == null ? 0 : wheelAnchors.Length;
        public Transform GetWheelAnchor(int index) => wheelAnchors[index];

        public void Configure(VehiclePhysics owner, Transform body, Transform[] anchors)
        {
            vehicle = owner;
            bodyRoot = body;
            wheelAnchors = anchors;
        }

        public void SetSuspensions(WheelSuspensionVisual[] suspensions)
        {
            wheelSuspensions = suspensions;
        }

        private void LateUpdate()
        {
            ApplyPose();
        }

        public void ApplyPose()
        {
            if (vehicle == null || wheelAnchors == null)
            {
                return;
            }
            for (int i = 0; i < Mathf.Min(vehicle.WheelCount, wheelAnchors.Length); i++)
            {
                Transform anchor = wheelAnchors[i];
                if (anchor == null)
                {
                    continue;
                }
                VehicleWheel wheel = vehicle.GetWheelDefinition(i);
                VehicleWheelState state = vehicle.GetWheelVisualState(i);
                anchor.position = vehicle.transform.TransformPoint(wheel.GetPosition(state.Extension));
                anchor.rotation = vehicle.transform.rotation * Quaternion.Euler(0f, state.Steering, 0f);
                if (wheelSuspensions != null && i < wheelSuspensions.Length && wheelSuspensions[i] != null)
                {
                    wheelSuspensions[i].Apply(vehicle.transform, wheel, state);
                }
            }
        }
    }
}
