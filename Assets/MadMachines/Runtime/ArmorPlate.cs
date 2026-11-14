using UnityEngine;

namespace MadMachines
{
    internal enum ArmorImpact
    {
        Stopped,
        Penetrated,
        Ricochet
    }

    [RequireComponent(typeof(BoxCollider))]
    public sealed class ArmorPlate : MonoBehaviour
    {
        private static readonly int[] Thicknesses = { 32, 48, 64, 80 };
        private static readonly int[] Tilts = { 0, 35, 55, 75 };
        [SerializeField] private int distance;
        [SerializeField, Range(0, 3)] private int initialThickness;
        [SerializeField, Range(0, 3)] private int initialTilt;
        [SerializeField] private TextMesh label;
        private int thicknessIndex;
        private int tiltIndex;
        private ShootingRange range;
        private Quaternion flatRotation;

        internal int Distance => distance;
        internal int Thickness => Thicknesses[thicknessIndex];
        internal int Tilt => Tilts[tiltIndex];

        internal void Bind(ShootingRange owner)
        {
            range = owner;
            flatRotation = transform.localRotation * Quaternion.Inverse(Quaternion.Euler(Tilts[initialTilt], 0f, 0f));
            ResetPlate();
        }

        internal void ResetPlate()
        {
            thicknessIndex = initialThickness;
            tiltIndex = initialTilt;
            SetPose();
        }

        internal void CycleThickness()
        {
            thicknessIndex = (thicknessIndex + 1) % Thicknesses.Length;
            SetPose();
        }

        internal void CycleTilt()
        {
            tiltIndex = (tiltIndex + 1) % Tilts.Length;
            SetPose();
        }

        private void SetPose()
        {
            transform.localRotation = flatRotation * Quaternion.Euler(Tilt, 0f, 0f);
            Vector3 scale = transform.localScale;
            scale.z = Thickness * 0.006f;
            transform.localScale = scale;
            if (label != null) label.text = $"{Thickness} · {Tilt}°";
        }

        internal ArmorImpact Resolve(float penetration, float alignment, out float resistance)
        {
            // Range ratings are game values independent of the displayed mesh thickness.
            resistance = Thickness / Mathf.Lerp(0.45f, 1f, alignment);
            if (penetration <= 0f) return ArmorImpact.Stopped;
            if (alignment < 0.43f) return ArmorImpact.Ricochet;
            return penetration >= resistance ? ArmorImpact.Penetrated : ArmorImpact.Stopped;
        }

        internal void Report(string ammunition, ArmorImpact result, float alignment, VehiclePhysics shooter)
        {
            if (range != null) range.RecordImpact(this, ammunition, result, alignment, shooter);
        }
    }
}
