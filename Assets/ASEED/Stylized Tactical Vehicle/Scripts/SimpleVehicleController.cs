using UnityEngine;

namespace ASEED
{
    public class SimpleVehicleController : MonoBehaviour
    {
        [Header("Vehicle Settings")]
        public float speed = 20f;
        public float turnSpeed = 100f;

        [Header("Wheel Meshes")]
        public Transform wheelMesh_FL;
        public Transform wheelMesh_FR;
        public Transform wheelMesh_RL;
        public Transform wheelMesh_RR;

        private Rigidbody rb;
        private float moveInput;
        private float turnInput;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
        }

        void Update()
        {
            moveInput = Input.GetAxis("Vertical");
            turnInput = Input.GetAxis("Horizontal");

            SpinWheels();
        }

        void FixedUpdate()
        {
            Vector3 velocity = transform.forward * moveInput * speed;
            velocity.y = rb.linearVelocity.y;
            rb.linearVelocity = velocity;

            if (Mathf.Abs(moveInput) > 0.05f)
            {
                float turnDir = moveInput < 0 ? -turnInput : turnInput;
                Quaternion turn = Quaternion.Euler(0, turnDir * turnSpeed * Time.fixedDeltaTime, 0);
                rb.MoveRotation(rb.rotation * turn);
            }
        }

        void SpinWheels()
        {
            float spinAngle = moveInput * speed * 300f * Time.deltaTime;

            if (wheelMesh_FL != null) wheelMesh_FL.Rotate(Vector3.right * spinAngle, Space.Self);
            if (wheelMesh_FR != null) wheelMesh_FR.Rotate(Vector3.right * spinAngle, Space.Self);
            if (wheelMesh_RL != null) wheelMesh_RL.Rotate(Vector3.right * spinAngle, Space.Self);
            if (wheelMesh_RR != null) wheelMesh_RR.Rotate(Vector3.right * spinAngle, Space.Self);
        }
    }
}