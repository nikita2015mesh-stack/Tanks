using System;
using System.IO;
using Unity.Netcode;
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
        private const string Menu = "Tools/Mad Machines/Networking/";
        internal const string ChecksMenu = Menu + "Проверки/";

        [MenuItem(Menu + "Собрать сервер", false, 100)]
        public static void BuildLinuxMatchServer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Остановите Play Mode перед сборкой.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
                throw new BuildFailedException("Установите через Unity Hub модуль Linux Dedicated Server Build Support.");

            ValidateScene(GameFlow.ThreeRoadsScenePath);
            // Publish only a successful build. The previous build can still be running.
            string id = Guid.NewGuid().ToString("N");
            string output = NetworkBuildOutput.GetBuildPath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameFlow.FrontEndScenePath, GameFlow.ThreeRoadsScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(output))
                throw new BuildFailedException($"Сборка сервера: {report.summary.result}. Проверьте Editor.log и модули Linux.");
            NetworkBuildOutput.Publish(id);
            Debug.Log("[MM NET] Опубликована сборка: " + output);
        }

        [MenuItem(ChecksMenu + "Проверить ссылки открытой сцены")]
        public static void ValidateActiveScene()
        {
            string path = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Откройте сохранённую сетевую сцену.");
            ValidateScene(path);
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
                    bool hasCameraOrListener = root.GetComponentInChildren<Camera>(true) != null ||
                        root.GetComponentInChildren<AudioListener>(true) != null;
                    if (hasCameraOrListener ||
                        root.GetComponentInChildren<NetworkClientView>(true) != null || root.GetComponentInChildren<NetworkManager>(true) != null ||
                        root.GetComponentInChildren<NetworkObject>(true) != null)
                        throw new InvalidOperationException(sceneName + " должна создавать камеру, HUD, NetworkManager и сетевые объекты только при запуске.");
                }
                if (session == null || !session.isActiveAndEnabled)
                    throw new InvalidOperationException("Нет активной Network session.");
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
