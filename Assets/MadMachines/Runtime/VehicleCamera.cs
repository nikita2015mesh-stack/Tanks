using UnityEngine;

namespace MadMachines
{
    [RequireComponent(typeof(Camera))]
    public sealed class VehicleCamera : MonoBehaviour
    {
        internal const float AimTransitionSeconds = 0.28f;
        internal const float DefaultAimFieldOfView = 35f;

        [SerializeField] private Transform target;
        [SerializeField, Range(15f, 60f)] private float aimFieldOfView = DefaultAimFieldOfView;
        private Camera viewCamera;
        private WeaponSystem weapons;
        private Collider[] hullColliders;
        private Vector3 chasePosition;
        private float chaseFieldOfView;
        private float aimBlend;
        private float yaw;
        private float pitch = 12f;
        private float recoilStart = -1f;
        private float recoilDuration;
        private float recoilDistance;
        public bool AimMode { get; private set; }
        internal Camera ViewCamera => viewCamera;
        public Vector3 AimDirection => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        public void Configure(Transform follow)
        {
            target = follow;
        }

        private void Start()
        {
            viewCamera = GetComponent<Camera>();
            chasePosition = transform.position;
            if (target == null) return;
            weapons = target.GetComponent<WeaponSystem>();
            hullColliders = target.GetComponentsInChildren<Collider>();
        }

        public void ToggleAimMode()
        {
            if (weapons == null) return;
            if (!AimMode && aimBlend == 0f) chaseFieldOfView = viewCamera.fieldOfView;
            AimMode = !AimMode;
        }

        public void Look(Vector2 delta)
        {
            delta = GameSettings.AdjustLook(delta);
            yaw += delta.x * 0.12f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -42f, 30f);
        }
        public void Shake()
        {
            ProjectileDefinition ammunition = weapons.CurrentProjectile;
            recoilStart = Time.time;
            recoilDuration = ammunition.IsHeavyShell ? 0.36f : ammunition.IsShell ? 0.24f : ammunition.TimedBounce ? 0.28f : 0.07f;
            recoilDistance = ammunition.IsHeavyShell ? 0.36f : ammunition.IsShell ? 0.18f : ammunition.TimedBounce ? 0.23f : 0.025f;
        }

        public void ResetView()
        {
            yaw = target.eulerAngles.y;
            pitch = 12f;
            recoilStart = -1f;
        }

        private void LateUpdate()
        {
            if (target == null || Time.deltaTime == 0f) return;
            Vector3 pivot = target.position + Vector3.up * 1.6f;
            Vector3 offset = Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 1f, -9f);
            if (Physics.SphereCast(pivot, 0.3f, offset.normalized, out RaycastHit hit, offset.magnitude, 1, QueryTriggerInteraction.Ignore)) offset = offset.normalized * Mathf.Max(0.6f, hit.distance - 0.2f);
            chasePosition = Vector3.Lerp(chasePosition, pivot + offset, 1f - Mathf.Exp(-12f * Time.deltaTime));
            Vector3 position = chasePosition;
            if (AimMode || aimBlend > 0f)
            {
                aimBlend = Mathf.MoveTowards(aimBlend, AimMode ? 1f : 0f, Time.deltaTime / AimTransitionSeconds);
                float blend = Mathf.SmoothStep(0f, 1f, aimBlend);
                viewCamera.fieldOfView = Mathf.Lerp(chaseFieldOfView, Mathf.Min(aimFieldOfView, chaseFieldOfView), blend);
                if (blend > 0f)
                {
                    Vector3 aimPosition = GetAimPosition(weapons.MountPosition, AimDirection, hullColliders);
                    position = ClearHull(Vector3.Lerp(chasePosition, aimPosition, blend));
                }
            }
            if (recoilStart >= 0f)
            {
                float age = Mathf.Clamp01((Time.time - recoilStart) / recoilDuration);
                float recoil = Mathf.Sin(Mathf.PI * age) * (1f - age);
                position += (Vector3.up * 0.64f - AimDirection * 0.36f) * (recoil * recoilDistance);
            }
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(AimDirection);
            if (weapons != null) weapons.GuideRocket(position, AimDirection);
        }

        internal static Vector3 GetAimPosition(Vector3 mount, Vector3 lookDirection, Collider[] hullColliders)
        {
            Vector3 offset = -lookDirection * 3.8f + Vector3.up * 1.35f;
            Vector3 direction = offset.normalized;
            float distance = offset.magnitude;
            foreach (RaycastHit hit in Physics.SphereCastAll(mount, 0.25f, direction, distance, 1, QueryTriggerInteraction.Ignore))
            {
                if (System.Array.IndexOf(hullColliders, hit.collider) >= 0) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.15f));
            }
            return mount + direction * distance;
        }

        private Vector3 ClearHull(Vector3 position) => ClearHull(position, hullColliders);

        internal static Vector3 ClearHull(Vector3 position, Collider[] hullColliders)
        {
            // Lift the transition over the chassis, keeping the near plane clear even when it rolls.
            const float clearance = 0.5f;
            foreach (Collider hull in hullColliders)
            {
                if (!hull.enabled || hull.isTrigger) continue;
                Bounds bounds = hull.bounds;
                Vector3 horizontal = new Vector3(position.x, bounds.center.y, position.z);
                float distance = Vector3.Distance(horizontal, bounds.ClosestPoint(horizontal));
                float lift = Mathf.SmoothStep(1f, 0f, (distance - clearance) / clearance);
                position.y = Mathf.Lerp(position.y, Mathf.Max(position.y, bounds.max.y + clearance), lift);
            }
            return position;
        }
    }
}
