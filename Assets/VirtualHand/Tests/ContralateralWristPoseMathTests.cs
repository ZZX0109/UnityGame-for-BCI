using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class ContralateralWristPoseMathTests
    {
        private const float PositionTolerance = 0.0001f;
        private const float RotationToleranceDegrees = 0.05f;

        [Test]
        public void IdentityPoseAcrossVerticalSagittalPlaneKeepsIdentityOrientationAndLabelsProvenance()
        {
            Pose operatingPose = new Pose(new Vector3(0.2f, 0.4f, 0.6f), Quaternion.identity);

            bool succeeded = ContralateralWristPoseMath.TryDeriveFromTrackedOperatingWrist(
                operatingPose,
                true,
                0.73f,
                SeatedHandSide.Right,
                SeatedHandSide.Left,
                Vector3.zero,
                Vector3.right,
                out Pose targetPose,
                out float targetGrip,
                out string provenance);

            Assert.That(succeeded, Is.True);
            AssertVectorApproximately(new Vector3(-0.2f, 0.4f, 0.6f), targetPose.position);
            Assert.That(Quaternion.Angle(Quaternion.identity, targetPose.rotation), Is.LessThan(RotationToleranceDegrees));
            Assert.That(targetGrip, Is.EqualTo(0.73f).Within(0.000001f));
            Assert.That(provenance, Is.EqualTo("Mirror-derived from tracked right wrist"));
        }

        [Test]
        public void KnownYawAcrossVerticalSagittalPlaneChangesYawSignAndRestoresProperHandedness()
        {
            Quaternion sourceRotation = Quaternion.Euler(0f, 37f, 0f);
            Pose operatingPose = new Pose(new Vector3(0.1f, 0.2f, 0.3f), sourceRotation);

            bool succeeded = ContralateralWristPoseMath.TryDeriveFromTrackedOperatingWrist(
                operatingPose,
                true,
                0.4f,
                SeatedHandSide.Left,
                SeatedHandSide.Right,
                Vector3.zero,
                Vector3.right,
                out Pose targetPose,
                out _,
                out _);

            Assert.That(succeeded, Is.True);
            Assert.That(
                Quaternion.Angle(Quaternion.Euler(0f, -37f, 0f), targetPose.rotation),
                Is.LessThan(RotationToleranceDegrees));

            Vector3 sourceRight = sourceRotation * Vector3.right;
            Vector3 reflectedSourceRight = Reflect(sourceRight, Vector3.right);
            Vector3 targetRight = targetPose.rotation * Vector3.right;
            Assert.That(Vector3.Dot(targetRight, reflectedSourceRight), Is.LessThan(-0.999f));

            Vector3 targetUp = targetPose.rotation * Vector3.up;
            Vector3 targetForward = targetPose.rotation * Vector3.forward;
            Assert.That(Vector3.Dot(Vector3.Cross(targetRight, targetUp), targetForward), Is.GreaterThan(0.999f));
        }

        [Test]
        public void TiltedTranslatedPlaneReflectsPositionAndOrientationAxes()
        {
            Vector3 planePoint = new Vector3(0.4f, -0.2f, 0.3f);
            Vector3 normal = new Vector3(1f, 1f, 0f).normalized;
            Quaternion sourceRotation = Quaternion.Euler(13f, -28f, 41f);
            Pose operatingPose = new Pose(planePoint + normal * 0.75f, sourceRotation);

            bool succeeded = ContralateralWristPoseMath.TryDeriveFromTrackedOperatingWrist(
                operatingPose,
                true,
                0.8f,
                SeatedHandSide.Right,
                SeatedHandSide.Left,
                planePoint,
                normal,
                out Pose targetPose,
                out _,
                out _);

            Assert.That(succeeded, Is.True);
            AssertVectorApproximately(planePoint - normal * 0.75f, targetPose.position);
            AssertDirectionApproximately(
                Reflect(sourceRotation * Vector3.forward, normal),
                targetPose.rotation * Vector3.forward);
            AssertDirectionApproximately(
                Reflect(sourceRotation * Vector3.up, normal),
                targetPose.rotation * Vector3.up);
            AssertDirectionApproximately(
                -Reflect(sourceRotation * Vector3.right, normal),
                targetPose.rotation * Vector3.right);
        }

        [Test]
        public void ReflectingTwiceAcrossSamePlaneRestoresSymmetricFrame()
        {
            Vector3 planePoint = new Vector3(0.35f, -0.12f, 0.27f);
            Vector3 normal = new Vector3(0.31f, 0.82f, -0.47f);
            Pose original = new Pose(new Vector3(-0.2f, 1.1f, 0.6f), Quaternion.Euler(-24f, 53f, 17f));

            bool firstSucceeded = ContralateralWristPoseMath.TryReflectPoseAcrossPlane(
                original, planePoint, normal, out Pose onceReflected);
            bool secondSucceeded = ContralateralWristPoseMath.TryReflectPoseAcrossPlane(
                onceReflected, planePoint, normal, out Pose twiceReflected);

            Assert.That(firstSucceeded, Is.True);
            Assert.That(secondSucceeded, Is.True);
            AssertVectorApproximately(original.position, twiceReflected.position);
            Assert.That(Quaternion.Angle(original.rotation, twiceReflected.rotation), Is.LessThan(RotationToleranceDegrees));
        }

        [Test]
        public void RejectsUntrackedPoseInvalidSidesAndNonFiniteOrDegenerateInputs()
        {
            Pose validPose = new Pose(new Vector3(0.1f, 0.2f, 0.3f), Quaternion.identity);
            AssertRejected(validPose, false, 0.5f, SeatedHandSide.Right, SeatedHandSide.Left, Vector3.zero, Vector3.right);
            AssertRejected(validPose, true, 0.5f, SeatedHandSide.Unspecified, SeatedHandSide.Left, Vector3.zero, Vector3.right);
            AssertRejected(validPose, true, 0.5f, (SeatedHandSide)99, SeatedHandSide.Left, Vector3.zero, Vector3.right);
            AssertRejected(validPose, true, 0.5f, SeatedHandSide.Right, SeatedHandSide.Right, Vector3.zero, Vector3.right);
            AssertRejected(validPose, true, float.NaN, SeatedHandSide.Right, SeatedHandSide.Left, Vector3.zero, Vector3.right);
            AssertRejected(new Pose(new Vector3(float.PositiveInfinity, 0f, 0f), Quaternion.identity), true, 0.5f,
                SeatedHandSide.Right, SeatedHandSide.Left, Vector3.zero, Vector3.right);
            AssertRejected(new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f)), true, 0.5f,
                SeatedHandSide.Right, SeatedHandSide.Left, Vector3.zero, Vector3.right);
            AssertRejected(validPose, true, 0.5f, SeatedHandSide.Right, SeatedHandSide.Left,
                new Vector3(float.NaN, 0f, 0f), Vector3.right);
            AssertRejected(validPose, true, 0.5f, SeatedHandSide.Right, SeatedHandSide.Left, Vector3.zero, Vector3.zero);
            AssertRejected(validPose, true, 1.1f, SeatedHandSide.Right, SeatedHandSide.Left, Vector3.zero, Vector3.right);
        }

        private static void AssertRejected(
            Pose operatingPose,
            bool tracked,
            float grip,
            SeatedHandSide operatingSide,
            SeatedHandSide targetSide,
            Vector3 planePoint,
            Vector3 planeNormal)
        {
            bool succeeded = ContralateralWristPoseMath.TryDeriveFromTrackedOperatingWrist(
                operatingPose,
                tracked,
                grip,
                operatingSide,
                targetSide,
                planePoint,
                planeNormal,
                out _,
                out _,
                out string provenance);

            Assert.That(succeeded, Is.False);
            Assert.That(provenance, Is.Empty);
        }

        private static Vector3 Reflect(Vector3 value, Vector3 normal)
        {
            normal.Normalize();
            return value - 2f * Vector3.Dot(value, normal) * normal;
        }

        private static void AssertVectorApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(PositionTolerance));
        }

        private static void AssertDirectionApproximately(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Angle(expected, actual), Is.LessThan(RotationToleranceDegrees));
        }
    }
}
