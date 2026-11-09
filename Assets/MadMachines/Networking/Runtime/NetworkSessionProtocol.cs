using System;
using System.Buffers.Binary;

namespace MadMachines.Networking
{
    public static class NetworkSessionProtocol
    {
        // Keep the NGO handshake stable across application protocol updates so approval can report a reason.
        public const ushort NgoHandshakeVersion = 3;
        public const ushort Version = 3;
        public const string RejectionPrefix = "[MM INCOMPATIBLE] ";
        private const int PayloadSize = 12;

        public static byte[] CreatePayload(uint vehicleHash, uint projectileHash)
        {
            byte[] payload = new byte[PayloadSize];
            payload[0] = (byte)'M';
            payload[1] = (byte)'M';
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), Version);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), vehicleHash);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), projectileHash);
            return payload;
        }

        public static string CheckCompatibility(byte[] client, byte[] server)
        {
            if (client == null || client.Length < 4 || client[0] != (byte)'M' || client[1] != (byte)'M')
                return RejectionPrefix + "Клиент не сообщил версию Mad Machines. Пересоберите клиент.";
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(client.AsSpan(2));
            if (version != Version)
                return RejectionPrefix + $"Несовместимая версия сети. Клиент: {version}, сервер: {Version}. Пересоберите обе роли из одной версии проекта.";
            if (client.Length != PayloadSize || !client.AsSpan(4).SequenceEqual(server.AsSpan(4)))
                return RejectionPrefix + "Не совпадают сетевые prefab. Соберите клиент и сервер из одних ресурсов NetworkSandbox.";
            return null;
        }
    }
}
