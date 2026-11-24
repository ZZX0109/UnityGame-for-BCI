using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class SeatedWristCalibrationTests
    {
        [Test]
        public void SolvesAndReconstructsWithNonIdentityRotations()
        {
            Pose grip = new Pose(
                new Vector3(0.31f, 0.82f, -0.14f),
                Quaternion.Euler(23f, -41f, 68f));
            Pose desired = new Pose(
                new Vector3(0.38f, 0.91f, -0.09f),
                Quaternion.Euler(-17f, 96f, 12f));

            Assert.That(SeatedWristCalibration.TryCalculateOffset(grip, true, desired, out var offset), Is.True);
            AssertMapsAsFirstPersonWrist(grip, desired, offset);
        }

        [Test]
        public void LeftAndRightInputsUseTheirOwnGripLocalOffsets()
        {
            Pose leftGrip = new Pose(
                new Vector3(-0.24f, 0.79f, 0.18f),
                Quaternion.Euler(11f, -27f, 6f));
            AssertHandFixture(leftGrip,
                new Vector3(-0.042f, 0.013f, 0.026f),
                Quaternion.Euler(5f, -9f, 3f));

            Pose rightGrip = new Pose(
                new Vector3(0.25f, 0.80f, 0.17f),
                Quaternion.Euler(-12f, 29f, -4f));
            AssertHandFixture(rightGrip,
                new Vector3(0.046f, 0.011f, 0.024f),
                Quaternion.Euler(-6f, 8f, -2f));
        }

        [Test]
        public void RejectsUntrackedGripAndClearsOutput()
        {
            Pose grip = new Pose(Vector3.one, Quaternion.identity);
            Pose desired = new Pose(Vector3.zero, Quaternion.identity);
            SeatedHandSettings.WristOffset offset = new SeatedHandSettings.WristOffset(
                Vector3.one, Vector3.one);

            Assert.That(SeatedWristCalibration.TryCalculateOffset(grip, false, desired, out offset), Is.False);
            Assert.That(offset.translationMeters, Is.EqualTo(Vector3.zero));
            Assert.That(offset.rotationEulerDegrees, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void RejectsNonFinitePositionsAndZeroOrNonFiniteRotations()
        {
            Pose valid = new Pose(Vector3.zero, Quaternion.identity);

            AssertRejected(new Pose(new Vector3(float.NaN, 0f, 0f), Quaternion.identity), valid);
            AssertRejected(valid, new Pose(new Vector3(0f, float.PositiveInfinity, 0f), Quaternion.identity));
            AssertRejected(new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f)), valid);
            AssertRejected(valid, new Pose(Vector3.zero, new Quaternion(0f, float.NaN, 0f, 1f)));
        }

        [Test]
        public void WrapsEulerAcrossThe180DegreeBoundary()
        {
            Pose grip = new Pose(Vector3.zero, Quaternion.Euler(0f, 179f, 0f));
            Pose desired = new Pose(new Vector3(0.03f, 0.02f, -0.01f), Quaternion.Euler(0f, -179f, 0f));

            Assert.That(SeatedWristCalibration.TryCalculateOffset(grip, true, desired, out var offset), Is.True);
            Assert.That(Mathf.Abs(offset.rotationEulerDegrees.y - 2f), Is.LessThan(0.02f));
            AssertEulerComponentsWrapped(offset.rotationEulerDegrees);
            AssertMapsAsFirstPersonWrist(grip, desired, offset);
        }

        [Test]
        public void ReconstructsRotationNear180Degrees()
        {
            Pose grip = new Pose(new Vector3(-0.2f, 0.6f, 0.1f), Quaternion.Euler(31f, -48f, 17f));
            Quaternion nearHalfTurn = Quaternion.AngleAxis(179.99f, new Vector3(1f, 2f, -3f).normalized);
            Pose desired = new Pose(grip.position + new Vector3(0.01f, -0.02f, 0.03f), grip.rotation * nearHalfTurn);

            Assert.That(SeatedWristCalibration.TryCalculateOffset(grip, true, desired, out var offset), Is.True);
            AssertEulerComponentsWrapped(offset.rotationEulerDegrees);
            AssertMapsAsFirstPersonWrist(grip, desired, offset, 0.05f);
        }

        private static void AssertHandFixture(Pose grip, Vector3 expectedTranslation, Quaternion expectedRotation)
        {
            Pose desired = new Pose(
                grip.position + grip.rotation * expectedTranslation,
                grip.rotation * expectedRotation);

            Assert.That(SeatedWristCalibration.TryCalculateOffset(grip, true, desired, out var offset), Is.True);
            Assert.That(Vector3.Distance(offset.translationMeters, expectedTranslation), Is.LessThan(0.0001f));
            AssertMapsAsFirstPersonWrist(grip, desired, offset);
        }

        private static void AssertRejected(Pose grip, Pose desired)
        {
            SeatedHandSettings.WristOffset offset = new SeatedHandSettings.WristOffset(
                Vector3.one, Vector3.one);

            Assert.That(SeatedWristCalibration.TryCalculateOffset(grip, true, desired, out offset), Is.False);
            Assert.That(offset.translationMeters, Is.EqualTo(Vector3.zero));
            Assert.That(offset.rotationEulerDegrees, Is.EqualTo(Vector3.zero));
        }

        // Mirrors FirstPersonWrist's grip + gripRotation * translation and gripRotation * Euler(rotation) mapping.
        private static void AssertMapsAsFirstPersonWrist(
            Pose grip,
            Pose desired,
            SeatedHandSettings.WristOffset offset,
            float rotationToleranceDegrees = 0.01f)
        {
            Vector3 mappedPosition = grip.position + grip.rotation * offset.translationMeters;
            Quaternion mappedRotation = grip.rotation * Quaternion.Euler(offset.rotationEulerDegrees);

            Assert.That(Vector3.Distance(mappedPosition, desired.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(mappedRotation, desired.rotation), Is.LessThan(rotationToleranceDegrees));
        }

        private static void AssertEulerComponentsWrapped(Vector3 euler)
        {
            Assert.That(euler.x, Is.GreaterThanOrEqualTo(-180f).And.LessThan(180f));
            Assert.That(euler.y, Is.GreaterThanOrEqualTo(-180f).And.LessThan(180f));
            Assert.That(euler.z, Is.GreaterThanOrEqualTo(-180f).And.LessThan(180f));
        }
    }
}
