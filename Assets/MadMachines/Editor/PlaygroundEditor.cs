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
        public static void BuildWindows() => BuildPlayer(BuildTarget.StandaloneWindows64, "Builds/MadMachines/MadMachines.exe");

        [MenuItem("Tools/Mad Machines/Build Linux player")]
        public static void BuildLinux() => BuildPlayer(BuildTarget.StandaloneLinux64, "Builds/MadMachines-Linux/MadMachines.x86_64");

        private static void BuildPlayer(BuildTarget target, string output)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = GameFlow.PlayerScenes,
                locationPathName = output,
                target = target,
                subtarget = (int)StandaloneBuildSubtarget.Player,
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
