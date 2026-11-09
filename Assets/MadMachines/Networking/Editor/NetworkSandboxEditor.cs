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
        private const string VehicleDefinitionPath = "Assets/MadMachines/Vehicles/T100.asset";
        private const string CannonDefinitionPath = "Assets/MadMachines/Generated/Ballisticcannon.asset";
        public const string ScenePath = Root + "/Scenes/NetworkSandbox.unity";
        private const string QuarryScenePath = "Assets/MadMachines/Scenes/QuarryOnline.unity";

        [MenuItem("Tools/Mad Machines/Networking/Create or open sandbox")]
        public static void OpenSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Generate();
            EditorSceneManager.OpenScene(ScenePath);
        }

        // This runs only when the user explicitly selects the menu or invokes a build method.
        // Keep saved scenes open. Unity cannot create a scene additively beside an untitled scene.
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Остановите Play Mode перед созданием сцены.");
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();
            Scene previousScene = SceneManager.GetActiveScene();
            bool replaceUntitled = ShouldReplaceUntitledScene();
            NewSceneSetup previousSetup = replaceUntitled && previousScene.rootCount == 0
                ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            Scene sandbox = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceUntitled ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(sandbox);
                Material ground = MaterialAsset("Ground", new Color(0.22f, 0.28f, 0.3f));
                Material concrete = MaterialAsset("Concrete", new Color(0.48f, 0.5f, 0.55f));
                Material shot = MaterialAsset("Projectile", new Color(1f, 0.8f, 0.12f));
                Material orange = MaterialAsset("SpawnOrange", new Color(0.8f, 0.24f, 0.05f));
                Material blue = MaterialAsset("SpawnBlue", new Color(0.05f, 0.3f, 0.8f));
                VehicleDefinition definition = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(VehicleDefinitionPath);
                WeaponDefinition cannon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(CannonDefinitionPath);
                if (definition == null || definition.Prefab == null || cannon == null)
                    throw new InvalidOperationException("Для NetworkSandbox нужны описание T100 с префабом и штатная баллистическая пушка.");
                NetworkBallisticProjectile projectile = ProjectilePrefab(shot, cannon);
                GameObject vehicle = VehiclePrefab(definition, projectile);

                GameObject arena = new GameObject("Network arena");
                Primitive("Ground", PrimitiveType.Cube, arena.transform, new Vector3(0f, -0.5f, 0f), new Vector3(120f, 1f, 120f), ground, true);
                Primitive("North wall", PrimitiveType.Cube, arena.transform, new Vector3(0f, 2f, 60f), new Vector3(120f, 4f, 1f), concrete, true);
                Primitive("South wall", PrimitiveType.Cube, arena.transform, new Vector3(0f, 2f, -60f), new Vector3(120f, 4f, 1f), concrete, true);
                Primitive("East wall", PrimitiveType.Cube, arena.transform, new Vector3(60f, 2f, 0f), new Vector3(1f, 4f, 120f), concrete, true);
                Primitive("West wall", PrimitiveType.Cube, arena.transform, new Vector3(-60f, 2f, 0f), new Vector3(1f, 4f, 120f), concrete, true);
                Primitive("Obstacle 1", PrimitiveType.Cube, arena.transform, new Vector3(-12f, 1f, 14f), new Vector3(5f, 2f, 3f), concrete, true);
                Primitive("Obstacle 2", PrimitiveType.Cube, arena.transform, new Vector3(12f, 1.5f, -14f), new Vector3(3f, 3f, 7f), concrete, true);
                GameObject ramp = Primitive("Ramp", PrimitiveType.Cube, arena.transform, new Vector3(15f, 0.35f, 18f), new Vector3(7f, 1f, 8f), concrete, true);
                ramp.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                Primitive("Spawn pad 0", PrimitiveType.Cube, arena.transform, new Vector3(-8f, 0.01f, 0f), new Vector3(5f, 0.02f, 5f), orange, false);
                Primitive("Spawn pad 1", PrimitiveType.Cube, arena.transform, new Vector3(8f, 0.01f, 0f), new Vector3(5f, 0.02f, 5f), blue, false);

                Transform[] starts = new Transform[2];
                for (int i = 0; i < starts.Length; i++)
                {
                    starts[i] = new GameObject("Spawn " + i).transform;
                    starts[i].SetParent(arena.transform);
                    starts[i].SetPositionAndRotation(new Vector3(i == 0 ? -8f : 8f, 1.5f, 0f), Quaternion.Euler(0f, i == 0 ? 90f : -90f, 0f));
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
                if (!replaceUntitled)
                {
                    if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(sandbox, true);
                }
                else if (string.IsNullOrEmpty(sandbox.path))
                {
                    EditorSceneManager.NewScene(previousSetup, NewSceneMode.Single);
                }
            }
        }

        private static bool ShouldReplaceUntitledScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(scene.path)) continue;
                if (SceneManager.sceneCount == 1 && !scene.isDirty && EditorSceneManager.GetSceneManagerSetup().Length <= 1)
                    return true;

                throw new InvalidOperationException("Сохраните сцену без имени или закройте её, затем повторите команду Networking.");
            }
            return false;
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

        private static NetworkBallisticProjectile ProjectilePrefab(Material material, WeaponDefinition definition)
        {
            string path = Generated + "/NetworkProjectile.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<NetworkBallisticProjectile>();
            GameObject shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                shot.name = "NetworkProjectile";
                shot.layer = 2;
                shot.transform.localScale = Vector3.one * definition.Size * 2f;
                shot.GetComponent<Renderer>().sharedMaterial = material;
                Rigidbody body = shot.AddComponent<Rigidbody>();
                body.mass = definition.Mass;
                body.useGravity = definition.Gravity;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                AddReplication(shot);
                shot.AddComponent<NetworkBallisticProjectile>().Configure(definition);
                return PrefabUtility.SaveAsPrefabAsset(shot, path).GetComponent<NetworkBallisticProjectile>();
            }
            finally { UnityEngine.Object.DestroyImmediate(shot); }
        }

        private static GameObject VehiclePrefab(VehicleDefinition definition, NetworkBallisticProjectile projectile)
        {
            string path = Generated + "/NetworkVehicle.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            GameObject vehicle = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab.gameObject);
            try
            {
                vehicle.name = "NetworkVehicle";
                AddReplication(vehicle);
                vehicle.AddComponent<NetworkVehicle>().Configure(definition, projectile);
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

        [MenuItem("Tools/Mad Machines/Quarry/Open online")]
        public static void OpenQuarryOnline()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(QuarryScenePath);
        }

        [MenuItem("Tools/Mad Machines/Quarry/Build Development Server")]
        public static void BuildQuarryServer() => Build(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Server,
            NetworkBuildOutput.WindowsServer, NetworkBuildOutput.WindowsServerExecutable, QuarryScenePath);

        [MenuItem("Tools/Mad Machines/Quarry/Build Windows client")]
        public static void BuildQuarryClient() => Build(BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player,
            NetworkBuildOutput.WindowsClient, NetworkBuildOutput.WindowsClientExecutable, QuarryScenePath);

        [MenuItem("Tools/Mad Machines/Quarry/Build Linux dedicated server")]
        public static void BuildQuarryLinuxServer() => Build(BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server,
            NetworkBuildOutput.LinuxServer, NetworkBuildOutput.LinuxServerExecutable, QuarryScenePath);

        private static bool Build(BuildTarget target, StandaloneBuildSubtarget subtarget, string outputTarget, string executable,
            string scenePath = ScenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new BuildFailedException("Остановите Play Mode перед сборкой.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new BuildFailedException("Установите через Unity Hub модуль поддержки " + target + ", а для сервера — Dedicated Server Build Support.");
            if (scenePath == ScenePath) Generate();
            ValidateScene(scenePath);
            // Build into a new directory so a running player and the previous publication stay usable.
            string id = Guid.NewGuid().ToString("N");
            string output = NetworkBuildOutput.GetBuildPath(outputTarget, executable, id);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
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
        public static void ValidateSandbox() => ValidateScene(ScenePath);

        [MenuItem("Tools/Mad Machines/Quarry/Validate online assets")]
        public static void ValidateQuarryOnline() => ValidateScene(QuarryScenePath);

        private static void ValidateScene(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Проверяйте ресурсы вне Play Mode.");
            if (!File.Exists(path)) throw new FileNotFoundException("Не найдена сцена для сетевой игры.", path);
            string sceneName = Path.GetFileNameWithoutExtension(path);
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool closeAfterValidation = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                bool replaceUntitled = ShouldReplaceUntitledScene();
                scene = EditorSceneManager.OpenScene(path, replaceUntitled ? OpenSceneMode.Single : OpenSceneMode.Additive);
                closeAfterValidation = !replaceUntitled;
            }
            try
            {
                NetworkSandboxSession session = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (NetworkSandboxSession candidate in root.GetComponentsInChildren<NetworkSandboxSession>(true))
                    {
                        if (session != null) throw new InvalidOperationException(sceneName + " содержит несколько сессий.");
                        session = candidate;
                    }
                    if (root.GetComponentInChildren<Camera>(true) != null || root.GetComponentInChildren<AudioListener>(true) != null ||
                        root.GetComponentInChildren<NetworkClientView>(true) != null || root.GetComponentInChildren<NetworkManager>(true) != null ||
                        root.GetComponentInChildren<NetworkObject>(true) != null)
                        throw new InvalidOperationException(sceneName + " должна создавать камеру, HUD, NetworkManager и сетевые объекты только при запуске.");
                }
                if (session == null || !session.isActiveAndEnabled) throw new InvalidOperationException("Нет активной Network session.");
                session.ValidateConfiguration();
                Debug.Log($"[MM NET] Ссылки и настройки {sceneName} проверены. Сеть и физика этой командой не запускаются.");
            }
            finally
            {
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (closeAfterValidation) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
