using UnityEngine;

namespace MadMachines
{
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
        [SerializeField, Min(0f)] private float steeringDegreesPerSecond;

        public float SteeringDegreesPerSecond => steeringDegreesPerSecond;
        public bool Guided => steeringDegreesPerSecond > 0f;
    }
}
