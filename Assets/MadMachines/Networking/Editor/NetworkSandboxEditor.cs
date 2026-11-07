using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Networking.Editor
{
    public static class NetworkSandboxEditor
    {
        private const string Root = "Assets/MadMachines/Networking";
        private const string Generated = Root + "/Generated";
        public const string ScenePath = Root + "/Scenes/NetworkSandbox.unity";

        [MenuItem("Tools/Mad Machines/Networking/Create or open sandbox")]
        public static void OpenSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Generate();
            EditorSceneManager.OpenScene(ScenePath);
        }

        // This runs only when the user explicitly selects the menu or invokes a build method.
        // Additive generation preserves the current scene and never opens or saves Playground.
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Остановите Play Mode перед созданием сцены.");
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();
            Scene previousScene = SceneManager.GetActiveScene();
            Scene sandbox = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(sandbox);
            try
            {
                Material ground = MaterialAsset("Ground", new Color(0.22f, 0.28f, 0.3f));
                Material concrete = MaterialAsset("Concrete", new Color(0.48f, 0.5f, 0.55f));
                Material hull = MaterialAsset("Hull", Color.white);
                Material rubber = MaterialAsset("Rubber", new Color(0.06f, 0.07f, 0.08f));
                Material shot = MaterialAsset("Projectile", new Color(1f, 0.8f, 0.12f));
                Material orange = MaterialAsset("SpawnOrange", new Color(0.8f, 0.24f, 0.05f));
                Material blue = MaterialAsset("SpawnBlue", new Color(0.05f, 0.3f, 0.8f));
                NetworkBallisticProjectile projectile = ProjectilePrefab(shot);
                GameObject vehicle = VehiclePrefab(hull, rubber, projectile);

                GameObject arena = new GameObject("Network arena");
                Primitive("Ground", PrimitiveType.Cube, arena.transform, new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 120f), ground, true);
                Primitive("North wall", PrimitiveType.Cube, arena.transform, new Vector3(0f, 2f, 60f), new Vector3(120f, 4f, 1f), concrete, true);
                Primitive("South wall", PrimitiveType.Cube, arena.transform, new Vector3(0f, 2f, -60f), new Vector3(120f, 4f, 1f), concrete, true);
                Primitive("East wall", PrimitiveType.Cube, arena.transform, new Vector3(60f, 2f, 0f), new Vector3(1f, 4f, 120f), concrete, true);
                Primitive("West wall", PrimitiveType.Cube, arena.transform, new Vector3(-60f, 2f, 0f), new Vector3(1f, 4f, 120f), concrete, true);
                Primitive("Obstacle 1", PrimitiveType.Cube, arena.transform, new Vector3(-12f, 1f, 14f), new Vector3(5f, 2f, 3f), concrete, true);
                Primitive("Obstacle 2", PrimitiveType.Cube, arena.transform, new Vector3(12f, 1.5f, -14f), new Vector3(3f, 3f, 7f), concrete, true);
                GameObject ramp = Primitive("Ramp", PrimitiveType.Cube, arena.transform, new Vector3(15f, 0.8f, 18f), new Vector3(7f, 1f, 8f), concrete, true);
                ramp.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                Primitive("Spawn pad 0", PrimitiveType.Cube, arena.transform, new Vector3(-8f, 0.01f, 0f), new Vector3(5f, 0.02f, 5f), orange, false);
                Primitive("Spawn pad 1", PrimitiveType.Cube, arena.transform, new Vector3(8f, 0.01f, 0f), new Vector3(5f, 0.02f, 5f), blue, false);

                Transform[] starts = new Transform[2];
                for (int i = 0; i < starts.Length; i++)
                {
                    starts[i] = new GameObject("Spawn " + i).transform;
                    starts[i].SetParent(arena.transform);
                    starts[i].SetPositionAndRotation(new Vector3(i == 0 ? -8f : 8f, 1.3f, 0f), Quaternion.Euler(0f, i == 0 ? 90f : -90f, 0f));
                }
                Light light = new GameObject("Sandbox sunlight").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.7f);
                new GameObject("Network session").AddComponent<NetworkSandboxSession>().Configure(vehicle, projectile.gameObject, starts);
                if (!EditorSceneManager.SaveScene(sandbox, ScenePath))
                    throw new IOException("Не удалось сохранить " + ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[MM NET] Создан NetworkSandbox. Камера и HUD создаются только клиентом.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(sandbox, true);
            }
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = Generated + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Не найден URP/Lit. Дождитесь импорта зависимостей проекта.");
            Material material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static NetworkBallisticProjectile ProjectilePrefab(Material material)
        {
            string path = Generated + "/NetworkProjectile.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<NetworkBallisticProjectile>();
            GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                shot.name = "NetworkProjectile";
                shot.layer = 2;
                shot.transform.localScale = Vector3.one * NetworkBallisticProjectile.Radius * 2f;
                shot.GetComponent<Renderer>().sharedMaterial = material;
                Rigidbody body = shot.AddComponent<Rigidbody>();
                body.mass = 4f;
                body.useGravity = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                AddReplication(shot);
                shot.AddComponent<NetworkBallisticProjectile>().Configure(material);
                return PrefabUtility.SaveAsPrefabAsset(shot, path).GetComponent<NetworkBallisticProjectile>();
            }
            finally { UnityEngine.Object.DestroyImmediate(shot); }
        }

        private static GameObject VehiclePrefab(Material hullMaterial, Material rubber, NetworkBallisticProjectile projectile)
        {
            string path = Generated + "/NetworkVehicle.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            GameObject vehicle = new GameObject("NetworkVehicle");
            try
            {
                vehicle.AddComponent<Rigidbody>();
                vehicle.AddComponent<VehiclePhysics>().Configure();
                AddReplication(vehicle);
                Renderer hull = Primitive("Hull", PrimitiveType.Cube, vehicle.transform, Vector3.zero, new Vector3(1.8f, 0.75f, 3.3f), hullMaterial, true).GetComponent<Renderer>();
                Primitive("Cabin", PrimitiveType.Cube, vehicle.transform, new Vector3(0f, 0.5f, -0.35f), new Vector3(1.4f, 0.5f, 1.2f), rubber, false);
                Transform[] wheels = new Transform[4];
                for (int i = 0; i < wheels.Length; i++)
                {
                    Vector3 position = new Vector3(i % 2 == 0 ? -1.05f : 1.05f, -0.43f, i < 2 ? 1.25f : -1.25f);
                    wheels[i] = Primitive("Wheel " + i, PrimitiveType.Cylinder, vehicle.transform, position, new Vector3(0.86f, 0.18f, 0.86f), rubber, false).transform;
                    wheels[i].localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
                Transform turret = Child("Turret", vehicle.transform, new Vector3(0f, 0.7f, 0.2f));
                Primitive("Turret geometry", PrimitiveType.Cube, turret, Vector3.zero, new Vector3(0.6f, 0.3f, 0.6f), hullMaterial, false);
                Transform barrel = Child("Barrel", turret, Vector3.zero);
                Primitive("Barrel geometry", PrimitiveType.Cube, barrel, new Vector3(0f, 0f, 1f), new Vector3(0.18f, 0.18f, 2f), rubber, false);
                Transform muzzle = Child("Muzzle", barrel, new Vector3(0f, 0f, 2.3f));
                vehicle.AddComponent<NetworkVehicle>().Configure(turret, barrel, muzzle, wheels, hull, projectile);
                return PrefabUtility.SaveAsPrefabAsset(vehicle, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(vehicle); }
        }

        private static void AddReplication(GameObject target)
        {
            target.AddComponent<NetworkObject>();
            target.AddComponent<NetworkPhysicsState>();
        }

        [MenuItem("Tools/Mad Machines/Networking/Upgrade sandbox replication")]
        public static void UpgradeReplication()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Остановите Play Mode перед миграцией prefab.");
            foreach (string name in new[] { "NetworkVehicle", "NetworkProjectile" })
            {
                string path = Generated + "/" + name + ".prefab";
                if (!File.Exists(path)) continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    NetworkRigidbody oldBody = contents.GetComponent<NetworkRigidbody>();
                    NetworkTransform oldTransform = contents.GetComponent<NetworkTransform>();
                    if (oldBody == null && oldTransform == null && contents.GetComponent<NetworkPhysicsState>() != null) continue;
                    if (oldBody != null) UnityEngine.Object.DestroyImmediate(oldBody);
                    if (oldTransform != null) UnityEngine.Object.DestroyImmediate(oldTransform);
                    if (contents.GetComponent<NetworkPhysicsState>() == null) contents.AddComponent<NetworkPhysicsState>();
                    if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null)
                        throw new IOException("Не удалось обновить " + path);
                    Debug.Log("[MM NET] Обновлена репликация " + path + "; GUID и остальные ссылки сохранены.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            AssetDatabase.SaveAssets();
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
            {
                item.layer = 2;
                UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            }
            return item;
        }

        [MenuItem("Tools/Mad Machines/Networking/Build Windows client")]
        public static void BuildWindowsClient() => Build(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player,
            NetworkBuildOutput.WindowsClient, NetworkBuildOutput.WindowsClientExecutable);

        [MenuItem("Tools/Mad Machines/Networking/Build Development Server", false, 103)]
        public static void BuildWindowsServer() => BuildDevelopmentServer();

        internal static bool BuildDevelopmentServer() => Build(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Server,
            NetworkBuildOutput.WindowsServer, NetworkBuildOutput.WindowsServerExecutable);

        [MenuItem("Tools/Mad Machines/Networking/Build Linux dedicated server")]
        public static void BuildLinuxServer() => Build(BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server,
            NetworkBuildOutput.LinuxServer, NetworkBuildOutput.LinuxServerExecutable);

        private static bool Build(BuildTarget target, StandaloneBuildSubtarget subtarget, string outputTarget, string executable)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new BuildFailedException("Остановите Play Mode перед сборкой.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new BuildFailedException("Установите через Unity Hub модуль поддержки " + target + ", а для сервера — Dedicated Server Build Support.");
            Generate();
            ValidateSandbox();
            // Build into a new directory so a running player and the previous publication stay usable.
            string id = Guid.NewGuid().ToString("N");
            string output = NetworkBuildOutput.GetBuildPath(outputTarget, executable, id);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                subtarget = (int)subtarget,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(output))
                throw new BuildFailedException($"Сборка {target}/{subtarget}: {report.summary.result}. Проверьте Editor.log и установленные модули платформы.");
            NetworkBuildOutput.Publish(outputTarget, id);
            Debug.Log("[MM NET] Опубликована сборка: " + output);
            return true;
        }

        [MenuItem("Tools/Mad Machines/Networking/Validate sandbox assets")]
        public static void ValidateSandbox()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Проверяйте ресурсы вне Play Mode.");
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Сначала выполните Create or open sandbox.");
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NetworkSandboxSession session = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (NetworkSandboxSession candidate in root.GetComponentsInChildren<NetworkSandboxSession>(true))
                    {
                        if (session != null) throw new InvalidOperationException("NetworkSandbox содержит несколько сессий.");
                        session = candidate;
                    }
                    if (root.GetComponentInChildren<Camera>(true) != null || root.GetComponentInChildren<AudioListener>(true) != null ||
                        root.GetComponentInChildren<NetworkClientView>(true) != null || root.GetComponentInChildren<NetworkManager>(true) != null ||
                        root.GetComponentInChildren<NetworkObject>(true) != null)
                        throw new InvalidOperationException("NetworkSandbox должен создавать камеру, HUD, NetworkManager и сетевые объекты только при запуске.");
                }
                if (session == null || !session.isActiveAndEnabled) throw new InvalidOperationException("Нет активной Network session.");
                session.ValidateConfiguration();
                Debug.Log("[MM NET] Ссылки и настройки NetworkSandbox проверены. Сеть и физика этой командой не запускаются.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
