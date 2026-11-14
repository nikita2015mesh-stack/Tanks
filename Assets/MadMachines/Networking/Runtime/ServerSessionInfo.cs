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
        public int maxHealth;
        public string result;
        public DeathmatchStanding[] standings;

        // Earlier servers send only each player's remaining health.
        internal int DeathmatchMaxHealth => maxHealth > 0 ? maxHealth : 300;

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
        public bool isBot;

        public string Name => isBot ? "Бот" : $"Игрок {slot + 1}";
    }
}
