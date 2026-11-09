using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    public struct NetworkPhysicsPose : INetworkSerializable, IEquatable<NetworkPhysicsPose>
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public double ServerTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Rotation);
            serializer.SerializeValue(ref ServerTime);
        }

        public bool Equals(NetworkPhysicsPose other) => Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && ServerTime.Equals(other.ServerTime);
    }

    public sealed class NetworkPoseBuffer
    {
        private const int MaxSamples = 32;
        private readonly List<NetworkPhysicsPose> samples = new List<NetworkPhysicsPose>(MaxSamples);

        public void Reset() => samples.Clear();

        public void Push(NetworkPhysicsPose current)
        {
            if (samples.Count > 0 && current.ServerTime <= samples[samples.Count - 1].ServerTime) return;
            if (samples.Count == MaxSamples) samples.RemoveAt(0);
            samples.Add(current);
        }

        public bool Sample(double renderTime, out NetworkPhysicsPose result)
        {
            result = default;
            if (samples.Count == 0) return false;
            while (samples.Count > 1 && samples[1].ServerTime <= renderTime) samples.RemoveAt(0);
            NetworkPhysicsPose from = samples[0];
            result = from;
            if (samples.Count == 1) return true;
            NetworkPhysicsPose to = samples[1];
            float blend = Mathf.Clamp01((float)((renderTime - from.ServerTime) / (to.ServerTime - from.ServerTime)));
            result.Position = Vector3.Lerp(from.Position, to.Position, blend);
            result.Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, blend);
            return true;
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
        private CollisionDetectionMode serverCollisionMode;
        private NetworkTickSystem ticks;
        private Vector3 initialScale;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
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
            SetBodyAuthority(IsServer);
            if (IsServer)
            {
                PublishPose();
                ticks = NetworkManager.NetworkTickSystem;
                ticks.Tick += PublishPose;
            }
            else
            {
                pose.OnValueChanged += ReceivePose;
                samples.Push(pose.Value);
                ApplyPose(pose.Value.Position, pose.Value.Rotation);
            }
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
            if (ticks != null) ticks.Tick -= PublishPose;
            ticks = null;
            pose.OnValueChanged -= ReceivePose;
        }

        private void PublishPose()
        {
            if (!IsServer || !IsSpawned || NetworkManager.ShutdownInProgress) return;
            pose.Value = new NetworkPhysicsPose
            {
                Position = body.position,
                Rotation = body.rotation,
                ServerTime = NetworkManager.ServerTime.Time
            };
        }

        private void ReceivePose(NetworkPhysicsPose previous, NetworkPhysicsPose current)
        {
            samples.Push(current);
        }

        private void Update()
        {
            if (!IsSpawned || IsServer) return;
            double renderTime = NetworkManager.ServerTime.Time - InterpolationDelay;
            // Hold the newest authoritative pose on underrun; never simulate or extrapolate a hit.
            if (samples.Sample(renderTime, out NetworkPhysicsPose sample)) ApplyPose(sample.Position, sample.Rotation);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = initialScale;
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
