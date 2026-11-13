using System;
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
    /// <summary>Pure-rule regression tests. Execution requires the repository's Unity Editor/Test Framework.</summary>
    public sealed class StreetRulesTests
    {
        private readonly List<UnityEngine.Object> temporaryObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void CleanUp()
        {
            foreach (UnityEngine.Object item in temporaryObjects) UnityEngine.Object.DestroyImmediate(item);
            temporaryObjects.Clear();
        }

        private IntersectionPhaseTable ShortTable()
        {
            IntersectionPhaseTable table = ScriptableObject.CreateInstance<IntersectionPhaseTable>();
            temporaryObjects.Add(table);
            foreach (IntersectionPhaseTable.PhaseTiming phase in table.phases) phase.duration = 1;
            table.clearanceWatchdogSeconds = 5;
            return table;
        }

        [Test]
        public void SignalCrossesExactBoundariesInTheSpecifiedSixPhaseOrder()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            List<TrafficPhase> transitions = new List<TrafficPhase>();
            signal.PhaseChanged += transitions.Add;
            signal.Tick(0.99f, false, false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.VehicleGreen));
            Assert.That(signal.Remaining, Is.EqualTo(0.01f).Within(0.00002f));
            signal.Tick(0.01f, false, false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.VehicleAmber));
            signal.Tick(5, false, false);
            CollectionAssert.AreEqual(new[] { TrafficPhase.VehicleAmber, TrafficPhase.AllRedBeforeWalk,
                TrafficPhase.Walk, TrafficPhase.WalkFlashing, TrafficPhase.AllRedAfterWalk, TrafficPhase.VehicleGreen }, transitions);
            Assert.That(signal.Remaining, Is.EqualTo(1).Within(0.00002f));
        }

        [Test]
        public void ZeroPausedClockAndInvalidDeltasCannotAdvanceTheSignal()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            signal.Tick(0.4f, false, false);
            float remaining = signal.Remaining;
            for (int frame = 0; frame < 100; frame++) signal.Tick(0, false, false);
            signal.Tick(-1, false, false);
            signal.Tick(float.NaN, false, false);
            signal.Tick(float.PositiveInfinity, false, false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.VehicleGreen));
            Assert.That(signal.Remaining, Is.EqualTo(remaining));
        }

        [Test]
        public void TenCyclesNeverAuthorizeConflictingNewEntries()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            for (int step = 0; step < 3000; step++)
            {
                signal.Tick(0.02f, false, false);
                Assert.That(signal.CanEnterVehicles && signal.CanEnterPedestrians, Is.False, "step=" + step);
                if (signal.Phase == TrafficPhase.WalkFlashing) Assert.That(signal.CanEnterPedestrians, Is.False);
            }
        }

        [Test]
        public void VehicleClearanceHoldsAllRedBeforeWalkUntilTheRoadIsClear()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            signal.Tick(3.1f, true, false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.AllRedBeforeWalk));
            Assert.That(signal.IsClearanceHeld, Is.True);
            Assert.That(signal.CanEnterPedestrians, Is.False);
            Assert.That(signal.CanEnterVehicles, Is.False);
            signal.Tick(0.02f, false, false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.Walk));
            Assert.That(signal.IsClearanceHeld, Is.False);
        }

        [Test]
        public void PedestrianClearanceCanExtendBeyondTheTimerWithoutReleasingCars()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            signal.Tick(6.1f, false, true);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.AllRedAfterWalk));
            signal.Tick(15, false, true);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.AllRedAfterWalk));
            Assert.That(signal.CanEnterVehicles, Is.False);
            signal.Tick(0.02f, false, false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.VehicleGreen));
            Assert.That(signal.CanEnterVehicles, Is.True);
        }

        [Test]
        public void ClearanceWatchdogReportsOnceAndNeverOverridesOccupiedRoadway()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            int reports = 0;
            signal.ClearanceBlocked += message => reports++;
            signal.Tick(3.1f, true, false);
            signal.Tick(6, true, false);
            signal.Tick(100, true, false);
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.AllRedBeforeWalk));
            Assert.That(signal.CanEnterVehicles || signal.CanEnterPedestrians, Is.False);
            signal.Reset();
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.VehicleGreen));
            Assert.That(signal.IsClearanceHeld, Is.False);
            Assert.That(signal.Remaining, Is.EqualTo(1));
        }

        [Test]
        public void OccupiedRoadwayRevokesNewVehicleAdmissionDuringGreen()
        {
            IntersectionController signal = new IntersectionController(ShortTable());
            signal.SetPedestrianOccupancy(true);
            Assert.That(signal.CanEnterVehicles, Is.False);
            signal.SetPedestrianOccupancy(false);
            Assert.That(signal.CanEnterVehicles, Is.True);
        }

        [Test]
        public void PhaseDataRejectsSimultaneousConflictPermissions()
        {
            IntersectionPhaseTable table = ShortTable();
            table.phases[(int)TrafficPhase.Walk].vehicleEntry = true;
            Assert.Throws<ArgumentException>(() => table.Validate());
        }

        [Test]
        public void RedLightBrakingIsGradualAndStopsTheFrontBeforeTheLine()
        {
            VehicleKinematics car = new VehicleKinematics();
            car.Reset(0, 7);
            bool sawBrakingAtPositiveSpeed = false;
            float previousDistance = car.Distance;
            for (int step = 0; step < 2000; step++)
            {
                car.Step(0.02f, 30, false, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f);
                Assert.That(car.Front, Is.LessThanOrEqualTo(29.881f));
                Assert.That(car.Distance, Is.GreaterThanOrEqualTo(previousDistance));
                previousDistance = car.Distance;
                sawBrakingAtPositiveSpeed |= car.State == VehicleMotionState.Braking && car.Speed > 0.1f;
            }
            Assert.That(sawBrakingAtPositiveSpeed, Is.True);
            Assert.That(car.Speed, Is.LessThan(0.04f));
            Assert.That(car.State, Is.EqualTo(VehicleMotionState.Stopped));
        }

        [Test]
        public void QueuedFollowerMaintainsGapAndRestartsWithBoundedAcceleration()
        {
            VehicleKinematics leader = new VehicleKinematics();
            VehicleKinematics follower = new VehicleKinematics();
            leader.Reset(25, 0);
            follower.Reset(0, 7);
            for (int step = 0; step < 2000; step++)
            {
                follower.Step(0.02f, 30, false, leader.Rear, 2.5f, 1.7f, 3, 0.6f);
                Assert.That(leader.Rear - follower.Front, Is.GreaterThanOrEqualTo(2.499f));
            }
            Assert.That(follower.Speed, Is.LessThan(0.04f));
            float stoppedDistance = follower.Distance;
            follower.Step(0.02f, 30, true, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f);
            Assert.That(follower.Speed, Is.GreaterThan(0));
            Assert.That(follower.Speed, Is.LessThanOrEqualTo(1.7f * 0.02f + 0.001f));
            Assert.That(follower.Distance, Is.GreaterThan(stoppedDistance));
        }

        [Test]
        public void AmberAllowsOnlyTheCarTooCloseToStopToFinishItsCommitment()
        {
            VehicleKinematics near = new VehicleKinematics();
            VehicleKinematics far = new VehicleKinematics();
            near.Reset(20, 7);
            far.Reset(0, 7);
            near.DecideAmber(30, 3, 0.6f);
            far.DecideAmber(30, 3, 0.6f);
            Assert.That(near.Committed, Is.True);
            Assert.That(far.Committed, Is.False);
            for (int step = 0; step < 500; step++)
            {
                near.Step(0.02f, 30, false, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f);
                far.Step(0.02f, 30, false, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f);
            }
            Assert.That(near.Rear, Is.GreaterThan(30));
            Assert.That(far.Front, Is.LessThan(30));
        }

        [Test]
        public void ConflictOccupancyIncludesTheEntireVehicleBody()
        {
            StreetScenarioDefinition scenario = ScriptableObject.CreateInstance<StreetScenarioDefinition>();
            temporaryObjects.Add(scenario);
            VehicleLanePath lane = new VehicleLanePath(scenario, 0);
            VehicleKinematics car = new VehicleKinematics();
            car.Reset(lane.ConflictEntry - VehicleKinematics.HalfLength - 0.01f, 1);
            Assert.That(lane.OccupiesConflict(car), Is.False);
            car.Reset(lane.ConflictEntry - VehicleKinematics.HalfLength + 0.01f, 1);
            Assert.That(lane.OccupiesConflict(car), Is.True);
            car.Reset(lane.ConflictExit + VehicleKinematics.HalfLength - 0.01f, 1);
            Assert.That(lane.OccupiesConflict(car), Is.True, "The rear bumper is still inside.");
            car.Reset(lane.ConflictExit + VehicleKinematics.HalfLength + 0.01f, 1);
            Assert.That(lane.OccupiesConflict(car), Is.False);
            VehicleLanePath opposite = new VehicleLanePath(scenario, 1);
            Assert.That(lane.Direction, Is.EqualTo(-opposite.Direction));
            Assert.That(lane.Position(lane.StopDistance).x, Is.EqualTo(-scenario.stopLineOffset).Within(0.0001f));
            Assert.That(opposite.Position(opposite.StopDistance).x, Is.EqualTo(scenario.stopLineOffset).Within(0.0001f));
        }

        [Test]
        public void SlowCommittedCarUpstreamOfCrosswalkStillHoldsWalkUntilItsRearClears()
        {
            StreetScenarioDefinition scenario = ScriptableObject.CreateInstance<StreetScenarioDefinition>();
            temporaryObjects.Add(scenario);
            VehicleLanePath lane = new VehicleLanePath(scenario, 0);
            VehicleKinematics car = new VehicleKinematics();
            car.Reset(lane.StopDistance - VehicleKinematics.HalfLength - 0.2f, 1);
            car.DecideAmber(lane.StopDistance, 3, 0.6f);
            Assert.That(car.Committed, Is.True);
            Assert.That(lane.OccupiesConflict(car), Is.False, "The car has reserved entry but has not reached the marked crossing.");
            Assert.That(lane.HasUnclearedCommittedVehicle(car), Is.True);
            IntersectionController signal = new IntersectionController(ShortTable());
            signal.Tick(3.1f, lane.HasUnclearedCommittedVehicle(car), false);
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.AllRedBeforeWalk));
            for (int step = 0; step < 2000 && lane.HasUnclearedCommittedVehicle(car); step++)
            {
                car.Step(0.02f, lane.StopDistance, false, float.PositiveInfinity, 2.5f, 1.7f, 3, 0.6f);
                signal.Tick(0.02f, lane.HasUnclearedCommittedVehicle(car), false);
                if (lane.HasUnclearedCommittedVehicle(car)) Assert.That(signal.CanEnterPedestrians, Is.False);
            }
            Assert.That(car.Rear, Is.GreaterThanOrEqualTo(lane.ConflictExit));
            Assert.That(signal.Phase, Is.EqualTo(TrafficPhase.Walk));
        }

        [Test]
        public void IdenticalSeedsReproduceVehicleTimesSpeedsAndColors()
        {
            TrafficSpawnSequence first = new TrafficSpawnSequence(3101);
            TrafficSpawnSequence restarted = new TrafficSpawnSequence(3101);
            float firstTime = 0, restartedTime = 0;
            for (int spawn = 0; spawn < 100; spawn++)
            {
                firstTime += first.Between(7, 9);
                restartedTime += restarted.Between(7, 9);
                Assert.That(restartedTime, Is.EqualTo(firstTime));
                Assert.That(restarted.Between(6, 7), Is.EqualTo(first.Between(6, 7)));
                Assert.That(restarted.NextColor(), Is.EqualTo(first.NextColor()));
            }
        }

        private static RoadObservationEvidence VisibleEvidence(float yaw, bool atKerb = true)
        {
            return new RoadObservationEvidence { yaw = yaw, pitch = 0, atKerb = atKerb,
                beforeEntry = true, leftTargetVisible = true, rightTargetVisible = true };
        }

        [Test]
        public void ObservationRequiresSustainedVisibleHeadingAtKerbAndEmitsEachSideOnce()
        {
            ObservationTracker tracker = new ObservationTracker();
            List<bool> events = new List<bool>();
            tracker.Observed += events.Add;
            tracker.Tick(VisibleEvidence(-90), 0.2f);
            tracker.Tick(VisibleEvidence(-90, false), 1);
            tracker.Tick(VisibleEvidence(-90), 0.2f);
            Assert.That(tracker.Left, Is.False, "Leaving the kerb must reset the unfinished dwell.");
            tracker.Tick(VisibleEvidence(-90), 0.16f);
            tracker.Tick(VisibleEvidence(-90), 2);
            tracker.Tick(VisibleEvidence(0), 5);
            Assert.That(tracker.Right, Is.False);
            tracker.Tick(VisibleEvidence(90), 0.36f);
            tracker.Tick(VisibleEvidence(90), 2);
            CollectionAssert.AreEqual(new[] { true, false }, events);
        }

        private static void Approach(CrossingEvaluator evaluation)
        {
            evaluation.Tick(CrossingRegion.Approach, TrafficPhase.VehicleGreen, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.Kerb, TrafficPhase.VehicleGreen, true, false, 0.5f);
            evaluation.Tick(CrossingRegion.Entry, TrafficPhase.Walk, true, false, 0.02f);
        }

        private static void Arrive(CrossingEvaluator evaluation)
        {
            evaluation.Tick(CrossingRegion.Exit, TrafficPhase.WalkFlashing, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.Destination, TrafficPhase.AllRedAfterWalk, true, false, 0.02f);
        }

        [Test]
        public void WalkEntryRemainsLegalThroughFlashingAndLaterRedUntilDestination()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator(false, false);
            List<string> events = new List<string>();
            evaluation.Event += (name, detail) => events.Add(name);
            Approach(evaluation);
            evaluation.Tick(CrossingRegion.Entry | CrossingRegion.RoadwayHazard, TrafficPhase.Walk, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.WalkFlashing, true, false, 0.02f);
            Assert.That(evaluation.Finished, Is.False);
            Arrive(evaluation);
            Arrive(evaluation);
            Assert.That(evaluation.SafetySuccess, Is.True);
            Assert.That(evaluation.SignalAtEntry, Is.EqualTo(TrafficPhase.Walk));
            Assert.That(evaluation.Violations, Is.Zero);
            Assert.That(events.FindAll(name => name == "SafeArrival").Count, Is.EqualTo(1));
            Assert.That(events.FindAll(name => name == "CrossingStarted").Count, Is.EqualTo(1));
            Assert.That(events.FindAll(name => name == "CrossingEnd").Count, Is.EqualTo(1));
            Assert.That(evaluation.WaitingSeconds, Is.EqualTo(0.5f));
            Assert.That(evaluation.LeftObserved || evaluation.RightObserved, Is.False, "Walking must not invent observation events.");
        }

        [TestCase(TrafficPhase.VehicleGreen)]
        [TestCase(TrafficPhase.WalkFlashing)]
        [TestCase(TrafficPhase.AllRedAfterWalk)]
        public void NonWalkEntryIsMarkedOnceAndCannotBecomeSafeWhenSignalChanges(TrafficPhase phase)
        {
            CrossingEvaluator evaluation = new CrossingEvaluator();
            Approach(evaluation);
            evaluation.Tick(CrossingRegion.Entry | CrossingRegion.RoadwayHazard, phase, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, true, false, 1);
            Arrive(evaluation);
            Assert.That(evaluation.SignalAtEntry, Is.EqualTo(phase));
            Assert.That(evaluation.Violations, Is.EqualTo(1));
            Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [Test]
        public void UnclearedVehicleAtEntryCannotBeForgottenAtArrival()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator();
            Approach(evaluation);
            evaluation.Observe(true); evaluation.Observe(false);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, true, true, 0.02f);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.WalkFlashing, true, false, 0.02f);
            Arrive(evaluation);
            Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [Test]
        public void ObservationNearMissCollisionAndArrivalEventsAreIndependentlyDeduplicated()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator();
            List<string> events = new List<string>();
            evaluation.Event += (name, detail) => events.Add(name);
            Approach(evaluation);
            evaluation.Observe(true);
            evaluation.Observe(true);
            evaluation.Observe(false);
            evaluation.Observe(false);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, true, false, 0.02f);
            evaluation.NearMiss(11);
            evaluation.NearMiss(11);
            evaluation.NearMiss(12);
            evaluation.Collision(11);
            evaluation.Collision(12);
            evaluation.NearMiss(13);
            Arrive(evaluation);
            Assert.That(evaluation.NearMisses, Is.EqualTo(2));
            Assert.That(evaluation.Collisions, Is.EqualTo(1));
            Assert.That(events.FindAll(name => name == "LeftLook").Count, Is.EqualTo(1));
            Assert.That(events.FindAll(name => name == "RightLook").Count, Is.EqualTo(1));
            Assert.That(events.FindAll(name => name == "NearMiss").Count, Is.EqualTo(2));
            Assert.That(events.FindAll(name => name == "Collision").Count, Is.EqualTo(1));
            Assert.That(events.FindAll(name => name.EndsWith("Arrival", StringComparison.Ordinal)).Count, Is.Zero);
            Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [Test]
        public void DestinationAloneOrAnUnpreparedBypassCannotCompleteASafeCrossing()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator();
            evaluation.Tick(CrossingRegion.Destination, TrafficPhase.Walk, true, false, 0.02f);
            Assert.That(evaluation.Finished, Is.False);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, true, false, 0.02f);
            Arrive(evaluation);
            Assert.That(evaluation.Finished, Is.True);
            Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [Test]
        public void LeavingMarkedCrosswalkRecordsOneRouteViolationAndRejectsSafeArrival()
        {
            CrossingEvaluator evaluation = new CrossingEvaluator();
            Approach(evaluation);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, true, false, 0.02f);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, false, false, 0.02f);
            evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.Walk, false, false, 0.02f);
            Arrive(evaluation);
            Assert.That(evaluation.Violations, Is.EqualTo(1));
            Assert.That(evaluation.SafetySuccess, Is.False);
        }

        [Test]
        public void SerializedS01ProvidesACompleteComfortableSpeedCrossingWindow()
        {
            StreetScenarioDefinition scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>("Assets/StreetCrossing/Scenarios/S01.asset");
            Assert.That(scenario, Is.Not.Null, "S01 must be a reviewable serialized scenario, not only runtime defaults.");
            Assert.DoesNotThrow(() => scenario.Validate());
            Assert.That(scenario.phaseTable.phases[(int)TrafficPhase.Walk].duration, Is.GreaterThanOrEqualTo(scenario.SafeCrossingSeconds));
            Assert.That(scenario.playerSpawn.z, Is.LessThan(-scenario.roadHalfWidth));
            Assert.That(scenario.destination.z, Is.GreaterThan(scenario.roadHalfWidth));
        }
    }
}
