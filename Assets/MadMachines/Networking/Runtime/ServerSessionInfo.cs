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

        public string Summary => $"{location}\n{(mode == DeathmatchMode ? "Deathmatch · проверка входа" : ModeName(mode == TeamSkirmishMode) + " · открытая сессия")}\nИгроков: {players}/{capacity}";

        public static string ModeName(bool teamPlay) => teamPlay ? "Командная стычка" : "Свободная езда";
    }
}
