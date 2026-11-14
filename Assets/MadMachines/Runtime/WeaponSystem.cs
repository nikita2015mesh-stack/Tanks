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
        internal const int GuidedRocketSlot = 3;
        internal const int UnguidedRocketSlot = 4;
        private const int UnguidedRocketCapacity = 2;
        public delegate bool ProjectileLauncher(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId);

        [SerializeField] private WeaponDefinition[] definitions;
        [SerializeField] private Transform turret;
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        [SerializeField] private WeaponMount[] mounts;
        [SerializeField] private WeaponDefinition localGuidedRocket;
        [SerializeField] private WeaponDefinition localUnguidedRocket;
        [SerializeField] private Transform rocketRest;
        [SerializeField] private Transform leftRocketRest;
        [SerializeField] private Transform rightRocketRest;
        private VehiclePhysics vehicle;
        private WeaponCooldown cooldown;
        private WeaponCooldown rocketCooldown;
        private WeaponCooldown unguidedRocketCooldown;
        private int unguidedRocketsSpent;
        private int shotSequence;
        private int bulletCount;
        private int[] ammunitionSelections;
        private WeaponMount activeMount;
        private WeaponDefinition guidedRocket;
        private WeaponDefinition unguidedRocket;
        private Projectile activeRocket;

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
        internal bool HasGuidedRocket => guidedRocket != null;
        internal bool HasMultipleWeapons => (definitions != null && definitions.Length > 1) || HasGuidedRocket || HasUnguidedRocket;
        internal bool GuidedRocketSelected => HasGuidedRocket && Selected == GuidedRocketSlot;
        internal bool HasUnguidedRocket => unguidedRocket != null;
        internal bool UnguidedRocketSelected => HasUnguidedRocket && Selected == UnguidedRocketSlot;
        internal bool SinglePressFire => GuidedRocketSelected || UnguidedRocketSelected;
        internal int UnguidedRocketsLoaded => unguidedRocketsSpent == UnguidedRocketCapacity
            ? (unguidedRocketCooldown.Remaining(Time.timeAsDouble) > 0f ? 0 : UnguidedRocketCapacity)
            : UnguidedRocketCapacity - unguidedRocketsSpent;
        internal bool RocketInFlight => activeRocket != null && activeRocket.InFlight;
        internal bool IsGuidingRocket => RocketInFlight && activeRocket.IsGuiding;
        internal float RocketFuel => RocketInFlight ? activeRocket.RemainingLifetimeFraction : RocketReloaded ? 1f : 0f;
        private bool RocketReloaded => rocketCooldown.Remaining(Time.timeAsDouble) == 0f;
        public WeaponDefinition Current => UnguidedRocketSelected ? unguidedRocket : GuidedRocketSelected ? guidedRocket : definitions[Selected];
        public ProjectileDefinition CurrentProjectile =>
            Current.GetProjectile(SinglePressFire || ammunitionSelections == null ? 0 : ammunitionSelections[Selected]);
        public float Reload => Mathf.Clamp01((UnguidedRocketSelected ? unguidedRocketCooldown :
            GuidedRocketSelected ? rocketCooldown : cooldown).Remaining(Time.timeAsDouble) / Current.Interval);
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
            bool selectGuidedRocket = HasGuidedRocket && index == GuidedRocketSlot;
            bool selectUnguidedRocket = HasUnguidedRocket && index == UnguidedRocketSlot;
            if (!selectGuidedRocket && !selectUnguidedRocket && (definitions == null || index < 0 || index >= definitions.Length)) return;
            if (GuidedRocketSelected && !selectGuidedRocket) StopRocketGuidance();
            Selected = index;

            WeaponMount previous = ActiveMount;
            WeaponMount next = FindMount(Current);
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

        private WeaponMount FindMount(WeaponDefinition weapon)
        {
            if (mounts != null)
            {
                foreach (WeaponMount mount in mounts)
                {
                    if (mount.Weapon == weapon && mount.IsValid(transform)) return mount;
                }
            }
            return DefaultMount;
        }

        internal void EnableLocalRockets(WeaponDefinition guidedOverride, WeaponDefinition unguidedOverride)
        {
            EnableGuidedRocket(guidedOverride != null ? guidedOverride : localGuidedRocket);
            EnableUnguidedRocket(unguidedOverride != null ? unguidedOverride : localUnguidedRocket);
        }

        private void EnableGuidedRocket(WeaponDefinition definition)
        {
            if (HasGuidedRocket || definition == null || !definition.GetProjectile().Guided || rocketRest == null) return;
            WeaponMount mount = FindMount(definition);
            if (!mount.IsValid(transform) || !rocketRest.IsChildOf(mount.Pitch)) return;
            guidedRocket = definition;
            ProjectileVisual.Attach(rocketRest.gameObject, ProjectileAppearance.Rocket);
        }

        private void EnableUnguidedRocket(WeaponDefinition definition)
        {
            if (HasUnguidedRocket || definition == null || leftRocketRest == null || rightRocketRest == null) return;
            ProjectileDefinition projectile = definition.GetProjectile();
            WeaponMount mount = FindMount(definition);
            if (projectile.Appearance != ProjectileAppearance.UnguidedRocket || projectile.Guided ||
                !mount.IsValid(transform) || !leftRocketRest.IsChildOf(mount.Pitch) || !rightRocketRest.IsChildOf(mount.Pitch)) return;
            unguidedRocket = definition;
            ProjectileVisual.Attach(leftRocketRest.gameObject, projectile.Appearance);
            ProjectileVisual.Attach(rightRocketRest.gameObject, projectile.Appearance);
        }

        private void LateUpdate()
        {
            if (GuidedRocketSelected)
            {
                bool loaded = !RocketInFlight && RocketReloaded;
                if (rocketRest.gameObject.activeSelf != loaded) rocketRest.gameObject.SetActive(loaded);
            }
            if (UnguidedRocketSelected)
            {
                int loaded = UnguidedRocketsLoaded;
                if (leftRocketRest.gameObject.activeSelf != (loaded > 0)) leftRocketRest.gameObject.SetActive(loaded > 0);
                if (rightRocketRest.gameObject.activeSelf != (loaded > 1)) rightRocketRest.gameObject.SetActive(loaded > 1);
            }
        }

        internal void GuideRocket(Vector3 origin, Vector3 direction)
        {
            if (!IsGuidingRocket) return;
            ProjectileDefinition ammunition = CurrentProjectile;
            float distance = ammunition.Speed * ammunition.Lifetime + Vector3.Distance(origin, MuzzlePosition);
            if (Physics.Raycast(origin, direction, out RaycastHit first, distance, 1, QueryTriggerInteraction.Ignore))
            {
                if (first.rigidbody != vehicle.Body) distance = first.distance;
                else
                {
                    foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, distance, 1, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.rigidbody != vehicle.Body && hit.distance < distance)
                            distance = hit.distance;
                    }
                }
            }
            activeRocket.SetGuidancePoint(origin + direction * distance);
        }

        internal void StopRocketGuidance()
        {
            if (IsGuidingRocket) activeRocket.StopGuidance();
        }

        internal void SelectNextAmmunition()
        {
            if (SinglePressFire || Current.AmmunitionCount <= 1) return;
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
            bool guided = GuidedRocketSelected;
            bool unguided = UnguidedRocketSelected;
            if (!Authority || (SinglePressFire && launch != null) || (guided && RocketInFlight)) return false;
            WeaponDefinition definition = Current;
            double now = Time.timeAsDouble;
            int loaded = 0;
            if (unguided)
            {
                loaded = UnguidedRocketsLoaded;
                if (loaded == 0) return false;
            }
            else
            {
                ref WeaponCooldown selectedCooldown = ref (guided ? ref rocketCooldown : ref cooldown);
                if (!selectedCooldown.TryBegin(now, definition.Interval)) return false;
            }
            ProjectileDefinition ammunition = CurrentProjectile;
            Transform shotMuzzle = ActiveMount.Muzzle;
            Vector3 position = guided ? rocketRest.position : shotMuzzle.position;
            if (unguided) position = (loaded == UnguidedRocketCapacity ? rightRocketRest : leftRocketRest).position;
            Vector2 spread = UnityEngine.Random.insideUnitCircle * definition.Spread;
            Vector3 direction = Quaternion.AngleAxis(spread.x, shotMuzzle.up) * Quaternion.AngleAxis(spread.y, shotMuzzle.right) * shotMuzzle.forward;
            int shotId = ++shotSequence;
            if (launch == null)
            {
                if (ammunition.Appearance == ProjectileAppearance.Bullet)
                    bulletCount = (bulletCount + 1) % TracerInterval;
                bool tracer = ammunition.Appearance == ProjectileAppearance.Bullet && bulletCount == 0;
                Projectile projectile = Projectile.Launch(ammunition, position, direction, vehicle, shotId, tracer);
                if (guided) activeRocket = projectile;
                if (unguided)
                {
                    unguidedRocketsSpent = UnguidedRocketCapacity - loaded + 1;
                    if (unguidedRocketsSpent == UnguidedRocketCapacity)
                        unguidedRocketCooldown.TryBegin(now, definition.Interval);
                }
            }
            else if (!launch(definition, position, direction, vehicle, shotId)) return false;
            vehicle.Body.AddForceAtPosition(-direction * definition.Recoil, position, ForceMode.Impulse);
            if (launch == null)
                PlaygroundEffects.ShowShot(gameObject.scene, ammunition, position, shotMuzzle.forward);
            Fired?.Invoke();
            return true;
        }

        public void ResetReload()
        {
            StopRocketGuidance();
            cooldown = default;
            rocketCooldown = default;
            unguidedRocketCooldown = default;
            unguidedRocketsSpent = 0;
            bulletCount = 0;
        }
    }
}
