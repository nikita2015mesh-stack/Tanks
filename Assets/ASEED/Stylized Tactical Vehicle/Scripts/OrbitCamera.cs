using UnityEngine;

namespace ASEED
{
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public float distance = 8f;
        public float mouseSensitivity = 3f;

        private float rotationY = 0f;
        private float rotationX = 20f;

        void LateUpdate()
        {
            if (target == null) return;

            rotationY += Input.GetAxis("Mouse X") * mouseSensitivity;
            rotationX -= Input.GetAxis("Mouse Y") * mouseSensitivity;

            rotationX = Mathf.Clamp(rotationX, 0f, 60f);

            Quaternion rotation = Quaternion.Euler(rotationX, rotationY, 0);
            Vector3 position = target.position - (rotation * Vector3.forward * distance);

            transform.position = position;
            transform.LookAt(target.position + Vector3.up * 1.5f);
        }
    }
}