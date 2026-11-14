using UnityEngine;

namespace MadMachines
{
    public enum ProjectileAppearance
    {
        Grenade,
        Bullet,
        Shell,
        Penetrator,
        Rocket
    }

    [CreateAssetMenu(menuName = "Mad Machines/Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string Label;
        public float Speed = 40f;
        public float Mass = 4f;
        public float Interval = 1.2f;
        public float Recoil = 1800f;
        public float Spread = 0.4f;
        public float Lifetime = 6f;
        public float Radius = 5f;
        public float Impulse = 7000f;
        public float Size = 0.2f;
        public float Damage = 100f;
        public bool Gravity = true;
        public bool TimedBounce;
        public Color Color = Color.yellow;
        [SerializeField] private ProjectileAppearance appearance;
        [SerializeField] private ProjectileDefinition[] ammunition;
        [SerializeField, Min(0f)] private float steeringDegreesPerSecond;

        public ProjectileAppearance Appearance => appearance;
        internal float SteeringDegreesPerSecond => steeringDegreesPerSecond;
        public int AmmunitionCount => ammunition == null || ammunition.Length == 0 ? 1 : ammunition.Length;
        public ProjectileDefinition GetProjectile(int index = 0)
        {
            // Keep single-ammunition assets and older serialized weapon definitions usable.
            return ammunition == null || ammunition.Length == 0
                ? new ProjectileDefinition(this)
                : ammunition[Mathf.Clamp(index, 0, ammunition.Length - 1)];
        }
    }
}
