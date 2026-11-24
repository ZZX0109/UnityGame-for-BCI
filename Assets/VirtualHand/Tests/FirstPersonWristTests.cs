using System.Collections.Generic;
using Neuralife.VirtualHand;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class FirstPersonWristTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                    Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void MapsPoseThroughRotatedOriginIndependentOfMountParentAndPreservesScale()
        {
            Transform tracking = CreateObject("TrackingSpace").transform;
            tracking.SetPositionAndRotation(new Vector3(4f, -1f, 2f), Quaternion.Euler(10f, 35f, -5f));

            Transform mountParent = CreateObject("MountParent").transform;
            mountParent.SetPositionAndRotation(new Vector3(-3f, 2f, 5f), Quaternion.Euler(-15f, 70f, 8f));
            GameObject mount = CreateObject("Mount");
            mount.transform.SetParent(mountParent, false);
            mount.transform.localScale = new Vector3(1.5f, 0.75f, 2f);

            GameObject visual = CreateObject("VisualRoot");
            visual.transform.SetParent(mount.transform, false);
            visual.transform.localScale = new Vector3(0.8f, 1.1f, 1.3f);

            FirstPersonWrist wrist = CreateWrist(mount, tracking, visual.transform);
            Pose pose = new Pose(new Vector3(0.2f, 0.4f, -0.3f), Quaternion.Euler(8f, -20f, 11f));
            Vector3 expectedPosition = tracking.TransformPoint(pose.position);
            Quaternion expectedRotation = tracking.rotation * pose.rotation;
            Vector3 mountScale = mount.transform.localScale;
            Vector3 visualScale = visual.transform.localScale;

            Assert.That(wrist.ApplyTrackedPose(pose, true), Is.True);
            Assert.That(Vector3.Distance(mount.transform.position, expectedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(mount.transform.rotation, expectedRotation), Is.LessThan(0.001f));
            Assert.That(mount.transform.localScale, Is.EqualTo(mountScale));
            Assert.That(visual.transform.localScale, Is.EqualTo(visualScale));
            Assert.That(visual.activeSelf, Is.True);
        }

        [Test]
        public void AppliesGripSpaceOffsetsAndFollowsTrackingSpaceRecentering()
        {
            Transform tracking = CreateObject("TrackingSpace").transform;
            tracking.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
            GameObject mount = CreateObject("Mount");
            GameObject visual = CreateObject("VisualRoot");
            visual.transform.SetParent(mount.transform, false);
            FirstPersonWrist wrist = CreateWrist(mount, tracking, visual.transform);
            wrist.wristPositionOffset = new Vector3(0.2f, 0.05f, -0.1f);
            wrist.wristRotationOffsetEuler = new Vector3(5f, 15f, -3f);
            Pose pose = new Pose(new Vector3(0.1f, 0.3f, -0.2f), Quaternion.Euler(0f, 20f, 0f));

            Assert.That(wrist.ApplyTrackedPose(pose, true), Is.True);
            Vector3 firstPosition = tracking.TransformPoint(pose.position) + (tracking.rotation * pose.rotation) * wrist.wristPositionOffset;
            Quaternion firstRotation = tracking.rotation * pose.rotation * Quaternion.Euler(wrist.wristRotationOffsetEuler);
            Assert.That(Vector3.Distance(mount.transform.position, firstPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(mount.transform.rotation, firstRotation), Is.LessThan(0.001f));

            tracking.SetPositionAndRotation(new Vector3(5f, -2f, 1f), Quaternion.Euler(0f, -30f, 0f));
            Assert.That(wrist.ApplyTrackedPose(pose, true), Is.True);
            Vector3 recenteredPosition = tracking.TransformPoint(pose.position) + (tracking.rotation * pose.rotation) * wrist.wristPositionOffset;
            Quaternion recenteredRotation = tracking.rotation * pose.rotation * Quaternion.Euler(wrist.wristRotationOffsetEuler);
            Assert.That(Vector3.Distance(mount.transform.position, recenteredPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(mount.transform.rotation, recenteredRotation), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(mount.transform.position, firstPosition), Is.GreaterThan(0.5f));
        }

        [Test]
        public void HidesOnLossOrInvalidPoseAndShowsAgainOnRecovery()
        {
            Transform tracking = CreateObject("TrackingSpace").transform;
            GameObject mount = CreateObject("Mount");
            GameObject visual = CreateObject("VisualRoot");
            visual.transform.SetParent(mount.transform, false);
            FirstPersonWrist wrist = CreateWrist(mount, tracking, visual.transform);
            Pose validPose = new Pose(new Vector3(0.1f, 0.2f, 0.3f), Quaternion.identity);

            Assert.That(wrist.ApplyTrackedPose(validPose, true), Is.True);
            Assert.That(visual.activeSelf, Is.True);

            Assert.That(wrist.ApplyTrackedPose(validPose, false), Is.False);
            Assert.That(visual.activeSelf, Is.False);

            Assert.That(wrist.ApplyTrackedPose(validPose, true), Is.True);
            Assert.That(visual.activeSelf, Is.True);

            Pose nonFinitePose = new Pose(new Vector3(float.NaN, 0f, 0f), Quaternion.identity);
            Assert.That(wrist.ApplyTrackedPose(nonFinitePose, true), Is.False);
            Assert.That(visual.activeSelf, Is.False);

            Pose zeroRotationPose = new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f));
            Assert.That(wrist.ApplyTrackedPose(zeroRotationPose, true), Is.False);
            Assert.That(visual.activeSelf, Is.False);

            Assert.That(wrist.ApplyTrackedPose(validPose, true), Is.True);
            Assert.That(visual.activeSelf, Is.True);
        }

        [Test]
        public void AcceptsAncestorTrackingSpaceRejectsSelfAndDescendantsAndKeepsHandsIndependent()
        {
            Transform tracking = CreateObject("TrackingSpace").transform;
            GameObject leftMount = CreateObject("LeftMount");
            GameObject rightMount = CreateObject("RightMount");
            leftMount.transform.SetParent(tracking, false);
            rightMount.transform.SetParent(tracking, false);

            GameObject leftVisual = CreateObject("LeftVisual");
            GameObject rightVisual = CreateObject("RightVisual");
            leftVisual.transform.SetParent(leftMount.transform, false);
            rightVisual.transform.SetParent(rightMount.transform, false);
            FirstPersonWrist left = CreateWrist(leftMount, tracking, leftVisual.transform);
            FirstPersonWrist right = CreateWrist(rightMount, tracking, rightVisual.transform);

            Pose leftPose = new Pose(new Vector3(-0.25f, 0.1f, 0.2f), Quaternion.identity);
            Pose rightPose = new Pose(new Vector3(0.4f, -0.1f, 0.15f), Quaternion.identity);
            Assert.That(left.ApplyTrackedPose(leftPose, true), Is.True);
            Assert.That(right.ApplyTrackedPose(rightPose, true), Is.True);
            Assert.That(leftVisual.activeSelf, Is.True);
            Assert.That(rightVisual.activeSelf, Is.True);
            Assert.That(leftMount.transform.position.x, Is.LessThan(rightMount.transform.position.x));

            left.trackingSpace = leftMount.transform;
            Assert.That(left.ApplyTrackedPose(leftPose, true), Is.False);
            Assert.That(leftMount.activeSelf, Is.True);
            Assert.That(leftVisual.activeSelf, Is.False);

            GameObject descendantSpace = CreateObject("DescendantSpace");
            descendantSpace.transform.SetParent(leftMount.transform, false);
            left.trackingSpace = descendantSpace.transform;
            Assert.That(left.ApplyTrackedPose(leftPose, true), Is.False);
            Assert.That(leftMount.activeSelf, Is.True);
            Assert.That(leftVisual.activeSelf, Is.False);

            left.trackingSpace = tracking;
            Assert.That(left.ApplyTrackedPose(leftPose, true), Is.True);
            left.visualRoot = leftMount.transform;
            Assert.That(left.ApplyTrackedPose(leftPose, true), Is.False);
            Assert.That(leftMount.activeSelf, Is.True);
            Assert.That(leftVisual.activeSelf, Is.False);
        }

        private FirstPersonWrist CreateWrist(GameObject mount, Transform tracking, Transform visual)
        {
            visual.gameObject.SetActive(false);
            FirstPersonWrist wrist = mount.AddComponent<FirstPersonWrist>();
            wrist.trackingSpace = tracking;
            wrist.visualRoot = visual;
            return wrist;
        }

        private GameObject CreateObject(string objectName)
        {
            GameObject created = new GameObject(objectName);
            createdObjects.Add(created);
            return created;
        }
    }
}
