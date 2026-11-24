using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class SeatedHandSettingsRuntimeTests
    {
        private string playerPrefsKey;
        private GameObject pairObject;
        private ControllerHandPair pair;
        private FirstPersonWrist leftWrist;
        private FirstPersonWrist rightWrist;
        private Transform leftVisualRoot;
        private Transform rightVisualRoot;

        [SetUp]
        public void SetUp()
        {
            playerPrefsKey = "Neuralife.VirtualHand.Tests." + Guid.NewGuid().ToString("N");
            PlayerPrefs.DeleteKey(playerPrefsKey);

            pairObject = new GameObject("ControllerHandPair test fixture");
            pairObject.SetActive(false);
            pair = pairObject.AddComponent<ControllerHandPair>();
            SetPrivateField(pair, "seatedHandSettingsPlayerPrefsKey", playerPrefsKey);

            leftWrist = CreateWrist("Left wrist", pairObject.transform, out leftVisualRoot);
            rightWrist = CreateWrist("Right wrist", pairObject.transform, out rightVisualRoot);
            pair.leftWrist = leftWrist;
            pair.rightWrist = rightWrist;

            leftWrist.wristPositionOffset = new Vector3(0.01f, -0.02f, 0.03f);
            leftWrist.wristRotationOffsetEuler = new Vector3(1f, 2f, 3f);
            rightWrist.wristPositionOffset = new Vector3(-0.04f, 0.05f, 0.06f);
            rightWrist.wristRotationOffsetEuler = new Vector3(4f, 5f, 6f);
        }

        [TearDown]
        public void TearDown()
        {
            if (pairObject != null)
                UnityEngine.Object.DestroyImmediate(pairObject);

            if (!string.IsNullOrEmpty(playerPrefsKey))
            {
                PlayerPrefs.DeleteKey(playerPrefsKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void MissingPreferencePreservesAuthoredWristOffsetsAndMeasuredDisplay()
        {
            Vector3 authoredLeftPosition = leftWrist.wristPositionOffset;
            Vector3 authoredLeftRotation = leftWrist.wristRotationOffsetEuler;
            Vector3 authoredRightPosition = rightWrist.wristPositionOffset;
            Vector3 authoredRightRotation = rightWrist.wristRotationOffsetEuler;

            pairObject.SetActive(true);

            Assert.AreEqual(authoredLeftPosition, leftWrist.wristPositionOffset);
            Assert.AreEqual(authoredLeftRotation, leftWrist.wristRotationOffsetEuler);
            Assert.AreEqual(authoredRightPosition, rightWrist.wristPositionOffset);
            Assert.AreEqual(authoredRightRotation, rightWrist.wristRotationOffsetEuler);
            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Left));
            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Right));
        }

        [Test]
        public void LoadsSavedOffsetsAndAppliesAnExplicitSaveToTheEnabledPair()
        {
            SeatedHandSettings startupSettings = SeatedHandSettings.CreateDefaults();
            startupSettings.leftWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(0.11f, -0.12f, 0.13f), new Vector3(10f, 20f, 30f));
            startupSettings.rightWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(-0.21f, 0.22f, -0.23f), new Vector3(-10f, 40f, 5f));
            Assert.IsTrue(startupSettings.Save(playerPrefsKey));

            pairObject.SetActive(true);

            Assert.AreEqual(startupSettings.leftWristOffset.translationMeters, leftWrist.wristPositionOffset);
            Assert.AreEqual(startupSettings.leftWristOffset.rotationEulerDegrees, leftWrist.wristRotationOffsetEuler);
            Assert.AreEqual(startupSettings.rightWristOffset.translationMeters, rightWrist.wristPositionOffset);
            Assert.AreEqual(startupSettings.rightWristOffset.rotationEulerDegrees, rightWrist.wristRotationOffsetEuler);

            SeatedHandSettings changedSettings = SeatedHandSettings.CreateDefaults();
            changedSettings.leftWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(0.31f, 0.32f, 0.33f), new Vector3(15f, 25f, 35f));
            changedSettings.rightWristOffset = new SeatedHandSettings.WristOffset(
                new Vector3(-0.41f, -0.42f, 0.43f), new Vector3(45f, 55f, 65f));
            changedSettings.displayMode = SeatedHandDisplayMode.SingleOperatingHand;
            changedSettings.operatingHand = SeatedHandSide.Right;

            Assert.IsTrue(changedSettings.Save(playerPrefsKey));

            Assert.AreEqual(changedSettings.leftWristOffset.translationMeters, leftWrist.wristPositionOffset);
            Assert.AreEqual(changedSettings.leftWristOffset.rotationEulerDegrees, leftWrist.wristRotationOffsetEuler);
            Assert.AreEqual(changedSettings.rightWristOffset.translationMeters, rightWrist.wristPositionOffset);
            Assert.AreEqual(changedSettings.rightWristOffset.rotationEulerDegrees, rightWrist.wristRotationOffsetEuler);
            Assert.IsFalse(ShouldDisplay(pair, SeatedHandSide.Left));
            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Right));
        }

        [Test]
        public void SingleOperatingHandWithUnspecifiedSideDisplaysNeitherHand()
        {
            pairObject.SetActive(true);

            SeatedHandSettings settings = SeatedHandSettings.CreateDefaults();
            settings.displayMode = SeatedHandDisplayMode.SingleOperatingHand;
            settings.operatingHand = SeatedHandSide.Unspecified;
            Assert.IsTrue(settings.Save(playerPrefsKey));

            Assert.IsFalse(ShouldDisplay(pair, SeatedHandSide.Left));
            Assert.IsFalse(ShouldDisplay(pair, SeatedHandSide.Right));
        }

        [Test]
        public void MissingSelectedWristDoesNotFallBackToTheOtherSide()
        {
            pair.rightWrist = null;
            pairObject.SetActive(true);

            SeatedHandSettings settings = SeatedHandSettings.CreateDefaults();
            settings.displayMode = SeatedHandDisplayMode.SingleOperatingHand;
            settings.operatingHand = SeatedHandSide.Right;
            Assert.IsTrue(settings.Save(playerPrefsKey));

            InvokePrivate(pair, "Refresh");

            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Right));
            Assert.IsFalse(ShouldDisplay(pair, SeatedHandSide.Left));
            Assert.IsFalse(leftVisualRoot.gameObject.activeSelf);
            Assert.IsFalse(rightVisualRoot.gameObject.activeSelf);
        }

        [Test]
        public void MirrorModeShowsOnlyTheSelectedMeasuredSourceAndCreatesNoDerivedHand()
        {
            pairObject.SetActive(true);

            SeatedHandSettings settings = SeatedHandSettings.CreateDefaults();
            settings.affectedHand = SeatedHandSide.Right;
            settings.operatingHand = SeatedHandSide.Left;
            settings.displayMode = SeatedHandDisplayMode.Mirror;
            Assert.IsTrue(settings.Save(playerPrefsKey));

            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Left));
            Assert.IsFalse(ShouldDisplay(pair, SeatedHandSide.Right));
        }

        [Test]
        public void NewerStoredSchemaLeavesAuthoredWristOffsetsUntouched()
        {
            Vector3 authoredLeftPosition = leftWrist.wristPositionOffset;
            Vector3 authoredRightPosition = rightWrist.wristPositionOffset;
            PlayerPrefs.SetString(playerPrefsKey, "{\"schemaVersion\":999}");
            PlayerPrefs.Save();

            pairObject.SetActive(true);

            Assert.AreEqual(authoredLeftPosition, leftWrist.wristPositionOffset);
            Assert.AreEqual(authoredRightPosition, rightWrist.wristPositionOffset);
            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Left));
            Assert.IsTrue(ShouldDisplay(pair, SeatedHandSide.Right));
        }

        [Test]
        public void WristOffsetKeepsGripLocalAxesAndDoesNotInheritTrackingOriginScale()
        {
            GameObject trackingSpaceObject = new GameObject("Tracking space");
            GameObject wristObject = new GameObject("Wrist");
            try
            {
                Transform trackingSpace = trackingSpaceObject.transform;
                trackingSpace.position = new Vector3(1f, 2f, 3f);
                trackingSpace.rotation = Quaternion.Euler(0f, 70f, 0f);
                trackingSpace.localScale = new Vector3(3f, 2f, 4f);

                FirstPersonWrist wrist = wristObject.AddComponent<FirstPersonWrist>();
                GameObject visualRootObject = new GameObject("Visual root");
                visualRootObject.transform.SetParent(wristObject.transform, false);
                wrist.trackingSpace = trackingSpace;
                wrist.visualRoot = visualRootObject.transform;
                wrist.wristPositionOffset = new Vector3(0.1f, 0.02f, -0.04f);
                wrist.wristRotationOffsetEuler = new Vector3(5f, -20f, 30f);

                Pose trackedPose = new Pose(
                    new Vector3(0.2f, -0.3f, 0.1f),
                    Quaternion.Euler(20f, 30f, -15f));
                Quaternion gripRotation = trackingSpace.rotation * trackedPose.rotation;
                Vector3 expectedPosition = trackingSpace.TransformPoint(trackedPose.position) +
                                           gripRotation * wrist.wristPositionOffset;
                Quaternion expectedRotation = gripRotation * Quaternion.Euler(wrist.wristRotationOffsetEuler);

                Assert.IsTrue(wrist.ApplyTrackedPose(trackedPose, true));
                Assert.That(Vector3.Distance(expectedPosition, wrist.transform.position), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(expectedRotation, wrist.transform.rotation), Is.LessThan(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(trackingSpaceObject);
                UnityEngine.Object.DestroyImmediate(wristObject);
            }
        }

        private static FirstPersonWrist CreateWrist(string objectName, Transform parent, out Transform visualRoot)
        {
            GameObject wristObject = new GameObject(objectName);
            wristObject.transform.SetParent(parent, false);
            FirstPersonWrist wrist = wristObject.AddComponent<FirstPersonWrist>();

            GameObject visualRootObject = new GameObject(objectName + " visuals");
            visualRootObject.transform.SetParent(wristObject.transform, false);
            wrist.visualRoot = visualRootObject.transform;
            visualRoot = visualRootObject.transform;
            return wrist;
        }

        private static bool ShouldDisplay(ControllerHandPair target, SeatedHandSide side)
        {
            return (bool)InvokePrivate(target, "ShouldDisplayHand", side);
        }

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Expected private method was not found: " + methodName);
            return method.Invoke(target, arguments);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected private field was not found: " + fieldName);
            field.SetValue(target, value);
        }
    }
}
