using UnityEngine;

namespace MadMachines
{
    public sealed class Target : MonoBehaviour
    {
        [SerializeField] private int points = 100;
        [SerializeField] private float travel;
        [SerializeField] private Vector3 home;
        [SerializeField] private ChallengeManager rules;
        [SerializeField, Min(1f)] private float hitPoints = 100f;
        private float health;
        public bool Destroyed => health <= 0f;
        public int Points => points;

        private void Awake()
        {
            health = hitPoints;
        }

        public void Configure(ChallengeManager manager, int value, float movement)
        {
            rules = manager;
            points = value;
            travel = movement;
            home = transform.position;
        }

        private void FixedUpdate()
        {
            if (!Destroyed && travel > 0f)
                GetComponent<Rigidbody>().MovePosition(home + Vector3.right * Mathf.Sin(Time.time * 0.8f) * travel);
        }

        public void Hit(float damage, VehiclePhysics attacker, int shotId = 0)
        {
            if (Destroyed || attacker == null) return;
            health -= damage;
            if (rules != null) rules.RegisterHit(attacker, shotId);
            if (Destroyed)
            {
                if (rules != null) rules.TargetDestroyed(this, attacker);
                gameObject.SetActive(false);
            }
        }

        public void ResetTarget()
        {
            health = hitPoints;
            transform.position = home;
            if (TryGetComponent(out Rigidbody body)) body.position = home;
            gameObject.SetActive(true);
        }
    }
}
