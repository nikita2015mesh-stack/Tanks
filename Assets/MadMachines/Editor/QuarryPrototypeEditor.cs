using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MadMachines.Editor
{
    internal static class QuarryPrototypeEditor
    {
        internal const string Menu = "Tools/Tanks/Maps/";
        internal const string SceneFolder = "Assets/MadMachines/Scenes";
        internal const string SceneName = "Quarry_01_Prototype";
        internal const string GeneratedRoot = "Quarry_01_Generated";
        private const string TerrainFolder = "Assets/MadMachines/Generated/Quarry_01";

        [MenuItem(Menu + "Generate Quarry_01")]
        private static void Generate()
        {
            if (!CanGenerate()) return;
            try
            {
                string sceneName = SceneName;
                string scenePath = ScenePath(sceneName);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
                {
                    int choice = EditorUtility.DisplayDialogComplex("Quarry_01",
                        "Сцена уже существует. Можно открыть её или создать новую версию с отдельным TerrainData.",
                        "Открыть", "Отмена", "Создать новую версию");
                    if (choice == 1) return;
                    if (choice == 0)
                    {
                        if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != scenePath)
                        {
                            RequireCleanScenes();
                            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                        }
                        return;
                    }

                    for (int version = 2; ; version++)
                    {
                        sceneName = $"{SceneName}_v{version}";
                        if (!PathOccupied(ScenePath(sceneName)) && !PathOccupied(TerrainPath(sceneName)))
                            break;
                    }
                    scenePath = ScenePath(sceneName);
                }

                RequireCleanScenes();
                string terrainPath = TerrainPath(sceneName);
                if (PathOccupied(scenePath) || PathOccupied(terrainPath))
                    throw new IOException($"Путь сцены или TerrainData уже занят. Проверьте {scenePath} и {terrainPath}.");

                QuarryPrototypeSource source = QuarryPrototypeSource.Read();
                if (!AssetDatabase.IsValidFolder(TerrainFolder))
                {
                    string folderGuid = AssetDatabase.CreateFolder("Assets/MadMachines/Generated", "Quarry_01");
                    if (AssetDatabase.GUIDToAssetPath(folderGuid) != TerrainFolder)
                        throw new IOException("Не удалось создать каталог " + TerrainFolder);
                }

                CreateScene(source, scenePath, terrainPath);
                SceneView.lastActiveSceneView?.LookAtDirect(source.Size * 0.5f, Quaternion.Euler(55f, 0f, 0f), 1300f);
                Debug.Log("Создана тестовая сцена Quarry_01: " + scenePath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Quarry_01", exception.Message, "OK");
            }
        }

        [MenuItem(Menu + "Generate Quarry_01", true)]
        private static bool CanGenerate()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && PrefabStageUtility.GetCurrentPrefabStage() == null;
        }

        private static void RequireCleanScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                    throw new InvalidOperationException("Сохраните открытые сцены перед переключением на Quarry_01.");
            }
        }

        internal static string ScenePath(string name) => $"{SceneFolder}/{name}.unity";

        internal static string TerrainPath(string name) => $"{TerrainFolder}/{name}.asset";

        internal static bool PathOccupied(string assetPath)
        {
            string path = QuarryPrototypeSource.ProjectPath(assetPath);
            return File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path);
        }

        private static void CreateScene(QuarryPrototypeSource source, string scenePath, string terrainPath)
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            Scene previousScene = SceneManager.GetActiveScene();
            bool hadSavedScene = !string.IsNullOrEmpty(previousScene.path);
            NewSceneSetup untitledSetup = previousScene.rootCount == 0 ? NewSceneSetup.EmptyScene : NewSceneSetup.DefaultGameObjects;
            TerrainData data = null;
            Scene scene = default;
            bool writingTerrain = false;
            bool writingScene = false;
            try
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!scene.IsValid() || !scene.isLoaded || SceneManager.GetActiveScene() != scene)
                    throw new InvalidOperationException("Unity не открыла новую сцену Quarry_01.");

                // Opening a single scene unloads unused objects, including unsaved TerrainData.
                data = new TerrainData();
                data.heightmapResolution = source.Heights.GetLength(0);
                data.size = source.Size;
                data.SetHeights(0, 0, source.Heights);

                var root = new GameObject(GeneratedRoot).transform;
                GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.name = "Terrain";
                terrainObject.transform.SetParent(root, false);
                terrainObject.GetComponent<Terrain>().materialTemplate = GraphicsSettings.currentRenderPipeline?.defaultTerrainMaterial;
                CreateMarkers(root, data, source);
                CreateView(root, source.Size);

                writingTerrain = true;
                AssetDatabase.CreateAsset(data, terrainPath);
                if (AssetDatabase.GetAssetPath(data) != terrainPath)
                    throw new IOException("Не удалось сохранить TerrainData в " + terrainPath);
                AssetDatabase.SaveAssetIfDirty(data);

                writingScene = true;
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new IOException("Не удалось сохранить сцену " + scenePath);
            }
            catch
            {
                try
                {
                    if (scene.IsValid())
                    {
                        if (previousSetup.Length > 0 && hadSavedScene)
                            EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                        else
                            EditorSceneManager.NewScene(untitledSetup, NewSceneMode.Single);
                    }
                }
                finally
                {
                    if (writingScene) RemovePartialOutput(scenePath);
                    if (writingTerrain) RemovePartialOutput(terrainPath);
                    if (data != null && !EditorUtility.IsPersistent(data))
                        UnityEngine.Object.DestroyImmediate(data);
                }
                throw;
            }
        }

        private static void RemovePartialOutput(string assetPath)
        {
            if (PathOccupied(assetPath) && !AssetDatabase.DeleteAsset(assetPath))
                Debug.LogError("Проверьте частично сохранённый файл " + assetPath);
        }

        private static void CreateMarkers(Transform root, TerrainData data, QuarryPrototypeSource source)
        {
            var markers = new GameObject("Markers").transform;
            markers.SetParent(root, false);
            foreach (var item in source.Markers)
            {
                Vector2 xz = item.Value.Position;
                float height = data.GetInterpolatedHeight(xz.x / source.Size.x, xz.y / source.Size.z);
                var marker = new GameObject(item.Key).transform;
                marker.SetParent(markers, false);
                marker.localPosition = new Vector3(xz.x, height, xz.y);
                if (item.Value.Type != "industrial") continue;

                GameObject landmark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                landmark.name = "Placeholder";
                landmark.transform.SetParent(marker, false);
                landmark.transform.localPosition = Vector3.up * 5f;
                landmark.transform.localScale = new Vector3(12f, 10f, 12f);
                UnityEngine.Object.DestroyImmediate(landmark.GetComponent<Collider>());
            }
        }

        private static void CreateView(Transform root, Vector3 size)
        {
            Light sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(root, false);
            sun.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.49f, 0.52f);

            Camera overview = new GameObject("Overview Camera").AddComponent<Camera>();
            overview.transform.SetParent(root, false);
            Vector3 center = size * 0.5f;
            overview.transform.position = center + new Vector3(400f, 1550f, -1450f);
            overview.transform.LookAt(center);
            overview.farClipPlane = 5000f;
            overview.clearFlags = CameraClearFlags.SolidColor;
            overview.backgroundColor = new Color(0.26f, 0.30f, 0.35f);
        }
    }
}
