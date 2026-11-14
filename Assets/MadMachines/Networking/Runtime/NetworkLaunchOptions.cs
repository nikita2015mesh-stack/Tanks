using System;
using System.Net;
using System.Net.Sockets;

namespace MadMachines.Networking
{
    // Explicit command-line options take precedence over the current Editor user's settings.
    public sealed class NetworkLaunchOptions
    {
        public bool Server;
        public NetworkConnectionMode Mode { get; set; }
        public string Address;
        public string BindAddress = "0.0.0.0";
        public ushort Port;
        public string StopFile;

        public static NetworkLaunchOptions Parse(string[] arguments, bool serverByDefault, NetworkDevelopmentSettings settings = null)
        {
            settings ??= new NetworkDevelopmentSettings();
            NetworkLaunchOptions options = new NetworkLaunchOptions { Server = serverByDefault, Mode = settings.mode };
            string address = null;
            ushort? port = null;
            bool explicitMode = false;
            for (int i = 0; i < arguments.Length; i++)
            {
                string key = arguments[i];
                if (!key.StartsWith("-mm-", StringComparison.Ordinal)) continue;
                if (i + 1 >= arguments.Length || arguments[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException("Missing value for " + key);
                string value = arguments[++i];
                switch (key)
                {
                    case "-mm-mode":
                        options.Mode = ParseMode(value);
                        explicitMode = true;
                        break;
                    case "-mm-address": address = value; break;
                    case "-mm-bind": options.BindAddress = value; break;
                    case "-mm-port":
                        if (!ushort.TryParse(value, out ushort parsed) || parsed == 0)
                            throw new ArgumentException("Port must be an integer from 1 to 65535.");
                        port = parsed;
                        break;
                    case "-mm-stop-file": options.StopFile = value; break;
                    default: throw new ArgumentException("Unknown option: " + key);
                }
            }
            if (!explicitMode && address != null && !options.Server) options.Mode = NetworkConnectionMode.RemoteOnline;
            bool remote = !options.Server && options.Mode == NetworkConnectionMode.RemoteOnline;
            options.Address = address ?? (remote ? settings.remoteAddress : NetworkDevelopmentSettings.LocalAddress);
            options.Port = port ?? (ushort)(remote ? settings.remotePort : settings.localPort);
            if (!IsClientAddress(options.Address) || !IsIpv4(options.BindAddress))
                throw new ArgumentException("Use numeric IPv4 addresses. The client address cannot be 0.0.0.0.");
            options.Address = IPAddress.Parse(options.Address).ToString();
            options.BindAddress = IPAddress.Parse(options.BindAddress).ToString();
            return options;
        }

        private static NetworkConnectionMode ParseMode(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "offline": return NetworkConnectionMode.Offline;
                case "development": return NetworkConnectionMode.DevelopmentOnline;
                case "remote": return NetworkConnectionMode.RemoteOnline;
                default: throw new ArgumentException("Connection mode must be offline, development, or remote.");
            }
        }

        public static bool IsClientAddress(string value)
        {
            return IsIpv4(value) && !IPAddress.Parse(value).Equals(IPAddress.Any);
        }

        private static bool IsIpv4(string value)
        {
            return IPAddress.TryParse(value, out IPAddress address) && address.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
