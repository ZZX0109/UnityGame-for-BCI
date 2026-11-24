using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>Identifies how a body distance was entered. Presentation defaults are not individual measurements.</summary>
    public enum AvatarBodyMeasurementSource
    {
        PresentationOnlyDefault = 0,
        Measured = 1,
        ManualEdit = 2
    }

    /// <summary>A positive distance in meters and its explicit origin.</summary>
    [Serializable]
    public struct AvatarBodyMeasurement
    {
        public float meters;
        public AvatarBodyMeasurementSource source;

        public AvatarBodyMeasurement(float meters, AvatarBodyMeasurementSource source)
        {
            this.meters = meters;
            this.source = source;
        }
    }

    /// <summary>Validated distances prepared for a seated full-body solver. All values are in meters.</summary>
    [Serializable]
    public struct AvatarBodySolverMeasurements
    {
        public float shoulderWidthMeters;
        public float halfShoulderWidthMeters;
        public float upperArmLengthMeters;
        public float forearmLengthMeters;
        public float straightArmChainLengthMeters;
        public float torsoHeightMeters;
        public float eyeHeightFromPelvisMeters;
        public float seatedPelvisHeightFromFloorMeters;
        public float eyeHeightFromFloorMeters;
    }

    /// <summary>
    /// Geometry-only calibration for a seated avatar. Distances are explicit measurements or manual edits;
    /// no arm length is inferred from wrist samples, and this model contains no clinical or hand-preference data.
    /// </summary>
    [Serializable]
    public sealed class AvatarBodyCalibration
    {
        public const int CurrentSchemaVersion = 1;

        // Generic avatar presentation placeholders only. They are not measurements or estimates for any person.
        public const float PresentationDefaultShoulderWidthMeters = 0.40f;
        public const float PresentationDefaultUpperArmLengthMeters = 0.30f;
        public const float PresentationDefaultForearmLengthMeters = 0.26f;
        public const float PresentationDefaultTorsoHeightMeters = 0.52f;
        public const float PresentationDefaultEyeHeightFromPelvisMeters = 0.64f;
        public const float PresentationDefaultSeatedPelvisHeightFromFloorMeters = 0.48f;

        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>Distance between left and right shoulder-joint centers.</summary>
        public AvatarBodyMeasurement shoulderWidth;

        /// <summary>Shoulder-center to elbow-center distance.</summary>
        public AvatarBodyMeasurement upperArmLength;

        /// <summary>Elbow-center to wrist-center distance.</summary>
        public AvatarBodyMeasurement forearmLength;

        /// <summary>Vertical distance from seated pelvis center to the shoulder line.</summary>
        public AvatarBodyMeasurement torsoHeight;

        /// <summary>Vertical distance from seated pelvis center to the eye midpoint.</summary>
        public AvatarBodyMeasurement eyeHeightFromPelvis;

        /// <summary>Vertical distance from the floor plane to seated pelvis center.</summary>
        public AvatarBodyMeasurement seatedPelvisHeightFromFloor;

        [NonSerialized]
        private bool protectNewerStoredSchema;

        [NonSerialized]
        private string preservedNewerSchemaJson;

        /// <summary>True when loaded data uses a newer schema and must remain untouched.</summary>
        public bool IsProtectedByNewerSchema => protectNewerStoredSchema;

        public AvatarBodyCalibration()
        {
            ResetToPresentationDefaults();
        }

        public static AvatarBodyCalibration CreatePresentationDefaults()
        {
            return new AvatarBodyCalibration();
        }

        /// <summary>Resets values to generic presentation placeholders, explicitly marked as such.</summary>
        public void ResetToPresentationDefaults()
        {
            schemaVersion = CurrentSchemaVersion;
            shoulderWidth = PresentationDefault(PresentationDefaultShoulderWidthMeters);
            upperArmLength = PresentationDefault(PresentationDefaultUpperArmLengthMeters);
            forearmLength = PresentationDefault(PresentationDefaultForearmLengthMeters);
            torsoHeight = PresentationDefault(PresentationDefaultTorsoHeightMeters);
            eyeHeightFromPelvis = PresentationDefault(PresentationDefaultEyeHeightFromPelvisMeters);
            seatedPelvisHeightFromFloor = PresentationDefault(PresentationDefaultSeatedPelvisHeightFromFloorMeters);
            protectNewerStoredSchema = false;
            preservedNewerSchemaJson = null;
        }

        /// <summary>Validates schema, provenance, and finite positive distances without changing this instance.</summary>
        public bool Validate(out string error)
        {
            error = null;

            if (protectNewerStoredSchema)
            {
                error = "This calibration was loaded from a newer schema and is protected from edits.";
                return false;
            }

            if (schemaVersion != 0 && schemaVersion != CurrentSchemaVersion)
            {
                error = "Unsupported avatar body calibration schema version: " + schemaVersion + ".";
                return false;
            }

            if (!ValidateMeasurement(shoulderWidth, "Shoulder width", out error) ||
                !ValidateMeasurement(upperArmLength, "Upper arm length", out error) ||
                !ValidateMeasurement(forearmLength, "Forearm length", out error) ||
                !ValidateMeasurement(torsoHeight, "Torso height", out error) ||
                !ValidateMeasurement(eyeHeightFromPelvis, "Eye height from pelvis", out error) ||
                !ValidateMeasurement(seatedPelvisHeightFromFloor, "Seated pelvis height from floor", out error))
            {
                return false;
            }

            if (!IsFinite(upperArmLength.meters + forearmLength.meters))
            {
                error = "The combined arm segment length must be finite.";
                return false;
            }

            if (!IsFinite(eyeHeightFromPelvis.meters + seatedPelvisHeightFromFloor.meters))
            {
                error = "The derived eye height from floor must be finite.";
                return false;
            }

            return true;
        }

        /// <summary>Creates a current-schema copy after validation. No distances are estimated or clamped.</summary>
        public bool TryNormalize(out AvatarBodyCalibration normalized, out string error)
        {
            normalized = null;
            if (!Validate(out error))
                return false;

            normalized = new AvatarBodyCalibration
            {
                schemaVersion = CurrentSchemaVersion,
                shoulderWidth = shoulderWidth,
                upperArmLength = upperArmLength,
                forearmLength = forearmLength,
                torsoHeight = torsoHeight,
                eyeHeightFromPelvis = eyeHeightFromPelvis,
                seatedPelvisHeightFromFloor = seatedPelvisHeightFromFloor
            };
            return true;
        }

        /// <summary>Normalizes this instance to the current schema after validation.</summary>
        public bool Normalize(out string error)
        {
            if (!TryNormalize(out AvatarBodyCalibration normalized, out error))
                return false;

            schemaVersion = normalized.schemaVersion;
            shoulderWidth = normalized.shoulderWidth;
            upperArmLength = normalized.upperArmLength;
            forearmLength = normalized.forearmLength;
            torsoHeight = normalized.torsoHeight;
            eyeHeightFromPelvis = normalized.eyeHeightFromPelvis;
            seatedPelvisHeightFromFloor = normalized.seatedPelvisHeightFromFloor;
            return true;
        }

        /// <summary>Builds solver distances using only supplied measurements and direct sums of those measurements.</summary>
        public bool TryBuildSolverMeasurements(out AvatarBodySolverMeasurements measurements, out string error)
        {
            return AvatarBodyCalibrationMath.TryBuildSolverMeasurements(this, out measurements, out error);
        }

        /// <summary>Serializes validated current-schema data. A loaded newer schema is returned byte-for-byte as read.</summary>
        public bool TryToJson(out string json, out string error)
        {
            json = null;
            if (protectNewerStoredSchema)
            {
                if (preservedNewerSchemaJson == null)
                {
                    error = "The newer schema JSON is unavailable.";
                    return false;
                }

                json = preservedNewerSchemaJson;
                error = null;
                return true;
            }

            if (!TryNormalize(out AvatarBodyCalibration normalized, out error))
                return false;

            json = JsonUtility.ToJson(normalized);
            return true;
        }

        /// <summary>Serializes validated data, throwing InvalidOperationException when validation fails.</summary>
        public string ToJson()
        {
            if (!TryToJson(out string json, out string error))
                throw new InvalidOperationException(error);
            return json;
        }

        /// <summary>Parses supported schemas; newer schemas are retained verbatim and returned as protected instances.</summary>
        public static bool TryFromJson(string json, out AvatarBodyCalibration calibration, out string error)
        {
            calibration = null;
            error = null;

            if (!LooksLikeJsonObject(json))
            {
                error = "Calibration JSON must be a JSON object.";
                return false;
            }

            AvatarBodyCalibration parsed;
            try
            {
                parsed = JsonUtility.FromJson<AvatarBodyCalibration>(json);
            }
            catch (Exception exception)
            {
                error = "Calibration JSON could not be parsed: " + exception.Message;
                return false;
            }

            if (parsed == null)
            {
                error = "Calibration JSON did not contain an object.";
                return false;
            }

            if (parsed.schemaVersion > CurrentSchemaVersion)
            {
                calibration = CreatePresentationDefaults();
                calibration.schemaVersion = parsed.schemaVersion;
                calibration.protectNewerStoredSchema = true;
                calibration.preservedNewerSchemaJson = json;
                return true;
            }

            if (parsed.schemaVersion < 0)
            {
                error = "Calibration schema version cannot be negative.";
                return false;
            }

            parsed.protectNewerStoredSchema = false;
            parsed.preservedNewerSchemaJson = null;
            if (!parsed.Validate(out error))
                return false;

            calibration = parsed;
            return true;
        }

        /// <summary>Saves validated data to one PlayerPrefs key. Protected newer-schema data is never overwritten.</summary>
        public bool Save(string playerPrefsKey, out string error)
        {
            if (string.IsNullOrWhiteSpace(playerPrefsKey))
            {
                error = "A non-empty PlayerPrefs key is required.";
                return false;
            }

            if (protectNewerStoredSchema)
            {
                error = "Calibration loaded from a newer schema cannot be overwritten.";
                return false;
            }

            if (!TryToJson(out string json, out error))
                return false;

            PlayerPrefs.SetString(playerPrefsKey, json);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Loads a key. A missing key returns presentation defaults; invalid data returns false and defaults.</summary>
        public static bool TryLoad(string playerPrefsKey, out AvatarBodyCalibration calibration, out string error)
        {
            calibration = CreatePresentationDefaults();
            error = null;

            if (string.IsNullOrWhiteSpace(playerPrefsKey))
            {
                error = "A non-empty PlayerPrefs key is required.";
                return false;
            }

            if (!PlayerPrefs.HasKey(playerPrefsKey))
                return true;

            string json = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            if (!TryFromJson(json, out AvatarBodyCalibration parsed, out error))
                return false;

            calibration = parsed;
            return true;
        }

        /// <summary>Loads a key, falling back to presentation defaults when the key is missing or invalid.</summary>
        public static AvatarBodyCalibration Load(string playerPrefsKey)
        {
            if (TryLoad(playerPrefsKey, out AvatarBodyCalibration calibration, out string error))
                return calibration;
            return CreatePresentationDefaults();
        }

        private static AvatarBodyMeasurement PresentationDefault(float meters)
        {
            return new AvatarBodyMeasurement(meters, AvatarBodyMeasurementSource.PresentationOnlyDefault);
        }

        private static bool ValidateMeasurement(AvatarBodyMeasurement measurement, string label, out string error)
        {
            if (!IsFinite(measurement.meters))
            {
                error = label + " must be finite.";
                return false;
            }

            if (measurement.meters <= 0f)
            {
                error = label + " must be greater than zero meters.";
                return false;
            }

            if (!IsValidSource(measurement.source))
            {
                error = label + " has an unsupported measurement source.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsValidSource(AvatarBodyMeasurementSource source)
        {
            return source == AvatarBodyMeasurementSource.PresentationOnlyDefault ||
                   source == AvatarBodyMeasurementSource.Measured ||
                   source == AvatarBodyMeasurementSource.ManualEdit;
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
    }

    /// <summary>Pure composition of explicit body distances into solver-ready seated-avatar geometry.</summary>
    public static class AvatarBodyCalibrationMath
    {
        public static bool TryBuildSolverMeasurements(
            AvatarBodyCalibration calibration,
            out AvatarBodySolverMeasurements measurements,
            out string error)
        {
            measurements = default(AvatarBodySolverMeasurements);
            error = null;

            if (calibration == null)
            {
                error = "Avatar body calibration is required.";
                return false;
            }

            if (!calibration.Validate(out error))
                return false;

            float armChainLength = calibration.upperArmLength.meters + calibration.forearmLength.meters;
            float eyeHeightFromFloor = calibration.seatedPelvisHeightFromFloor.meters + calibration.eyeHeightFromPelvis.meters;

            measurements = new AvatarBodySolverMeasurements
            {
                shoulderWidthMeters = calibration.shoulderWidth.meters,
                halfShoulderWidthMeters = calibration.shoulderWidth.meters * 0.5f,
                upperArmLengthMeters = calibration.upperArmLength.meters,
                forearmLengthMeters = calibration.forearmLength.meters,
                straightArmChainLengthMeters = armChainLength,
                torsoHeightMeters = calibration.torsoHeight.meters,
                eyeHeightFromPelvisMeters = calibration.eyeHeightFromPelvis.meters,
                seatedPelvisHeightFromFloorMeters = calibration.seatedPelvisHeightFromFloor.meters,
                eyeHeightFromFloorMeters = eyeHeightFromFloor
            };
            return true;
        }
    }
}
