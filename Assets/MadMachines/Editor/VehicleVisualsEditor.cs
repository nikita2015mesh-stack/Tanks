using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MadMachines.Editor
{
    public static class VehicleVisualsEditor
    {
        private const string UndoLabel = "Отделить внешний вид машины";

        [MenuItem("Tools/Mad Machines/Separate selected vehicle visuals")]
        public static void SeparateSelectedVehicle()
        {
            VehiclePhysics vehicle = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VehiclePhysics>() : null;
            if (Application.isPlaying || vehicle == null || !vehicle.gameObject.scene.IsValid()
                || PrefabUtility.IsPartOfPrefabInstance(vehicle) || EditorUtility.IsPersistent(vehicle))
            {
                Debug.LogWarning("Выберите корень машины в сцене вне Play Mode (не экземпляр Prefab).");
                return;
            }
            if (vehicle.GetComponentInChildren<VehicleVisuals>(true) != null
                || vehicle.transform.Find("Vehicle visuals") != null)
            {
                Debug.LogWarning("Внешний вид этой машины уже отделён.");
                return;
            }

            string[] bodyNames = { "Armoured tub", "Orange bonnet", "Driver cage" };
            Transform[] parts = new Transform[3];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = vehicle.transform.Find(bodyNames[i]);
                if (parts[i] == null || parts[i].GetComponent<MeshFilter>() == null
                    || parts[i].GetComponent<MeshRenderer>() == null)
                {
                    Debug.LogWarning("Не найден исходный кузов машины. Сцена не изменена.");
                    return;
                }
            }
            SerializedObject serializedVehicle = new SerializedObject(vehicle);
            SerializedProperty legacyWheels = serializedVehicle.FindProperty("wheels");
            Transform[] wheels = new Transform[4];
            if (legacyWheels.arraySize != 4) return;
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i] = legacyWheels.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (wheels[i] == null || wheels[i].parent != vehicle.transform)
                {
                    Debug.LogWarning("Ожидались четыре исходных колеса непосредственно под корнем машины. Сцена не изменена.");
                    return;
                }
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            try
            {
                Transform root = CreateChild("Vehicle visuals", vehicle.transform);
                Transform body = CreateChild("Body visuals", root);
                foreach (Transform part in parts)
                {
                    Transform geometry = CreateChild(part.name + " geometry", body);
                    geometry.gameObject.layer = part.gameObject.layer;
                    geometry.localPosition = part.localPosition;
                    geometry.localRotation = part.localRotation;
                    geometry.localScale = part.localScale;
                    geometry.gameObject.SetActive(part.gameObject.activeSelf);
                    Undo.AddComponent<MeshFilter>(geometry.gameObject).sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
                    MeshRenderer source = part.GetComponent<MeshRenderer>();
                    MeshRenderer destination = Undo.AddComponent<MeshRenderer>(geometry.gameObject);
                    EditorUtility.CopySerialized(source, destination);
                    Undo.RecordObject(source, UndoLabel);
                    source.enabled = false;
                }
                Transform[] anchors = new Transform[4];
                for (int i = 0; i < wheels.Length; i++)
                {
                    anchors[i] = CreateChild("Wheel anchor " + i, root);
                    anchors[i].position = wheels[i].position;
                    anchors[i].rotation = wheels[i].rotation;
                    Undo.SetTransformParent(wheels[i], anchors[i], UndoLabel);
                }
                VehicleVisuals visuals = Undo.AddComponent<VehicleVisuals>(root.gameObject);
                Undo.RecordObject(visuals, UndoLabel);
                visuals.Configure(vehicle, body, anchors);
                EditorUtility.SetDirty(visuals);
                EditorSceneManager.MarkSceneDirty(vehicle.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                Debug.Log("Внешний вид отделён. Проверьте машину и сохраните сцену; изменение можно отменить через Undo.");
            }
            catch (Exception)
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static Transform CreateChild(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, UndoLabel);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
