using System;
using System.Collections.Generic;
using UnityEngine;

namespace MadMachines
{
    public static class LocalRecords
    {
        [Serializable]
        private sealed class Entries
        {
            public List<float> Values = new List<float>();
        }
        public static List<float> Read(ChallengeMode mode, string trackId = null)
        {
            try
            {
                return JsonUtility.FromJson<Entries>(PlayerPrefs.GetString(Key(mode, trackId), "{}"))?.Values ?? new List<float>();
            }
            catch (ArgumentException)
            {
                return new List<float>();
            }
        }

        public static void Save(ChallengeMode mode, float value, string trackId = null)
        {
            List<float> values = Read(mode, trackId);
            values.Add(value);
            values.Sort((a, b) => mode == ChallengeMode.Shooting ? b.CompareTo(a) : a.CompareTo(b));
            if (values.Count > 5) values.RemoveRange(5, values.Count - 5);
            PlayerPrefs.SetString(Key(mode, trackId), JsonUtility.ToJson(new Entries { Values = values }));
            PlayerPrefs.Save();
        }

        private static string Key(ChallengeMode mode, string trackId)
        {
            return string.IsNullOrEmpty(trackId) ? "MadMachines.Records.v1." + mode : $"MadMachines.Records.v1.Tracks.{trackId}.{mode}";
        }
    }
}
