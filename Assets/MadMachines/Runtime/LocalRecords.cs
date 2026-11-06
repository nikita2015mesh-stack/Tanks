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
        public static List<float> Read(ChallengeMode mode)
        {
            try
            {
                return JsonUtility.FromJson<Entries>(PlayerPrefs.GetString("MadMachines.Records.v1." + mode, "{}"))?.Values ?? new List<float>();
            }
            catch (ArgumentException)
            {
                return new List<float>();
            }
        }

        public static void Save(ChallengeMode mode, float value)
        {
            List<float> values = Read(mode);
            values.Add(value);
            values.Sort((a, b) => mode == ChallengeMode.Shooting ? b.CompareTo(a) : a.CompareTo(b));
            if (values.Count > 5) values.RemoveRange(5, values.Count - 5);
            PlayerPrefs.SetString("MadMachines.Records.v1." + mode, JsonUtility.ToJson(new Entries { Values = values }));
            PlayerPrefs.Save();
        }
    }
}
