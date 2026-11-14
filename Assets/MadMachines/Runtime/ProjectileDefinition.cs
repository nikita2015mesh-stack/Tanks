using System;
using UnityEngine;

namespace MadMachines
{
    [Serializable]
    public struct ProjectileDefinition
    {
        [SerializeField] private string label;
        [SerializeField, Min(0.01f)] private float speed;
        [SerializeField, Min(0.01f)] private float mass;
        [SerializeField, Min(0.01f)] private float lifetime;
        [SerializeField, Min(0.01f)] private float size;
        [SerializeField, Min(0f)] private float damage;
        [SerializeField, Min(0f)] private float blastRadius;
        [SerializeField, Min(0f)] private float blastImpulse;
        [SerializeField, Min(0f)] private float penetration;
        [SerializeField] private bool gravity;
        [SerializeField] private bool timedBounce;
        [SerializeField] private Color color;
        [SerializeField] private ProjectileAppearance appearance;
        [SerializeField, Min(0f)] private float steeringDegreesPerSecond;

        public string Label => label;
        public float Speed => speed;
        public float Mass => mass;
        public float Lifetime => lifetime;
        public float Size => size;
        public float Damage => damage;
        public float BlastRadius => blastRadius;
        public float BlastImpulse => blastImpulse;
        public float Penetration => penetration;
        public bool Gravity => gravity;
        public bool TimedBounce => timedBounce;
        public Color Color => color;
        public ProjectileAppearance Appearance => appearance;
        internal float SteeringDegreesPerSecond => steeringDegreesPerSecond;
        internal bool Guided => steeringDegreesPerSecond > 0f;
        internal Vector3 Acceleration => gravity ? Physics.gravity : Vector3.zero;
        internal bool IsShell => appearance == ProjectileAppearance.Shell || appearance == ProjectileAppearance.Penetrator;

        internal Vector3 LaunchVelocity(Vector3 direction, Vector3 carrierVelocity) =>
            direction * speed + (Guided ? Vector3.zero : carrierVelocity);

        internal ProjectileDefinition(WeaponDefinition weapon)
        {
            label = weapon.Label;
            speed = weapon.Speed;
            mass = weapon.Mass;
            lifetime = weapon.Lifetime;
            size = weapon.Size;
            damage = weapon.Damage;
            blastRadius = weapon.Radius;
            blastImpulse = weapon.Impulse;
            penetration = 0f;
            gravity = weapon.Gravity;
            timedBounce = weapon.TimedBounce;
            color = weapon.Color;
            appearance = weapon.Appearance;
            steeringDegreesPerSecond = weapon.SteeringDegreesPerSecond;
            // Earlier assets used the default appearance for every projectile.
            if (appearance == ProjectileAppearance.Grenade && !timedBounce)
                appearance = blastRadius > 0f ? ProjectileAppearance.Shell : ProjectileAppearance.Bullet;
        }
    }
}
