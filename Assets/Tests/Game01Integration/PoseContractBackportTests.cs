#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
// EditMode backport contract tests for the Game01 copy of the VirtualHand
// full-body pose controller (FullBodyPoseController.cs backported byte-identical
// from Game03-Integration). These tests pin the Game03 pose contract:
//   BothArmsForward -> body-space wrists (±0.18, 0.50, 0.52)
//   HandsOnLap      -> body-space wrists (±0.12, 0.18, 0.32)
// Touches no rendering pixels, no PlayMode loop and no graphics device state.
using NUnit.Framework;
using UnityEngine;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;

namespace Game01Integration.Tests
{
    /// <summary>
    /// Verifies the backported <see cref="FullBodyPoseController"/> exposes the
    /// Game03 pose contract on the Game01 FirstPersonFullBody prefab.
    /// </summary>
    public sealed class PoseContractBackportTests : FirstPersonFullBodyTestBase
    {
        const float ContractDelta = 0.001f;

        [Test]
        public void BothArmsForward_Pose_WristTargetsMatchContract()
        {
            GameObject root = InstantiateRoot();
            try
            {
                FullBodyPoseController controller =
                    root.GetComponentInChildren<FullBodyPoseController>(true);
                Assert.IsNotNull(controller, "FirstPersonFullBody prefab must carry a FullBodyPoseController.");

                Assert.IsTrue(controller.ApplyPose("BothArmsForward"),
                    "ApplyPose must accept the Game03 'BothArmsForward' pose on the backported controller.");
                Assert.AreEqual(FullBodyPoseController.PoseBothArmsForward, controller.CurrentPoseName,
                    "CurrentPoseName must follow the applied pose.");

                // The static body-space contract is authoritative (Game03 parity):
                // left is -X, right is +X, +Z forward, +Y up.
                AssertAreEqualPerComponent(
                    new Vector3(-0.18f, 0.50f, 0.52f),
                    FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseBothArmsForward, SeatedHandSide.Left),
                    ContractDelta, "BothArmsForward left wrist target");
                AssertAreEqualPerComponent(
                    new Vector3(0.18f, 0.50f, 0.52f),
                    FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseBothArmsForward, SeatedHandSide.Right),
                    ContractDelta, "BothArmsForward right wrist target");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void HandsOnLap_Pose_WristTargetsMatchContract()
        {
            GameObject root = InstantiateRoot();
            try
            {
                FullBodyPoseController controller =
                    root.GetComponentInChildren<FullBodyPoseController>(true);
                Assert.IsNotNull(controller, "FirstPersonFullBody prefab must carry a FullBodyPoseController.");

                Assert.IsTrue(controller.ApplyPose("HandsOnLap"),
                    "ApplyPose must accept the 'HandsOnLap' pose on the backported controller.");
                Assert.AreEqual(FullBodyPoseController.PoseHandsOnLap, controller.CurrentPoseName,
                    "CurrentPoseName must follow the applied pose.");

                AssertAreEqualPerComponent(
                    new Vector3(-0.12f, 0.18f, 0.32f),
                    FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseHandsOnLap, SeatedHandSide.Left),
                    ContractDelta, "HandsOnLap left wrist target");
                AssertAreEqualPerComponent(
                    new Vector3(0.12f, 0.18f, 0.32f),
                    FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseHandsOnLap, SeatedHandSide.Right),
                    ContractDelta, "HandsOnLap right wrist target");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void PoseSet_ContainsBothArmsForward()
        {
            GameObject root = InstantiateRoot();
            try
            {
                FullBodyPoseController controller =
                    root.GetComponentInChildren<FullBodyPoseController>(true);
                Assert.IsNotNull(controller, "FirstPersonFullBody prefab must carry a FullBodyPoseController.");

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "ApplyPose(PoseBothArmsForward) must succeed on the backported controller.");
                Assert.AreEqual("BothArmsForward", controller.CurrentPoseName,
                    "the canonical name of the BothArmsForward pose must be preserved by the backport.");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        // The bundled NUnit build (com.unity.ext.nunit 1.0.6) only exposes Double and object
        // AreEqual overloads, so a Vector3 comparison with a delta is asserted per component
        // (same technique as the Game03 VirtualHand EditorTests).
        static void AssertAreEqualPerComponent(Vector3 expected, Vector3 actual, float delta, string message)
        {
            Assert.AreEqual(expected.x, actual.x, delta, message + " (x)");
            Assert.AreEqual(expected.y, actual.y, delta, message + " (y)");
            Assert.AreEqual(expected.z, actual.z, delta, message + " (z)");
        }
    }
}
#endif
