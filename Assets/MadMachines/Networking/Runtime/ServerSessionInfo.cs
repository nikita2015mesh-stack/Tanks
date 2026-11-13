using System;

namespace MadMachines.Networking
{
    [Serializable]
    public sealed class ServerSessionInfo
    {
        public const string FreeMode = "free";
        public const string TeamSkirmishMode = "team_skirmish";
        public const string DeathmatchMode = "deathmatch";

        public string location;
        public string mode;
        public int players;
        public int capacity;
        public int secondsLeft;
        public string result;
        public DeathmatchStanding[] standings;

        public string Summary => $"{location}\n{(mode == DeathmatchMode ? "Deathmatch" : ModeName(mode == TeamSkirmishMode) + " · открытая сессия")}\nИгроков: {players}/{capacity}";

        public static string ModeName(bool teamPlay) => teamPlay ? "Командная стычка" : "Свободная езда";
    }

    public enum DeathmatchPlayerStatus { Playing, Respawning, Disconnected, Left }

    [Serializable]
    public sealed class DeathmatchStanding
    {
        public int slot;
        public int kills;
        public int deaths;
        public int health;
        public DeathmatchPlayerStatus status;

        public string Name => $"Игрок {slot + 1}";
    }
}
