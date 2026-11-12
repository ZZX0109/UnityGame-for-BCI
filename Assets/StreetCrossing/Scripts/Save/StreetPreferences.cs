using UnityEngine;

namespace StreetCrossing.Save
{
    public static class StreetPreferences
    {
        private const string Prefix = "StreetCrossing.Settings.";
        public static float Sensitivity { get => Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "Sensitivity", 2), 0.3f, 5); set => PlayerPrefs.SetFloat(Prefix + "Sensitivity", Mathf.Clamp(value, 0.3f, 5)); }
        public static float FieldOfView { get => Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "FOV", 78), 75, 85); set => PlayerPrefs.SetFloat(Prefix + "FOV", Mathf.Clamp(value, 75, 85)); }
        public static float Volume { get => Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Volume", 0.25f)); set => PlayerPrefs.SetFloat(Prefix + "Volume", Mathf.Clamp01(value)); }
        public static bool ComfortMode { get => PlayerPrefs.GetInt(Prefix + "Comfort", 0) != 0; set => PlayerPrefs.SetInt(Prefix + "Comfort", value ? 1 : 0); }
        public static bool FastWalkEnabled { get => PlayerPrefs.GetInt(Prefix + "FastWalk", 1) != 0; set => PlayerPrefs.SetInt(Prefix + "FastWalk", value ? 1 : 0); }
        public static bool LargeText { get => PlayerPrefs.GetInt(Prefix + "LargeText", 0) != 0; set => PlayerPrefs.SetInt(Prefix + "LargeText", value ? 1 : 0); }
        public static int BestStars(string scenarioId) => PlayerPrefs.GetInt("StreetCrossing.Progress." + scenarioId, 0);
        public static void SaveStars(string scenarioId, int stars) { PlayerPrefs.SetInt("StreetCrossing.Progress." + scenarioId, Mathf.Max(stars, BestStars(scenarioId))); PlayerPrefs.Save(); }
        public static void Save() { PlayerPrefs.Save(); }
    }
}
