using UnityEngine;

namespace MadMachines
{
    public sealed class SurfaceGrip : MonoBehaviour
    {
        [SerializeField] private float multiplier = 1f;
        public float Multiplier { get => multiplier; set => multiplier = value; }
    }
}
