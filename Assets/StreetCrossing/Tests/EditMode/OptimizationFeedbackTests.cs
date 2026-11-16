using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Traffic;
using System.Reflection;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class OptimizationFeedbackTests
    {
        [Test]
        public void RiskOverridesInspectionAndExpiryRevealsTheRemainingMessage()
        {
            var notices = new StreetNoticeBoard();
            notices.Set(StreetNoticeKind.Inspection, "sign", 8);
            notices.Set(StreetNoticeKind.Safety, "risk", 4);
            Assert.AreEqual("risk", notices.Resolve(2));
            Assert.AreEqual("sign", notices.Resolve(4));
            Assert.AreEqual("", notices.Resolve(8));
            notices.Set(StreetNoticeKind.Occlusion, "blocked");
            notices.Clear(StreetNoticeKind.Occlusion);
            Assert.AreEqual("", notices.Resolve(9));
        }

        [Test]
        public void TransientDangerExpiry_RevealsPersistentViolationInsteadOfErasingIt()
        {
            var notices = new StreetNoticeBoard();
            notices.Set(StreetNoticeKind.Inspection, "sign", 8);
            notices.Set(StreetNoticeKind.Violation, "route violation");
            notices.Set(StreetNoticeKind.Safety, "near miss", 3);
            Assert.AreEqual("near miss", notices.Resolve(2));
            Assert.AreEqual("route violation", notices.Resolve(3));
            notices.Set(StreetNoticeKind.Safety, "stationary contact", 7);
            Assert.AreEqual("stationary contact", notices.Resolve(6));
            Assert.AreEqual("route violation", notices.Resolve(7));
        }

        [Test]
        public void TerminalResult_SurvivesCrossingNoticeClearsAndResetRemovesEverything()
        {
            var notices = new StreetNoticeBoard();
            notices.Set(StreetNoticeKind.Violation, "violation");
            notices.Set(StreetNoticeKind.Result, "collision");
            notices.Set(StreetNoticeKind.Safety, "temporary risk", 4);
            notices.Set(StreetNoticeKind.ClearanceWatchdog, "waiting");
            Assert.AreEqual("collision", notices.Resolve(2));
            notices.Clear(StreetNoticeKind.Violation);
            notices.Clear(StreetNoticeKind.Safety);
            Assert.AreEqual("collision", notices.Resolve(100));
            notices.Reset();
            Assert.AreEqual("", notices.Resolve(0));
        }

        [Test]
        public void FreshnessRequiresRenewingBothSidesButDoesNotDuplicateObservationCredit()
        {
            var tracker = new ObservationTracker();
            int events = 0; tracker.Observed += _ => events++;
            var left = new RoadObservationEvidence { atKerb = true, beforeEntry = true, yaw = -90, leftTargetVisible = true };
            var right = new RoadObservationEvidence { atKerb = true, beforeEntry = true, yaw = 90, rightTargetVisible = true };
            tracker.Tick(left, .4f); tracker.Tick(right, .4f);
            tracker.Tick(new RoadObservationEvidence(), 10.1f);
            Assert.IsTrue(tracker.NeedsRefresh(10));
            tracker.Tick(left, .4f);
            Assert.IsTrue(tracker.NeedsRefresh(10), "The right observation remains stale.");
            tracker.Tick(right, .4f);
            Assert.IsFalse(tracker.NeedsRefresh(10));
            Assert.AreEqual(2, events);
        }

        [Test]
        public void ObservationFreshnessCanBeRenewedWithoutChangingScoringOrEmittingDuplicateCredit()
        {
            var tracker = new ObservationTracker();
            int events = 0; tracker.Observed += _ => events++;
            var left = new RoadObservationEvidence { atKerb = true, beforeEntry = true, yaw = -90, leftTargetVisible = true };
            tracker.Tick(left, 0.4f);
            tracker.Tick(new RoadObservationEvidence(), 10.1f);
            Assert.IsTrue(tracker.Left);
            Assert.IsTrue(tracker.NeedsRefresh(10));
            tracker.Tick(left, 0.4f);
            Assert.IsFalse(tracker.NeedsRefresh(10));
            Assert.AreEqual(1, events);
            tracker.Tick(new RoadObservationEvidence(), 0);
            Assert.IsFalse(tracker.NeedsRefresh(10), "A paused clock must not age evidence.");
        }

        [Test]
        public void FullVisibilityBufferDoesNotDropAnOccludingSurface()
        {
            var player = new GameObject("player");
            var cameraObject = new GameObject("camera"); cameraObject.transform.SetParent(player.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1, 0);
            var blockers = new GameObject[40];
            var blocker = new GameObject("actual occluder");
            FieldInfo buffer = typeof(RoadObservationVisibility).GetField("hits", BindingFlags.NonPublic | BindingFlags.Static);
            var previousBuffer = (RaycastHit[])buffer.GetValue(null);
            buffer.SetValue(null, new RaycastHit[16]);
            try
            {
                for (int i = 0; i < blockers.Length; i++)
                {
                    // Every initial hit can be an ignored player child. The
                    // real occluder must still be found beyond a full buffer.
                    blockers[i] = new GameObject("ignored player child");
                    blockers[i].transform.SetParent(player.transform);
                    blockers[i].transform.position = new Vector3(0, 1, 2 + i * .1f);
                    blockers[i].AddComponent<BoxCollider>().size = new Vector3(1, 1, .05f);
                }
                blocker.transform.position = new Vector3(0, 1, 7);
                blocker.AddComponent<BoxCollider>().size = new Vector3(1, 1, .05f);
                Physics.SyncTransforms();
                Assert.IsFalse(RoadObservationVisibility.PointVisible(camera, new Vector3(0, 1, 10), player.transform));
                Assert.GreaterOrEqual(((RaycastHit[])buffer.GetValue(null)).Length, 64);
                Object.DestroyImmediate(blocker);
                Physics.SyncTransforms();
                Assert.IsTrue(RoadObservationVisibility.PointVisible(camera, new Vector3(0, 1, 10), player.transform));
            }
            finally
            {
                buffer.SetValue(null, previousBuffer);
                if (blocker) Object.DestroyImmediate(blocker);
                foreach (GameObject child in blockers) if (child) Object.DestroyImmediate(child);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void FullContactBuffers_RetryForSweptAndRestingPlayerContacts()
        {
            var scenario = ScriptableObject.CreateInstance<StreetScenarioDefinition>();
            var vehicleObject = new GameObject("vehicle", typeof(BoxCollider), typeof(VehicleController));
            var playerObject = new GameObject("player", typeof(CharacterController));
            var clutter = new GameObject[40];
            try
            {
                var lane = new VehicleLanePath(scenario, 0);
                var vehicle = vehicleObject.GetComponent<VehicleController>();
                vehicle.Initialize(lane, 1, 0, 5);
                var player = playerObject.GetComponent<CharacterController>();
                player.height = 1.8f; player.radius = .3f; player.center = Vector3.up * .9f;
                // Recreate the native character actor at its authored pose.
                // EditMode does not run the player's normal first Move update.
                player.enabled = false;
                playerObject.transform.position = lane.Position(5);
                player.enabled = true;
                player.Move(Vector3.zero);
                for (int i = 0; i < clutter.Length; i++)
                {
                    clutter[i] = new GameObject("sweep clutter", typeof(BoxCollider));
                    clutter[i].transform.position = lane.Position(2.5f + i * .18f) + Vector3.up * .8f;
                    clutter[i].GetComponent<BoxCollider>().size = Vector3.one * .15f;
                }
                Vector3 old = vehicle.transform.position;
                vehicle.CaptureMotion();
                vehicle.Motion.Reset(10, 5, vehicle.Motion.BodyHalfLength);
                vehicle.ApplyPosition(); vehicle.UpdateVisuals(0);
                Physics.SyncTransforms();
                Assert.IsTrue(vehicle.SweptContact(player, old),
                    "The sweep must include the initialized CharacterController after retrying full buffers.");
                var sweep = typeof(VehicleController).GetField("sweepHits", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.GreaterOrEqual(((RaycastHit[])sweep.GetValue(vehicle)).Length, 64);
                vehicle.Motion.Reset(5, 0, vehicle.Motion.BodyHalfLength);
                vehicle.ApplyPosition(); vehicle.CaptureMotion();
                foreach (GameObject item in clutter) item.transform.position = lane.Position(5) + Vector3.up * .8f;
                Physics.SyncTransforms();
                Assert.IsTrue(vehicle.SweptContact(player, vehicle.transform.position));
                var overlap = typeof(VehicleController).GetField("overlapHits", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.GreaterOrEqual(((Collider[])overlap.GetValue(vehicle)).Length, 64);
                Assert.AreEqual(0, vehicle.ContactSpeed);
            }
            finally
            {
                foreach (GameObject item in clutter) if (item) Object.DestroyImmediate(item);
                Object.DestroyImmediate(vehicleObject); Object.DestroyImmediate(playerObject); Object.DestroyImmediate(scenario);
            }
        }
    }
}
