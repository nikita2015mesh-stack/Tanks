using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace MadMachines.Networking.Editor
{
    internal static class NetworkBuildOutput
    {
        public static string GetBuildPath(BuildTarget target, string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("Неверный идентификатор сетевой сборки. Соберите её заново.");
            string executable = target == BuildTarget.StandaloneWindows64 ? "MadMachinesServer.exe" : "MadMachinesServer.x86_64";
            return Path.GetFullPath(Path.Combine(GetServerDirectory(target), "versions", id, executable));
        }

        public static void Publish(BuildTarget target, string id)
        {
            WriteAtomically(Path.Combine(GetServerDirectory(target), "active.txt"), id + Environment.NewLine);
        }

        private static string GetServerDirectory(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows64 => "Builds/Network/Match-WindowsServer",
                BuildTarget.StandaloneLinux64 => "Builds/Network/Match-LinuxServer",
                _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
            };
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
