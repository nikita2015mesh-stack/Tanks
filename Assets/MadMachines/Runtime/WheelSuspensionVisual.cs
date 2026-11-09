using UnityEngine;

namespace MadMachines
{
    // A parallel double wishbone rig. Meshes are decorative; all contact forces belong to VehiclePhysics.
    public sealed class WheelSuspensionVisual : MonoBehaviour
    {
        [SerializeField] private Transform wheelSpin;
        [SerializeField] private Transform[] lowerArms;
        [SerializeField] private Transform[] upperArms;
        [SerializeField] private Transform upright;
        [SerializeField] private Transform shockBody;
        [SerializeField] private Transform shockRod;
        [SerializeField, Min(0.01f)] private float uprightHeight = 0.3f;
        [SerializeField, Min(0.01f)] private float hingeHalfWidth = 0.18f;
        [SerializeField, Min(0.01f)] private float shockMountHeight = 0.5f;
        [SerializeField, Range(0.1f, 0.9f)] private float shockArmFraction = 0.65f;
        [SerializeField, Min(0.005f)] private float armThickness = 0.055f;

        public Transform WheelSpin => wheelSpin;

        public void Configure(Transform spin, Transform[] lower, Transform[] upper,
            Transform hub, Transform damperBody, Transform damperRod)
        {
            wheelSpin = spin;
            lowerArms = lower;
            upperArms = upper;
            upright = hub;
            shockBody = damperBody;
            shockRod = damperRod;
        }

        public void Apply(Transform chassis, VehicleWheel wheel, VehicleWheelState state)
        {
            if (wheelSpin != null)
            {
                // Steering is on the parent anchor; local X is the axle, irrespective of mesh import axes.
                wheelSpin.localRotation = Quaternion.AngleAxis(state.Rotation, Vector3.right);
            }
            bool showLinkage = wheel.Geometry == SuspensionGeometry.ParallelWishbone;
            SetLinkageActive(showLinkage);
            if (!showLinkage)
            {
                return;
            }

            Vector3 hub = wheel.GetPosition(state.Extension);
            Vector3 lowerHub = hub - Vector3.up * uprightHeight * 0.5f;
            Vector3 upperHub = hub + Vector3.up * uprightHeight * 0.5f;
            Vector3 lowerPivot = wheel.ArmPivot - Vector3.up * uprightHeight * 0.5f;
            Vector3 upperPivot = wheel.ArmPivot + Vector3.up * uprightHeight * 0.5f;
            PlaceWishbone(chassis, lowerArms, lowerPivot, lowerHub);
            PlaceWishbone(chassis, upperArms, upperPivot, upperHub);
            PlaceLink(chassis, upright, lowerHub, upperHub, armThickness * 1.5f);

            Vector3 shockTop = lowerPivot + Vector3.up * shockMountHeight;
            Vector3 shockBottom = Vector3.Lerp(lowerPivot, lowerHub, shockArmFraction);
            // Keep the housing length fixed. Only the exposed portion of the sliding rod changes length.
            Vector3 compressedHub = wheel.GetPosition(0f) - Vector3.up * uprightHeight * 0.5f;
            float minimumLength = Vector3.Distance(shockTop, Vector3.Lerp(lowerPivot, compressedHub, shockArmFraction));
            Vector3 sleeveEnd = shockTop + (shockBottom - shockTop).normalized * minimumLength * 0.7f;
            PlaceLink(chassis, shockBody, shockTop, sleeveEnd, armThickness * 1.8f);
            PlaceLink(chassis, shockRod, sleeveEnd, shockBottom, armThickness * 0.75f);
        }

        private void PlaceWishbone(Transform chassis, Transform[] links, Vector3 pivot, Vector3 end)
        {
            if (links == null)
            {
                return;
            }
            for (int i = 0; i < Mathf.Min(2, links.Length); i++)
            {
                Vector3 hinge = pivot + Vector3.forward * (i == 0 ? -hingeHalfWidth : hingeHalfWidth);
                PlaceLink(chassis, links[i], hinge, end, armThickness);
            }
        }

        private static void PlaceLink(Transform chassis, Transform link, Vector3 from, Vector3 to, float thickness)
        {
            if (link == null)
            {
                return;
            }
            Vector3 direction = to - from;
            link.position = chassis.TransformPoint((from + to) * 0.5f);
            link.rotation = chassis.rotation * Quaternion.FromToRotation(Vector3.up, direction.normalized);
            // Unit cylinders have height 2 along Y. Their descendants can contain replacement geometry.
            link.localScale = new Vector3(thickness, direction.magnitude * 0.5f, thickness);
        }

        private void SetLinkageActive(bool active)
        {
            SetActive(lowerArms, active);
            SetActive(upperArms, active);
            SetActive(upright, active);
            SetActive(shockBody, active);
            SetActive(shockRod, active);
        }

        private static void SetActive(Transform[] parts, bool active)
        {
            if (parts == null)
            {
                return;
            }
            foreach (Transform part in parts)
            {
                SetActive(part, active);
            }
        }

        private static void SetActive(Transform part, bool active)
        {
            if (part != null && part.gameObject.activeSelf != active)
            {
                part.gameObject.SetActive(active);
            }
        }
    }
}
