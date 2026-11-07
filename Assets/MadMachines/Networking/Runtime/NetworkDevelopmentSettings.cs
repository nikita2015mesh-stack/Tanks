using System;
using System.IO;
using UnityEngine;

namespace MadMachines.Networking
{
    public enum NetworkConnectionMode
    {
        Offline,
        DevelopmentOnline,
        RemoteOnline
    }

    [Serializable]
    public sealed class NetworkDevelopmentSettings
    {
        public const string LocalAddress = "127.0.0.1";
        public const int DefaultPort = 7777;
        public const string FilePath = "UserSettings/MadMachines.Networking.json";

        public NetworkConnectionMode mode = NetworkConnectionMode.DevelopmentOnline;
        public int localPort = DefaultPort;
        public string remoteAddress = LocalAddress;
        public int remotePort = DefaultPort;

        public static NetworkDevelopmentSettings Load()
        {
            NetworkDevelopmentSettings settings = new NetworkDevelopmentSettings();
#if UNITY_EDITOR
            if (File.Exists(FilePath))
                JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
#endif
            settings.Validate();
            return settings;
        }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(NetworkConnectionMode), mode))
                throw new ArgumentException("Неизвестный режим подключения.");
            if (localPort < 1 || localPort > ushort.MaxValue || remotePort < 1 || remotePort > ushort.MaxValue)
                throw new ArgumentException("Порт должен быть числом от 1 до 65535.");
            if (!NetworkLaunchOptions.IsClientAddress(remoteAddress))
                throw new ArgumentException("Укажите числовой IPv4 удалённого сервера.");
        }
    }
}
