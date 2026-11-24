// Assets/VirtualHand/Tests/Editor/SeatedHandSettingsTests.cs
using System;
using Neuralife.VirtualHand;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class SeatedHandSettingsTests
    {
        private string key;
        private string unrelatedKey;

        [SetUp]
        public void SetUp()
        {
            key = "SeatedHandSettingsTests." + Guid.NewGuid().ToString("N");
            unrelatedKey = key + ".unrelated";
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.DeleteKey(unrelatedKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.DeleteKey(unrelatedKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void FreshDefaultsAreUnspecifiedMeasuredAndZeroed()
        {
            AssertDefaults(SeatedHandSettings.CreateDefaults());
        }

        [Test]
        public void SaveAndLoadRoundTripKeepSidesOffsetsAndDisplayModeSeparate()
        {
            SeatedHandSettings input = SeatedHandSettings.CreateDefaults();
            input.affectedHand = SeatedHandSide.Left;
            input.operatingHand = SeatedHandSide.Right;
            input.displayMode = SeatedHandDisplayMode.SingleOperatingHand;
            input.leftWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(0.012f, -0.025f, 0.031f), new Vector3(2f, 15f, -4f));
            input.rightWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(-0.041f, 0.008f, 0.019f), new Vector3(-7f, 3f, 22f));

            Assert.IsTrue(input.Save(key));
            SeatedHandSettings loaded = SeatedHandSettings.Load(key);

            Assert.AreNotSame(input, loaded);
            Assert.AreEqual(SeatedHandSide.Left, loaded.affectedHand);
            Assert.AreEqual(SeatedHandSide.Right, loaded.operatingHand);
            Assert.AreEqual(SeatedHandDisplayMode.SingleOperatingHand, loaded.displayMode);
            AssertVector(input.leftWristOffset.translationMeters, loaded.leftWristOffset.translationMeters);
            AssertVector(input.leftWristOffset.rotationEulerDegrees, loaded.leftWristOffset.rotationEulerDegrees);
            AssertVector(input.rightWristOffset.translationMeters, loaded.rightWristOffset.translationMeters);
            AssertVector(input.rightWristOffset.rotationEulerDegrees, loaded.rightWristOffset.rotationEulerDegrees);

            input.displayMode = SeatedHandDisplayMode.Mirror;
            Assert.IsTrue(input.Save(key));
            Assert.AreEqual(SeatedHandDisplayMode.Mirror, SeatedHandSettings.Load(key).displayMode);
        }

        [Test]
        public void MissingJsonFieldsUseHarmlessDefaults()
        {
            PlayerPrefs.SetString(key,
                "{\"schemaVersion\":1,\"leftWristOffset\":{\"translationMeters\":{\"x\":0.12}}}");

            SeatedHandSettings loaded = SeatedHandSettings.Load(key);

            Assert.AreEqual(SeatedHandSide.Unspecified, loaded.affectedHand);
            Assert.AreEqual(SeatedHandSide.Unspecified, loaded.operatingHand);
            Assert.AreEqual(SeatedHandDisplayMode.MeasuredHands, loaded.displayMode);
            Assert.That(loaded.leftWristOffset.translationMeters.x, Is.EqualTo(0.12f).Within(0.00001f));
            Assert.AreEqual(Vector3.zero, new Vector3(
                loaded.leftWristOffset.translationMeters.y,
                loaded.leftWristOffset.translationMeters.z,
                loaded.leftWristOffset.rotationEulerDegrees.x));
            Assert.AreEqual(default(SeatedHandSettings.WristOffset), loaded.rightWristOffset);
        }

        [Test]
        public void CorruptJsonReturnsDefaultsWithoutRewritingStoredValue()
        {
            const string corrupt = "{ malformed";
            PlayerPrefs.SetString(key, corrupt);

            AssertDefaults(SeatedHandSettings.Load(key));
            Assert.AreEqual(corrupt, PlayerPrefs.GetString(key));
        }

        [Test]
        public void InvalidEnumValuesReturnDefaultsWithoutRewritingStoredValue()
        {
            const string invalid = "{\"schemaVersion\":1,\"affectedHand\":99}";
            PlayerPrefs.SetString(key, invalid);

            AssertDefaults(SeatedHandSettings.Load(key));
            Assert.AreEqual(invalid, PlayerPrefs.GetString(key));
        }

        [Test]
        public void SaveRejectsNonFiniteValuesInvalidEnumsAndUnsupportedVersions()
        {
            SeatedHandSettings existing = SeatedHandSettings.CreateDefaults();
            existing.displayMode = SeatedHandDisplayMode.SingleOperatingHand;
            Assert.IsTrue(existing.Save(key));
            string originalJson = PlayerPrefs.GetString(key);

            SeatedHandSettings invalid = SeatedHandSettings.CreateDefaults();
            invalid.leftWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(float.NaN, 0f, 0f), Vector3.zero);
            Assert.IsFalse(invalid.Save(key));
            Assert.IsTrue(float.IsNaN(invalid.leftWristOffset.translationMeters.x));

            invalid.leftWristOffset = default(SeatedHandSettings.WristOffset);
            invalid.affectedHand = (SeatedHandSide)99;
            Assert.IsFalse(invalid.Save(key));

            invalid.affectedHand = SeatedHandSide.Unspecified;
            invalid.displayMode = (SeatedHandDisplayMode)99;
            Assert.IsFalse(invalid.Save(key));

            invalid.displayMode = SeatedHandDisplayMode.MeasuredHands;
            invalid.schemaVersion = -1;
            Assert.IsFalse(invalid.Save(key));
            Assert.AreEqual(originalJson, PlayerPrefs.GetString(key));
        }

        [Test]
        public void FutureVersionLoadsDefaultsAndCannotBeOverwrittenBySavingLoadedResult()
        {
            const string future = "{\"schemaVersion\":27,\"displayMode\":2}";
            PlayerPrefs.SetString(key, future);

            SeatedHandSettings loaded = SeatedHandSettings.Load(key);

            AssertDefaults(loaded);
            Assert.IsFalse(loaded.Save(key));
            Assert.AreEqual(future, PlayerPrefs.GetString(key));
        }

        [Test]
        public void ResetChangesOnlyTheRequestedPreferenceKey()
        {
            PlayerPrefs.SetString(key, "{\"schemaVersion\":1,\"displayMode\":2}");
            PlayerPrefs.SetString(unrelatedKey, "leave-this-value");

            SeatedHandSettings.Reset(key);

            AssertDefaults(SeatedHandSettings.Load(key));
            Assert.AreEqual("leave-this-value", PlayerPrefs.GetString(unrelatedKey));
        }

        [Test]
        public void LoadingSettingsDoesNotMutateALiveWrist()
        {
            SeatedHandSettings saved = SeatedHandSettings.CreateDefaults();
            saved.leftWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(0.01f, 0.02f, 0.03f), new Vector3(1f, 2f, 3f));
            Assert.IsTrue(saved.Save(key));

            GameObject wristObject = new GameObject("SettingsLoadWristTest");
            try
            {
                FirstPersonWrist wrist = wristObject.AddComponent<FirstPersonWrist>();
                wrist.wristPositionOffset = new Vector3(0.4f, 0.5f, 0.6f);
                wrist.wristRotationOffsetEuler = new Vector3(10f, 20f, 30f);
                Vector3 positionBefore = wrist.wristPositionOffset;
                Vector3 rotationBefore = wrist.wristRotationOffsetEuler;

                SeatedHandSettings loaded = SeatedHandSettings.Load(key);

                AssertVector(saved.leftWristOffset.translationMeters, loaded.leftWristOffset.translationMeters);
                AssertVector(positionBefore, wrist.wristPositionOffset);
                AssertVector(rotationBefore, wrist.wristRotationOffsetEuler);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wristObject);
            }
        }

        private static void AssertDefaults(SeatedHandSettings settings)
        {
            Assert.AreEqual(SeatedHandSettings.CurrentSchemaVersion, settings.schemaVersion);
            Assert.AreEqual(SeatedHandSide.Unspecified, settings.affectedHand);
            Assert.AreEqual(SeatedHandSide.Unspecified, settings.operatingHand);
            Assert.AreEqual(SeatedHandDisplayMode.MeasuredHands, settings.displayMode);
            Assert.AreEqual(default(SeatedHandSettings.WristOffset), settings.leftWristOffset);
            Assert.AreEqual(default(SeatedHandSettings.WristOffset), settings.rightWristOffset);
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.00001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.00001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.00001f));
        }
    }
}
