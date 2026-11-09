using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MadMachines.Networking.Editor
{
    [InitializeOnLoad]
    internal static class NetworkDevelopmentServer
    {
        private const string Menu = "Tools/Mad Machines/Networking/";
        private const string Runs = "Logs/Network/Development";
        private const string StatePath = Runs + "/current.json";
        private static ServerRun run;
        private static Process process;
        private static double nextPoll;
        private static string lastObserverError;

        [Serializable]
        private sealed class ServerRun
        {
            public string id;
            public string build;
            public int pid;
            public int port;
            public long started;
            public long stopRequested;
            public string restartBuild;
            public int restartPort;
            public bool ready;
            public bool ended;
            public bool stopTimeoutReported;

            public string DirectoryPath => Path.GetFullPath(Path.Combine(Runs, id));
            public string StopFile => Path.Combine(DirectoryPath, "server.stop");
            public string LogFile => Path.Combine(DirectoryPath, "server.log");
        }

        static NetworkDevelopmentServer()
        {
            EditorApplication.delayCall += () => Execute(Refresh);
            EditorApplication.update += Poll;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseProcess;
        }

        [MenuItem(Menu + "Start Development Server", false, 100)]
        private static void StartServer() => Execute(() =>
        {
            if (IsRunning()) throw new InvalidOperationException("Сервер уже запущен. " + DescribeRun());
            Launch(ActiveBuild(), NetworkDevelopmentSettings.Load().localPort);
        });

        [MenuItem(Menu + "Stop Development Server", false, 101)]
        private static void StopServer() => Execute(() =>
        {
            if (!IsRunning())
            {
                Debug.Log("[MM NET] " + DescribeRun());
                return;
            }
            RequestStop(null, 0);
        });

        [MenuItem(Menu + "Restart Development Server", false, 102)]
        private static void RestartServer() => Execute(Restart);

        [MenuItem(Menu + "Build and Restart Development Server", false, 104)]
        private static void BuildAndRestart() => Execute(() =>
        {
            if (NetworkSandboxEditor.BuildDevelopmentServer()) Restart();
        });

        [MenuItem(Menu + "Development Server Status", false, 105)]
        private static void ShowStatus() => Execute(() =>
        {
            Refresh();
            EditorUtility.DisplayDialog("Mad Machines · локальный сервер", DescribeRun(), "OK");
        });

        private static void Restart()
        {
            string build = ActiveBuild();
            int port = NetworkDevelopmentSettings.Load().localPort;
            if (IsRunning())
            {
                if (run.port != port) CheckPort(port);
                RequestStop(build, port);
            }
            else Launch(build, port);
        }

        private static void Execute(Action action)
        {
            try
            {
                LoadRun();
                action();
            }
            catch (Exception error)
            {
                Debug.LogError("[MM NET] " + error.Message);
            }
        }

        private static void LoadRun()
        {
            if (run != null || !File.Exists(StatePath)) return;
            ServerRun saved = JsonUtility.FromJson<ServerRun>(File.ReadAllText(StatePath));
            if (saved == null || !Guid.TryParseExact(saved.id, "N", out _) ||
                !Guid.TryParseExact(saved.build, "N", out _) || saved.pid < 1 || saved.started <= 0)
                throw new InvalidDataException("Некорректное состояние сервера: " + Path.GetFullPath(StatePath));
            run = saved;
        }

        private static void SaveRun() => NetworkBuildOutput.WriteAtomically(StatePath, JsonUtility.ToJson(run, true));

        private static string ActiveBuild()
        {
            string build = NetworkBuildOutput.GetActiveBuild(NetworkBuildOutput.WindowsServer);
            ResolveExecutable(build);
            return build;
        }

        private static string ResolveExecutable(string build)
        {
            string exe = NetworkBuildOutput.GetBuildPath(NetworkBuildOutput.WindowsServer, NetworkBuildOutput.WindowsServerExecutable, build);
            if (!File.Exists(exe))
                throw new FileNotFoundException("Серверная сборка не найдена. Выполните Build Development Server. Ожидается: " + exe);
            return exe;
        }

        private static void Launch(string build, int port)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new PlatformNotSupportedException("Локальный запуск из меню поддерживается в редакторе Windows.");
            string exe = ResolveExecutable(build);
            CheckPort(port);
            ServerRun next = new ServerRun { id = Guid.NewGuid().ToString("N"), build = build, port = port };
            Directory.CreateDirectory(next.DirectoryPath);
            ReleaseProcess();
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetFullPath("."),
                    Arguments = $"-batchmode -nographics -mm-bind {NetworkDevelopmentSettings.LocalAddress} -mm-port {port} " +
                        $"-mm-stop-file \"{next.StopFile}\" -logFile \"{next.LogFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }) ?? throw new InvalidOperationException("Не удалось создать процесс.");
                next.pid = process.Id;
                next.started = process.StartTime.ToUniversalTime().Ticks;
                run = next;
                SaveRun();
            }
            catch (Exception error)
            {
                if (process != null && !process.HasExited) File.WriteAllText(next.StopFile, string.Empty);
                throw new InvalidOperationException($"Не удалось запустить сервер. {error.Message}\nЖурнал: {next.LogFile}", error);
            }
            Debug.Log("[MM NET] " + DescribeRun());
        }

        private static void CheckPort(int port)
        {
            try
            {
                using UdpClient probe = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
                probe.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (SocketException error)
            {
                throw new InvalidOperationException($"UDP {NetworkDevelopmentSettings.LocalAddress}:{port} недоступен. Проверьте занятый порт или измените Development Settings. {error.Message}", error);
            }
        }

        private static bool IsRunning()
        {
            if (run == null || run.ended) return false;
            if (process != null) return !process.HasExited;
            Process candidate;
            try { candidate = Process.GetProcessById(run.pid); }
            catch (ArgumentException) { return false; }
            try
            {
                if (!candidate.HasExited && candidate.StartTime.ToUniversalTime().Ticks == run.started)
                {
                    process = candidate;
                    return true;
                }
            }
            catch (InvalidOperationException) { }
            candidate.Dispose();
            return false;
        }

        private static void RequestStop(string restartBuild, int restartPort)
        {
            run.stopRequested = DateTime.UtcNow.Ticks;
            run.restartBuild = restartBuild;
            run.restartPort = restartPort;
            run.stopTimeoutReported = false;
            SaveRun();
            File.WriteAllText(run.StopFile, string.Empty);
            Debug.Log("[MM NET] " + DescribeRun());
        }

        private static void Poll()
        {
            if (run == null || run.ended || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            try
            {
                Refresh();
                lastObserverError = null;
            }
            catch (Exception error)
            {
                if (lastObserverError != error.Message) Debug.LogError("[MM NET] " + error.Message);
                lastObserverError = error.Message;
            }
        }

        private static void Refresh()
        {
            if (run == null || run.ended) return;
            if (!IsRunning())
            {
                bool stopping = run.stopRequested != 0;
                int? exitCode = process?.ExitCode;
                ReleaseProcess();
                run.ended = true;
                SaveRun();
                string detail = DescribeRun() + (exitCode.HasValue ? $" Код выхода: {exitCode}." : string.Empty);
                if (!stopping || (exitCode.HasValue && exitCode != 0)) Debug.LogError("[MM NET] " + detail);
                else Debug.Log("[MM NET] " + detail);
                if (!string.IsNullOrEmpty(run.restartBuild)) Launch(run.restartBuild, run.restartPort);
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            if (run.stopRequested != 0)
            {
                if (!File.Exists(run.StopFile)) File.WriteAllText(run.StopFile, string.Empty);
                if (!run.stopTimeoutReported && now - run.stopRequested >= TimeSpan.FromSeconds(15).Ticks)
                {
                    run.stopTimeoutReported = true;
                    SaveRun();
                    Debug.LogError("[MM NET] Сервер не завершился за 15 секунд. " + DescribeRun());
                }
                return;
            }

            if (run.ready) return;
            if (HasReadyMarker(run.LogFile))
            {
                run.ready = true;
                SaveRun();
                Debug.Log("[MM NET] " + DescribeRun());
            }
            else if (now - run.started >= TimeSpan.FromSeconds(45).Ticks)
            {
                Debug.LogError("[MM NET] Сервер не сообщил о готовности за 45 секунд. Журнал: " + run.LogFile);
                RequestStop(null, 0);
            }
        }

        private static bool HasReadyMarker(string path)
        {
            if (!File.Exists(path)) return false;
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader log = new StreamReader(stream);
            string line;
            while ((line = log.ReadLine()) != null)
            {
                if (line.Contains(NetworkSandboxSession.ServerReadyMarker)) return true;
            }
            return false;
        }

        private static string DescribeRun()
        {
            if (run == null) return "Локальный сервер ещё не запускался.";
            string state = run.ended ? "Процесс завершён" : run.stopRequested != 0 ? "Завершение" :
                run.ready ? "Сервер готов" : "Запуск";
            if (!run.ended && !string.IsNullOrEmpty(run.restartBuild)) state += ", затем перезапуск";
            return $"{state}. PID {run.pid}, UDP {NetworkDevelopmentSettings.LocalAddress}:{run.port}.\nЖурнал: {run.LogFile}";
        }

        private static void ReleaseProcess()
        {
            process?.Dispose();
            process = null;
        }
    }
}
