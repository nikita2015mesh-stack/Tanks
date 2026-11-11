using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadMachines.Editor
{
    internal static class QuarryPreviewEditor
    {
        private const string FlatMenu = "Tools/Mad Machines/Quarry_01 — собрать дорожный прототип";
        private const string TerraceMenu = "Tools/Mad Machines/Quarry_01 — вырезать ступени по контурам";
        private const float HalfWidth = 2.5f;
        private const float GroundTop = -0.04f;

        [MenuItem(FlatMenu)]
        private static void BuildFlat() => Build(false);

        [MenuItem(TerraceMenu)]
        private static void BuildTerraces() => Build(true);

        private static void Build(bool withTerraces)
        {
            try
            {
                BuildScene(withTerraces);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Quarry_01: {exception.Message}");
            }
        }

        [MenuItem(FlatMenu, true)]
        private static bool CanBuildFlat() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(TerraceMenu, true)]
        private static bool CanBuildTerraces() => CanBuildFlat();

        private static void BuildScene(bool withTerraces)
        {
            string name = withTerraces ? "Quarry_01_Terraces" : "Quarry_01";
            string assetFolder = $"Assets/MadMachines/Generated/{name}";
            string scenePath = $"Assets/MadMachines/Scenes/{name}.unity";
            QuarryRoadImport layout = withTerraces ? null : QuarryRoadImport.Load();
            VehicleDefinition vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(
                "Assets/MadMachines/Vehicles/T100.asset");
            if (vehicle == null || vehicle.Prefab == null) throw new InvalidOperationException("Не найдена T100.");

            string shaderName = GraphicsSettings.currentRenderPipeline != null ?
                "Universal Render Pipeline/Lit" : "Standard";
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Не найден шейдер {shaderName}.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var terraces = withTerraces ? new QuarryTerraceMesh() : null;
            var terraceContours = withTerraces ? QuarryRoadImport.LoadTerraces() : null;
            Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
            // Opening a new scene unloads temporary meshes. Store this one before switching scenes.
            Mesh terraceMesh = withTerraces
                ? SaveAsset(terraces.Build(terraceContours), $"{assetFolder}/Ступени.asset")
                : null;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Quarry_01 — Большая чаша");
            if (withTerraces)
                CreateTerraces(root.transform, terraces, terraceMesh, shader, assetFolder);
            else
                CreateFlatLayout(root.transform, layout, shader, assetFolder);

            var sun = new GameObject("Солнце");
            sun.AddComponent<Light>().type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var start = new GameObject(withTerraces ? "Старт T100 — дно" : "Старт T100 — D01").transform;
            start.SetParent(root.transform, false);
            if (withTerraces)
                start.position = World(terraces.FloorStart, 1.5f);
            else
            {
                List<Vector2> loop = layout.Roads["D01"].Points;
                start.position = World(loop[0], 1.5f);
                start.rotation = Quaternion.LookRotation(World(loop[1] - loop[0]));
            }
            var camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            camera.AddComponent<Camera>();
            camera.AddComponent<AudioListener>();
            camera.transform.SetPositionAndRotation(start.position - start.forward * 9f + Vector3.up * 2.5f,
                start.rotation);
            VehicleCamera view = camera.AddComponent<VehicleCamera>();

            var session = new GameObject("Свободная езда");
            session.AddComponent<LocalVehicleSession>().Configure(vehicle, start, view);
            session.GetComponent<ChallengeManager>().Configure(null, Array.Empty<Checkpoint>(), Array.Empty<Target>());
            session.AddComponent<QuarryPreviewStart>();
            var hud = new SerializedObject(session.GetComponent<PlaygroundHud>());
            hud.FindProperty("locationName").stringValue = "Карьер — Большая чаша";
            hud.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException($"Не удалось сохранить {scenePath}.");

            Selection.activeGameObject = root;
            Debug.Log($"{name}: прототип сохранён. Нажмите Play для свободной езды.");
        }

        private static void CreateTerraces(Transform parent, QuarryTerraceMesh terraces, Mesh mesh, Shader shader, string folder)
        {
            var obj = new GameObject("Ступени карьера");
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            var materials = new List<Material>();
            foreach (int height in terraces.FaceHeights)
            {
                string name = height < 0 ? "Стенки" : $"Площадка {height} м";
                Color color = height < 0 ? new Color(0.44f, 0.37f, 0.31f) :
                    Color.Lerp(new Color(0.64f, 0.52f, 0.39f), new Color(0.57f, 0.59f, 0.49f),
                        (float)height / QuarryTerraceMesh.TopHeight);
                var material = new Material(shader) { color = color };
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.05f);
                materials.Add(SaveAsset(material, $"{folder}/{name}.mat"));
            }
            obj.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
        }

        private static void CreateFlatLayout(Transform parent, QuarryRoadImport layout, Shader shader, string folder)
        {
            Material roadMaterial = SaveAsset(new Material(shader) { color = new Color(0.76f, 0.45f, 0.22f) },
                $"{folder}/Дороги.mat");
            Material groundMaterial = SaveAsset(new Material(shader) { color = new Color(0.27f, 0.29f, 0.28f) },
                $"{folder}/Площадка.mat");

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Площадка 1500 × 1500 м";
            ground.transform.SetParent(parent, false);
            float halfMap = QuarryRoadImport.MapSize * 0.5f;
            ground.transform.position = new Vector3(halfMap, GroundTop - 0.5f, halfMap);
            ground.transform.localScale = new Vector3(QuarryRoadImport.MapSize, 1f, QuarryRoadImport.MapSize);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var roads = new GameObject("Дороги D01–D10");
            roads.transform.SetParent(parent, false);
            foreach (var entry in layout.Roads)
                AddSurface(roads.transform, entry.Key, RoadMesh(entry.Value), roadMaterial, folder);

            var junctions = new GameObject("Перекрёстки");
            junctions.transform.SetParent(parent, false);
            foreach (var entry in layout.Junctions)
            {
                var surface = new Surface();
                surface.AddDisc(Vector2.zero);
                foreach (Vector2 connection in entry.Value.Connections)
                {
                    Vector2 end = connection - entry.Value.Position;
                    if (end.sqrMagnitude < 0.000001f) continue;
                    Vector2 side = Side(end) * HalfWidth;
                    surface.AddQuad(side, -side, end + side, end - side);
                    surface.AddDisc(end);
                }
                GameObject junction = AddSurface(junctions.transform, entry.Key, surface.Build(), roadMaterial, folder);
                junction.transform.position = World(entry.Value.Position);
            }
        }

        private static Vector3 World(Vector2 point, float height = 0f) => new Vector3(point.x, height, point.y);

        private static Vector2 Side(Vector2 direction) => new Vector2(-direction.y, direction.x).normalized;

        private static Mesh RoadMesh(QuarryRoadImport.Road road)
        {
            var surface = new Surface();
            int count = road.Points.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 point = road.Points[i];
                Vector2 before = Side(point - road.Points[(i + count - 1) % count]);
                Vector2 after = Side(road.Points[(i + 1) % count] - point);
                if (!road.Closed && i == 0) before = after;
                if (!road.Closed && i == count - 1) after = before;
                Vector2 bisector = (before + after).normalized;
                Vector2 offset = bisector == Vector2.zero ? after * HalfWidth :
                    bisector * (HalfWidth / Mathf.Max(0.5f, Vector2.Dot(bisector, after)));
                surface.Vertices.Add(World(point + offset));
                surface.Vertices.Add(World(point - offset));
            }

            for (int i = 0; i < (road.Closed ? count : count - 1); i++)
            {
                int a = 2 * i;
                int b = 2 * ((i + 1) % count);
                surface.AddTriangle(a, b, a + 1);
                surface.AddTriangle(a + 1, b, b + 1);
            }
            return surface.Build();
        }

        private static GameObject AddSurface(Transform parent, string id, Mesh mesh, Material material, string folder)
        {
            mesh = SaveAsset(mesh, $"{folder}/{id}.asset");
            var obj = new GameObject(id);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            return obj;
        }

        private static T SaveAsset<T>(T value, string path) where T : UnityEngine.Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(value, path);
                return value;
            }
            value.name = existing.name;
            EditorUtility.CopySerialized(value, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(value);
            return existing;
        }

        private sealed class Surface
        {
            private const int CircleSegments = 24;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();

            public void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            public void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                int first = Vertices.Count;
                Vertices.Add(World(a));
                Vertices.Add(World(b));
                Vertices.Add(World(c));
                Vertices.Add(World(d));
                AddTriangle(first, first + 2, first + 1);
                AddTriangle(first + 1, first + 2, first + 3);
            }

            public void AddDisc(Vector2 point)
            {
                int center = Vertices.Count;
                Vertices.Add(World(point));
                for (int i = 0; i < CircleSegments; i++)
                {
                    float angle = i * (2f * Mathf.PI / CircleSegments);
                    Vertices.Add(World(point + HalfWidth * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))));
                    AddTriangle(center, center + 1 + (i + 1) % CircleSegments, center + 1 + i);
                }
            }

            public Mesh Build()
            {
                var mesh = new Mesh();
                mesh.SetVertices(Vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
