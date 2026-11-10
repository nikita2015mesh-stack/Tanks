using System;
using System.Buffers.Binary;
using System.Text;

namespace MadMachines.Networking
{
    public static class NetworkSessionProtocol
    {
        // Keep the NGO handshake stable across application protocol updates so approval can report a reason.
        public const ushort NgoHandshakeVersion = 3;
        public const ushort Version = 6;
        public const string RejectionPrefix = "[MM INCOMPATIBLE] ";
        private const int MapIdOffset = 13;

        public static byte[] CreatePayload(uint vehicleHash, uint projectileHash, string mapId = "sandbox", VehicleDrive drive = VehicleDrive.Configured)
        {
            byte[] map = Encoding.UTF8.GetBytes(mapId);
            byte[] payload = new byte[MapIdOffset + map.Length];
            payload[0] = (byte)'M';
            payload[1] = (byte)'M';
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), Version);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), vehicleHash);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), projectileHash);
            payload[12] = (byte)drive;
            map.CopyTo(payload, MapIdOffset);
            return payload;
        }

        public static VehicleDrive ReadDrive(byte[] payload) => (VehicleDrive)payload[12];

        public static string CheckCompatibility(byte[] client, byte[] server, out string diagnostic)
        {
            if (client == null || client.Length < 4 || client[0] != (byte)'M' || client[1] != (byte)'M')
            {
                diagnostic = "Missing Mad Machines protocol header. Rebuild the client.";
                return RejectionPrefix + "Клиент не сообщил версию Mad Machines. Пересоберите клиент.";
            }
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(client.AsSpan(2));
            if (version != Version)
            {
                diagnostic = $"Protocol mismatch (client {version}, server {Version}).";
                return RejectionPrefix + $"Несовместимая версия сети. Клиент: {version}, сервер: {Version}. Пересоберите обе роли из одной версии проекта.";
            }
            if (client.Length < MapIdOffset || !client.AsSpan(4, 8).SequenceEqual(server.AsSpan(4, 8)))
            {
                diagnostic = "Network prefab IDs do not match. Build both players from the same project revision.";
                return RejectionPrefix + "Не совпадают сетевые префабы. Соберите клиент и сервер из одной версии проекта.";
            }
            if (client[12] > (byte)VehicleDrive.Rear)
            {
                diagnostic = "Unsupported vehicle configuration.";
                return RejectionPrefix + "Этот привод недоступен в сессии.";
            }
            if (!client.AsSpan(MapIdOffset).SequenceEqual(server.AsSpan(MapIdOffset)))
            {
                diagnostic = "Client and server selected different maps.";
                return RejectionPrefix + "Клиент и сервер запущены на разных картах. Откройте ту же карту, что на сервере.";
            }
            diagnostic = null;
            return null;
        }
    }
}
