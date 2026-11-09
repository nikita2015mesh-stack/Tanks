using UnityEngine;

namespace MadMachines
{
    public sealed class SurfaceGrip : MonoBehaviour
    {
        [SerializeField] private float multiplier = 1f;
        [SerializeField] private BoxCollider zone;
        public float Multiplier { get => multiplier; set => multiplier = value; }

        public float At(Vector3 contact)
        {
            return zone == null || (zone.ClosestPoint(contact) - contact).sqrMagnitude < 0.0001f ? multiplier : 1f;
        }
    }
}
