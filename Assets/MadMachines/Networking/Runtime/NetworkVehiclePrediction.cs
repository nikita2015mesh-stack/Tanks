using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Networking
{
    internal sealed class NetworkVehiclePrediction : IDisposable
    {
        private const int HistoryCapacity = 24;
        private const float SnapDistance = 0.6f;
        private const float SnapAngle = 15f;
        private const float VisualOffsetLimit = 0.18f;
        private const float VisualAngleLimit = 6f;
        private const float OffsetDecay = 20f;

        private readonly Scene scene;
        private readonly PhysicsScene physicsScene;
        private readonly VehiclePhysics vehicle;
        private readonly Rigidbody body;
        private readonly List<NetworkVehicleCommand> pending = new List<NetworkVehicleCommand>(HistoryCapacity);
        private NetworkPhysicsPose previous;
        private NetworkPhysicsPose current;
        private Vector3 positionOffset;
        private Quaternion rotationOffset = Quaternion.identity;
        private ulong lastServerStep;
        private uint lastRecoveryCount;
        private uint discardedSequence;
        private bool historyTruncated;
        private bool initialized;
        private bool ready;
        private int replays;
        private int replayedSteps;
        private int snaps;
        private float maxError;
        private float maxAngle;

        public NetworkVehiclePrediction(Scene map, NetworkVehicle source)
        {
            scene = SceneManager.CreateScene("Mad Machines Prediction " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physicsScene = scene.GetPhysicsScene();
            try
            {
                CopyMap(map);
                GameObject chassis = NewObject("Prediction chassis", source.gameObject.layer);
                chassis.transform.SetPositionAndRotation(source.Body.position, source.Body.rotation);
                chassis.transform.localScale = source.transform.lossyScale;
                Dictionary<Transform, Transform> hull = new Dictionary<Transform, Transform> { [source.transform] = chassis.transform };
                foreach (Collider collider in source.GetComponentsInChildren<Collider>())
                {
                    if (collider.enabled && !collider.isTrigger && collider.attachedRigidbody == source.Body)
                        CopyCollider(collider, CopyTransform(collider.transform, hull).gameObject);
                }
                Rigidbody original = source.Body;
                body = chassis.AddComponent<Rigidbody>();
                body.mass = original.mass;
                body.linearDamping = original.linearDamping;
                body.angularDamping = original.angularDamping;
                body.useGravity = original.useGravity;
                body.constraints = original.constraints;
                body.includeLayers = original.includeLayers;
                body.excludeLayers = original.excludeLayers;
                body.centerOfMass = original.centerOfMass;
                if (!original.automaticInertiaTensor)
                {
                    body.inertiaTensor = original.inertiaTensor;
                    body.inertiaTensorRotation = original.inertiaTensorRotation;
                }
                body.maxAngularVelocity = original.maxAngularVelocity;
                body.maxLinearVelocity = original.maxLinearVelocity;
                body.solverIterations = original.solverIterations;
                body.solverVelocityIterations = original.solverVelocityIterations;
                body.sleepThreshold = original.sleepThreshold;
                body.collisionDetectionMode = source.GetComponent<NetworkPhysicsState>().ServerCollisionMode;
                vehicle = chassis.AddComponent<VehiclePhysics>();
                vehicle.Authority = false;
                vehicle.enabled = false;
                vehicle.CopySimulationSettings(source.GetComponent<VehiclePhysics>(), source.ConfirmedDrive);
                Physics.SyncTransforms();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Advance(NetworkPhysicsPose authoritative, NetworkVehicleCommand input)
        {
            float step = Time.fixedDeltaTime;
            if (authoritative.RecoveryCount != lastRecoveryCount)
            {
                lastRecoveryCount = authoritative.RecoveryCount;
                pending.Clear();
                historyTruncated = initialized = ready = false;
            }
            if (!initialized || authoritative.PhysicsStep != lastServerStep)
            {
                lastServerStep = authoritative.PhysicsStep;
                int confirmed = 0;
                while (authoritative.HasAppliedInput && confirmed < pending.Count &&
                    IsConfirmed(pending[confirmed].Sequence, authoritative.AppliedSequence))
                    confirmed++;
                if (confirmed > 0) pending.RemoveRange(0, confirmed);
                if (historyTruncated && authoritative.HasAppliedInput && IsConfirmed(discardedSequence, authoritative.AppliedSequence))
                    historyTruncated = false;

                if (!historyTruncated)
                {
                    Restore(authoritative);
                    foreach (NetworkVehicleCommand command in pending) Simulate(command, step);
                    NetworkPhysicsPose replayed = Capture();
                    if (ready) CorrectPresentation(current, replayed);
                    else
                    {
                        positionOffset = Vector3.zero;
                        rotationOffset = Quaternion.identity;
                    }
                    current = replayed;
                    initialized = ready = true;
                    replays++;
                    replayedSteps += pending.Count;
                }
            }
            if (pending.Count == HistoryCapacity)
            {
                discardedSequence = pending[0].Sequence;
                pending.RemoveAt(0);
                historyTruncated = true;
            }
            pending.Add(input);
            if (historyTruncated)
            {
                ready = false;
                return;
            }
            previous = current;
            Simulate(input, step);
            current = Capture();
        }

        public bool TryRender(out NetworkPhysicsPose pose)
        {
            pose = default;
            if (!ready) return false;
            float blend = Mathf.Clamp01((float)((Time.timeAsDouble - Time.fixedTimeAsDouble) / Time.fixedDeltaTime));
            pose = NetworkPoseBuffer.Interpolate(previous, current, blend);
            float remaining = Mathf.Exp(-OffsetDecay * Time.unscaledDeltaTime);
            positionOffset *= remaining;
            rotationOffset = Quaternion.Slerp(Quaternion.identity, rotationOffset, remaining);
            float offsetDistance = positionOffset.magnitude;
            if (offsetDistance > 0.001f && body.SweepTest(positionOffset / offsetDistance, out RaycastHit hit,
                offsetDistance, QueryTriggerInteraction.Ignore))
            {
                positionOffset *= Mathf.Clamp01((hit.distance - 0.01f) / offsetDistance);
                rotationOffset = Quaternion.identity;
            }
            pose.Position += positionOffset;
            pose.Rotation = rotationOffset * pose.Rotation;
            return true;
        }

        public void LogDiagnostics(ulong objectId)
        {
            Debug.Log($"[MM NET PREDICT] Машина {objectId}, активно={ready}, серверный шаг={lastServerStep}, " +
                $"команд в очереди={pending.Count}, согласований={replays}, повторено шагов={replayedSteps}, " +
                $"резких коррекций={snaps}, макс. ошибка={maxError:F3} м / {maxAngle:F1}°.");
            replays = replayedSteps = snaps = 0;
            maxError = maxAngle = 0;
        }

        public void Dispose()
        {
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }

        private static bool IsConfirmed(uint sequence, uint confirmed) => unchecked((int)(sequence - confirmed)) <= 0;

        private void Restore(NetworkPhysicsPose pose)
        {
            if (pose.Wheels.Length != vehicle.WheelCount || pose.WheelSpeeds.Length != vehicle.WheelCount)
                throw new InvalidOperationException("Снимок машины содержит неполное состояние опор.");
            body.position = pose.Position;
            body.rotation = pose.Rotation;
            body.linearVelocity = pose.LinearVelocity;
            body.angularVelocity = pose.AngularVelocity;
            for (int i = 0; i < vehicle.WheelCount; i++)
            {
                Vector3 wheel = pose.Wheels[i];
                vehicle.RestoreWheelState(i, new VehicleWheelState
                {
                    Extension = wheel.x,
                    Steering = wheel.y,
                    Rotation = wheel.z,
                    AngularSpeed = pose.WheelSpeeds[i]
                });
            }
        }

        private void Simulate(NetworkVehicleCommand input, float step)
        {
            vehicle.Step(input.Movement, physicsScene, step);
            physicsScene.Simulate(step);
        }

        private NetworkPhysicsPose Capture()
        {
            NetworkPhysicsPose pose = new NetworkPhysicsPose
            {
                RecoveryCount = lastRecoveryCount,
                Position = body.position,
                Rotation = body.rotation,
                LinearVelocity = body.linearVelocity,
                AngularVelocity = body.angularVelocity
            };
            for (int i = 0; i < vehicle.WheelCount; i++)
            {
                VehicleWheelState wheel = vehicle.GetWheelVisualState(i);
                pose.Wheels.Add(new Vector3(wheel.Extension, wheel.Steering, wheel.Rotation));
            }
            return pose;
        }

        private void CorrectPresentation(NetworkPhysicsPose before, NetworkPhysicsPose after)
        {
            Vector3 correction = before.Position - after.Position;
            float distance = correction.magnitude;
            float angle = Quaternion.Angle(before.Rotation, after.Rotation);
            maxError = Mathf.Max(maxError, distance);
            maxAngle = Mathf.Max(maxAngle, angle);
            if (distance > SnapDistance || angle > SnapAngle)
            {
                positionOffset = Vector3.zero;
                rotationOffset = Quaternion.identity;
                snaps++;
                return;
            }
            positionOffset = Vector3.ClampMagnitude(positionOffset + correction, VisualOffsetLimit);
            Quaternion accumulated = rotationOffset * before.Rotation * Quaternion.Inverse(after.Rotation);
            rotationOffset = Quaternion.RotateTowards(Quaternion.identity, accumulated, VisualAngleLimit);
        }

        private void CopyMap(Scene map)
        {
            Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();
            bool hasGround = false;
            foreach (GameObject root in map.GetRootGameObjects())
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if (!collider.gameObject.activeInHierarchy || !collider.enabled || collider.isTrigger ||
                        collider.attachedRigidbody != null) continue;
                    GameObject copy = CopyTransform(collider.transform, copies).gameObject;
                    CopyCollider(collider, copy);
                    if (collider.gameObject.layer == 0) hasGround = true;
                    SurfaceGrip grip = collider.GetComponent<SurfaceGrip>();
                    if (grip != null && !copy.TryGetComponent(out SurfaceGrip _))
                        copy.AddComponent<SurfaceGrip>().CopySettings(grip);
                }
            }
            if (!hasGround) throw new InvalidOperationException("В сцене карты нет статической поверхности для подвески.");
        }

        private Transform CopyTransform(Transform source, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out Transform copy)) return copy;
            copy = NewObject(source.name, source.gameObject.layer).transform;
            if (source.parent != null) copy.SetParent(CopyTransform(source.parent, copies), false);
            copy.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            copy.localScale = source.localScale;
            copies.Add(source, copy);
            return copy;
        }

        private GameObject NewObject(string name, int layer)
        {
            GameObject copy = new GameObject(name) { layer = layer };
            SceneManager.MoveGameObjectToScene(copy, scene);
            return copy;
        }

        private static void CopyCollider(Collider source, GameObject target)
        {
            Collider copy;
            switch (source)
            {
                case BoxCollider box:
                    BoxCollider boxCopy = target.AddComponent<BoxCollider>();
                    boxCopy.center = box.center;
                    boxCopy.size = box.size;
                    copy = boxCopy;
                    break;
                case MeshCollider mesh:
                    MeshCollider meshCopy = target.AddComponent<MeshCollider>();
                    meshCopy.convex = mesh.convex;
                    meshCopy.cookingOptions = mesh.cookingOptions;
                    meshCopy.sharedMesh = mesh.sharedMesh;
                    copy = meshCopy;
                    break;
                case SphereCollider sphere:
                    SphereCollider sphereCopy = target.AddComponent<SphereCollider>();
                    sphereCopy.center = sphere.center;
                    sphereCopy.radius = sphere.radius;
                    copy = sphereCopy;
                    break;
                case CapsuleCollider capsule:
                    CapsuleCollider capsuleCopy = target.AddComponent<CapsuleCollider>();
                    capsuleCopy.center = capsule.center;
                    capsuleCopy.direction = capsule.direction;
                    capsuleCopy.height = capsule.height;
                    capsuleCopy.radius = capsule.radius;
                    copy = capsuleCopy;
                    break;
                case TerrainCollider terrain:
                    TerrainCollider terrainCopy = target.AddComponent<TerrainCollider>();
                    terrainCopy.terrainData = terrain.terrainData;
                    copy = terrainCopy;
                    break;
                default:
                    throw new InvalidOperationException($"Тип коллайдера {source.GetType().Name} не поддерживается в предсказании.");
            }
            copy.sharedMaterial = source.sharedMaterial;
            copy.contactOffset = source.contactOffset;
            copy.includeLayers = source.includeLayers;
            copy.excludeLayers = source.excludeLayers;
            copy.layerOverridePriority = source.layerOverridePriority;
        }
    }
}
