using UnityEngine;

namespace MadMachines
{
    public sealed class Target : MonoBehaviour
    {
        [SerializeField] private int points = 100;
        [SerializeField] private float travel;
        [SerializeField] private Vector3 home;
        [SerializeField] private ChallengeManager rules;
        private float health = 100f;
        public bool Destroyed => health <= 0f;
        public int Points => points;

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
            rules.RegisterHit(attacker, shotId);
            PlaygroundEffects.Flash(transform.position, Color.white, 1.5f, 0.12f);
            if (Destroyed)
            {
                rules.TargetDestroyed(this, attacker);
                gameObject.SetActive(false);
            }
        }

        public void ResetTarget()
        {
            health = 100f;
            transform.position = home;
            if (TryGetComponent(out Rigidbody body)) body.position = home;
            gameObject.SetActive(true);
        }
    }
}
