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

        public bool Accept(NetworkVehicleCommand command, double now)
        {
            tokens = System.Math.Min(Burst, tokens + System.Math.Max(0, now - updated) * CommandsPerSecond);
            updated = now;
            if (tokens < 1) return false;
            tokens--;
            if (!command.IsValid()) return false;
            // Signed modular comparison also handles uint wraparound.
            if (hasSequence && unchecked((int)(command.Sequence - lastSequence)) <= 0) return false;
            lastSequence = command.Sequence;
            hasSequence = true;
            return true;
        }
    }
}
