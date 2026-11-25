#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEngine;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Edit-mode coverage for the upper arm alignment added to <see cref="FullBodyPoseController"/>.
    /// The bound upper arm bone must aim its position-derived world axis along the solved
    /// shoulder-to-elbow direction on both the instant path (k = 1) and the blended path
    /// (the same exponential smoothing factor used for the position writes), must recompute
    /// directions from world positions every call so any authoring bone orientation works,
    /// and must degrade to a no-op when bindings are missing or the directions degenerate.
    /// </summary>
    public sealed class FullBodyUpperArmAlignmentTests
    {
        const float Delta = 0.01f;

        [Test]
        public void InstantPathAimsBoneAxisAlongSolvedShoulderToElbow()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject upperArm = new GameObject("LeftUpperArm");
            GameObject elbow = new GameObject("LeftElbow");
            try
            {
                Vector3 shoulderWorld = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Left));
                upperArm.transform.position = shoulderWorld;
                upperArm.transform.rotation = Quaternion.Euler(30f, 40f, 50f);
                elbow.transform.position = shoulderWorld + Vector3.down;

                controller.leftUpperArm = upperArm.transform;
                controller.leftElbow = elbow.transform;

                Quaternion beforeAlignment = upperArm.transform.rotation;
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the pose should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution solution),
                    "the left arm should solve");

                Vector3 targetDirection = controller.transform.TransformPoint(solution.ElbowPositionMeters)
                    - shoulderWorld;
                Vector3 previousAxis = (shoulderWorld + Vector3.down) - shoulderWorld;
                Quaternion expected = Quaternion.FromToRotation(
                    previousAxis.normalized, targetDirection.normalized) * beforeAlignment;
                Assert.Less(Quaternion.Angle(expected, upperArm.transform.rotation), Delta,
                    "the instant path should snap the bone axis onto the solved direction");

                Vector3 achievedAxis = (elbow.transform.position - upperArm.transform.position).normalized;
                Assert.Less(Vector3.Angle(achievedAxis, targetDirection.normalized), Delta,
                    "the aligned chain should aim from the upper arm at the solved elbow");
            }
            finally
            {
                Destroy(root);
                Destroy(upperArm);
                Destroy(elbow);
            }
        }

        [Test]
        public void RotatedLocalFrameStillAimsAlongWorldDirection()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject upperArm = new GameObject("LeftUpperArm");
            GameObject elbow = new GameObject("LeftElbow");
            try
            {
                Vector3 shoulderWorld = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Left));
                upperArm.transform.position = shoulderWorld;
                upperArm.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                elbow.transform.position = shoulderWorld + Vector3.up;

                controller.leftUpperArm = upperArm.transform;
                controller.leftElbow = elbow.transform;

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the pose should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution solution),
                    "the left arm should solve");

                Vector3 targetDirection = controller.transform.TransformPoint(solution.ElbowPositionMeters)
                    - shoulderWorld;
                Vector3 achievedAxis = (elbow.transform.position - upperArm.transform.position).normalized;
                Assert.Less(Vector3.Angle(achievedAxis, targetDirection.normalized), Delta,
                    "the axis must come from world positions, not the authored local up");
            }
            finally
            {
                Destroy(root);
                Destroy(upperArm);
                Destroy(elbow);
            }
        }

        [Test]
        public void BodySpaceYawIsFollowedEveryFrame()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject upperArm = new GameObject("RightUpperArm");
            GameObject elbow = new GameObject("RightElbow");
            try
            {
                controller.rightUpperArm = upperArm.transform;
                controller.rightElbow = elbow.transform;
                upperArm.transform.position = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Right));
                elbow.transform.position = upperArm.transform.position + Vector3.down;

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the pose should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Right, out SeatedArmSolver.Solution before),
                    "the right arm should solve");
                Vector3 beforeDirection = controller.transform.TransformPoint(before.ElbowPositionMeters)
                    - upperArm.transform.position;

                controller.transform.Rotate(0f, 90f, 0f);
                upperArm.transform.position = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Right));
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the repeated pose should apply");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Right, out SeatedArmSolver.Solution after),
                    "the right arm should solve again");
                Vector3 afterDirection = controller.transform.TransformPoint(after.ElbowPositionMeters)
                    - upperArm.transform.position;

                Assert.Greater(Vector3.Angle(beforeDirection, afterDirection), 45f,
                    "a 90 degree yaw must rotate the solved direction for this test to be meaningful");
                Vector3 achievedAxis = (elbow.transform.position - upperArm.transform.position).normalized;
                Assert.Less(Vector3.Angle(achievedAxis, afterDirection.normalized), Delta,
                    "directions must be recomputed from the moved body space, not cached");
            }
            finally
            {
                Destroy(root);
                Destroy(upperArm);
                Destroy(elbow);
            }
        }

        [Test]
        public void BlendPathSlerpsWithThePositionBlendingFactor()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject upperArm = new GameObject("LeftUpperArm");
            GameObject elbow = new GameObject("LeftElbow");
            try
            {
                controller.blendPoseTransitions = true;
                Vector3 shoulderWorld = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Left));
                upperArm.transform.position = shoulderWorld;
                upperArm.transform.rotation = Quaternion.Euler(10f, 20f, 30f);
                elbow.transform.position = shoulderWorld + Vector3.down;

                controller.leftUpperArm = upperArm.transform;
                controller.leftElbow = elbow.transform;

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the pose should solve without snapping while blending is on");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution solution),
                    "the left arm should solve");

                Quaternion initialRotation = upperArm.transform.rotation;
                Vector3 initialElbow = elbow.transform.position;
                Vector3 targetDirection = controller.transform.TransformPoint(solution.ElbowPositionMeters)
                    - shoulderWorld;
                Vector3 currentAxis = (initialElbow - shoulderWorld).normalized;
                Quaternion targetRotation = Quaternion.FromToRotation(
                    currentAxis, targetDirection.normalized) * initialRotation;
                float k = 1f - Mathf.Exp(-0.5f / 0.15f);

                Assert.IsTrue(controller.BlendTowardCurrent(0.5f),
                    "the blend step should run while the flag is set");

                Quaternion expected = Quaternion.Slerp(initialRotation, targetRotation, k);
                Assert.Less(Quaternion.Angle(expected, upperArm.transform.rotation), 0.05f,
                    "the upper arm must use the same exponential blend factor as the positions");
                Vector3 expectedElbow = initialElbow + k
                    * (controller.transform.TransformPoint(solution.ElbowPositionMeters) - initialElbow);
                Assert.Less((expectedElbow - elbow.transform.position).magnitude, 0.002f,
                    "the elbow position step should share the same factor");
            }
            finally
            {
                Destroy(root);
                Destroy(upperArm);
                Destroy(elbow);
            }
        }

        [Test]
        public void DegenerateDirectionsLeaveTheBoneUntouched()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject upperArm = new GameObject("LeftUpperArm");
            GameObject elbow = new GameObject("LeftElbow");
            try
            {
                Vector3 shoulderWorld = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Left));
                upperArm.transform.position = shoulderWorld;
                upperArm.transform.rotation = Quaternion.Euler(15f, 25f, 35f);
                elbow.transform.position = shoulderWorld;

                controller.leftUpperArm = upperArm.transform;
                controller.leftElbow = elbow.transform;

                Quaternion beforeAlignment = upperArm.transform.rotation;
                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "a degenerate current axis must not abort the pose application");
                Assert.AreEqual(0f, Quaternion.Angle(beforeAlignment, upperArm.transform.rotation), Delta,
                    "a zero-length current axis must leave the bone untouched instead of producing a NaN rotation");
                Assert.IsFalse(float.IsNaN(upperArm.transform.rotation.x)
                    || float.IsNaN(upperArm.transform.rotation.y)
                    || float.IsNaN(upperArm.transform.rotation.z)
                    || float.IsNaN(upperArm.transform.rotation.w),
                    "the guarded rotation must stay finite");
            }
            finally
            {
                Destroy(root);
                Destroy(upperArm);
                Destroy(elbow);
            }
        }

        [Test]
        public void RepeatedApplicationDoesNotAccumulateRotation()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject upperArm = new GameObject("LeftUpperArm");
            GameObject elbow = new GameObject("LeftElbow");
            try
            {
                Vector3 shoulderWorld = controller.transform.TransformPoint(
                    FullBodyPoseController.GetBodySpaceShoulder(SeatedHandSide.Left));
                upperArm.transform.position = shoulderWorld;
                upperArm.transform.rotation = Quaternion.identity;
                elbow.transform.position = shoulderWorld + Vector3.down;

                controller.leftUpperArm = upperArm.transform;
                controller.leftElbow = elbow.transform;

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the first application should apply");
                Quaternion firstRotation = upperArm.transform.rotation;

                for (int i = 0; i < 10; i++)
                {
                    Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                        "the repeated application {0} should apply", i);
                    Assert.AreEqual(0f, Quaternion.Angle(firstRotation, upperArm.transform.rotation), Delta,
                        "repeated identical applications must be idempotent (call {0})", i);
                }
            }
            finally
            {
                Destroy(root);
                Destroy(upperArm);
                Destroy(elbow);
            }
        }

        [Test]
        public void MissingUpperArmBindingsKeepArmPositionSolveIntact()
        {
            GameObject root = CreateController(out FullBodyPoseController controller);
            GameObject elbow = new GameObject("LeftElbow");
            GameObject wrist = new GameObject("LeftWrist");
            try
            {
                Assert.IsNull(controller.leftUpperArm, "the upper arm binding should default to null");
                Assert.IsNull(controller.rightUpperArm, "the upper arm binding should default to null");

                controller.leftElbow = elbow.transform;
                controller.leftWrist = wrist.transform;

                Assert.IsTrue(controller.ApplyPose(FullBodyPoseController.PoseBothArmsForward),
                    "the pose should apply without any upper arm binding");
                Assert.IsTrue(controller.TryGetSolvedArm(SeatedHandSide.Left, out SeatedArmSolver.Solution solution),
                    "the left arm should solve");

                Vector3 expectedElbow = controller.transform.TransformPoint(solution.ElbowPositionMeters);
                Assert.Less((expectedElbow - elbow.transform.position).magnitude, 0.0001f,
                    "the elbow position solve must be unaffected by the absent upper arm binding");
            }
            finally
            {
                Destroy(root);
                Destroy(elbow);
                Destroy(wrist);
            }
        }

        static GameObject CreateController(out FullBodyPoseController controller)
        {
            GameObject root = new GameObject("Upper Arm Alignment Test Root");
            controller = root.AddComponent<FullBodyPoseController>();
            return root;
        }

        static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
                UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
#endif
