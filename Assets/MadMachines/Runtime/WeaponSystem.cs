using System;
using UnityEngine;

namespace MadMachines
{
    public struct WeaponCooldown
    {
        private double readyAt;

        public float Remaining(double now) => (float)Math.Max(0, readyAt - now);

        public bool TryBegin(double now, float interval)
        {
            if (now < readyAt) return false;
            readyAt = now + interval;
            return true;
        }
    }

    public sealed class WeaponSystem : MonoBehaviour
    {
        public delegate bool ProjectileLauncher(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId);

        [SerializeField] private WeaponDefinition[] definitions;
        [SerializeField] private Transform turret;
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        private VehiclePhysics vehicle;
        private WeaponCooldown cooldown;
        private int shotSequence;
        public int Selected { get; private set; } = 1;
        public bool Authority { get; set; } = true;
        public WeaponDefinition Current => definitions[Selected];
        public float Reload => Mathf.Clamp01(cooldown.Remaining(Time.timeAsDouble) / Current.Interval);
        public Vector3 AimDirection => muzzle.forward;
        public Vector3 MuzzlePosition => muzzle.position;
        public Vector3 MountPosition => turret.position;
        public Vector2 AimAngles => new Vector2(turret.localEulerAngles.y, barrel.localEulerAngles.x);
        public bool HasValidMount => turret != null && barrel != null && muzzle != null &&
            turret.IsChildOf(transform) && barrel.IsChildOf(turret) && muzzle.IsChildOf(barrel);
        public event Action Fired;

        public void Configure(VehiclePhysics owner, Transform mount, Transform gun, Transform end, WeaponDefinition[] weapons)
        {
            vehicle = owner;
            turret = mount;
            barrel = gun;
            muzzle = end;
            definitions = weapons;
        }

        private void Awake()
        {
            vehicle = GetComponent<VehiclePhysics>();
        }

        public void Select(int index)
        {
            if (index >= 0 && index < definitions.Length) Selected = index;
        }

        public int IndexOf(WeaponDefinition definition) => definitions == null ? -1 : Array.IndexOf(definitions, definition);

        public void Aim(Vector3 worldDirection)
        {
            Vector3 local = transform.InverseTransformDirection(worldDirection.normalized);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            ApplyAimAngles(new Vector2(yaw, Mathf.Clamp(pitch, -45f, 15f)));
        }

        public void ApplyAimAngles(Vector2 angles)
        {
            turret.localRotation = Quaternion.Euler(0f, angles.x, 0f);
            barrel.localRotation = Quaternion.Euler(angles.y, 0f, 0f);
        }

        // Only the simulation authority accepts a request and creates a projectile.
        public bool RequestFire() => RequestFire(null);

        public bool RequestFire(ProjectileLauncher launch)
        {
            if (!Authority || !cooldown.TryBegin(Time.timeAsDouble, Current.Interval)) return false;
            WeaponDefinition definition = Current;
            Vector2 spread = UnityEngine.Random.insideUnitCircle * definition.Spread;
            Vector3 direction = Quaternion.AngleAxis(spread.x, muzzle.up) * Quaternion.AngleAxis(spread.y, muzzle.right) * muzzle.forward;
            int shotId = ++shotSequence;
            if (launch == null) Projectile.Launch(definition, muzzle.position, direction, vehicle, shotId);
            else if (!launch(definition, muzzle.position, direction, vehicle, shotId)) return false;
            vehicle.Body.AddForceAtPosition(-direction * definition.Recoil, muzzle.position, ForceMode.Impulse);
            if (launch == null) PlaygroundEffects.Flash(muzzle.position, definition.Color, 0.8f, 0.1f);
            Fired?.Invoke();
            return true;
        }

        public void ResetReload()
        {
            cooldown = default;
        }
    }
}
