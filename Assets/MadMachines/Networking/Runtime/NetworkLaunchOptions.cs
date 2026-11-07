using System;
using System.Net;
using System.Net.Sockets;

namespace MadMachines.Networking
{
    // These switches belong to this sandbox and do not change Unity/UTP global settings.
    public sealed class NetworkLaunchOptions
    {
        public bool Server;
        public string Address = "127.0.0.1";
        public string BindAddress = "0.0.0.0";
        public ushort Port = 7777;
        public string StopFile;

        public static NetworkLaunchOptions Parse(string[] arguments, bool serverByDefault)
        {
            NetworkLaunchOptions options = new NetworkLaunchOptions { Server = serverByDefault };
            for (int i = 0; i < arguments.Length; i++)
            {
                string key = arguments[i];
                if (!key.StartsWith("-mm-", StringComparison.Ordinal)) continue;
                if (key == "-mm-server")
                {
                    options.Server = true;
                    continue;
                }
                if (i + 1 >= arguments.Length || arguments[i + 1].StartsWith("-"))
                    throw new ArgumentException("Не задано значение " + key);
                string value = arguments[++i];
                switch (key)
                {
                    case "-mm-address": options.Address = value; break;
                    case "-mm-bind": options.BindAddress = value; break;
                    case "-mm-port":
                        if (!ushort.TryParse(value, out options.Port) || options.Port == 0)
                            throw new ArgumentException("Порт должен быть числом от 1 до 65535.");
                        break;
                    case "-mm-stop-file": options.StopFile = value; break;
                    default: throw new ArgumentException("Неизвестный параметр: " + key);
                }
            }
            if (!IsIpv4(options.Address) || options.Address == "0.0.0.0" || !IsIpv4(options.BindAddress))
                throw new ArgumentException("Укажите числовой IPv4; адрес клиента не может быть 0.0.0.0.");
            return options;
        }

        public static bool IsIpv4(string value)
        {
            return IPAddress.TryParse(value, out IPAddress address) && address.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
