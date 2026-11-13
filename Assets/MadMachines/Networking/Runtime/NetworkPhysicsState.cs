using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MadMachines.Networking
{
    public struct NetworkPhysicsPose : INetworkSerializable, IEquatable<NetworkPhysicsPose>
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public double ServerTime;
        // Completed simulation steps for this spawn, not the NGO send tick or an input acknowledgement.
        public ulong PhysicsStep;
        public ulong ControllerClientId;
        public uint AppliedSequence;
        public bool HasAppliedInput;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
        public Vector2 AimAngles;
        // Per-wheel extension in metres, steering and spin in degrees, in VehiclePhysics order.
        public FixedList512Bytes<Vector3> Wheels;
        public FixedList512Bytes<float> WheelSpeeds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref ServerTime);
            serializer.SerializeValue(ref PhysicsStep);
            serializer.SerializeValue(ref ControllerClientId);
            serializer.SerializeValue(ref AppliedSequence);
            serializer.SerializeValue(ref HasAppliedInput);
            serializer.SerializeValue(ref LinearVelocity);
            serializer.SerializeValue(ref AngularVelocity);
            serializer.SerializeValue(ref AimAngles);
            byte wheelCount = (byte)Wheels.Length;
            serializer.SerializeValue(ref wheelCount);
            if (serializer.IsReader)
            {
                Wheels.Length = wheelCount;
                WheelSpeeds.Length = wheelCount;
            }
            for (int i = 0; i < wheelCount; i++)
            {
                Vector3 wheel = Wheels[i];
                float wheelSpeed = serializer.IsReader ? 0 : WheelSpeeds[i];
                serializer.SerializeValue(ref wheel);
                serializer.SerializeValue(ref wheelSpeed);
                if (serializer.IsReader)
                {
                    Wheels[i] = wheel;
                    WheelSpeeds[i] = wheelSpeed;
                }
            }
        }

        public bool Equals(NetworkPhysicsPose other) => Position.Equals(other.Position) && Rotation.Equals(other.Rotation) &&
            ServerTime.Equals(other.ServerTime) && PhysicsStep == other.PhysicsStep &&
            ControllerClientId == other.ControllerClientId && AppliedSequence == other.AppliedSequence && HasAppliedInput == other.HasAppliedInput &&
            LinearVelocity.Equals(other.LinearVelocity) && AngularVelocity.Equals(other.AngularVelocity) &&
            AimAngles.Equals(other.AimAngles) && Wheels.Equals(other.Wheels) && WheelSpeeds.Equals(other.WheelSpeeds);
    }

    public enum NetworkPoseSampleMode { Empty, Buffering, Interpolating, Holding }

    public sealed class NetworkPoseBuffer
    {
        private const int MaxSamples = 32;
        private readonly List<NetworkPhysicsPose> samples = new List<NetworkPhysicsPose>(MaxSamples);

        public int Count => samples.Count;
        public double LatestTime => samples.Count == 0 ? double.NaN : samples[samples.Count - 1].ServerTime;
        public NetworkPoseSampleMode Mode { get; private set; }

        public void Reset()
        {
            samples.Clear();
            Mode = NetworkPoseSampleMode.Empty;
        }

        public void Push(NetworkPhysicsPose current)
        {
            if (samples.Count > 0 && current.ServerTime <= samples[samples.Count - 1].ServerTime) return;
            if (samples.Count == MaxSamples) samples.RemoveAt(0);
            samples.Add(current);
        }

        public bool Sample(double renderTime, out NetworkPhysicsPose result)
        {
            result = default;
            Mode = NetworkPoseSampleMode.Empty;
            if (samples.Count == 0) return false;
            while (samples.Count > 1 && samples[1].ServerTime <= renderTime) samples.RemoveAt(0);
            NetworkPhysicsPose from = samples[0];
            result = from;
            Mode = renderTime < from.ServerTime ? NetworkPoseSampleMode.Buffering :
                renderTime > LatestTime ? NetworkPoseSampleMode.Holding : NetworkPoseSampleMode.Interpolating;
            if (samples.Count == 1) return true;
            NetworkPhysicsPose to = samples[1];
            float blend = Mathf.Clamp01((float)((renderTime - from.ServerTime) / (to.ServerTime - from.ServerTime)));
            result = Interpolate(from, to, blend);
            result.ServerTime = Math.Max(from.ServerTime, Math.Min(renderTime, to.ServerTime));
            return true;
        }

        internal static NetworkPhysicsPose Interpolate(NetworkPhysicsPose from, NetworkPhysicsPose to, float blend)
        {
            NetworkPhysicsPose result = from;
            result.Position = Vector3.Lerp(from.Position, to.Position, blend);
            result.Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, blend);
            result.AimAngles = new Vector2(Mathf.LerpAngle(from.AimAngles.x, to.AimAngles.x, blend),
                Mathf.LerpAngle(from.AimAngles.y, to.AimAngles.y, blend));
            for (int i = 0; i < Mathf.Min(from.Wheels.Length, to.Wheels.Length); i++)
            {
                Vector3 start = from.Wheels[i];
                Vector3 end = to.Wheels[i];
                result.Wheels[i] = new Vector3(Mathf.Lerp(start.x, end.x, blend), Mathf.Lerp(start.y, end.y, blend),
                    Mathf.LerpAngle(start.z, end.z, blend));
            }
            return result;
        }
    }

    // No client-to-server pose RPC. The server alone owns the object and writes this variable.
    // This intentionally replaces NetworkTransform/NetworkRigidbody only in NetworkSandbox.
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    public sealed class NetworkPhysicsState : NetworkBehaviour
    {
        public const double InterpolationDelay = 0.1;
        private readonly NetworkVariable<NetworkPhysicsPose> pose = new NetworkVariable<NetworkPhysicsPose>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkPoseBuffer samples = new NetworkPoseBuffer();
        private Rigidbody body;
        private NetworkVehicle vehicle;
        private CollisionDetectionMode serverCollisionMode;
        private Vector3 initialScale;
        private ulong physicsStep;
        private ulong capturedStep;
        private double lastReceivedAt;
        private double lastReceivedTime;
        private double lastRenderTime;
        private double nextDiagnostic;
        private double maxReceiveGap;
        private double maxSampleGap;
        private double minBufferAhead;
        private float maxFrameTime;
        private int received;
        private int renderedFrames;
        private int holdingFrames;
        private int underruns;
        private int clockRewinds;
        private bool wasHolding;
        private bool hasReceivedPair;
        private NetworkVehiclePrediction prediction;
        private bool predictionFailed;
        internal CollisionDetectionMode ServerCollisionMode => serverCollisionMode;

        public string SnapshotDiagnostics => IsServer
            ? $"физический шаг={pose.Value.PhysicsStep}, время снимка={pose.Value.ServerTime:F3} с"
            : $"шаг={pose.Value.PhysicsStep}, снимков={received}, кадров={renderedFrames}, нехваток={underruns}, кадров удержания={holdingFrames}, " +
                $"буфер={samples.Count}, мин. запас={(double.IsPositiveInfinity(minBufferAhead) ? 0 : minBufferAhead * 1000):F1} мс, " +
                $"макс. интервал получения={maxReceiveGap * 1000:F1} мс, снимков={maxSampleGap * 1000:F1} мс, " +
                $"макс. кадр={maxFrameTime * 1000:F1} мс, кадров с откатом часов={clockRewinds}";

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            vehicle = GetComponent<NetworkVehicle>();
            initialScale = transform.localScale;
            serverCollisionMode = body.collisionDetectionMode;
            SetBodyAuthority(false);
            body.interpolation = RigidbodyInterpolation.None;

            // NGO 2.13.3 applies ParentSyncMessage transform data separately from parenting.
            // Keep these objects server-owned and disable both paths.
            NetworkObject networkObject = GetComponent<NetworkObject>();
            networkObject.AutoObjectParentSync = false;
            networkObject.AllowOwnerToParent = false;
            networkObject.SyncOwnerTransformWhenParented = false;
            networkObject.AlwaysReplicateAsRoot = true;
        }

        public override void OnNetworkSpawn()
        {
            samples.Reset();
            physicsStep = capturedStep = 0;
            predictionFailed = false;
            lastReceivedAt = lastReceivedTime = lastRenderTime = double.NaN;
            wasHolding = hasReceivedPair = false;
            ResetDiagnostics();
            SetBodyAuthority(IsServer);
            if (IsServer)
            {
                NetworkPhysicsSnapshotLoop.Register(this);
            }
            else
            {
                pose.OnValueChanged += ReceivePose;
                ReceivePose(default, pose.Value);
                ApplyPose(pose.Value);
            }
        }

        protected override void OnNetworkPostSpawn()
        {
            // Controller and drive must be initialized even if this behaviour spawned first.
            if (IsServer) PublishPose(NetworkManager.ServerTime.Time);
        }

        public override void OnNetworkDespawn()
        {
            Unsubscribe();
            samples.Reset();
            SetBodyAuthority(false);
        }

        private void SetBodyAuthority(bool authoritative)
        {
            if (authoritative)
            {
                body.isKinematic = false;
                body.collisionDetectionMode = serverCollisionMode;
            }
            else
            {
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
            }
        }

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            StopPrediction();
            NetworkPhysicsSnapshotLoop.Unregister(this);
            pose.OnValueChanged -= ReceivePose;
        }

        private void FixedUpdate()
        {
            if (IsSpawned && IsServer && !NetworkManager.ShutdownInProgress) physicsStep++;
        }

        internal void CaptureAfterPhysics()
        {
            if (!IsSpawned || !IsServer || !isActiveAndEnabled || NetworkManager.ShutdownInProgress ||
                physicsStep == capturedStep) return;
            // This hook precedes NGO PreUpdate. Mirror its pending unscaled clock advance, then subtract
            // the remainder since the last completed fixed step. The session fixes timeScale to 1.
            // Use the continuous clock, never the temporarily quantized clock inside Tick callbacks.
            double serverFrameTime = NetworkManager.NetworkTimeSystem.ServerTime + Time.unscaledDeltaTime;
            double sampleTime = serverFrameTime - Math.Max(0, Time.timeAsDouble - Time.fixedTimeAsDouble);
            if (sampleTime <= pose.Value.ServerTime) return;
            capturedStep = physicsStep;
            PublishPose(sampleTime);
        }

        private void PublishPose(double sampleTime)
        {
            if (!IsServer || !IsSpawned || NetworkManager.ShutdownInProgress) return;
            NetworkPhysicsPose sample = new NetworkPhysicsPose
            {
                Position = body.position,
                Rotation = body.rotation,
                LinearVelocity = body.linearVelocity,
                AngularVelocity = body.angularVelocity,
                ServerTime = sampleTime,
                PhysicsStep = physicsStep
            };
            if (vehicle != null) vehicle.CapturePresentation(ref sample);
            pose.Value = sample;
        }

        private void ReceivePose(NetworkPhysicsPose previous, NetworkPhysicsPose current)
        {
            if (samples.Count > 0 && current.ServerTime <= samples.LatestTime) return;
            if (samples.Count > 0) hasReceivedPair = true;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!double.IsNaN(lastReceivedAt)) maxReceiveGap = Math.Max(maxReceiveGap, now - lastReceivedAt);
            if (!double.IsNaN(lastReceivedTime)) maxSampleGap = Math.Max(maxSampleGap, current.ServerTime - lastReceivedTime);
            lastReceivedAt = now;
            lastReceivedTime = current.ServerTime;
            received++;
            samples.Push(current);
        }

        private void Update()
        {
            if (!IsSpawned || IsServer || NetworkManager.ShutdownInProgress) return;
            if (prediction != null && (!vehicle.IsLocalController || !vehicle.isActiveAndEnabled)) StopPrediction();
            double renderTime = NetworkManager.ServerTime.Time - InterpolationDelay;
            // A backwards clock correction must not rewind the car through already displayed snapshots.
            if (!double.IsNaN(lastRenderTime) && renderTime < lastRenderTime)
            {
                clockRewinds++;
                renderTime = lastRenderTime;
            }
            lastRenderTime = renderTime;
            // Hold the newest authoritative pose on underrun; never simulate or extrapolate a hit.
            if (samples.Sample(renderTime, out NetworkPhysicsPose sample))
            {
                if (prediction != null && prediction.TryRender(out NetworkPhysicsPose predicted))
                {
                    predicted.AimAngles = sample.AimAngles;
                    ApplyPose(predicted);
                }
                else ApplyPose(sample);
                bool holding = hasReceivedPair && samples.Mode == NetworkPoseSampleMode.Holding;
                if (holding && !wasHolding) underruns++;
                if (holding) holdingFrames++;
                wasHolding = holding;
                if (hasReceivedPair) minBufferAhead = Math.Min(minBufferAhead, samples.LatestTime - renderTime);
            }
            renderedFrames++;
            maxFrameTime = Mathf.Max(maxFrameTime, Time.unscaledDeltaTime);
            if (!double.IsNaN(lastReceivedAt))
                maxReceiveGap = Math.Max(maxReceiveGap, Time.realtimeSinceStartupAsDouble - lastReceivedAt);
            if (vehicle != null && Time.realtimeSinceStartupAsDouble >= nextDiagnostic)
            {
                ulong rtt = NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId);
                Debug.Log($"[MM NET SMOOTH] Машина {NetworkObjectId}, своя={vehicle.IsLocalController}, RTT={rtt} мс: {SnapshotDiagnostics}.");
                prediction?.LogDiagnostics(NetworkObjectId);
                ResetDiagnostics();
            }
        }

        internal void Predict(NetworkVehicleCommand command, Scene map)
        {
            if (predictionFailed || !IsSpawned || IsServer || !vehicle.IsLocalController ||
                pose.Value.ControllerClientId != NetworkManager.LocalClientId) return;
            try
            {
                prediction ??= new NetworkVehiclePrediction(map, vehicle);
                prediction.Advance(pose.Value, command);
            }
            catch (Exception error)
            {
                StopPrediction();
                predictionFailed = true;
                Debug.LogWarning($"[MM NET PREDICT] Машина {NetworkObjectId} перешла на интерполяцию. {error.Message}");
            }
        }

        internal void StopPrediction()
        {
            prediction?.Dispose();
            prediction = null;
        }

        internal void RetryPrediction()
        {
            predictionFailed = false;
        }

        private void ResetDiagnostics()
        {
            received = renderedFrames = holdingFrames = underruns = clockRewinds = 0;
            maxReceiveGap = maxSampleGap = 0;
            minBufferAhead = double.PositiveInfinity;
            maxFrameTime = 0;
            nextDiagnostic = Time.realtimeSinceStartupAsDouble + 5;
        }

        private void ApplyPose(NetworkPhysicsPose sample)
        {
            transform.SetPositionAndRotation(sample.Position, sample.Rotation);
            transform.localScale = initialScale;
            if (vehicle != null) vehicle.ApplyPresentation(sample);
        }

#if UNITY_EDITOR
        public void RequestClientPoseProbe()
        {
            if (!IsSpawned || IsServer) throw new InvalidOperationException("Нужен подключённый клиент.");
            // Expected NGO write-permission error. This must not become a server pose update.
            NetworkPhysicsPose invalid = pose.Value;
            invalid.Position += Vector3.up * 100;
            pose.Value = invalid;
            transform.position = invalid.Position;
            transform.localScale = Vector3.one * 5;
        }
#endif
    }
}
