using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    public static class PlaygroundEffects
    {
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        public static Material Material(Color color)
        {
            if (!Materials.TryGetValue(color, out Material material) || material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                material = new Material(shader) { color = color };
                Materials[color] = material;
            }
            return material;
        }

        internal static void ShowShot(Scene scene, ProjectileDefinition ammunition, Vector3 point, Vector3 direction)
        {
            if (Application.isBatchMode) return;
            DrivingEffects.In(scene).Muzzle(ammunition, point, direction);
        }

        internal static void ShowImpact(Scene scene, ProjectileDefinition ammunition, Vector3 point, bool hit)
        {
            if (Application.isBatchMode || (!hit && ammunition.BlastRadius <= 0f)) return;
            DrivingEffects.In(scene).Impact(ammunition, point);
        }

        internal static void ShowArmorImpact(Scene scene, Vector3 point, Vector3 normal, ArmorImpact result)
        {
            if (Application.isBatchMode) return;
            DrivingEffects.In(scene).PlateImpact(point, normal, result);
        }
    }
}
