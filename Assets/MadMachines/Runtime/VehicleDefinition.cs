using UnityEngine;

namespace MadMachines
{
    [CreateAssetMenu(menuName = "Mad Machines/Vehicle")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private VehiclePhysics prefab;

        public string Id => id;
        public string DisplayName => displayName;
        public VehiclePhysics Prefab => prefab;
    }
}
