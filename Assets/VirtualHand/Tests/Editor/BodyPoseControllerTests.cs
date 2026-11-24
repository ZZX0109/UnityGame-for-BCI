#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEngine;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Edit-mode coverage for <see cref="FullBodyPoseController"/>: pose name resolution,
    /// body-space presets and the pure SeatedArmSolver integration.
    /// </summary>
    public sealed class BodyPoseControllerTests
    {
        const float Delta = 0.0001f;

        [Test]
        public void ApplyPoseAcceptsKnownNamesCaseInsensitive()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            try
            {
                Assert.AreEqual(FullBodyPoseController.PoseNaturalSeated, controller.CurrentPoseName,
                    "the controller should start in the seated idle pose");

                Assert.IsTrue(controller.ApplyPose("  naturalSeated  "),
                    "whitespace and casing must be tolerated");
                Assert.AreEqual(FullBodyPoseController.PoseNaturalSeated, controller.CurrentPoseName,
                    "the resolved name should be canonical");

                Assert.IsTrue(controller.ApplyPose("HANDSONLAP"), "upper case names must be accepted");
                Assert.AreEqual(FullBodyPoseController.PoseHandsOnLap, controller.CurrentPoseName,
                    "the resolved name should follow the switch");

                Assert.IsTrue(controller.ApplyPose("RightArmForward"), "mixed case names must be accepted");
                Assert.AreEqual(FullBodyPoseController.PoseRightArmForward, controller.CurrentPoseName,
                    "the resolved name should follow the switch");
            }
            finally
            {
                Destroy(root);
            }
        }

        [Test]
        public void ApplyPoseRejectsUnknownNames()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            try
            {
                Assert.IsFalse(controller.ApplyPose("JazzHands"),
                    "unknown pose names must be rejected");
                Assert.IsFalse(controller.ApplyPose(null), "null must be rejected");
                Assert.IsFalse(controller.ApplyPose(""), "empty names must be rejected");
                Assert.AreEqual(FullBodyPoseController.PoseNaturalSeated, controller.CurrentPoseName,
                    "a rejected pose must keep the current pose");
            }
            finally
            {
                Destroy(root);
            }
        }

        [Test]
        public void SeatedPropertyAutoSwitchesIdlePoses()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            try
            {
                Assert.IsTrue(controller.Seated, "the controller should start seated");

                controller.Seated = false;
                Assert.AreEqual(FullBodyPoseController.PoseStanding, controller.CurrentPoseName,
                    "standing up should auto-apply the standing idle pose");
                SeatedArmSolver.Solution solution;
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out solution),
                    "the left arm should solve in the standing pose");
                Assert.IsTrue(solution.IsReachable, "the standing left arm target should be reachable");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Right, out solution),
                    "the right arm should solve in the standing pose");
                Assert.IsTrue(solution.IsReachable, "the standing right arm target should be reachable");

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseHandsOnLap),
                    "an explicit pose should still apply while standing");

                controller.Seated = true;
                Assert.AreEqual(FullBodyPoseController.PoseHandsOnLap, controller.CurrentPoseName,
                    "sitting down must not clobber an explicit non-idle pose");

                // From the standing idle pose, sitting down auto-restores the seated idle pose.
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseStanding),
                    "the standing pose should apply explicitly");
                controller.Seated = false;
                Assert.AreEqual(FullBodyPoseController.PoseStanding, controller.CurrentPoseName,
                    "standing up from the standing idle pose keeps it");
                controller.Seated = true;
                Assert.AreEqual(FullBodyPoseController.PoseNaturalSeated, controller.CurrentPoseName,
                    "sitting down from the standing idle pose should restore the seated idle pose");
            }
            finally
            {
                Destroy(root);
            }
        }

        [Test]
        public void GetBodySpaceShoulderMatchesPresentationDefaults()
        {
            Vector3 left = FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Left);
            Vector3 right = FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Right);

            Assert.AreEqual(-0.20f, left.x, Delta, "the left shoulder should sit left of centre");
            Assert.AreEqual(0.20f, right.x, Delta, "the right shoulder should sit right of centre");
            Assert.AreEqual(0.52f, left.y, Delta, "the shoulders should sit at torso height");
            Assert.AreEqual(0.52f, right.y, Delta, "the shoulders should sit at torso height");
            Assert.AreEqual(0f, left.z, Delta, "the shoulders should not be offset forward");
            Assert.AreEqual(0f, right.z, Delta, "the shoulders should not be offset forward");
        }

        [Test]
        public void GetBodySpaceWristTargetMatchesPosePresets()
        {
            Vector3 naturalLeft = new Vector3(-0.16f, 0.28f, 0.18f);
            Vector3 naturalRight = new Vector3(0.16f, 0.28f, 0.18f);

            Assert.AreEqual(naturalLeft,
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Left),
                "the NaturalSeated left preset drifted");
            Assert.AreEqual(naturalRight,
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Right),
                "the NaturalSeated right preset drifted");

            Assert.AreEqual(new Vector3(-0.17f, 0.16f, 0.20f),
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseStanding, SeatedHandSide.Left),
                "the Standing left preset drifted");
            Assert.AreEqual(new Vector3(0.17f, 0.16f, 0.20f),
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseStanding, SeatedHandSide.Right),
                "the Standing right preset drifted");

            Assert.AreEqual(new Vector3(-0.12f, 0.18f, 0.32f),
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseHandsOnLap, SeatedHandSide.Left),
                "the HandsOnLap left preset drifted");
            Assert.AreEqual(new Vector3(0.12f, 0.18f, 0.32f),
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseHandsOnLap, SeatedHandSide.Right),
                "the HandsOnLap right preset drifted");

            Assert.AreEqual(new Vector3(-0.18f, 0.50f, 0.52f),
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseLeftArmForward, SeatedHandSide.Left),
                "the LeftArmForward left preset drifted");
            Assert.AreEqual(naturalRight,
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseLeftArmForward, SeatedHandSide.Right),
                "the one-sided left forward pose must keep the right arm on NaturalSeated");

            Assert.AreEqual(naturalLeft,
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseRightArmForward, SeatedHandSide.Left),
                "the one-sided right forward pose must keep the left arm on NaturalSeated");
            Assert.AreEqual(new Vector3(0.18f, 0.50f, 0.52f),
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseRightArmForward, SeatedHandSide.Right),
                "the RightArmForward right preset drifted");

            Assert.AreEqual(naturalLeft,
                FullBodyPoseController.GetBodySpaceWristTarget("NotAPose", SeatedHandSide.Left),
                "unknown pose names should fall back to the NaturalSeated preset");
        }

        [Test]
        public void BothArmsForwardAppliesToBothArms()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            try
            {
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the both-arms-forward pose should apply");
                Assert.AreEqual(FullBodyPoseController.PoseBothArmsForward, controller.CurrentPoseName,
                    "the resolved name should be the canonical both-arms-forward name");

                Assert.AreEqual(new Vector3(-0.18f, 0.50f, 0.52f),
                    FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseBothArmsForward, SeatedHandSide.Left),
                    "the BothArmsForward left preset drifted");
                Assert.AreEqual(new Vector3(0.18f, 0.50f, 0.52f),
                    FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseBothArmsForward, SeatedHandSide.Right),
                    "the BothArmsForward right preset drifted");

                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution left),
                    "the left arm should solve for the both-arms-forward pose");
                Assert.IsTrue(left.IsValid, "the left arm solution must be valid for the both-arms-forward pose");
                Assert.IsTrue(left.IsReachable, "the left arm target must be reachable for the both-arms-forward pose");

                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Right, out SeatedArmSolver.Solution right),
                    "the right arm should solve for the both-arms-forward pose");
                Assert.IsTrue(right.IsValid, "the right arm solution must be valid for the both-arms-forward pose");
                Assert.IsTrue(right.IsReachable, "the right arm target must be reachable for the both-arms-forward pose");
            }
            finally
            {
                Destroy(root);
            }
        }

        [Test]
        public void TryGetSolvedArmReturnsReachableSolutions()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            try
            {
                SeatedArmSolver.Solution unused;
                Assert.IsFalse(controller.TryGetSolvedArm(SeatedHandSide.Left, out unused),
                    "no arm should report a solution before the first ApplyPose");

                string[] poses =
                {
                    FullBodyPoseController.PoseNaturalSeated,
                    FullBodyPoseController.PoseStanding,
                    FullBodyPoseController.PoseHandsOnLap,
                    FullBodyPoseController.PoseLeftArmForward,
                    FullBodyPoseController.PoseRightArmForward,
                    FullBodyPoseController.PoseBothArmsForward
                };
                for (int i = 0; i < poses.Length; i++)
                {
                    Assert.IsTrue(controller.ApplyPose(poses[i]), "pose '{0}' should apply", poses[i]);

                    Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution left),
                        "the left arm should solve for pose '{0}'", poses[i]);
                    Assert.IsTrue(left.IsValid, "the left arm solution must be valid for pose '{0}'", poses[i]);
                    Assert.IsTrue(left.IsReachable, "the left arm target must be reachable for pose '{0}'", poses[i]);
                    AssertAreEqual(left.WristPositionMeters, left.SolvedWristPositionMeters, Delta,
                        string.Format("a reachable left wrist must not be clamped for pose '{0}'", poses[i]));

                    Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Right, out SeatedArmSolver.Solution right),
                        "the right arm should solve for pose '{0}'", poses[i]);
                    Assert.IsTrue(right.IsValid, "the right arm solution must be valid for pose '{0}'", poses[i]);
                    Assert.IsTrue(right.IsReachable, "the right arm target must be reachable for pose '{0}'", poses[i]);
                    AssertAreEqual(right.WristPositionMeters, right.SolvedWristPositionMeters, Delta,
                        string.Format("a reachable right wrist must not be clamped for pose '{0}'", poses[i]));
                }
            }
            finally
            {
                Destroy(root);
            }
        }

        [Test]
        public void ApplyPoseDrivesRigTransforms()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject elbow = new GameObject("Left Elbow");
            GameObject wrist = new GameObject("Left Wrist");
            try
            {
                root.transform.position = new Vector3(1f, 2f, 3f);
                root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                elbow.transform.SetParent(root.transform, false);
                wrist.transform.SetParent(root.transform, false);
                controller.leftElbow = elbow.transform;
                controller.leftWrist = wrist.transform;

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseNaturalSeated),
                    "the natural seated pose should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution solution),
                    "the left arm should solve");

                Vector3 expectedWrist =
                    root.transform.TransformPoint(FullBodyPoseController.GetBodySpaceWristTarget(
                        FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Left));
                AssertAreEqual(expectedWrist, wrist.transform.position, Delta,
                    "the wrist transform should land on the solved wrist in world space");
                AssertAreEqual(root.transform.TransformPoint(solution.ElbowPositionMeters),
                    elbow.transform.position, Delta,
                    "the elbow transform should land on the solved elbow in world space");
            }
            finally
            {
                Destroy(root);
                Destroy(elbow);
                Destroy(wrist);
            }
        }

        [Test]
        public void PoseTransitionBlendsInsteadOfSnapping()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject leftElbow = new GameObject("Left Elbow");
            GameObject leftWrist = new GameObject("Left Wrist");
            GameObject rightElbow = new GameObject("Right Elbow");
            GameObject rightWrist = new GameObject("Right Wrist");
            GameObject freshRoot = CreateController(out FullBodyPoseController freshController);
            try
            {
                root.transform.position = new Vector3(1f, 2f, 3f);
                root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                leftElbow.transform.SetParent(root.transform, false);
                leftWrist.transform.SetParent(root.transform, false);
                rightElbow.transform.SetParent(root.transform, false);
                rightWrist.transform.SetParent(root.transform, false);
                controller.leftElbow = leftElbow.transform;
                controller.leftWrist = leftWrist.transform;
                controller.rightElbow = rightElbow.transform;
                controller.rightWrist = rightWrist.transform;

                Vector3 initialLeftElbow = leftElbow.transform.position;
                Vector3 initialLeftWrist = leftWrist.transform.position;
                Vector3 initialRightElbow = rightElbow.transform.position;
                Vector3 initialRightWrist = rightWrist.transform.position;

                controller.blendPoseTransitions = true;
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the both-arms-forward pose should apply");

                AssertAreEqual(initialLeftElbow, leftElbow.transform.position, Delta,
                    "blending must suppress the instant snap of the left elbow");
                AssertAreEqual(initialLeftWrist, leftWrist.transform.position, Delta,
                    "blending must suppress the instant snap of the left wrist");
                AssertAreEqual(initialRightElbow, rightElbow.transform.position, Delta,
                    "blending must suppress the instant snap of the right elbow");
                AssertAreEqual(initialRightWrist, rightWrist.transform.position, Delta,
                    "blending must suppress the instant snap of the right wrist");

                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution left),
                    "the left arm should solve");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Right, out SeatedArmSolver.Solution right),
                    "the right arm should solve");
                Vector3 leftWristTarget = root.transform.TransformPoint(left.SolvedWristPositionMeters);
                Vector3 leftElbowTarget = root.transform.TransformPoint(left.ElbowPositionMeters);
                Vector3 rightWristTarget = root.transform.TransformPoint(right.SolvedWristPositionMeters);
                Vector3 rightElbowTarget = root.transform.TransformPoint(right.ElbowPositionMeters);

                float k = 1f - Mathf.Exp(-(1f / 60f) / 0.15f);
                Assert.IsTrue(controller.BlendTowardCurrent(1f / 60f),
                    "the first blend step should run once the opt-in flag is set");
                AssertAreEqual(initialLeftElbow + k * (leftElbowTarget - initialLeftElbow),
                    leftElbow.transform.position, 0.002f,
                    "the first blend step should move the left elbow by k of the gap");
                AssertAreEqual(initialLeftWrist + k * (leftWristTarget - initialLeftWrist),
                    leftWrist.transform.position, 0.002f,
                    "the first blend step should move the left wrist by k of the gap");
                AssertAreEqual(initialRightElbow + k * (rightElbowTarget - initialRightElbow),
                    rightElbow.transform.position, 0.002f,
                    "the first blend step should move the right elbow by k of the gap");
                AssertAreEqual(initialRightWrist + k * (rightWristTarget - initialRightWrist),
                    rightWrist.transform.position, 0.002f,
                    "the first blend step should move the right wrist by k of the gap");

                Assert.IsFalse(controller.BlendTowardCurrent(0f),
                    "a non-positive deltaTime must refuse to blend");
                Assert.IsFalse(freshController.BlendTowardCurrent(1f / 60f),
                    "a fresh controller with the flag left false must refuse to blend");

                for (int i = 0; i < 200; i++)
                {
                    Assert.IsTrue(controller.BlendTowardCurrent(1f / 60f),
                        "blend step {0} should run while the flag is set", i);
                }

                AssertAreEqual(leftWristTarget, leftWrist.transform.position, 0.001f,
                    "the blended left wrist should converge on the solved target");
                AssertAreEqual(rightWristTarget, rightWrist.transform.position, 0.001f,
                    "the blended right wrist should converge on the solved target");
            }
            finally
            {
                Destroy(root);
                Destroy(leftElbow);
                Destroy(leftWrist);
                Destroy(rightElbow);
                Destroy(rightWrist);
                Destroy(freshRoot);
            }
        }

        [Test]
        public void RepeatedSamePoseIsDeterministic()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            try
            {
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseLeftArmForward),
                    "the first application should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution first),
                    "the first solve should succeed");

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseLeftArmForward),
                    "the repeated application should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution second),
                    "the repeated solve should succeed");

                AssertAreEqual(first.ElbowPositionMeters, second.ElbowPositionMeters, 0.00005f,
                    "identical inputs including the continuity hint must reproduce the same elbow");
            }
            finally
            {
                Destroy(root);
            }
        }

        static GameObject CreateController(out FullBodyPoseController controller)
        {
            GameObject root = new GameObject("Body Pose Test Root");
            controller = root.AddComponent<FullBodyPoseController>();
            return root;
        }

        // The bundled NUnit build (com.unity.ext.nunit 1.0.6) only exposes Double and object
        // AreEqual overloads, so a Vector3 comparison with a delta is asserted per component.
        static void AssertAreEqual(Vector3 expected, Vector3 actual, float delta, string message)
        {
            Assert.AreEqual(expected.x, actual.x, delta, message + " (x)");
            Assert.AreEqual(expected.y, actual.y, delta, message + " (y)");
            Assert.AreEqual(expected.z, actual.z, delta, message + " (z)");
        }

        static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
                UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
#endif
