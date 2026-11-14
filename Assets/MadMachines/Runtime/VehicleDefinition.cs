using System;
using UnityEngine;

namespace MadMachines
{
    [CreateAssetMenu(menuName = "Mad Machines/Vehicle")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private VehiclePhysics prefab;
        [SerializeField] private TurretOption[] turrets;

        public string Id => id;
        public string DisplayName => displayName;
        public VehiclePhysics Prefab => prefab;
        internal TurretOption[] Turrets => turrets ?? Array.Empty<TurretOption>();

        internal TurretOption FindTurret(string optionId)
        {
            if (string.IsNullOrEmpty(optionId)) return default;
            return Array.Find(Turrets, option => option.Id == optionId && option.Mesh != null);
        }

        [Serializable]
        internal struct TurretOption
        {
            [SerializeField] private string id;
            [SerializeField] private string displayName;
            [SerializeField] private Mesh mesh;
            [SerializeField] private Material[] materials;

            public string Id => id;
            public string DisplayName => displayName;
            public Mesh Mesh => mesh;
            public Material[] Materials => materials;
        }
    }
}
