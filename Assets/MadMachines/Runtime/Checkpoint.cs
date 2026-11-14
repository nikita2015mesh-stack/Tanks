using UnityEngine;

namespace MadMachines
{
    public sealed class Checkpoint : MonoBehaviour
    {
        [SerializeField] private ChallengeManager rules;
        [SerializeField] private int index;
        [SerializeField] private Renderer[] posts;
        public int Index => index;
        public void Configure(ChallengeManager manager, int number, Renderer[] renderers)
        {
            rules = manager;
            index = number;
            posts = renderers;
        }

        public void Highlight(bool active)
        {
            foreach (Renderer post in posts)
                post.sharedMaterial = PlaygroundEffects.Material(active ? new Color(0.2f, 1f, 0.6f) : new Color(0.2f, 0.32f, 0.4f));
        }

        private void OnTriggerEnter(Collider other)
        {
            VehiclePhysics vehicle = other.GetComponentInParent<VehiclePhysics>();
            if (vehicle != null) rules.PassCheckpoint(index, vehicle);
        }
    }
}
