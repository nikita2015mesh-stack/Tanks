using System.IO;
using MadMachines;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Editor
{
    [InitializeOnLoad]
    public static class PlaygroundEditor
    {
        private const string ScenePath = "Assets/MadMachines/Scenes/Playground.unity";
        private const string QuarryScenePath = "Assets/MadMachines/Scenes/Quarry.unity";
        private const string VehiclePath = "Assets/MadMachines/Vehicles/T100.asset";
        private const string PendingKey = "MadMachines.Verify";
        private static double deadline;
        private static bool probeStarted;

        static PlaygroundEditor()
        {
            EditorApplication.update += Monitor;
        }

        [MenuItem("Tools/Mad Machines/Create or open playground")]
        public static void CreateOrOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Generate();
        }

        public static void Generate()
        {
            // Never overwrite an existing user-edited playground.
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            Directory.CreateDirectory("Assets/MadMachines/Scenes");
            Directory.CreateDirectory("Assets/MadMachines/Generated");
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WeaponDefinition machineGun = Weapon("Machinegun", "Пулемёт", 110, 0.08f, 60, 0, 0.065f, 22);
            machineGun.Gravity = false;
            machineGun.Spread = 1.2f;
            machineGun.Lifetime = 0.8f;
            machineGun.Mass = 0.06f;
            WeaponDefinition cannon = Weapon("Ballisticcannon", "Баллистическая пушка", 38, 1.15f, 2200, 5, 0.22f, 100);
            WeaponDefinition wrecker = Weapon("Wreckergrenade", "Тяжёлая граната", 24, 3.6f, 8500, 10, 0.65f, 180);
            wrecker.TimedBounce = true;
            wrecker.Lifetime = 3.2f;
            wrecker.Mass = 35;
            wrecker.Impulse = 24000;
            wrecker.Color = new Color(1, 0.22f, 0.08f);
            VehicleDefinition vehicle = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(VehiclePath);
            GameObject root = PlaygroundBuilder.Build(new[] { machineGun, cannon, wrecker }, vehicle);
            int materialIndex = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                Material material = renderer.sharedMaterial;
                if (material != null && !AssetDatabase.Contains(material))
                    AssetDatabase.CreateAsset(material, $"Assets/MadMachines/Generated/Material{materialIndex++}.mat");
            }
            int meshIndex = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh != null && !AssetDatabase.Contains(mesh))
                    AssetDatabase.CreateAsset(mesh, $"Assets/MadMachines/Generated/Ramp{meshIndex++}.asset");
            }
            EditorUtility.SetDirty(machineGun);
            EditorUtility.SetDirty(cannon);
            EditorUtility.SetDirty(wrecker);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            Debug.Log("MAD MACHINES: scene generated at " + ScenePath);
        }

        private static WeaponDefinition Weapon(string assetName, string label, float speed, float interval, float recoil, float radius, float size, float damage)
        {
            string path = $"Assets/MadMachines/Generated/{assetName}.asset";
            WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (definition != null) return definition;
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            definition.Label = label;
            definition.Speed = speed;
            definition.Interval = interval;
            definition.Recoil = recoil;
            definition.Radius = radius;
            definition.Size = size;
            definition.Damage = damage;
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        public static void BuildAndVerify()
        {
            Generate();
            SessionState.SetBool(PendingKey, true);
            deadline = EditorApplication.timeSinceStartup + 150;
            EditorApplication.isPlaying = true;
        }

        public static void OpenPreview()
        {
            Generate();
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Mad Machines/Build Windows player")]
        public static void BuildWindows()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Generate();
            BuildWindowsPlayer(ScenePath, "Builds/MadMachines/MadMachines.exe");
        }

        [MenuItem("Tools/Mad Machines/Quarry/Open offline")]
        public static void OpenQuarry()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(QuarryScenePath);
        }

        [MenuItem("Tools/Mad Machines/Quarry/Build Windows offline player")]
        public static void BuildQuarryWindows()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildWindowsPlayer(QuarryScenePath, "Builds/MadMachines/Quarry/MadMachines.exe");
        }

        private static void BuildWindowsPlayer(string scenePath, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("MAD MACHINES player build failed: " + report.summary.result);
        }

        private static void Monitor()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 150;
            if (EditorApplication.isPlaying && !probeStarted)
            {
                probeStarted = true;
                new GameObject("Automated gameplay verification").AddComponent<PlaygroundVerification>();
            }
            if (PlaygroundVerification.Done || EditorApplication.timeSinceStartup > deadline)
            {
                bool success = PlaygroundVerification.Done && PlaygroundVerification.Passed;
                SessionState.SetBool(PendingKey, false);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/MadMachines-verification.txt", (success ? "PASS\n" : "FAIL or timeout\n") + PlaygroundVerification.Report);
                Debug.Log("MAD MACHINES VERIFY: " + (success ? "PASS" : "FAIL"));
                EditorApplication.Exit(success ? 0 : 1);
            }
        }
    }
}
