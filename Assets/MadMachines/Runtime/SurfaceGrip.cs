using UnityEngine;

namespace MadMachines
{
    public sealed class SurfaceGrip : MonoBehaviour
    {
        [SerializeField] private float multiplier = 1f;
        [SerializeField] private BoxCollider zone;
        public float Multiplier { get => multiplier; set => multiplier = value; }

        internal void CopySettings(SurfaceGrip source)
        {
            multiplier = source.multiplier;
            // The optional zone is map data, queried only for its bounds at the contact point.
            zone = source.zone;
        }

        public float At(Vector3 contact)
        {
            return zone == null || (zone.ClosestPoint(contact) - contact).sqrMagnitude < 0.0001f ? multiplier : 1f;
        }
    }
}
