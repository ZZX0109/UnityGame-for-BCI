// Assets/VirtualHand/Scripts/Runtime/SeatedHandSettings.cs
using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    public enum SeatedHandSide
    {
        Unspecified = 0,
        Left = 1,
        Right = 2
    }

    /// <summary>Requested presentation mode. Mirror is stored as a setting; rendering behavior is host-owned.</summary>
    public enum SeatedHandDisplayMode
    {
        MeasuredHands = 0,
        SingleOperatingHand = 1,
        Mirror = 2
    }

    /// <summary>Local seated-hand preferences. Contains no patient identifiers or live component references.</summary>
    [Serializable]
    public sealed class SeatedHandSettings
    {
        public const int CurrentSchemaVersion = 1;

        internal static event Action<string> PreferencesChanged;

        public int schemaVersion = CurrentSchemaVersion;
        public SeatedHandSide affectedHand = SeatedHandSide.Unspecified;
        public SeatedHandSide operatingHand = SeatedHandSide.Unspecified;
        public SeatedHandDisplayMode displayMode = SeatedHandDisplayMode.MeasuredHands;
        public WristOffset leftWristOffset;
        public WristOffset rightWristOffset;

        [NonSerialized]
        private bool protectNewerStoredVersion;

        internal bool IsProtectedByNewerVersion => protectNewerStoredVersion;

        /// <summary>Offsets consumed by FirstPersonWrist: translation is in meters and rotation is Euler degrees.</summary>
        [Serializable]
        public struct WristOffset
        {
            public Vector3 translationMeters;
            public Vector3 rotationEulerDegrees;

            public WristOffset(Vector3 translationMeters, Vector3 rotationEulerDegrees)
            {
                this.translationMeters = translationMeters;
                this.rotationEulerDegrees = rotationEulerDegrees;
            }
        }

        public SeatedHandSettings()
        {
            ResetToDefaults();
        }

        public static SeatedHandSettings CreateDefaults()
        {
            return new SeatedHandSettings();
        }

        /// <summary>Resets this in-memory model to safe defaults.</summary>
        public void ResetToDefaults()
        {
            schemaVersion = CurrentSchemaVersion;
            affectedHand = SeatedHandSide.Unspecified;
            operatingHand = SeatedHandSide.Unspecified;
            displayMode = SeatedHandDisplayMode.MeasuredHands;
            leftWristOffset = default(WristOffset);
            rightWristOffset = default(WristOffset);
            protectNewerStoredVersion = false;
        }

        /// <summary>Loads one preference key. Missing or invalid data returns defaults without writing preferences.</summary>
        public static SeatedHandSettings Load(string playerPrefsKey)
        {
            ValidateKey(playerPrefsKey);
            if (!PlayerPrefs.HasKey(playerPrefsKey))
                return CreateDefaults();

            string json = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (!LooksLikeJsonObject(json))
                return CreateDefaults();

            SeatedHandSettings parsed;
            try
            {
                parsed = JsonUtility.FromJson<SeatedHandSettings>(json);
            }
            catch (Exception)
            {
                return CreateDefaults();
            }

            if (parsed == null)
                return CreateDefaults();

            // Version zero is treated as a legacy/missing version so older JSON with omitted fields remains harmless.
            if (parsed.schemaVersion > CurrentSchemaVersion)
                return CreateProtectedDefaults();

            if (parsed.schemaVersion < 0 ||
                (parsed.schemaVersion != 0 && parsed.schemaVersion != CurrentSchemaVersion))
                return CreateDefaults();

            if (!parsed.TryCreateSanitizedCopy(out SeatedHandSettings sanitized))
                return CreateDefaults();

            return sanitized;
        }

        /// <summary>Saves this model to exactly the supplied key. Invalid data is rejected without changing preferences.</summary>
        public bool Save(string playerPrefsKey)
        {
            ValidateKey(playerPrefsKey);
            if (protectNewerStoredVersion)
                return false;

            if (!TryCreateSanitizedCopy(out SeatedHandSettings sanitized))
                return false;

            PlayerPrefs.SetString(playerPrefsKey, JsonUtility.ToJson(sanitized));
            PlayerPrefs.Save();
            PreferencesChanged?.Invoke(playerPrefsKey);
            return true;
        }

        /// <summary>Writes defaults to exactly the supplied key, preserving all other PlayerPrefs keys.</summary>
        public static void Reset(string playerPrefsKey)
        {
            ValidateKey(playerPrefsKey);
            PlayerPrefs.SetString(playerPrefsKey, JsonUtility.ToJson(CreateDefaults()));
            PlayerPrefs.Save();
            PreferencesChanged?.Invoke(playerPrefsKey);
        }

        private bool TryCreateSanitizedCopy(out SeatedHandSettings sanitized)
        {
            sanitized = null;

            if ((schemaVersion != 0 && schemaVersion != CurrentSchemaVersion) ||
                !IsValidSide(affectedHand) ||
                !IsValidSide(operatingHand) ||
                !IsValidDisplayMode(displayMode) ||
                !IsFinite(leftWristOffset.translationMeters) ||
                !IsFinite(leftWristOffset.rotationEulerDegrees) ||
                !IsFinite(rightWristOffset.translationMeters) ||
                !IsFinite(rightWristOffset.rotationEulerDegrees))
            {
                return false;
            }

            sanitized = new SeatedHandSettings
            {
                schemaVersion = CurrentSchemaVersion,
                affectedHand = affectedHand,
                operatingHand = operatingHand,
                displayMode = displayMode,
                leftWristOffset = leftWristOffset,
                rightWristOffset = rightWristOffset
            };
            return true;
        }

        private static SeatedHandSettings CreateProtectedDefaults()
        {
            SeatedHandSettings defaults = CreateDefaults();
            defaults.protectNewerStoredVersion = true;
            return defaults;
        }

        private static bool IsValidSide(SeatedHandSide side)
        {
            return side == SeatedHandSide.Unspecified ||
                   side == SeatedHandSide.Left ||
                   side == SeatedHandSide.Right;
        }

        private static bool IsValidDisplayMode(SeatedHandDisplayMode mode)
        {
            return mode == SeatedHandDisplayMode.MeasuredHands ||
                   mode == SeatedHandDisplayMode.SingleOperatingHand ||
                   mode == SeatedHandDisplayMode.Mirror;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool LooksLikeJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            string trimmed = json.Trim();
            return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}';
        }

        private static void ValidateKey(string playerPrefsKey)
        {
            if (string.IsNullOrWhiteSpace(playerPrefsKey))
                throw new ArgumentException("A non-empty PlayerPrefs key is required.", nameof(playerPrefsKey));
        }
    }
}
