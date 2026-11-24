using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Neuralife.XR.Tests
{
    public sealed class PicoSafetyTests
    {
        [Test]
        public void HeldButtonAfterReconnectRequiresReleaseBeforeNewPress()
        {
            var button = new ReleaseToArmButton();
            button.Sample(true, true); Assert.IsFalse(button.Pressed);
            button.Sample(true, false); button.Sample(true, true); Assert.IsTrue(button.Pressed);
            button.Sample(true, true); Assert.IsFalse(button.Pressed);
            button.Sample(false, true); Assert.IsTrue(button.Released); Assert.IsFalse(button.Held);
            button.Sample(true, true); Assert.IsFalse(button.Pressed);
            button.Sample(true, false); button.Sample(true, true); Assert.IsTrue(button.Pressed);
        }

        [Test]
        public void HeldMovementAfterReconnectCannotMoveUntilStickIsCentred()
        {
            var axis = new CentreToArmAxis();
            Assert.AreEqual(Vector2.zero, axis.Sample(true, Vector2.up));
            axis.Sample(true, Vector2.zero); Assert.AreEqual(Vector2.up, axis.Sample(true, Vector2.up));
            Assert.AreEqual(Vector2.zero, axis.Sample(false, Vector2.up));
            Assert.AreEqual(Vector2.zero, axis.Sample(true, Vector2.up));
            axis.Sample(true, Vector2.zero); Assert.AreEqual(Vector2.up, axis.Sample(true, Vector2.up));
        }

        [TestCase(1, 30)] [TestCase(-1, -30)]
        public void SnapTurnFiresOncePerDeflectionAndNeedsNeutralAfterTrackingLoss(float value, float degrees)
        {
            var turn = new SnapTurnLatch();
            Assert.AreEqual(0, turn.Sample(true, value));
            turn.Sample(true, 0); Assert.AreEqual(degrees, turn.Sample(true, value));
            for (int i = 0; i < 100; i++) Assert.AreEqual(0, turn.Sample(true, value));
            turn.Sample(false, value); Assert.AreEqual(0, turn.Sample(true, value));
            turn.Sample(true, 0); Assert.AreEqual(degrees, turn.Sample(true, value));
        }

        [TestCase(0.1f, 0.1f)] [TestCase(float.NaN, 0)] [TestCase(0, float.PositiveInfinity)]
        public void DeadzoneAndNonFiniteAxesAreNeutral(float x, float y)
            => Assert.AreEqual(Vector2.zero, PicoControls.Deadzone(new Vector2(x, y)));

        [Test]
        public void DiagonalStickCannotExceedUnitSpeed()
            => Assert.AreEqual(1, PicoControls.Deadzone(new Vector2(1, 1)).magnitude, 0.0001f);

        [Test]
        public void LosingEitherControllerStopsOnlyItsCommands()
        {
            var controls = new PicoControls();
            controls.Sample(true, true, true, Vector2.zero, Vector2.zero, false, false, false, false, false, false);
            controls.Sample(true, true, true, Vector2.up, Vector2.right, true, true, true, true, true, true);
            Assert.AreEqual(Vector2.up, controls.Move); Assert.IsTrue(controls.Select.Pressed);
            controls.Sample(true, false, true, Vector2.up, Vector2.zero, false, false, false, true, true, false);
            Assert.AreEqual(Vector2.zero, controls.Move); Assert.IsFalse(controls.Pause.Held);
            controls.Sample(false, true, true, Vector2.up, Vector2.right, true, true, true, true, true, true);
            Assert.AreEqual(Vector2.zero, controls.Move); Assert.IsFalse(controls.Select.Held); Assert.AreEqual(0, controls.TurnDegrees);
        }


        [Test]
        public void RecenterAndSnapMoveTheOriginWithoutOverwritingTrackedLocalPose()
        {
            bool preview = SessionState.GetBool(PicoXR.PreviewKey, false);
            var root = new GameObject("XR origin test");
            try
            {
                SessionState.SetBool(PicoXR.PreviewKey, true);
                Set("Ready", true); Set("HeadTracked", true); Set("FloorTracking", false);
                Vector3 raw = new Vector3(1.2f, 1.72f, -0.3f);
                Quaternion rotation = Quaternion.Euler(-18, 42, 0);
                Set("HeadPosition", raw); Set("HeadRotation", rotation);
                Camera camera = new GameObject("XR test view").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
                PicoRig rig = PicoRig.Attach(camera, root.transform); rig.Recenter();
                Assert.AreEqual(raw, camera.transform.localPosition);
                Assert.Less(Quaternion.Angle(rotation, camera.transform.localRotation), 0.001f);
                Assert.AreEqual(1.65f, camera.transform.position.y, 0.001f);
                Vector3 before = camera.transform.position;
                rig.SnapTurn(30);
                Assert.Less(Vector3.Distance(before, camera.transform.position), 0.001f);
                Assert.Less(Quaternion.Angle(rotation, camera.transform.localRotation), 0.001f);
                Set("FloorTracking", true); rig.Recenter();
                Assert.AreEqual(raw.y, camera.transform.position.y, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                SessionState.SetBool(PicoXR.PreviewKey, preview);
                Set("Ready", false); Set("HeadTracked", false); Set("FloorTracking", false);
            }
        }

        private static void Set(string name, object value)
            => typeof(PicoXR).GetProperty(name, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
    }
}
