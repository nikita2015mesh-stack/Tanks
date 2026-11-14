using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    [RequireComponent(typeof(ChallengeManager))]
    public sealed class ShootingRange : MonoBehaviour
    {
        private const int MinimumZeroDistance = 15;
        private const int MaximumZeroDistance = 1000;

        [SerializeField] private Transform armorStands;
        private ArmorPlate[] plates;
        private Target[] standTargets;
        private ChallengeManager rules;
        private VehiclePhysics player;
        private string lastImpact;
        private float impactExpires;
        private float recoveryExpires;

        internal int ZeroDistance { get; private set; } = BallisticSight.Distances[0];
        internal string LastImpact => Time.time < impactExpires ? lastImpact : null;
        internal bool RecoveredAtBoundary => Time.time < recoveryExpires;

        internal void Bind(VehiclePhysics vehicle)
        {
            player = vehicle;
            rules = GetComponent<ChallengeManager>();
            plates = armorStands.GetComponentsInChildren<ArmorPlate>(true);
            standTargets = armorStands.GetComponentsInChildren<Target>(true);
            if (!Application.isBatchMode)
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                foreach (TextMesh label in armorStands.GetComponentsInChildren<TextMesh>(true))
                {
                    label.font = font;
                    label.GetComponent<Renderer>().sharedMaterial = font.material;
                }
            }
            foreach (ArmorPlate plate in plates) plate.Bind(this);
        }

        internal void ChangeZero(float scroll, bool fine)
        {
            if (scroll == 0f) return;
            int direction = scroll > 0f ? 1 : -1;
            if (fine)
            {
                ZeroDistance = Mathf.Clamp(ZeroDistance + direction * 5, MinimumZeroDistance, MaximumZeroDistance);
                return;
            }

            int[] marks = BallisticSight.Distances;
            for (int i = direction > 0 ? 0 : marks.Length - 1; i >= 0 && i < marks.Length; i += direction)
            {
                if ((marks[i] - ZeroDistance) * direction <= 0) continue;
                ZeroDistance = marks[i];
                return;
            }
        }

        internal bool MeasureRange(Ray sight, Vector3 muzzle)
        {
            if (!gameObject.scene.GetPhysicsScene().Raycast(sight.origin, sight.direction, out RaycastHit hit,
                1100f, 1, QueryTriggerInteraction.Ignore)) return false;
            float distance = BallisticSight.HorizontalDistance(muzzle, hit.point);
            if (distance < MinimumZeroDistance || distance > MaximumZeroDistance) return false;
            ZeroDistance = Mathf.RoundToInt(distance);
            return true;
        }

        private void FixedUpdate()
        {
            if (player == null || rules.Phase == SessionPhase.Countdown) return;
            Vector3 position = transform.InverseTransformPoint(player.Body.position);
            // Keep the recovery line inside the outer edges of the berm meshes.
            if (Mathf.Abs(position.x) < 122f && position.z > -67f && position.z < 1077f && position.y > -3f)
                return;

            rules.Recover();
            recoveryExpires = Time.time + 3f;
        }

        internal ArmorPlate ViewedPlate(Camera view)
        {
            Ray ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            return gameObject.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out RaycastHit hit,
                350f, 1, QueryTriggerInteraction.Ignore) ? hit.collider.GetComponent<ArmorPlate>() : null;
        }

        internal void RecordImpact(ArmorPlate plate, string ammunition, ArmorImpact result, float alignment, VehiclePhysics shooter)
        {
            if (shooter == null || shooter != player) return;
            string outcome = result switch
            {
                ArmorImpact.Penetrated => "Пробитие",
                ArmorImpact.Ricochet => "Рикошет",
                _ => "Непробитие"
            };
            float angle = Mathf.Acos(alignment) * Mathf.Rad2Deg;
            lastImpact = $"{outcome} · {ammunition}\nСтенд {plate.Distance} м · плита {plate.Thickness} ед.\nУдар под {angle:0}° к перпендикуляру";
            impactExpires = Time.time + 4.5f;
        }

        internal void ResetTargets()
        {
            foreach (Projectile projectile in FindObjectsByType<Projectile>())
            {
                if (projectile.gameObject.scene != gameObject.scene) continue;
                projectile.gameObject.SetActive(false);
                Destroy(projectile.gameObject);
            }
            rules.ResetTargets();
            ResetStands();
        }

        internal void ResetStands()
        {
            foreach (ArmorPlate plate in plates) plate.ResetPlate();
            foreach (Target target in standTargets) target.ResetTarget();
            impactExpires = 0f;
        }
    }
}
