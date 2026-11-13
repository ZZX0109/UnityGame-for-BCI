using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Player;
using StreetCrossing.Traffic;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>Regression coverage for the audit's semantic boundaries. Requires Unity; no external hardware.</summary>
    public sealed class AuditRulesTests
    {
        private readonly List<Object> owned = new List<Object>();
        private static readonly CrossingRegion OnCrosswalk = CrossingRegion.RoadwayHazard | CrossingRegion.ProtectedCrosswalk | CrossingRegion.CrossingRoute;

        [TearDown]
        public void CleanUp()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear(); Physics.SyncTransforms();
        }
        private T Keep<T>(T item) where T : Object { owned.Add(item); return item; }
        private static StreetScenarioDefinition Scenario(string id)
        {
            StreetScenarioDefinition scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>("Assets/StreetCrossing/Scenarios/" + id + ".asset");
            Assert.That(scenario, Is.Not.Null, "Missing serialized " + id); return scenario;
        }
        private static void Prepare(CrossingEvaluator evaluation, bool left, bool right)
        {
            evaluation.Tick(CrossingRegion.Approach, TrafficPhase.VehicleGreen, false, false, 0.02f);
            evaluation.Tick(CrossingRegion.Kerb, TrafficPhase.VehicleGreen, true, false, 0.3f);
            if (left) evaluation.Observe(true);
            if (right) evaluation.Observe(false);
            evaluation.Tick(CrossingRegion.Entry | CrossingRegion.CrossingRoute, TrafficPhase.Walk, true, false, 0.02f);
        }
        private static void Finish(CrossingEvaluator evaluation)
        {
            evaluation.Tick(CrossingRegion.Exit | CrossingRegion.CrossingRoute, TrafficPhase.WalkFlashing, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.Destination, TrafficPhase.AllRedAfterWalk, false, false, 0.02f);
        }
        private CrossingZone Zone(string name, CrossingRegion region, Vector3 size)
        {
            GameObject go = Keep(new GameObject(name)); CrossingZone zone = go.AddComponent<CrossingZone>(); zone.region = region; zone.size = size; return zone;
        }

        [Test]
        public void RoadwayProtectedBandAndLegalRouteHaveIndependentVolumeContracts()
        {
            StreetScenarioDefinition scenario = Scenario("S01");
            CrossingZone road = Zone("Roadway hazard", CrossingRegion.RoadwayHazard, new Vector3(86, 3, scenario.roadHalfWidth * 2));
            CrossingZone protection = Zone("Protected crossing", CrossingRegion.ProtectedCrosswalk, new Vector3(scenario.conflictHalfWidth * 2, 3, scenario.roadHalfWidth * 2 + 0.1f));
            CrossingZone route = Zone("Legal route", CrossingRegion.CrossingRoute, new Vector3(scenario.crosswalkHalfWidth * 2, 3, scenario.roadHalfWidth * 2 + 1.8f));
            Vector3 remoteRoad = new Vector3(15, 0, 0);
            Assert.That(road.Contains(remoteRoad), Is.True);
            Assert.That(protection.Contains(remoteRoad), Is.False);
            Assert.That(route.Contains(remoteRoad), Is.False);
            Vector3 protectionMargin = new Vector3((scenario.crosswalkHalfWidth + scenario.conflictHalfWidth) * 0.5f, 0, 0);
            Assert.That(protection.Contains(protectionMargin), Is.True);
            Assert.That(route.Contains(protectionMargin), Is.False, "Signal protection is wider than the marked legal walking route.");
            Assert.That(road.Contains(Vector3.zero) && protection.Contains(Vector3.zero) && route.Contains(Vector3.zero), Is.True);
        }

        [Test]
        public void ProtectedFlagAloneDoesNotStartRoadwayCrossingAndOffRouteHazardDoesNotGrantProtection()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(false, false);
            evaluation.Tick(CrossingRegion.ProtectedCrosswalk, TrafficPhase.VehicleGreen, false, false, 0.02f);
            Assert.That(evaluation.Attempts, Is.Zero);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, false, false, 0.02f);
            Assert.That(evaluation.Attempts, Is.EqualTo(1));
            Assert.That(evaluation.Violations, Is.EqualTo(1));
            Assert.That(evaluation.IsCrossing, Is.True);
            IntersectionPhaseTable table = Keep(ScriptableObject.CreateInstance<IntersectionPhaseTable>());
            IntersectionController intersection = new IntersectionController(table);
            intersection.Tick(0.02f, false, false); // The remote hazard does not occupy the protected zone.
            Assert.That(intersection.CanEnterVehicles, Is.True);
            Finish(evaluation);
            Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [Test]
        public void ProtectedMarginCanHoldTrafficWhileStillRejectingAnIllegalWalkingRoute()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(false, false); Prepare(evaluation, false, false);
            evaluation.Tick(CrossingRegion.RoadwayHazard | CrossingRegion.ProtectedCrosswalk, TrafficPhase.Walk, false, false, 0.02f);
            Assert.That(evaluation.Violations, Is.EqualTo(1));
            IntersectionPhaseTable table = Keep(ScriptableObject.CreateInstance<IntersectionPhaseTable>());
            IntersectionController intersection = new IntersectionController(table); intersection.SetPedestrianOccupancy(true);
            Assert.That(intersection.CanEnterVehicles, Is.False);
            Finish(evaluation); Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [TestCase(true, false, true, false, true)]
        [TestCase(false, true, false, true, true)]
        [TestCase(false, false, false, false, true)]
        [TestCase(true, true, true, false, false)]
        [TestCase(true, true, false, true, false)]
        public void ObservationRequirementsAreScenarioConfigurationNotAutomaticCredit(bool requireLeft, bool requireRight, bool left, bool right, bool safe)
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(requireLeft, requireRight);
            Prepare(evaluation, left, right);
            evaluation.Tick(OnCrosswalk, TrafficPhase.Walk, true, false, 0.02f);
            Finish(evaluation);
            Assert.That(evaluation.ObservationRequirementsSatisfied, Is.EqualTo(safe));
            Assert.That(evaluation.SafetySuccess, Is.EqualTo(safe));
            Assert.That(evaluation.LeftObserved, Is.EqualTo(left));
            Assert.That(evaluation.RightObserved, Is.EqualTo(right));
        }

        [Test]
        public void DefaultObservationRequirementsRejectMissingAndLateObservations()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(); Prepare(evaluation, false, false);
            evaluation.Tick(OnCrosswalk, TrafficPhase.Walk, true, false, 0.02f);
            evaluation.Observe(true); evaluation.Observe(false);
            Assert.That(evaluation.MayRecordPreEntryObservation, Is.False);
            Assert.That(evaluation.LeftObserved || evaluation.RightObserved, Is.False);
            Assert.That(evaluation.ObservationRequirementsSatisfied, Is.False);
            Finish(evaluation);
            Assert.That(evaluation.SafetySuccess, Is.False);
            Assert.That(evaluation.Violations, Is.Zero, "Missing observation has its own explanation rather than inventing a red-light event.");
        }

        [Test]
        public void ReturningToKerbDoesNotRetroactivelyChangeTheFirstEntryObservationSnapshot()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(); Prepare(evaluation, true, false);
            evaluation.Tick(OnCrosswalk, TrafficPhase.Walk, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.Kerb, TrafficPhase.Walk, true, false, 0.02f);
            evaluation.Observe(false);
            Assert.That(evaluation.RightObserved, Is.False);
            Assert.That(evaluation.ObservationRequirementsSatisfied, Is.False);
        }

        [Test]
        public void LegacyYawOnlyCallsCannotFabricateVisibleRoadObservation()
        {
            ObservationTracker tracker = new ObservationTracker();
            tracker.Tick(-90, true, 10); tracker.Tick(90, true, 10);
            Assert.That(tracker.Left || tracker.Right, Is.False);
        }

        [TestCase(-90, -65, true)]
        [TestCase(-90, 65, true)]
        [TestCase(180, 0, true)]
        [TestCase(-90, 0, false)]
        public void SkyBackAndOccludedEvidenceCannotSatisfyObservation(float yaw, float pitch, bool targetVisible)
        {
            ObservationTracker tracker = new ObservationTracker();
            tracker.Tick(new RoadObservationEvidence { yaw = yaw, pitch = pitch, atKerb = true, beforeEntry = true,
                leftTargetVisible = targetVisible, rightTargetVisible = targetVisible }, 2);
            Assert.That(tracker.Left || tracker.Right, Is.False);
        }

        [Test]
        public void VisibilityLossResetsDwellAndRoadwayEvidenceCannotFinishIt()
        {
            ObservationTracker tracker = new ObservationTracker();
            RoadObservationEvidence evidence = new RoadObservationEvidence { yaw = -90, atKerb = true, beforeEntry = true, leftTargetVisible = true };
            tracker.Tick(evidence, 0.2f); evidence.leftTargetVisible = false; tracker.Tick(evidence, 0.2f);
            evidence.leftTargetVisible = true; tracker.Tick(evidence, 0.2f);
            Assert.That(tracker.Left, Is.False);
            evidence.beforeEntry = false; tracker.Tick(evidence, 2);
            Assert.That(tracker.Left, Is.False);
        }

        [Test]
        public void StationaryContactDoesNotFinishPracticeAndMovingContactIsCountedOnce()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(); Prepare(evaluation, true, true);
            evaluation.Tick(OnCrosswalk, TrafficPhase.Walk, true, false, 0.02f);
            List<string> events = new List<string>(); evaluation.Event += (name, detail) => events.Add(name);
            evaluation.Contact(10, 0); evaluation.Contact(10, 0.2f); evaluation.Contact(11, 0.25f);
            Assert.That(evaluation.StationaryContacts, Is.EqualTo(2));
            Assert.That(evaluation.MovingCollisions, Is.Zero); Assert.That(evaluation.Finished, Is.False);
            evaluation.Contact(10, 2); evaluation.Contact(10, 2); evaluation.Contact(12, 3);
            Assert.That(evaluation.MovingCollisions, Is.EqualTo(1)); Assert.That(evaluation.Finished, Is.True);
            Assert.That(evaluation.StationaryContacts, Is.EqualTo(2));
            Assert.That(events.FindAll(item => item == "StationaryContact").Count, Is.EqualTo(2));
            Assert.That(events.FindAll(item => item == "Collision").Count, Is.EqualTo(1));
            Assert.That(events.FindAll(item => item == "CrossingEnd").Count, Is.EqualTo(1));
        }

        [Test]
        public void StationaryContactAndNearMissRemainSeparateFromSafetyArrival()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(); Prepare(evaluation, true, true);
            evaluation.Contact(20, 0); evaluation.NearMiss(21); evaluation.NearMiss(21);
            evaluation.Tick(OnCrosswalk, TrafficPhase.Walk, true, false, 0.02f); Finish(evaluation);
            Assert.That(evaluation.SafetySuccess, Is.True);
            Assert.That(evaluation.NearMisses, Is.EqualTo(1)); Assert.That(evaluation.StationaryContacts, Is.EqualTo(1));
            Assert.That(evaluation.MovingCollisions, Is.Zero);
        }

        private Camera CameraAtIsolatedOrigin(out Transform player)
        {
            GameObject root = Keep(new GameObject("Visibility test pedestrian")); root.transform.position = new Vector3(2000, 1000, 2000); player = root.transform;
            GameObject view = new GameObject("Visibility camera"); view.transform.SetParent(root.transform, false);
            Camera camera = view.AddComponent<Camera>(); camera.fieldOfView = 60; camera.aspect = 16f / 9; camera.nearClipPlane = 0.1f; camera.farClipPlane = 100; return camera;
        }

        [Test]
        public void PhysicsVisibilityRejectsOpaqueOccluderAndAcceptsClearRoadTarget()
        {
            Camera camera = CameraAtIsolatedOrigin(out Transform player);
            Vector3 target = camera.transform.position + Vector3.forward * 20;
            GameObject blocker = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)); blocker.transform.position = camera.transform.position + Vector3.forward * 10; blocker.transform.localScale = new Vector3(4, 4, 1);
            Physics.SyncTransforms();
            Assert.That(RoadObservationVisibility.TargetVisible(camera, new[] { target }, player), Is.False);
            blocker.GetComponent<Collider>().enabled = false; Physics.SyncTransforms();
            Assert.That(RoadObservationVisibility.TargetVisible(camera, new[] { target }, player), Is.True);
            Assert.That(RoadObservationVisibility.TargetVisible(camera, new[] { camera.transform.position - Vector3.forward * 10 }, player), Is.False);
        }

        [Test]
        public void ObservingVehicleTargetMayHitThatVehiclesOwnCollider()
        {
            Camera camera = CameraAtIsolatedOrigin(out Transform player);
            GameObject targetCar = Keep(GameObject.CreatePrimitive(PrimitiveType.Cube)); targetCar.transform.position = camera.transform.position + Vector3.forward * 12; targetCar.transform.localScale = new Vector3(2, 2, 4);
            Physics.SyncTransforms(); Vector3[] target = { targetCar.transform.position };
            Assert.That(RoadObservationVisibility.TargetVisible(camera, target, player), Is.False);
            Assert.That(RoadObservationVisibility.TargetVisible(camera, target, player, targetCar.transform), Is.True);
        }

        [Test]
        public void SerializedScenariosHaveDistinctContentAndGuaranteedWalkingWindows()
        {
            StreetScenarioDefinition first = Scenario("S01"), dense = Scenario("S02"), turns = Scenario("S03");
            HashSet<string> ids = new HashSet<string>();
            foreach (StreetScenarioDefinition scenario in new[] { first, dense, turns })
            {
                Assert.DoesNotThrow(() => scenario.Validate()); Assert.That(ids.Add(scenario.scenarioId), Is.True);
                Assert.That(scenario.requireLeftObservation && scenario.requireRightObservation, Is.True);
                Assert.That(scenario.phaseTable.phases[(int)TrafficPhase.Walk].duration, Is.GreaterThanOrEqualTo(scenario.SafeCrossingSeconds));
                Assert.That(string.IsNullOrWhiteSpace(scenario.title) || string.IsNullOrWhiteSpace(scenario.description), Is.False);
            }
            Assert.That(first.maximumVehicles, Is.EqualTo(2));
            Assert.That(dense.maximumVehicles, Is.GreaterThan(first.maximumVehicles));
            Assert.That(dense.initialVehiclesPerStraightLane, Is.GreaterThan(first.initialVehiclesPerStraightLane));
            Assert.That(dense.maximumSpawnInterval, Is.LessThan(first.minimumSpawnInterval));
            Assert.That(turns.turningTraffic && turns.kerbOcclusion, Is.True);
            Assert.That(turns.turningShare, Is.GreaterThan(0));
        }

        [Test]
        public void VehicleVariantGeneratorReproducesAllThreeVariantKinds()
        {
            TrafficSpawnSequence first = new TrafficSpawnSequence(3202), reset = new TrafficSpawnSequence(3202);
            HashSet<int> variants = new HashSet<int>();
            for (int i = 0; i < 128; i++) { int variant = first.NextVariant(); Assert.That(reset.NextVariant(), Is.EqualTo(variant)); Assert.That(variant, Is.InRange(0, 2)); variants.Add(variant); }
            Assert.That(variants.Count, Is.EqualTo(3));
        }

        [TestCase(2, VehicleRouteKind.RightTurn)]
        [TestCase(3, VehicleRouteKind.LeftTurn)]
        public void TurningRoutesHaveRealCurvatureAndMergeWithTheNearLane(int routeIndex, VehicleRouteKind kind)
        {
            StreetScenarioDefinition scenario = Scenario("S03"); VehicleLanePath route = new VehicleLanePath(scenario, routeIndex);
            Assert.That(route.Kind, Is.EqualTo(kind)); Assert.That(route.IsTurning, Is.True);
            Vector3 startForward = route.RotationAt(0) * Vector3.forward, endForward = route.RotationAt(route.Length - 3) * Vector3.forward;
            Assert.That(Vector3.Angle(startForward, endForward), Is.GreaterThan(75));
            Assert.That(Vector3.Angle(startForward, endForward), Is.LessThan(105));
            Quaternion previous = route.RotationAt(0); bool diagonalHeading = false;
            for (int sample = 1; sample <= 400; sample++)
            {
                Quaternion heading = route.RotationAt(route.Length * sample / 400f);
                Assert.That(Quaternion.Angle(previous, heading), Is.LessThan(20), "The model must steer continuously, not rotate at a corner.");
                Vector3 forward = heading * Vector3.forward; diagonalHeading |= Mathf.Abs(forward.x) > 0.2f && Mathf.Abs(forward.z) > 0.2f; previous = heading;
            }
            Assert.That(diagonalHeading, Is.True);
            Assert.That(route.Position(route.Length - 2).z, Is.EqualTo(-scenario.laneOffset).Within(0.01f));
            Assert.That(route.StopDistance, Is.LessThan(route.JunctionExit));
            Assert.That(route.ConflictEntry, Is.LessThan(route.ConflictExit));
        }

        [Test]
        public void TurningVehicleWithoutAdmissionWaitsBeforeTheJunctionThenCanProceed()
        {
            VehicleLanePath route = new VehicleLanePath(Scenario("S03"), 2); VehicleKinematics car = new VehicleKinematics(); car.Reset(0, 5);
            for (int step = 0; step < 1000; step++) car.Step(0.02f, route.StopDistance, false, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f);
            Assert.That(car.Front, Is.LessThan(route.StopDistance)); Assert.That(car.Speed, Is.LessThan(0.04f));
            for (int step = 0; step < 600; step++) car.Step(0.02f, route.StopDistance, true, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f, 3.1f);
            Assert.That(car.Committed, Is.True); Assert.That(car.Front, Is.GreaterThan(route.JunctionEntry));
            Assert.That(car.Speed, Is.LessThanOrEqualTo(3.101f));
        }

        [Test]
        public void QueuedAmberCarReleasesUnusedCommitmentAfterStoppingBeforeLine()
        {
            VehicleLanePath path = new VehicleLanePath(Scenario("S03"), 0); VehicleKinematics car = new VehicleKinematics();
            car.Reset(path.StopDistance - VehicleKinematics.HalfLength - 8, 7); car.DecideAmber(path.StopDistance, 3, 0.6f);
            Assert.That(car.Committed, Is.True);
            for (int step = 0; step < 1000; step++) car.Step(0.02f, path.StopDistance, false, path.StopDistance + 0.4f, 2.5f, 1.7f, 3, 0.6f);
            Assert.That(car.Front, Is.LessThan(path.StopDistance - 0.15f)); Assert.That(car.Speed, Is.LessThan(0.04f));
            Assert.That(car.Committed, Is.False); Assert.That(path.HasUnclearedCommittedVehicle(car), Is.False);
        }

        [Test]
        public void MergedTurningLeaderConstrainsFasterStraightFollowerDespiteDifferentRouteIds()
        {
            StreetScenarioDefinition scenario = Scenario("S03"); VehicleLanePath straight = new VehicleLanePath(scenario, 0), turn = new VehicleLanePath(scenario, 2);
            VehicleKinematics follower = new VehicleKinematics(), leader = new VehicleKinematics();
            follower.Reset(straight.NearestDistance(new Vector3(2, 0, -scenario.laneOffset)), 8);
            leader.Reset(turn.NearestDistance(new Vector3(16, 0, -scenario.laneOffset)), 2, 2.6f);
            bool slowed = false;
            for (int step = 0; step < 300; step++)
            {
                leader.Step(0.02f, turn.StopDistance, true, float.PositiveInfinity, scenario.queueGap, scenario.acceleration, scenario.comfortableBraking, scenario.reactionTime);
                Vector3 leaderPosition = turn.Position(leader.Distance); Vector3 leaderForward = turn.RotationAt(leader.Distance) * Vector3.forward;
                float rear = VehicleFollowing.ProjectedRear(straight, follower, leaderPosition, leaderForward, leader.BodyHalfLength, 0.9f, 0.9f);
                Assert.That(float.IsPositiveInfinity(rear), Is.False, "A different route id must not hide a leader after merge.");
                follower.Step(0.02f, straight.StopDistance, true, rear, scenario.queueGap, scenario.acceleration, scenario.comfortableBraking, scenario.reactionTime);
                Assert.That(rear - follower.Front, Is.GreaterThanOrEqualTo(scenario.queueGap - 0.002f));
                slowed |= follower.Speed < 4;
            }
            Assert.That(slowed, Is.True); Assert.That(follower.Speed, Is.LessThan(3));
        }

        [Test]
        public void PhysicalFollowingIgnoresOppositeOffLaneAndBehindVehicles()
        {
            StreetScenarioDefinition scenario = Scenario("S03"); VehicleLanePath path = new VehicleLanePath(scenario, 0); VehicleKinematics follower = new VehicleKinematics();
            follower.Reset(path.NearestDistance(new Vector3(2, 0, -scenario.laneOffset)), 6);
            Vector3 ahead = new Vector3(12, 0, -scenario.laneOffset);
            Assert.That(float.IsPositiveInfinity(VehicleFollowing.ProjectedRear(path, follower, ahead, Vector3.left, 2.1f, 0.9f, 0.9f)), Is.True);
            Assert.That(float.IsPositiveInfinity(VehicleFollowing.ProjectedRear(path, follower, ahead + Vector3.forward * 5, Vector3.right, 2.1f, 0.9f, 0.9f)), Is.True);
            Assert.That(float.IsPositiveInfinity(VehicleFollowing.ProjectedRear(path, follower, new Vector3(-3, 0, -scenario.laneOffset), Vector3.right, 2.1f, 0.9f, 0.9f)), Is.True);
        }
    }
}
