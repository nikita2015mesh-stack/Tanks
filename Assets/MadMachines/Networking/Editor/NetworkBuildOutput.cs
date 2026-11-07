using System;
using System.IO;
using System.Text;

namespace MadMachines.Networking.Editor
{
    internal static class NetworkBuildOutput
    {
        public const string WindowsServer = "WindowsServer";
        public const string WindowsClient = "Client";
        public const string LinuxServer = "LinuxServer";
        public const string WindowsServerExecutable = "MadMachinesServer.exe";
        public const string WindowsClientExecutable = "MadMachinesClient.exe";
        public const string LinuxServerExecutable = "MadMachinesServer.x86_64";

        public static string GetBuildPath(string target, string executable, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("Неверный идентификатор сетевой сборки. Соберите её заново.");
            return Path.GetFullPath(Path.Combine("Builds", "Network", target, "versions", id, executable));
        }

        public static string GetActiveBuild(string target)
        {
            string path = ActivePath(target);
            if (!File.Exists(path))
                throw new FileNotFoundException("Нет опубликованной сетевой сборки. Выполните Build Development Server или сборку нужной платформы.", path);
            return File.ReadAllText(path).Trim();
        }

        public static void Publish(string target, string id)
        {
            WriteAtomically(ActivePath(target), id + Environment.NewLine);
        }

        private static string ActivePath(string target) => Path.Combine("Builds", "Network", target, "active.txt");

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
