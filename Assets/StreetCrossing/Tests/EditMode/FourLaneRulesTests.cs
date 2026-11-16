using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Traffic;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class FourLaneRulesTests
    {
        [Test]
        public void FourLaneCentersAreDistinctAndSymmetric()
        {
            float[] expected = { -4.95f, -1.65f, 1.65f, 4.95f };
            Assert.AreEqual(4, StreetLayout.StraightLaneCount);
            for (int lane = 0; lane < expected.Length; lane++)
            {
                Assert.AreEqual(expected[lane], StreetLayout.LaneZ(lane), .0001f);
                if (lane > 0) Assert.AreEqual(StreetLayout.LaneWidth,
                    StreetLayout.LaneZ(lane) - StreetLayout.LaneZ(lane - 1), .0001f);
            }
            Assert.Throws<System.ArgumentOutOfRangeException>(() => StreetLayout.LaneZ(4));
        }

        [Test]
        public void SidewalkOffsetMatchesTheNewSpawnAndDestination()
        {
            Assert.AreEqual(6.6f, StreetLayout.RoadHalfWidth);
            Assert.AreEqual(3.3f, StreetLayout.SideRoadHalfWidth);
            Assert.AreEqual(StreetLayout.SpawnZ, StreetLayout.SidewalkZ(-8), .0001f);
            Assert.AreEqual(StreetLayout.DestinationZ, StreetLayout.SidewalkZ(7.5f), .0001f);
            Assert.AreEqual(0, StreetLayout.SidewalkZ(0));
        }

        [TestCase("S01", 4, 4)] [TestCase("S02", 4, 8)] [TestCase("S03", 6, 6)]
        public void SerializedScenarioHasRealLanesAndSufficientTrafficCapacity(string id, int paths, int initialCars)
        {
            var scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>("Assets/StreetCrossing/Scenarios/" + id + ".asset");
            Assert.DoesNotThrow(scenario.Validate);
            Assert.AreEqual(paths, scenario.StraightLaneCount + (scenario.turningTraffic ? 2 : 0));
            Assert.AreEqual(initialCars, scenario.StraightLaneCount * scenario.initialVehiclesPerStraightLane + (scenario.turningTraffic ? 2 : 0));
            Assert.GreaterOrEqual(scenario.maximumVehicles, initialCars);
            Assert.GreaterOrEqual(scenario.phaseTable.phases[(int)TrafficPhase.Walk].duration, scenario.SafeCrossingSeconds);
            for (int lane = 0; lane < scenario.StraightLaneCount; lane++)
            {
                var path = new VehicleLanePath(scenario, lane);
                Assert.IsFalse(path.IsTurning);
                Assert.AreEqual(scenario.LaneZ(lane), path.Position(20).z, .0001f);
                Assert.AreEqual(lane < 2 ? 1 : -1, path.Direction);
                Assert.Less(Mathf.Abs(path.Z) + 1.08f, scenario.roadHalfWidth);
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void TurningRoutesMergeIntoOuterLaneWithMirroredTravelDirection(bool rightHand)
        {
            var scenario = Object.Instantiate(AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>("Assets/StreetCrossing/Scenarios/S03.asset"));
            try
            {
                scenario.rightHandTraffic = rightHand;
                Assert.AreEqual(scenario.DirectionForLane(0), scenario.DirectionForLane(1));
                Assert.AreEqual(-scenario.DirectionForLane(0), scenario.DirectionForLane(2));
                Assert.AreEqual(scenario.DirectionForLane(2), scenario.DirectionForLane(3));
                for (int turn = 0; turn < 2; turn++)
                {
                    var path = new VehicleLanePath(scenario, scenario.StraightLaneCount + turn);
                    Assert.IsTrue(path.IsTurning);
                    Assert.AreEqual(turn == 0 ? VehicleRouteKind.RightTurn : VehicleRouteKind.LeftTurn, path.Kind);
                    Assert.AreEqual(scenario.LaneZ(0), path.Position(path.Length - 1).z, .001f);
                    Assert.AreEqual(scenario.DirectionForLane(0), path.Direction);
                    Assert.Less(path.StopDistance, path.JunctionExit);
                }
            }
            finally { Object.DestroyImmediate(scenario); }
        }

        [Test]
        public void CrossingSafetyRequiresAllFourRoadSectionsBeforeFarSideArrival()
        {
            var evaluator = new CrossingEvaluator();
            evaluator.Observe(true); evaluator.Observe(false);
            evaluator.Tick(CrossingRegion.Approach | CrossingRegion.Kerb | CrossingRegion.Entry,
                TrafficPhase.Walk, true, false, .1f);
            for (int lane = 0; lane < StreetLayout.StraightLaneCount; lane++)
            {
                evaluator.Tick(CrossingRegion.RoadwayHazard | CrossingRegion.CrossingRoute,
                    lane == 0 ? TrafficPhase.Walk : TrafficPhase.WalkFlashing, true, false, .8f);
                Assert.IsTrue(evaluator.IsCrossing);
                Assert.IsFalse(evaluator.Finished);
            }
            // The legally begun crossing remains valid during flashing/red.
            // A far-side exit is still required before the destination counts.
            evaluator.Tick(CrossingRegion.Exit | CrossingRegion.CrossingRoute, TrafficPhase.AllRedAfterWalk, true, false, .1f);
            evaluator.Tick(CrossingRegion.Destination, TrafficPhase.AllRedAfterWalk, true, false, .1f);
            Assert.IsTrue(evaluator.SafetySuccess);
            Assert.AreEqual(1, evaluator.Attempts);
        }
    }
}
