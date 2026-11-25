using NUnit.Framework;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using StreetCrossing.Integration;
using StreetCrossing.Player;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Integration.Tests
{
    /// <summary>
    /// EditMode coverage for StreetCrossingFullBodyBinding: wiring, differential
    /// locomotion feed and the B/V toggles. Pure edit mode — no play mode, no graphics.
    /// </summary>
    [TestFixture]
    public class FullBodyBindingTests
    {
        GameObject _player;
        GameObject _bindingHost;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("TestPedestrianPlayer");
            _player.AddComponent<PedestrianPlayerController>(); // RequireComponent adds CharacterController
        }

        [TearDown]
        public void TearDown()
        {
            // Editor-safe cleanup; body instance is a child of _player and dies with it.
            if (_bindingHost != null)
                Object.DestroyImmediate(_bindingHost);
            if (_player != null)
                Object.DestroyImmediate(_player);
        }

        StreetCrossingFullBodyBinding CreateWiredBinding()
        {
            _bindingHost = new GameObject("TestFullBodyBinding");
            var binding = _bindingHost.AddComponent<StreetCrossingFullBodyBinding>();
            Assert.IsTrue(binding.EnsureWiring(), "EnsureWiring must succeed once a PedestrianPlayerController exists.");
            return binding;
        }

        [Test]
        public void EnsureWiring_InstantiatesBodyUnderPlayerRoot_WithAdapterAndVisibleDesktopHands()
        {
            var binding = CreateWiredBinding();

            Assert.IsNotNull(binding.BodyInstance, "Full-body prefab must be instantiated at runtime.");
            Assert.IsTrue(binding.BodyInstance.transform.IsChildOf(binding.PlayerRoot),
                "Body must be parented under the player root so ObservationTracker's IsChildOf(playerRoot) filter stays valid.");
            Assert.IsNotNull(binding.Adapter, "GameIntegrationAdapter must be added at runtime (prefab ships without it).");
            Assert.IsNotNull(binding.Adapter.poseController, "Adapter must auto-wire the FullBodyPoseController.");
            Assert.IsNotNull(binding.Adapter.locomotion, "Adapter must auto-wire the LowerBodyPoseController.");
            Assert.IsNotNull(binding.Adapter.visibility, "Adapter must auto-wire the FirstPersonVisibility.");
            Assert.IsNotNull(binding.Adapter.appearance, "Adapter must auto-wire the FullBodyAppearanceController.");
            Assert.IsTrue(binding.Adapter.visibility.FirstPersonActive, "Binding starts in first-person (head hidden).");
            Assert.AreEqual(FullBodyPoseController.PoseDesktopHandsVisible, binding.Adapter.poseController.CurrentPoseName,
                "Desktop first-person wiring must raise both hands.");
            Assert.IsTrue(binding.EnsureWiring(), "EnsureWiring must be idempotent.");
            Assert.AreSame(binding.Adapter, binding.Adapter, "Adapter reference must survive idempotent re-entry.");
        }

        [Test]
        public void SingleTrackedWristUsesWorldSpaceAndLeavesOtherHandIndependent()
        {
            var binding = CreateWiredBinding();
            var rig = binding.Adapter.poseController;
            binding.PlayerRoot.SetPositionAndRotation(new Vector3(3, 0.2f, -4), Quaternion.Euler(0, 80, 0));
            Vector3 target = rig.leftUpperArm.position + binding.PlayerRoot.rotation * new Vector3(-0.06f, -0.12f, 0.34f);
            Vector3 otherHand = rig.rightWrist.position;
            Vector3 elbowOffset = rig.leftElbow.localPosition;
            Vector3 wristOffset = rig.leftWrist.localPosition;
            Assert.IsTrue(rig.ApplyTrackedWrist(SeatedHandSide.Left, target));
            Assert.Less(Vector3.Distance(target, rig.leftWrist.position), 0.001f);
            Assert.AreEqual(otherHand, rig.rightWrist.position);
            Assert.AreEqual(elbowOffset, rig.leftElbow.localPosition);
            Assert.AreEqual(wristOffset, rig.leftWrist.localPosition);
        }

        [Test]
        public void TickLocomotion_ForwardMotion_ReachesLocomotionAsBodySpaceVelocity()
        {
            var binding = CreateWiredBinding();
            binding.TickLocomotion(0.1f); // prime the differential baseline

            binding.PlayerRoot.position += Vector3.forward * 0.05f;
            binding.TickLocomotion(0.1f);

            var fed = binding.Adapter.locomotion.PlanarVelocityMetersPerSecond;
            AssertAreEqual(0.5f, fed.y, 1e-4f, "Forward feed (body-space +Y) = displacement / dt.");
            AssertAreEqual(0f, fed.x, 1e-4f, "No lateral (body-space +X) component for straight motion.");

            // Explicit integration (edit mode): PathDistanceMeters is what WalkCycleDriver
            // consumes for its walk-cycle phase, so it must have advanced by the step length.
            binding.Adapter.locomotion.Integrate(0.1f);
            AssertAreEqual(0.05f, binding.Adapter.locomotion.PathDistanceMeters, 1e-4f,
                "Integrated path distance must grow by |v| * dt.");

            binding.TickLocomotion(0.1f); // no further motion -> idle feed
            AssertAreEqual(0f, binding.Adapter.locomotion.PlanarVelocityMetersPerSecond.y, 1e-4f,
                "Idle frames must feed a zero vector so the walk cycle settles.");
        }

        [Test]
        public void TickLocomotion_Yaw_ScalesWithRotation()
        {
            var binding = CreateWiredBinding();
            binding.TickLocomotion(0.1f); // prime

            binding.PlayerRoot.rotation = Quaternion.Euler(0f, 30f, 0f); // 30 deg CCW over 0.1 s
            binding.TickLocomotion(0.1f);

            AssertAreEqual(300f, binding.Adapter.locomotion.YawDegreesPerSecond, 1e-3f,
                "Yaw feed is degrees/second (30 deg / 0.1 s).");
        }

        [Test]
        public void Toggles_ChangeBodyOnly_LeaveHostStateUntouched()
        {
            var binding = CreateWiredBinding();
            Vector3 posBefore = binding.PlayerRoot.position;
            Quaternion rotBefore = binding.PlayerRoot.rotation;

            binding.ToggleFirstPerson();
            Assert.IsFalse(binding.Adapter.visibility.FirstPersonActive, "First B press reveals the head.");

            binding.ToggleFirstPerson();
            Assert.IsTrue(binding.Adapter.visibility.FirstPersonActive, "Second B press hides the head again.");

            binding.CycleAppearance();
            Assert.AreEqual(FullBodyAppearanceMode.Blue, binding.Adapter.appearance.Mode, "First V press switches to Blue.");
            binding.CycleAppearance();
            Assert.AreEqual(FullBodyAppearanceMode.Realistic, binding.Adapter.appearance.Mode, "Second V press restores Realistic.");

            Assert.AreEqual(posBefore, binding.PlayerRoot.position, "Toggles must never move the host player.");
            Assert.AreEqual(rotBefore, binding.PlayerRoot.rotation, "Toggles must never rotate the host player.");
        }

        static void AssertAreEqual(float expected, float actual, float delta, string message)
        {
            // Bundled NUnit (com.unity.ext.nunit 1.0.6) has no float/Vector3 overload of AreEqual.
            Assert.That(actual, Is.EqualTo(expected).Within(delta), message);
        }
    }
}
