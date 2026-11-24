using Neuralife.VirtualHand.Body;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// Table-driven EditMode coverage for the gesture→pose bridge in
    /// GameIntegrationAdapter, exercised through the public OnHandUpdate entry point.
    /// The left/right gesture argument slots decide the mapped body side; every
    /// HandGestureType must either land on the documented pose or keep the current pose
    /// unchanged.
    /// </summary>
    public sealed class MapGestureTableTests
    {
        GameObject host;
        GameIntegrationAdapter adapter;
        FullBodyPoseController poseController;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("MapGestureTableHost");
            poseController = host.AddComponent<FullBodyPoseController>();
            adapter = host.AddComponent<GameIntegrationAdapter>();
            adapter.poseController = poseController;
            Assert.IsTrue(adapter.gesturePosesEnabled, "Gesture→pose mapping must default to enabled.");
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
                Object.DestroyImmediate(host);
        }

        [TestCase(HandGestureType.Point, FullBodyPoseController.PoseLeftArmForward)]
        [TestCase(HandGestureType.Open, FullBodyPoseController.PoseHandsOnLap)]
        [TestCase(HandGestureType.Release, FullBodyPoseController.PoseHandsOnLap)]
        public void LeftGestureSlot_MapsToExpectedPose(HandGestureType gestureType, string expectedPose)
        {
            adapter.OnHandUpdate(
                new HandTrackingFrame(),
                new HandTrackingFrame(),
                new HandGestureEvent { type = gestureType },
                new HandGestureEvent { type = HandGestureType.None });

            Assert.AreEqual(expectedPose, poseController.CurrentPoseName);
        }

        [TestCase(HandGestureType.Point, FullBodyPoseController.PoseRightArmForward)]
        [TestCase(HandGestureType.Open, FullBodyPoseController.PoseHandsOnLap)]
        [TestCase(HandGestureType.Release, FullBodyPoseController.PoseHandsOnLap)]
        public void RightGestureSlot_MapsToExpectedPose(HandGestureType gestureType, string expectedPose)
        {
            adapter.OnHandUpdate(
                new HandTrackingFrame(),
                new HandTrackingFrame(),
                new HandGestureEvent { type = HandGestureType.None },
                new HandGestureEvent { type = gestureType });

            Assert.AreEqual(expectedPose, poseController.CurrentPoseName);
        }

        [TestCase(HandGestureType.None, SeatedHandSide.Left)]
        [TestCase(HandGestureType.Fist, SeatedHandSide.Left)]
        [TestCase(HandGestureType.Pinch, SeatedHandSide.Left)]
        [TestCase(HandGestureType.GraspStart, SeatedHandSide.Left)]
        [TestCase(HandGestureType.GraspEnd, SeatedHandSide.Left)]
        [TestCase(HandGestureType.None, SeatedHandSide.Right)]
        [TestCase(HandGestureType.Fist, SeatedHandSide.Right)]
        [TestCase(HandGestureType.Pinch, SeatedHandSide.Right)]
        [TestCase(HandGestureType.GraspStart, SeatedHandSide.Right)]
        [TestCase(HandGestureType.GraspEnd, SeatedHandSide.Right)]
        public void UnmappedGesture_KeepsCurrentPose(HandGestureType gestureType, SeatedHandSide slot)
        {
            Assert.IsTrue(poseController.ApplyPose(FullBodyPoseController.PoseNaturalSeated));
            string baseline = poseController.CurrentPoseName;

            HandGestureEvent gesture = new HandGestureEvent { type = gestureType };
            if (slot == SeatedHandSide.Left)
                adapter.OnHandUpdate(new HandTrackingFrame(), new HandTrackingFrame(), gesture, new HandGestureEvent());
            else
                adapter.OnHandUpdate(new HandTrackingFrame(), new HandTrackingFrame(), new HandGestureEvent(), gesture);

            Assert.AreEqual(baseline, poseController.CurrentPoseName,
                gestureType + " has no pose mapping and must leave the current pose unchanged.");
        }
    }
}
