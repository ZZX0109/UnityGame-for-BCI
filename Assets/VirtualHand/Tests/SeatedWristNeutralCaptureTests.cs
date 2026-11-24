using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class SeatedWristNeutralCaptureTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();
        private GameObject hostObject;
        private string playerPrefsKey;
        private SeatedHandSettingsPresenter presenter;
        private SeatedWristNeutralCapture capture;
        private Transform trackingOrigin;

        [SetUp]
        public void SetUp()
        {
            playerPrefsKey = "Neuralife.VirtualHand.SeatedWristNeutralCaptureTests." + Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(playerPrefsKey, "unchanged");

            hostObject = CreateObject("CaptureTestHost");
            presenter = hostObject.AddComponent<SeatedHandSettingsPresenter>();
            SetPrivateField(presenter, "playerPrefsKey", playerPrefsKey);
            presenter.Open();

            capture = hostObject.AddComponent<SeatedWristNeutralCapture>();
            SetPrivateField(capture, "presenter", presenter);

            GameObject originObject = CreateObject("TrackingOrigin");
            trackingOrigin = originObject.transform;
            trackingOrigin.SetPositionAndRotation(
                new Vector3(5f, 2f, -1f),
                Quaternion.Euler(0f, 90f, 0f));
            SetPrivateField(capture, "trackingOrigin", trackingOrigin);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
            PlayerPrefs.DeleteKey(playerPrefsKey);
        }

        [Test]
        public void CapturesLeftIntoDraftAndCancelLeavesPreferencesUntouched()
        {
            Pose gripTrackingLocal = new Pose(
                new Vector3(0.2f, 0.8f, -0.1f),
                Quaternion.Euler(10f, 25f, -5f));
            Pose desiredTrackingLocal = new Pose(
                new Vector3(0.35f, 0.92f, 0.04f),
                Quaternion.Euler(-4f, 38f, 12f));
            Transform leftAnchor = CreateWorldAnchor("LeftDesiredWrist", desiredTrackingLocal);
            SetPrivateField(capture, "leftDesiredWristAnchor", leftAnchor);

            Assert.IsTrue(SeatedWristCalibration.TryCalculateOffset(
                gripTrackingLocal, true, desiredTrackingLocal,
                out SeatedHandSettings.WristOffset expected));
            Assert.IsTrue(capture.CaptureFromTrackingLocalSample(
                SeatedHandSide.Left, gripTrackingLocal, true));

            SeatedHandSettings draft = GetPrivateField<SeatedHandSettings>(presenter, "draft");
            AssertVectorApproximately(expected.translationMeters, draft.leftWristOffset.translationMeters);
            AssertVectorApproximately(expected.rotationEulerDegrees, draft.leftWristOffset.rotationEulerDegrees);
            Assert.AreEqual("unchanged", PlayerPrefs.GetString(playerPrefsKey));

            presenter.Cancel();
            Assert.AreEqual("unchanged", PlayerPrefs.GetString(playerPrefsKey));
        }

        [Test]
        public void CapturesRightWithoutRequiringALeftAnchor()
        {
            Pose gripTrackingLocal = new Pose(
                new Vector3(-0.1f, 0.7f, 0.2f),
                Quaternion.Euler(2f, -15f, 8f));
            Pose desiredTrackingLocal = new Pose(
                new Vector3(-0.25f, 0.9f, 0.12f),
                Quaternion.Euler(5f, -2f, 3f));
            Transform rightAnchor = CreateWorldAnchor("RightDesiredWrist", desiredTrackingLocal);
            SetPrivateField(capture, "rightDesiredWristAnchor", rightAnchor);

            Assert.IsTrue(capture.CaptureFromTrackingLocalSample(
                SeatedHandSide.Right, gripTrackingLocal, true));

            SeatedHandSettings draft = GetPrivateField<SeatedHandSettings>(presenter, "draft");
            AssertVectorApproximately(Vector3.zero, draft.leftWristOffset.translationMeters);
            AssertVectorApproximately(Vector3.zero, draft.leftWristOffset.rotationEulerDegrees);
            Assert.IsFalse(draft.rightWristOffset.translationMeters == Vector3.zero &&
                           draft.rightWristOffset.rotationEulerDegrees == Vector3.zero);
            Assert.AreEqual("unchanged", PlayerPrefs.GetString(playerPrefsKey));
        }

        [Test]
        public void LostTrackingReportsErrorAndDoesNotChangeDraft()
        {
            Transform leftAnchor = CreateWorldAnchor("LeftDesiredWrist", new Pose(Vector3.one, Quaternion.identity));
            SetPrivateField(capture, "leftDesiredWristAnchor", leftAnchor);

            Assert.IsFalse(capture.CaptureFromTrackingLocalSample(
                SeatedHandSide.Left, Pose.identity, false));

            SeatedHandSettings draft = GetPrivateField<SeatedHandSettings>(presenter, "draft");
            AssertVectorApproximately(Vector3.zero, draft.leftWristOffset.translationMeters);
            string status = GetPrivateField<string>(presenter, "statusMessage");
            StringAssert.Contains("已失去追踪", status);
            Assert.AreEqual("unchanged", PlayerPrefs.GetString(playerPrefsKey));
        }

        private Transform CreateWorldAnchor(string name, Pose trackingLocalPose)
        {
            GameObject anchorObject = CreateObject(name);
            anchorObject.transform.SetPositionAndRotation(
                trackingOrigin.TransformPoint(trackingLocalPose.position),
                trackingOrigin.rotation * trackingLocalPose.rotation);
            return anchorObject.transform;
        }

        private GameObject CreateObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            return gameObject;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + fieldName);
            return (T)field.GetValue(target);
        }

        private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.001f));
        }
    }
}
