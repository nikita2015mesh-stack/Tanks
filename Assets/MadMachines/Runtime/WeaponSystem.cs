using System;
using UnityEngine;

namespace MadMachines
{
    public sealed class WeaponSystem : MonoBehaviour
    {
        [SerializeField] private WeaponDefinition[] definitions;
        [SerializeField] private Transform turret;
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        private VehiclePhysics vehicle;
        private float nextShot;
        private int shotSequence;
        public int Selected { get; private set; } = 1;
        public bool Authority { get; set; } = true;
        public WeaponDefinition Current => definitions[Selected];
        public float Reload => Mathf.Clamp01((nextShot - Time.time) / Current.Interval);
        public Vector3 AimDirection => muzzle.forward;
        public Vector3 MuzzlePosition => muzzle.position;
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

        public void Aim(Vector3 worldDirection)
        {
            Vector3 local = transform.InverseTransformDirection(worldDirection.normalized);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            turret.localRotation = Quaternion.Euler(0f, yaw, 0f);
            barrel.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -45f, 15f), 0f, 0f);
        }

        // Only the simulation authority accepts a request and creates a projectile.
        public bool RequestFire()
        {
            if (!Authority || Time.time < nextShot) return false;
            nextShot = Time.time + Current.Interval;
            Vector2 spread = UnityEngine.Random.insideUnitCircle * Current.Spread;
            Vector3 direction = Quaternion.AngleAxis(spread.x, muzzle.up) * Quaternion.AngleAxis(spread.y, muzzle.right) * muzzle.forward;
            Projectile.Launch(Current, muzzle.position, direction, vehicle, ++shotSequence);
            vehicle.Body.AddForceAtPosition(-direction * Current.Recoil, muzzle.position, ForceMode.Impulse);
            PlaygroundEffects.Flash(muzzle.position, Current.Color, 0.8f, 0.1f);
            Fired?.Invoke();
            return true;
        }

        public void ResetReload()
        {
            nextShot = 0f;
        }
    }
}
