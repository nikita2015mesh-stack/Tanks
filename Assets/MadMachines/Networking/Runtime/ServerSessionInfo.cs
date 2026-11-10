using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace MadMachines.Networking
{
    [Serializable]
    public sealed class ServerSessionInfo
    {
        public string token;
        public int protocol;
        public string mapId;
        public string location;
        public string mode;
        public string phase;
        public int players;
        public int capacity;
        public bool joinable;

        public string Scene => mapId == "quarry.v2" ? "QuarryOnline" : mapId == "sandbox" ? "NetworkSandbox" : null;
        public bool Supported => protocol == NetworkSessionProtocol.Version && Scene != null && mode == "free" && phase == "running";
        public string Summary => $"{location}\nСвободная езда · сессия идёт\nИгроков: {players}/{capacity}";
    }

    // A small read-only query endpoint at game UDP port + 1. Never allocates a player slot.
    public sealed class ServerSessionQuery : IDisposable
    {
        private const string Prefix = "MM_SESSION_1:";
        private UdpClient socket;
        private string token;
        private double deadline;
        public ServerSessionInfo Result { get; private set; }
        public string Status { get; private set; }
        public bool Pending => socket != null;

        public void Begin(string address, string port)
        {
            Dispose();
            Result = null;
            if (!NetworkLaunchOptions.IsClientAddress(address) || !ushort.TryParse(port, out ushort number) || number == 0 || number == ushort.MaxValue)
            {
                Status = "Укажите IPv4 и игровой порт от 1 до 65534.";
                return;
            }
            try
            {
                token = Guid.NewGuid().ToString("N");
                socket = new UdpClient();
                socket.Connect(address, number + 1);
                byte[] request = Encoding.UTF8.GetBytes(Prefix + token);
                socket.Send(request, request.Length);
                deadline = Time.realtimeSinceStartupAsDouble + 4;
                Status = "Запрос игровой сессии…";
            }
            catch (SocketException)
            {
                Status = "Не удалось запросить сессию.";
                Dispose();
            }
        }

        public void Poll()
        {
            if (socket == null) return;
            try
            {
                if (socket.Available > 0)
                {
                    IPEndPoint sender = null;
                    byte[] packet = socket.Receive(ref sender);
                    if (packet.Length <= 2048)
                    {
                        ServerSessionInfo info = JsonUtility.FromJson<ServerSessionInfo>(Encoding.UTF8.GetString(packet));
                        if (info != null && info.token == token)
                        {
                            Result = info;
                            Status = !info.Supported ? "Сессия несовместима с этой сборкой." :
                                info.joinable ? "Можно присоединиться." : "Сессия сейчас недоступна для входа.";
                            Dispose();
                            return;
                        }
                    }
                }
            }
            catch (Exception error) when (error is SocketException || error is ArgumentException)
            {
                Status = "Сервер не ответил на запрос сессии.";
                Dispose();
                return;
            }
            if (Time.realtimeSinceStartupAsDouble >= deadline)
            {
                Status = "Нет ответа. Проверьте адрес и доступность сервера.";
                Dispose();
            }
        }

        public static UdpClient OpenServer(string bindAddress, ushort gamePort)
        {
            if (gamePort == ushort.MaxValue) throw new ArgumentException("Session query requires game port below 65535.");
            return new UdpClient(new IPEndPoint(IPAddress.Parse(bindAddress), gamePort + 1));
        }

        public static void Respond(UdpClient server, ServerSessionInfo info)
        {
            if (server == null) return;
            for (int i = 0; i < 4 && server.Available > 0; i++)
            {
                IPEndPoint sender = null;
                byte[] request = server.Receive(ref sender);
                if (request.Length != Prefix.Length + 32) continue;
                string text = Encoding.UTF8.GetString(request);
                if (!text.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                info.token = text.Substring(Prefix.Length);
                byte[] response = Encoding.UTF8.GetBytes(JsonUtility.ToJson(info));
                server.Send(response, response.Length, sender);
            }
        }

        public void Dispose()
        {
            socket?.Dispose();
            socket = null;
        }
    }
}
