using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MadMachines.Editor
{
    internal static class QuarryDriveEditor
    {
        private const string PrepareMenu = QuarryPrototypeEditor.Menu + "Prepare Quarry_01 drive";
        private const string DriveRoot = "Quarry_01_Drive";
        private const string UndoLabel = "Prepare Quarry_01 drive";
        private const string GuideFolder = "Assets/MadMachines/Generated/Quarry_01/";
        private const string VehiclePath = "Assets/MadMachines/Vehicles/T100.asset";

        [MenuItem(PrepareMenu)]
        private static void PrepareDrive()
        {
            if (!CanPrepareDrive()) return;
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                GameObject[] roots = scene.GetRootGameObjects();
                GameObject existing = roots.FirstOrDefault(item => item.name == DriveRoot);
                if (existing != null)
                {
                    Selection.activeGameObject = existing;
                    Debug.Log("Пробный заезд Quarry_01 уже подготовлен. Нажмите Play и выберите «Свободная езда».", existing);
                    return;
                }
                if (roots.Any(item => item.GetComponentInChildren<LocalVehicleSession>(true) != null))
                    throw new InvalidOperationException("В сцене уже есть локальная сессия машины.");

                Transform generated = roots.Single(item => item.name == QuarryPrototypeEditor.GeneratedRoot).transform;
                Terrain terrain = generated.Find("Terrain")?.GetComponent<Terrain>();
                if (terrain == null || terrain.terrainData == null || !terrain.TryGetComponent(out TerrainCollider collider) ||
                    !collider.enabled || collider.terrainData != terrain.terrainData ||
                    terrain.terrainData.size != new Vector3(1500f, 210f, 1500f))
                    throw new InvalidOperationException("Для заезда нужен Terrain Quarry_01 со штатным размером и включённым TerrainCollider.");

                VehicleDefinition vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(VehiclePath);
                if (vehicle == null || vehicle.Prefab == null || !vehicle.Prefab.gameObject.activeSelf ||
                    !vehicle.Prefab.TryGetComponent<WeaponSystem>(out _))
                    throw new InvalidOperationException("Для заезда нужно описание T100 с активным префабом и оружием.");
                Vector2[][] routes = QuarryPrototypeSource.ReadRoutes();
                Material spiral = GetGuideMaterial("QuarrySpiralGuide", new Color(1f, 0.73f, 0.12f));
                Material rings = GetGuideMaterial("QuarryRingGuide", new Color(0.02f, 0.82f, 0.76f));
                CreateDrive(scene, generated.Find("Overview Camera"), terrain, routes, vehicle, spiral, rings);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Quarry_01", exception.Message, "OK");
            }
        }

        [MenuItem(PrepareMenu, true)]
        private static bool CanPrepareDrive()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null ||
                SceneManager.sceneCount != 1) return false;

            Scene scene = SceneManager.GetActiveScene();
            string prototype = QuarryPrototypeEditor.SceneName;
            return scene.path == QuarryPrototypeEditor.ScenePath(scene.name) &&
                (scene.name == prototype || scene.name.StartsWith(prototype + "_v", StringComparison.Ordinal));
        }

        private static Material GetGuideMaterial(string name, Color color)
        {
            string path = GuideFolder + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            if (File.Exists(QuarryPrototypeSource.ProjectPath(path)))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) return material;
            }

            // If Unity still cannot import the original file, keep it and let Unity write a new asset.
            if (QuarryPrototypeEditor.PathOccupied(path))
            {
                path = GuideFolder + name + "_Recovered.mat";
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) return material;
                if (QuarryPrototypeEditor.PathOccupied(path))
                    throw new IOException("Занят путь для восстановления материала: " + path);
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                throw new InvalidOperationException("Для маршрутной разметки нужен шейдер URP/Unlit.");
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Cull", (float)CullMode.Off);
            AssetDatabase.CreateAsset(material, path);
            if (AssetDatabase.GetAssetPath(material) != path)
                throw new IOException("Не удалось сохранить материал: " + path);
            return material;
        }

        private static void CreateDrive(Scene scene, Transform overview, Terrain terrain, Vector2[][] routes,
            VehicleDefinition vehicle, Material spiral, Material rings)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            GameObject root = null;
            try
            {
                root = new GameObject(DriveRoot);
                var guides = new GameObject("Route guides").transform;
                guides.SetParent(root.transform, false);
                AddGuide(guides, terrain, "M0", routes[0], spiral, false);
                for (int i = 1; i < routes.Length; i++)
                    AddGuide(guides, terrain, "R" + i, routes[i], rings, true);
                AddSession(root, terrain, routes[0], vehicle);

                Undo.RegisterCreatedObjectUndo(root, UndoLabel);
                if (overview != null)
                {
                    Undo.RegisterCompleteObjectUndo(overview.gameObject, UndoLabel);
                    overview.gameObject.SetActive(false);
                }
                Undo.RegisterCompleteObjectUndo(terrain, UndoLabel);
                terrain.heightmapPixelError = Mathf.Min(terrain.heightmapPixelError, 2f);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                Selection.activeGameObject = root;
                Debug.Log("Пробный заезд готов. Сохраните сцену, нажмите Play и выберите «Свободная езда».", root);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static void AddGuide(Transform parent, Terrain terrain, string id, Vector2[] route, Material material, bool loop)
        {
            LineRenderer line = new GameObject(id).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            line.alignment = LineAlignment.TransformZ;
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.loop = loop;
            line.widthMultiplier = loop ? 1.8f : 2.4f;
            Vector3 lift = Vector3.up * (loop ? 0.22f : 0.36f);
            var points = new Vector3[route.Length];
            for (int i = 0; i < route.Length; i++)
                points[i] = line.transform.InverseTransformPoint(SurfacePoint(terrain, route[i]) + lift);
            line.positionCount = points.Length;
            line.SetPositions(points);
        }

        private static void AddSession(GameObject root, Terrain terrain, Vector2[] spiral, VehicleDefinition vehicle)
        {
            int entryIndex = spiral.Length - 10;
            Vector3 entry = SurfacePoint(terrain, spiral[entryIndex]);
            Vector3 downhill = SurfacePoint(terrain, spiral[entryIndex - 2]) - entry;
            Quaternion heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(downhill, Vector3.up));
            var start = new GameObject("Vehicle start").transform;
            start.SetParent(root.transform, false);
            start.SetPositionAndRotation(entry + Vector3.up * 1.5f, heading);

            var cameraObject = new GameObject("Quarry Drive Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.tag = "MainCamera";
            Quaternion cameraAngle = Quaternion.Euler(12f, heading.eulerAngles.y, 0f);
            cameraObject.transform.SetPositionAndRotation(
                start.position + Vector3.up * 1.6f + cameraAngle * new Vector3(0f, 1f, -9f), cameraAngle);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 65f;
            camera.farClipPlane = 2400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.32f, 0.47f, 0.57f);
            VehicleCamera view = cameraObject.AddComponent<VehicleCamera>();

            root.AddComponent<LocalVehicleSession>().Configure(vehicle, start, view);
            root.GetComponent<ChallengeManager>().Configure(null, Array.Empty<Checkpoint>(), Array.Empty<Target>());
            using var hud = new SerializedObject(root.GetComponent<PlaygroundHud>());
            hud.FindProperty("locationName").stringValue = "Карьер «Большая чаша»";
            hud.FindProperty("freeDriveOnly").boolValue = true;
            hud.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector3 SurfacePoint(Terrain terrain, Vector2 xz)
        {
            TerrainData data = terrain.terrainData;
            float y = data.GetInterpolatedHeight(xz.x / data.size.x, xz.y / data.size.z);
            return terrain.GetPosition() + new Vector3(xz.x, y, xz.y);
        }
    }
}
