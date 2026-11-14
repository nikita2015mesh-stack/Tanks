using UnityEngine;

namespace MadMachines
{
    [DisallowMultipleComponent]
    public sealed class LooseGround : MonoBehaviour
    {
        [SerializeField] private Color dust = new Color(0.67f, 0.53f, 0.37f);
        [SerializeField] private Color track = new Color(0.21f, 0.14f, 0.09f);

        public Color Dust => dust;
        public Color Track => track;
    }
}
