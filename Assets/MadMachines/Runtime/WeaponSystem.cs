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
        public const int GuidedRocketSlot = 3;
        public delegate bool ProjectileLauncher(WeaponDefinition definition, Vector3 position, Vector3 direction, VehiclePhysics owner, int shotId);

        [SerializeField] private WeaponDefinition[] definitions;
        [SerializeField] private Transform turret;
        [SerializeField] private Transform barrel;
        [SerializeField] private Transform muzzle;
        [SerializeField] private GameObject cannonVisual;
        [SerializeField] private GameObject rocketRail;
        [SerializeField] private Transform rocketMuzzle;
        [SerializeField] private Transform rocketRest;
        private VehiclePhysics vehicle;
        private WeaponCooldown cooldown;
        private WeaponCooldown rocketCooldown;
        private int shotSequence;
        private WeaponDefinition guidedRocket;
        private Projectile activeRocket;
        public int Selected { get; private set; } = 1;
        public bool Authority { get; set; } = true;
        public bool HasGuidedRocket => guidedRocket != null;
        public bool GuidedRocketSelected => HasGuidedRocket && Selected == GuidedRocketSlot;
        public bool RocketInFlight => activeRocket != null && activeRocket.InFlight;
        public bool IsGuidingRocket => activeRocket != null && activeRocket.IsGuiding;
        public float RocketFuel => RocketInFlight ? activeRocket.RemainingLifetimeFraction : RocketReloaded ? 1f : 0f;
        public WeaponDefinition Current => GuidedRocketSelected ? guidedRocket : definitions[Selected];
        public float Reload => Mathf.Clamp01((GuidedRocketSelected ? rocketCooldown : cooldown).Remaining(Time.timeAsDouble) / Current.Interval);
        private bool RocketReloaded => rocketCooldown.Remaining(Time.timeAsDouble) == 0f;
        private Transform ActiveMuzzle => GuidedRocketSelected ? rocketMuzzle : muzzle;
        public Vector3 AimDirection => ActiveMuzzle.forward;
        public Vector3 MuzzlePosition => ActiveMuzzle.position;
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
            if (HasGuidedRocket && index == GuidedRocketSlot)
            {
                Selected = index;
                cannonVisual.SetActive(false);
                rocketRail.SetActive(true);
                return;
            }
            if (index < 0 || index >= definitions.Length) return;
            if (GuidedRocketSelected)
            {
                StopRocketGuidance();
                rocketRail.SetActive(false);
                cannonVisual.SetActive(true);
            }
            Selected = index;
        }

        public void EnableGuidedRocket(WeaponDefinition definition)
        {
            if (definition == null || !definition.Guided || cannonVisual == null || rocketRail == null ||
                rocketMuzzle == null || rocketRest == null) return;
            guidedRocket = definition;
            Projectile.AddRocketVisual(rocketRest);
        }

        private void LateUpdate()
        {
            if (!GuidedRocketSelected) return;
            bool loaded = !RocketInFlight && RocketReloaded;
            if (rocketRest.gameObject.activeSelf != loaded) rocketRest.gameObject.SetActive(loaded);
        }

        public void GuideRocket(Vector3 origin, Vector3 direction)
        {
            if (!IsGuidingRocket) return;
            float distance = guidedRocket.Speed * guidedRocket.Lifetime + Vector3.Distance(origin, MuzzlePosition);
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

        public void StopRocketGuidance()
        {
            if (IsGuidingRocket) activeRocket.StopGuidance();
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
            if (!Authority || (GuidedRocketSelected && (RocketInFlight || launch != null))) return false;
            WeaponDefinition definition = Current;
            ref WeaponCooldown selectedCooldown = ref (GuidedRocketSelected ? ref rocketCooldown : ref cooldown);
            if (!selectedCooldown.TryBegin(Time.timeAsDouble, definition.Interval)) return false;
            Transform exit = ActiveMuzzle;
            Vector2 spread = UnityEngine.Random.insideUnitCircle * definition.Spread;
            Vector3 direction = Quaternion.AngleAxis(spread.x, exit.up) * Quaternion.AngleAxis(spread.y, exit.right) * exit.forward;
            int shotId = ++shotSequence;
            Vector3 position = GuidedRocketSelected ? rocketRest.position : exit.position;
            if (launch == null)
            {
                Projectile projectile = Projectile.Launch(definition, position, direction, vehicle, shotId);
                if (GuidedRocketSelected) activeRocket = projectile;
            }
            else if (!launch(definition, position, direction, vehicle, shotId)) return false;
            vehicle.Body.AddForceAtPosition(-direction * definition.Recoil, position, ForceMode.Impulse);
            if (launch == null) PlaygroundEffects.Flash(position, definition.Color, 0.8f, 0.1f);
            Fired?.Invoke();
            return true;
        }

        public void ResetReload()
        {
            StopRocketGuidance();
            cooldown = default;
            rocketCooldown = default;
        }
    }
}
