using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Editor
{
    internal static class QuarryPrototypeChecks
    {
        [MenuItem(QuarryPrototypeEditor.Menu + "Check Quarry_01")]
        private static void Check()
        {
            ExpectInvalidData(() => QuarryPrototypeSource.ParseDefinition("{"), "Некорректный JSON");
            ExpectInvalidData(() => QuarryPrototypeSource.ParseDefinition("{\"map_id\":\"Quarry_01\"}"), "Неполный JSON");
            ExpectInvalidData(() => QuarryPrototypeSource.ReadHeights(Stream.Null), "RAW неверного размера");

            QuarryPrototypeSource source = QuarryPrototypeSource.Read();
            Require(source.Heights.GetLength(0) == 1025 && source.Heights.GetLength(1) == 1025, "Размер массива высот");
            Require(Mathf.Abs(source.Heights[256, 256] - 51235f / 65535f) < 0.00001f &&
                Mathf.Abs(source.Heights[768, 256] - 56175f / 65535f) < 0.00001f, "Нормализация R16 и направление +Z");
            Require(Vector2.Distance(source.Markers["spawn_a"].Position, new Vector2(1192.30f, 1094.77f)) < 0.1f &&
                Vector2.Distance(source.Markers["spawn_b"].Position, new Vector2(285.80f, 399.64f)) < 0.1f, "Координаты стартов из плана");

            string sceneName = QuarryPrototypeEditor.SceneName;
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path == QuarryPrototypeEditor.ScenePath(activeScene.name) &&
                activeScene.name.StartsWith(sceneName + "_v", StringComparison.Ordinal))
                sceneName = activeScene.name;

            string scenePath = QuarryPrototypeEditor.ScenePath(sceneName);
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null, "Сначала создайте сцену через Generate Quarry_01.");
            Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                CheckScene(scene, source, sceneName);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("Quarry_01: исходные данные и сохранённая сцена прошли проверки. " + scenePath);
        }

        [MenuItem(QuarryPrototypeEditor.Menu + "Check Quarry_01", true)]
        private static bool CanCheck()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && PrefabStageUtility.GetCurrentPrefabStage() == null;
        }

        private static void CheckScene(Scene scene, QuarryPrototypeSource source, string sceneName)
        {
            GameObject root = scene.GetRootGameObjects().Single(item => item.name == QuarryPrototypeEditor.GeneratedRoot);
            Terrain terrain = root.GetComponentsInChildren<Terrain>(true).Single();
            TerrainData data = terrain.terrainData;
            Require(data != null && AssetDatabase.GetAssetPath(data) == QuarryPrototypeEditor.TerrainPath(sceneName), "Ссылка на TerrainData");
            Require(terrain.TryGetComponent(out TerrainCollider collider) && collider.enabled &&
                collider.terrainData == data, "TerrainCollider использует тот же TerrainData");
            Require(data.heightmapResolution == 1025 && data.size == new Vector3(1500f, 210f, 1500f) &&
                terrain.transform.position == Vector3.zero, "Масштаб и начало Terrain");
            Require(data.GetHeight(512, 512) < 0.05f && Mathf.Abs(data.GetHeight(0, 0) - 180f) < 0.1f &&
                Mathf.Abs(data.GetHeight(256, 256) - 164.18f) < 0.1f &&
                Mathf.Abs(data.GetHeight(256, 768) - 180.01f) < 0.1f, "Высоты дна, края и двух уступов");

            foreach (var item in source.Markers)
            {
                Transform marker = root.transform.Find("Markers/" + item.Key);
                Vector2 xz = item.Value.Position;
                float height = data.GetInterpolatedHeight(xz.x / data.size.x, xz.y / data.size.z);
                Require(marker != null && Vector3.Distance(marker.position, new Vector3(xz.x, height, xz.y)) < 0.1f,
                    "Положение маркера " + item.Key);
            }
        }

        private static void ExpectInvalidData(Action action, string message)
        {
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidOperationException("Quarry_01: проверка должна отклонить данные. " + message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Quarry_01: " + message);
        }
    }
}
