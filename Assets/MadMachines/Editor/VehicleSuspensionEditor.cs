using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MadMachines.Editor
{
    public static class VehicleSuspensionEditor
    {
        private const string UndoLabel = "Добавить рычажную подвеску";

        [MenuItem("Tools/Mad Machines/Add wishbone suspension to selected vehicle")]
        public static void AddToSelectedVehicle()
        {
            VehiclePhysics vehicle = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<VehiclePhysics>() : null;
            if (Application.isPlaying || vehicle == null || !vehicle.gameObject.scene.IsValid()
                || PrefabUtility.IsPartOfPrefabInstance(vehicle) || EditorUtility.IsPersistent(vehicle))
            {
                Debug.LogWarning("Выберите корень машины в сцене вне Play Mode (не экземпляр Prefab).");
                return;
            }
            VehicleVisuals visuals = vehicle.GetComponentInChildren<VehicleVisuals>(true);
            if (visuals == null || visuals.Vehicle != vehicle || visuals.WheelAnchorCount != vehicle.WheelCount)
            {
                Debug.LogWarning("Сначала отделите внешний вид машины. Число точек колёс должно совпадать с числом опор в VehiclePhysics.");
                return;
            }
            if (vehicle.GetComponentInChildren<WheelSuspensionVisual>(true) != null)
            {
                Debug.LogWarning("У машины уже есть узлы подвески. Настройте существующие компоненты; повторное добавление отменено.");
                return;
            }
            if ((vehicle.transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f
                || (visuals.transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
            {
                Debug.LogWarning("Для рычажной подвески нужен единичный масштаб шасси и корня внешнего вида. Масштабируйте только дочернюю геометрию.");
                return;
            }

            HashSet<Transform> uniqueAnchors = new HashSet<Transform>();
            Transform[][] geometry = new Transform[vehicle.WheelCount][];
            for (int i = 0; i < vehicle.WheelCount; i++)
            {
                Transform anchor = visuals.GetWheelAnchor(i);
                if (anchor == null || !uniqueAnchors.Add(anchor) || anchor.parent != visuals.transform
                    || (anchor.localScale - Vector3.one).sqrMagnitude > 0.0001f
                    || PrefabUtility.IsPartOfPrefabInstance(anchor))
                {
                    Debug.LogWarning("Нужны отдельные точки колёс с единичным масштабом непосредственно под VehicleVisuals. Сцена не изменена.");
                    return;
                }
                if (anchor.GetComponentInChildren<Collider>(true) != null
                    || anchor.GetComponentInChildren<Rigidbody>(true) != null)
                {
                    Debug.LogWarning("Геометрия колёс должна быть без Collider и Rigidbody. Сцена не изменена.");
                    return;
                }
                geometry[i] = new Transform[anchor.childCount];
                for (int j = 0; j < anchor.childCount; j++)
                {
                    Transform child = anchor.GetChild(j);
                    if (PrefabUtility.IsPartOfPrefabInstance(child) && !PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                    {
                        Debug.LogWarning("Нельзя переносить внутренний объект Prefab. Под точкой колеса должен находиться целый визуальный Prefab или обычная геометрия.");
                        return;
                    }
                    geometry[i][j] = child;
                }
            }
            Renderer source = vehicle.transform.Find("Armoured tub")?.GetComponent<Renderer>();
            if (source == null)
            {
                source = vehicle.GetComponentInChildren<Renderer>(true);
            }
            if (source == null || source.sharedMaterial == null)
            {
                Debug.LogWarning("Не найден материал для временной геометрии подвески. Сцена не изменена.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            try
            {
                // Creation records flush pending RecordObject calls; snapshot existing objects immediately.
                Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { vehicle, visuals }, UndoLabel);
                vehicle.InitializeWheels();
                WheelSuspensionVisual[] rigs = new WheelSuspensionVisual[vehicle.WheelCount];
                for (int i = 0; i < vehicle.WheelCount; i++)
                {
                    Transform anchor = visuals.GetWheelAnchor(i);
                    Undo.RegisterCompleteObjectUndo(anchor, UndoLabel);
                    vehicle.GetWheelDefinition(i).EnableWishbone();
                    rigs[i] = VehicleSuspensionBuilder.CreateRig(visuals.transform, anchor, i, source.sharedMaterial);
                    Undo.RegisterCreatedObjectUndo(rigs[i].gameObject, UndoLabel);
                    Undo.RegisterCreatedObjectUndo(rigs[i].WheelSpin.gameObject, UndoLabel);
                    foreach (Transform child in geometry[i])
                    {
                        Undo.SetTransformParent(child, rigs[i].WheelSpin, UndoLabel);
                    }
                }
                visuals.SetSuspensions(rigs);
                visuals.ApplyPose();
                EditorUtility.SetDirty(vehicle);
                EditorUtility.SetDirty(visuals);
                EditorSceneManager.MarkSceneDirty(vehicle.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                Debug.Log("Рычажная подвеска добавлена. Проверьте ось вращения и центр моделей колёс, затем сохраните сцену. Доступен Undo.");
            }
            catch (Exception)
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }
    }
}
