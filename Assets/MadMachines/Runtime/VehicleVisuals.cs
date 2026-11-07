using UnityEngine;

namespace MadMachines
{
    // Presentation for the current four-wheel chassis. Geometry lives below these anchors.
    public sealed class VehicleVisuals : MonoBehaviour
    {
        [SerializeField] private VehiclePhysics vehicle;
        [SerializeField] private Transform bodyRoot;
        [SerializeField] private Transform[] wheelAnchors;

        public void Configure(VehiclePhysics owner, Transform body, Transform[] anchors)
        {
            vehicle = owner;
            bodyRoot = body;
            wheelAnchors = anchors;
        }

        private void LateUpdate()
        {
            if (vehicle == null || wheelAnchors == null) return;
            for (int i = 0; i < Mathf.Min(4, wheelAnchors.Length); i++)
            {
                Transform anchor = wheelAnchors[i];
                if (anchor == null) continue;
                anchor.position = vehicle.transform.TransformPoint(vehicle.GetWheelPosition(i));
                anchor.rotation = vehicle.transform.rotation * Quaternion.Euler(0f, vehicle.GetWheelSteering(i), 0f);
            }
        }
    }
}
