using System.Globalization;

namespace MadMachines
{
    internal static class PlaygroundText
    {
        public const string Title = "БЕЗУМНЫЕ МАШИНЫ";
        public const string Subtitle = "ФИЗИЧЕСКИЙ ПОЛИГОН";
        public const string MovingTarget = "200 / ДВИЖЕТСЯ";

        private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

        public static string ModeName(ChallengeMode mode)
        {
            return mode switch
            {
                ChallengeMode.Free => "СВОБОДНАЯ ЕЗДА",
                ChallengeMode.Race => "ГОНКА",
                ChallengeMode.Shooting => "СТРЕЛКОВЫЙ ПОЛИГОН",
                ChallengeMode.Combat => "БОЕВАЯ ТРАССА",
                _ => "ИСПЫТАНИЕ"
            };
        }

        public static string Seconds(float value, string format = "0.00")
        {
            return value.ToString(format, RussianCulture) + " с";
        }
    }
}
