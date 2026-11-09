using Unity.Netcode;
using UnityEngine;

namespace MadMachines.Networking
{
    public struct NetworkVehicleCommand : INetworkSerializable
    {
        public uint Sequence;
        public float Throttle;
        public float Steering;
        public bool Handbrake;
        public Vector3 Aim;
        public bool Fire;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref Steering);
            serializer.SerializeValue(ref Handbrake);
            serializer.SerializeValue(ref Aim);
            serializer.SerializeValue(ref Fire);
        }

        public bool IsValid()
        {
            return Finite(Throttle) && Mathf.Abs(Throttle) <= 1f &&
                Finite(Steering) && Mathf.Abs(Steering) <= 1f &&
                Finite(Aim.x) && Finite(Aim.y) && Finite(Aim.z) &&
                Aim.sqrMagnitude >= 0.9f && Aim.sqrMagnitude <= 1.1f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Server clock only. A small burst absorbs packet batching without allowing unlimited commands.
    public sealed class NetworkCommandGate
    {
        private const double CommandsPerSecond = 40;
        private const double Burst = 8;
        private double tokens = Burst;
        private double updated;
        private uint lastSequence;
        private bool hasSequence;

        public void Reset()
        {
            tokens = Burst;
            updated = 0;
            lastSequence = 0;
            hasSequence = false;
        }

        public bool Accept(NetworkVehicleCommand command, double now) => Evaluate(command, now) == CommandRejection.None;

        public CommandRejection Evaluate(NetworkVehicleCommand command, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < updated) return CommandRejection.Clock;
            tokens = System.Math.Min(Burst, tokens + System.Math.Max(0, now - updated) * CommandsPerSecond);
            updated = now;
            if (tokens < 1) return CommandRejection.Rate;
            tokens--;
            if (!command.IsValid()) return CommandRejection.Values;
            // Signed modular comparison also handles uint wraparound.
            if (hasSequence && unchecked((int)(command.Sequence - lastSequence)) <= 0) return CommandRejection.Sequence;
            lastSequence = command.Sequence;
            hasSequence = true;
            return CommandRejection.None;
        }
    }

    public enum CommandRejection { None, Rate, Values, Sequence, Clock, Sender, Inactive }

    // No MonoBehaviour or wall-clock reads: both the RPC adapter and editor checks use this policy.
    // Movement is a latest-value stream, fire is latched only until the next physics step.
    public sealed class NetworkCommandBuffer
    {
        public const double Timeout = 0.25;
        private readonly NetworkCommandGate gate = new NetworkCommandGate();
        private NetworkVehicleCommand latest;
        private double pendingFireAt = double.NegativeInfinity;
        private double lastAcceptedAt = double.NegativeInfinity;

        public void Reset()
        {
            gate.Reset();
            ClearInput();
        }

        public void ClearInput()
        {
            latest = default;
            pendingFireAt = double.NegativeInfinity;
            lastAcceptedAt = double.NegativeInfinity;
        }

        public CommandRejection Receive(NetworkVehicleCommand command, double now, ulong sender, ulong owner, bool active)
        {
            if (!active) return CommandRejection.Inactive;
            if (sender != owner) return CommandRejection.Sender;
            CommandRejection rejection = gate.Evaluate(command, now);
            if (rejection != CommandRejection.None) return rejection;
            latest = command;
            if (command.Fire) pendingFireAt = now;
            lastAcceptedAt = now;
            return CommandRejection.None;
        }

        public bool Sample(double now, out NetworkVehicleCommand command)
        {
            bool fresh = now >= lastAcceptedAt && now - lastAcceptedAt <= Timeout;
            command = fresh ? latest : new NetworkVehicleCommand { Handbrake = true };
            command.Fire = fresh && (latest.Fire || (now >= pendingFireAt && now - pendingFireAt <= Timeout));
            pendingFireAt = double.NegativeInfinity;
            return fresh;
        }
    }
}
