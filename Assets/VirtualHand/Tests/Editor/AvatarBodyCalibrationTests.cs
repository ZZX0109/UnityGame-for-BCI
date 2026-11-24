#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class AvatarBodyCalibrationTests
    {
        [Test]
        public void PlayerPrefsSaveLoad_PreservesExplicitMeasurementsAndSolverMath()
        {
            string key = "AvatarBodyCalibrationTest_" + Guid.NewGuid().ToString("N");
            try
            {
                AvatarBodyCalibration calibration = AvatarBodyCalibration.CreatePresentationDefaults();
                calibration.shoulderWidth = new AvatarBodyMeasurement(0.42f, AvatarBodyMeasurementSource.Measured);
                calibration.upperArmLength = new AvatarBodyMeasurement(0.31f, AvatarBodyMeasurementSource.ManualEdit);
                calibration.forearmLength = new AvatarBodyMeasurement(0.25f, AvatarBodyMeasurementSource.Measured);
                calibration.eyeHeightFromPelvis = new AvatarBodyMeasurement(0.65f, AvatarBodyMeasurementSource.ManualEdit);
                calibration.seatedPelvisHeightFromFloor = new AvatarBodyMeasurement(0.50f, AvatarBodyMeasurementSource.Measured);

                Assert.IsTrue(calibration.Save(key, out string saveError), saveError);
                Assert.IsTrue(AvatarBodyCalibration.TryLoad(key, out AvatarBodyCalibration loaded, out string loadError), loadError);
                Assert.AreEqual(0.42f, loaded.shoulderWidth.meters, 0.0001f);
                Assert.AreEqual(AvatarBodyMeasurementSource.Measured, loaded.shoulderWidth.source);
                Assert.AreEqual(AvatarBodyMeasurementSource.ManualEdit, loaded.upperArmLength.source);

                Assert.IsTrue(loaded.TryBuildSolverMeasurements(out AvatarBodySolverMeasurements solver, out string mathError), mathError);
                Assert.AreEqual(0.56f, solver.straightArmChainLengthMeters, 0.0001f);
                Assert.AreEqual(1.15f, solver.eyeHeightFromFloorMeters, 0.0001f);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void NonFiniteAndNonPositiveValues_AreRejectedWithoutReplacingStoredData()
        {
            string key = "AvatarBodyCalibrationTest_" + Guid.NewGuid().ToString("N");
            const string sentinel = "leave-existing-value";
            try
            {
                PlayerPrefs.SetString(key, sentinel);
                AvatarBodyCalibration calibration = AvatarBodyCalibration.CreatePresentationDefaults();

                calibration.upperArmLength = new AvatarBodyMeasurement(float.NaN, AvatarBodyMeasurementSource.ManualEdit);
                Assert.IsFalse(calibration.Validate(out string nanError));
                Assert.IsNotEmpty(nanError);
                Assert.IsFalse(calibration.Save(key, out string nanSaveError));
                Assert.IsNotEmpty(nanSaveError);
                Assert.AreEqual(sentinel, PlayerPrefs.GetString(key));

                calibration.upperArmLength = new AvatarBodyMeasurement(float.PositiveInfinity, AvatarBodyMeasurementSource.ManualEdit);
                Assert.IsFalse(calibration.Validate(out string infinityError));
                Assert.IsNotEmpty(infinityError);

                calibration.upperArmLength = new AvatarBodyMeasurement(0f, AvatarBodyMeasurementSource.ManualEdit);
                Assert.IsFalse(calibration.Validate(out string zeroError));
                Assert.IsNotEmpty(zeroError);
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void NewerSchema_IsPreservedVerbatimAndCannotBeSaved()
        {
            string key = "AvatarBodyCalibrationTest_" + Guid.NewGuid().ToString("N");
            const string futureJson = "{\"schemaVersion\":9,\"futureField\":\"keep me\"}";
            try
            {
                Assert.IsTrue(AvatarBodyCalibration.TryFromJson(futureJson, out AvatarBodyCalibration future, out string parseError), parseError);
                Assert.IsTrue(future.IsProtectedByNewerSchema);
                Assert.AreEqual(futureJson, future.ToJson());
                Assert.IsFalse(future.Save(key, out string saveError));
                Assert.IsNotEmpty(saveError);
                Assert.IsFalse(PlayerPrefs.HasKey(key));
            }
            finally
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }
    }
}
#endif
