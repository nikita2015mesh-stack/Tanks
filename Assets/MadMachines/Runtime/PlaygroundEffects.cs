using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines
{
    public sealed class PlaygroundEffects : MonoBehaviour
    {
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        private float birth;
        private float duration;
        private float size;
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

        public static void Flash(Vector3 position, Color color, float size, float duration)
        {
            GameObject effect = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            effect.name = "Impact burst";
            effect.transform.position = position;
            effect.layer = 2;
            effect.GetComponent<Collider>().enabled = false;
            Destroy(effect.GetComponent<Collider>());
            effect.GetComponent<Renderer>().sharedMaterial = Material(color);
            PlaygroundEffects animation = effect.AddComponent<PlaygroundEffects>();
            animation.birth = Time.time;
            animation.duration = duration;
            animation.size = size;
            effect.transform.localScale = Vector3.one * 0.1f;
            Destroy(effect, duration);
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

        private void Update()
        {
            float progress = Mathf.Clamp01((Time.time - birth) / duration);
            transform.localScale = Vector3.one * size * Mathf.Sin(progress * Mathf.PI);
        }
    }
}
