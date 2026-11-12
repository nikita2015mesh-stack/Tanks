using System;
using System.IO;
using System.Text;

namespace MadMachines.Networking.Editor
{
    internal static class NetworkBuildOutput
    {
        private const string ServerDirectory = "Builds/Network/Match-LinuxServer";

        public static string GetBuildPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("Неверный идентификатор сетевой сборки. Соберите её заново.");
            return Path.GetFullPath(Path.Combine(ServerDirectory, "versions", id, "MadMachinesServer.x86_64"));
        }

        public static void Publish(string id)
        {
            WriteAtomically(Path.Combine(ServerDirectory, "active.txt"), id + Environment.NewLine);
        }

        public static void WriteAtomically(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(staging, value, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(staging, path, null);
                else File.Move(staging, path);
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
    }
}
