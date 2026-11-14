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
        private const int TracerInterval = 5;
        public delegate bool ProjectileLauncher(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId);

        [SerializeField] private WeaponDefinition[] definitions;
        [SerializeField] private Transform turret;
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        [SerializeField] private WeaponMount[] mounts;
        private VehiclePhysics vehicle;
        private WeaponCooldown cooldown;
        private int shotSequence;
        private int bulletCount;
        private int[] ammunitionSelections;
        private WeaponMount activeMount;

        [Serializable]
        private struct WeaponMount
        {
            [SerializeField] private WeaponDefinition weapon;
            [SerializeField] private Transform yaw;
            [SerializeField] private Transform pitch;
            [SerializeField] private Transform muzzle;

            public WeaponDefinition Weapon => weapon;
            public Transform Yaw => yaw;
            public Transform Pitch => pitch;
            public Transform Muzzle => muzzle;

            public WeaponMount(Transform yaw, Transform pitch, Transform muzzle)
            {
                weapon = null;
                this.yaw = yaw;
                this.pitch = pitch;
                this.muzzle = muzzle;
            }

            public bool IsValid(Transform vehicle)
            {
                return yaw != null && pitch != null && muzzle != null &&
                    yaw.IsChildOf(vehicle) && pitch.IsChildOf(yaw) && muzzle.IsChildOf(pitch);
            }
        }

        public int Selected { get; private set; } = 1;
        public bool Authority { get; set; } = true;
        public WeaponDefinition Current => definitions[Selected];
        public ProjectileDefinition CurrentProjectile => Current.GetProjectile(ammunitionSelections == null ? 0 : ammunitionSelections[Selected]);
        public float Reload => Mathf.Clamp01(cooldown.Remaining(Time.timeAsDouble) / Current.Interval);
        private WeaponMount DefaultMount => new WeaponMount(turret, barrel, muzzle);
        private WeaponMount ActiveMount => activeMount.Yaw != null ? activeMount : DefaultMount;
        public Vector3 AimDirection => ActiveMount.Muzzle.forward;
        public Vector3 MuzzlePosition => ActiveMount.Muzzle.position;
        public Vector3 MountPosition => ActiveMount.Yaw.position;
        public Vector2 AimAngles => new Vector2(ActiveMount.Yaw.localEulerAngles.y, ActiveMount.Pitch.localEulerAngles.x);
        public bool HasValidMount => ActiveMount.IsValid(transform);
        public event Action Fired;

        public void Configure(VehiclePhysics owner, Transform mount, Transform gun, Transform end, WeaponDefinition[] weapons)
        {
            vehicle = owner;
            turret = mount;
            barrel = gun;
            muzzle = end;
            definitions = weapons;
            ammunitionSelections = null;
            if (definitions != null) Select(Mathf.Min(Selected, definitions.Length - 1));
        }

        private void Awake()
        {
            vehicle = GetComponent<VehiclePhysics>();
            if (definitions != null) Select(Mathf.Min(Selected, definitions.Length - 1));
        }

        public void Select(int index)
        {
            if (definitions == null || index < 0 || index >= definitions.Length) return;
            Selected = index;

            WeaponMount previous = ActiveMount;
            WeaponMount next = DefaultMount;
            if (mounts != null)
            {
                foreach (WeaponMount mount in mounts)
                {
                    if (mount.Weapon == Current && mount.IsValid(transform))
                    {
                        next = mount;
                        break;
                    }
                }
            }
            if (previous.Yaw == next.Yaw)
            {
                activeMount = next;
                return;
            }

            Vector2 angles = previous.IsValid(transform) ? AimAngles : Vector2.zero;
            if (previous.Yaw != null && previous.Yaw != transform) previous.Yaw.gameObject.SetActive(false);
            activeMount = next;
            next.Yaw.gameObject.SetActive(true);
            ApplyAimAngles(angles);
        }

        internal void SelectNextAmmunition()
        {
            if (Current.AmmunitionCount <= 1) return;
            ammunitionSelections ??= new int[definitions.Length];
            ammunitionSelections[Selected] = (ammunitionSelections[Selected] + 1) % Current.AmmunitionCount;
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
            WeaponMount mount = ActiveMount;
            mount.Yaw.localRotation = Quaternion.Euler(0f, angles.x, 0f);
            mount.Pitch.localRotation = Quaternion.Euler(angles.y, 0f, 0f);
        }

        // Only the simulation authority accepts a request and creates a projectile.
        public bool RequestFire() => RequestFire(null);

        public bool RequestFire(ProjectileLauncher launch)
        {
            if (!Authority || !cooldown.TryBegin(Time.timeAsDouble, Current.Interval)) return false;
            WeaponDefinition definition = Current;
            ProjectileDefinition ammunition = CurrentProjectile;
            Transform shotMuzzle = ActiveMount.Muzzle;
            Vector2 spread = UnityEngine.Random.insideUnitCircle * definition.Spread;
            Vector3 direction = Quaternion.AngleAxis(spread.x, shotMuzzle.up) * Quaternion.AngleAxis(spread.y, shotMuzzle.right) * shotMuzzle.forward;
            int shotId = ++shotSequence;
            if (launch == null)
            {
                if (ammunition.Appearance == ProjectileAppearance.Bullet)
                    bulletCount = (bulletCount + 1) % TracerInterval;
                bool tracer = ammunition.Appearance == ProjectileAppearance.Bullet && bulletCount == 0;
                Projectile.Launch(ammunition, shotMuzzle.position, direction, vehicle, shotId, tracer);
            }
            else if (!launch(definition, shotMuzzle.position, direction, vehicle, shotId)) return false;
            vehicle.Body.AddForceAtPosition(-direction * definition.Recoil, shotMuzzle.position, ForceMode.Impulse);
            if (launch == null)
                PlaygroundEffects.ShowShot(gameObject.scene, ammunition, shotMuzzle.position, shotMuzzle.forward);
            Fired?.Invoke();
            return true;
        }

        public void ResetReload()
        {
            cooldown = default;
            bulletCount = 0;
        }
    }
}
