using System;
using UnityEngine;

namespace ClawMachine
{
    [Serializable]
    public sealed class ClawPreferences
    {
        public const string Key = "ClawMachine.Settings.v1";
        public int version = 1;
        public float volume = 0.25f, textScale = 1;
        public bool positionAssist = true, centerOfMassAssist = true, smooth = true, pipLowQuality = true;

        public static ClawPreferences Decode(string json)
        {
            var result = new ClawPreferences();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, result); }
                catch (ArgumentException) { return new ClawPreferences(); }
            }
            if (result.version != 1) return new ClawPreferences();
            result.volume = Sanitize(result.volume, 0.25f, 0, 1);
            result.textScale = Sanitize(result.textScale, 1, 0.9f, 1.2f);
            return result;
        }
        public static ClawPreferences Load() => Decode(PlayerPrefs.GetString(Key, ""));
        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
        public static float Sanitize(float value, float fallback, float min, float max)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
