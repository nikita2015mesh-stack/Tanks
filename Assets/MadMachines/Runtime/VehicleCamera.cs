using UnityEngine;

namespace MadMachines
{
    public sealed class VehicleCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        private float yaw;
        private float pitch = 12f;
        private float shake;
        public Vector3 AimDirection => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        public void Configure(Transform follow)
        {
            target = follow;
        }
        public void Look(Vector2 delta)
        {
            yaw += delta.x * 0.12f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.12f, -42f, 30f);
        }
        public void Shake()
        {
            shake = 0.22f;
        }

        public void ResetView()
        {
            yaw = target.eulerAngles.y;
            pitch = 12f;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 pivot = target.position + Vector3.up * 1.6f;
            Vector3 offset = Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 1f, -9f);
            if (Physics.SphereCast(pivot, 0.3f, offset.normalized, out RaycastHit hit, offset.magnitude, 1, QueryTriggerInteraction.Ignore)) offset = offset.normalized * Mathf.Max(0.6f, hit.distance - 0.2f);
            transform.position = Vector3.Lerp(transform.position, pivot + offset, 1f - Mathf.Exp(-12f * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(AimDirection);
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime);
            if (shake > 0f) transform.position += Random.insideUnitSphere * shake * 0.2f;
        }
    }
}
