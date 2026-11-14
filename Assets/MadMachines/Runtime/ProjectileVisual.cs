using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadMachines
{
    internal static class ProjectileVisual
    {
        private const int Sides = 16;
        private static Mesh bullet;
        private static Mesh shell;
        private static Mesh penetrator;
        private static Mesh grenade;
        private static Mesh rocket;
        private static Material copper;
        private static Material steel;
        private static Material bronze;
        private static Material grenadeCase;
        private static Material rocketCase;
        private static Material rocketNose;

        internal static Transform Attach(GameObject shot, ProjectileAppearance appearance)
        {
            if (shot.TryGetComponent(out MeshRenderer original)) original.enabled = false;
            if (Application.isBatchMode) return null;

            GameObject model = new GameObject("Projectile body") { layer = shot.layer };
            model.transform.SetParent(shot.transform, false);
            MeshFilter filter = model.AddComponent<MeshFilter>();
            MeshRenderer renderer = model.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (appearance == ProjectileAppearance.Grenade)
            {
                if (grenade == null)
                {
                    grenade = Lathe("Heavy grenade", new[]
                    {
                        new Vector2(-0.56f, 0f), new Vector2(-0.56f, 0.20f), new Vector2(-0.49f, 0.28f),
                        new Vector2(-0.37f, 0.32f), new Vector2(0.19f, 0.32f), new Vector2(0.36f, 0.26f),
                        new Vector2(0.43f, 0.13f), new Vector2(0.49f, 0.13f), new Vector2(0.49f, 0f)
                    }, 2, 3);
                    grenadeCase = Metal("Grenade case", new Color(0.25f, 0.28f, 0.19f), 0.27f);
                    if (bronze == null) bronze = Metal("Shell bronze", new Color(0.58f, 0.36f, 0.18f), 0.48f);
                }
                model.transform.localScale = Vector3.one * 0.48f;
                filter.sharedMesh = grenade;
                renderer.sharedMaterials = new[] { grenadeCase, bronze };
            }
            else if (appearance == ProjectileAppearance.Rocket)
            {
                if (rocket == null)
                {
                    rocket = Lathe("Guided rocket", new[]
                    {
                        new Vector2(-0.48f, 0f), new Vector2(-0.48f, 0.07f), new Vector2(-0.4f, 0.105f),
                        new Vector2(0.28f, 0.105f), new Vector2(0.4f, 0.085f), new Vector2(0.52f, 0f)
                    }, 3, 5);
                    rocketCase = Metal("Rocket case", new Color(0.26f, 0.32f, 0.18f), 0.3f);
                    rocketNose = Metal("Rocket nose", new Color(0.95f, 0.45f, 0.08f), 0.38f);
                }
                filter.sharedMesh = rocket;
                renderer.sharedMaterials = new[] { rocketCase, rocketNose };
                AddRocketFin(model.transform, new Vector3(0.38f, 0.035f, 0.23f));
                AddRocketFin(model.transform, new Vector3(0.035f, 0.38f, 0.23f));
            }
            else if (appearance == ProjectileAppearance.Bullet)
            {
                if (bullet == null) CreateBullet();
                model.transform.localScale = Vector3.one * 0.48f;
                filter.sharedMesh = bullet;
                renderer.sharedMaterial = copper;
            }
            else if (appearance == ProjectileAppearance.Penetrator)
            {
                model.transform.localScale = Vector3.one * 0.39f;
                if (penetrator == null)
                    penetrator = Lathe("Penetrator", new[]
                    {
                        new Vector2(-1.16f, 0f), new Vector2(-1.16f, 0.22f), new Vector2(-1.04f, 0.28f),
                        new Vector2(-0.29f, 0.28f), new Vector2(0.03f, 0.255f), new Vector2(0.36f, 0.15f),
                        new Vector2(0.64f, 0.05f), new Vector2(0.72f, 0f)
                    });
                if (steel == null) steel = Metal("Shell steel", new Color(0.37f, 0.40f, 0.43f), 0.38f);
                filter.sharedMesh = penetrator;
                renderer.sharedMaterial = steel;
            }
            else
            {
                if (shell == null) CreateShell();
                model.transform.localScale = Vector3.one * 0.34f;
                filter.sharedMesh = shell;
                renderer.sharedMaterials = new[] { steel, bronze };
            }
            return model.transform;
        }

        internal static void PointAlong(Transform model, Vector3 velocity)
        {
            if (model != null && velocity.sqrMagnitude > 0.001f)
                model.rotation = Quaternion.LookRotation(velocity);
        }

        private static void AddRocketFin(Transform parent, Vector3 size)
        {
            GameObject fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.name = "Rocket fins";
            fin.layer = parent.gameObject.layer;
            fin.transform.SetParent(parent, false);
            fin.transform.localPosition = new Vector3(0f, 0f, -0.29f);
            fin.transform.localScale = size;
            Collider collider = fin.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            MeshRenderer renderer = fin.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.sharedMaterial = rocketCase;
        }

        private static void CreateBullet()
        {
            // Profiles are (axial position, radius) in units of the existing collision diameter.
            bullet = Lathe("Bullet", new[]
            {
                new Vector2(-1.03f, 0f), new Vector2(-1.03f, 0.26f), new Vector2(-0.96f, 0.32f),
                new Vector2(-0.25f, 0.32f), new Vector2(-0.05f, 0.30f), new Vector2(0.15f, 0.25f),
                new Vector2(0.34f, 0.16f), new Vector2(0.46f, 0.065f), new Vector2(0.48f, 0f)
            });
            copper = Metal("Bullet copper", new Color(0.64f, 0.35f, 0.21f), 0.46f);
        }

        private static void CreateShell()
        {
            shell = Lathe("Cannon shell", new[]
            {
                new Vector2(-1.04f, 0f), new Vector2(-1.04f, 0.26f), new Vector2(-0.95f, 0.33f),
                new Vector2(-0.77f, 0.33f), new Vector2(-0.73f, 0.40f), new Vector2(-0.57f, 0.40f),
                new Vector2(-0.53f, 0.34f), new Vector2(-0.16f, 0.34f), new Vector2(0.06f, 0.31f),
                new Vector2(0.25f, 0.24f), new Vector2(0.42f, 0.13f), new Vector2(0.49f, 0.045f),
                new Vector2(0.49f, 0f)
            }, 3, 6);
            if (steel == null) steel = Metal("Shell steel", new Color(0.37f, 0.40f, 0.43f), 0.38f);
            if (bronze == null) bronze = Metal("Shell bronze", new Color(0.58f, 0.36f, 0.18f), 0.48f);
        }

        private static Material Metal(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name, color = color };
            material.SetFloat("_Metallic", 0.58f);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static Mesh Lathe(string name, Vector2[] profile, int bandStart = 0, int bandEnd = 0)
        {
            Vector3[] vertices = new Vector3[profile.Length * Sides];
            List<int> body = new List<int>();
            List<int> band = new List<int>();
            for (int ring = 0; ring < profile.Length; ring++)
            {
                Vector2 section = profile[ring];
                List<int> indices = ring >= bandStart && ring < bandEnd ? band : body;
                for (int i = 0; i < Sides; i++)
                {
                    float angle = i * (2f * Mathf.PI / Sides);
                    int current = ring * Sides + i;
                    vertices[current] = new Vector3(Mathf.Cos(angle) * section.y, Mathf.Sin(angle) * section.y, section.x);
                    if (ring == profile.Length - 1) continue;

                    int beside = ring * Sides + (i + 1) % Sides;
                    if (section.y > 0f)
                    {
                        indices.Add(current);
                        indices.Add(beside);
                        indices.Add(current + Sides);
                    }
                    if (profile[ring + 1].y > 0f)
                    {
                        indices.Add(beside);
                        indices.Add(beside + Sides);
                        indices.Add(current + Sides);
                    }
                }
            }

            Mesh mesh = new Mesh { name = name, vertices = vertices, subMeshCount = band.Count == 0 ? 1 : 2 };
            mesh.SetTriangles(body, 0);
            if (band.Count > 0) mesh.SetTriangles(band, 1);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
