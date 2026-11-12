using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MadMachines.Editor
{
    [InitializeOnLoad]
    public static class PlaygroundEditor
    {
        private const string VerificationScenePath = "Assets/MadMachines/Editor/GameplayVerification.unity";
        private const string PendingKey = "MadMachines.Verify";
        private static double deadline;
        private static bool probeStarted;

        static PlaygroundEditor()
        {
            EditorApplication.update += Monitor;
        }

        public static void BuildAndVerify()
        {
            EditorSceneManager.OpenScene(VerificationScenePath);
            SessionState.SetBool(PendingKey, true);
            deadline = EditorApplication.timeSinceStartup + 150;
            EditorApplication.isPlaying = true;
        }

        public static void OpenPreview()
        {
            EditorSceneManager.OpenScene(VerificationScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Mad Machines/Build Windows player")]
        public static void BuildWindows()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string output = "Builds/MadMachines/MadMachines.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GameFlow.PlayerScenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("MAD MACHINES player build failed: " + report.summary.result);
        }

        public static string[] PlayerScenesStartingWith(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<string> { scenePath };
            foreach (string path in GameFlow.PlayerScenes)
                if (path != scenePath) scenes.Add(path);
            return scenes.ToArray();
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
