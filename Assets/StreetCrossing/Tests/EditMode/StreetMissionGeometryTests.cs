using System;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetMissionGeometryTests
    {
        [Test]
        public void EveryMissionUsesARealOppositeDoorstepAndOrthogonalSidewalkApproach()
        {
            foreach (StreetMissionDefinition mission in StreetMissionCatalog.All)
            for (int leg = 0; leg < (mission.roundTrip ? 2 : 1); leg++)
            {
                int sign = StreetMissionGeometry.ForwardSign(mission, leg);
                Vector3 spawn = StreetMissionGeometry.Spawn(mission, leg);
                Vector3 goal = StreetMissionGeometry.Doorstep(StreetMissionGeometry.GoalKind(mission, leg));
                Assert.Less(sign * spawn.z, -StreetLayout.RoadHalfWidth - 1);
                Assert.Greater(sign * goal.z, StreetLayout.RoadHalfWidth + 1);
                Assert.AreEqual(11.9f, Mathf.Abs(goal.z), .001f);
                CheckSidewalkPath(spawn, StreetMissionGeometry.Approach(spawn, sign, StreetLayout.RoadHalfWidth));
                Vector3[] arrival = StreetMissionGeometry.Arrival(goal, sign, StreetLayout.RoadHalfWidth);
                CheckSidewalkPath(arrival[0], arrival);
                Assert.AreEqual(0, arrival[0].x, "Leave the crosswalk before going along the shop frontages.");
                if (leg == 1)
                {
                    Assert.AreEqual(StreetDestinationKind.Residence, StreetMissionGeometry.GoalKind(mission, leg));
                    Assert.Less(Vector3.Distance(spawn, StreetMissionGeometry.Doorstep(mission.destinationKind)), .03f);
                }
            }
        }
        [Test]
        public void DifferentShopsHaveSeparateDoorstepsAndNoOverlappingArrivalPads()
        {
            var kinds = (StreetDestinationKind[])Enum.GetValues(typeof(StreetDestinationKind));
            for (int i = 0; i < kinds.Length; i++)
            for (int j = i + 1; j < kinds.Length; j++)
                Assert.Greater(Vector3.Distance(StreetMissionGeometry.Doorstep(kinds[i]), StreetMissionGeometry.Doorstep(kinds[j])), 2.8f);
        }
        [Test]
        public void RequiredSidewalkPathsKeepBodyWidthClearanceFromAllBackgroundRoutes()
        {
            foreach (StreetMissionDefinition mission in StreetMissionCatalog.All)
            for (int variant = 0; variant < 3; variant++)
            for (int leg = 0; leg < (mission.roundTrip ? 2 : 1); leg++)
            {
                int district = int.Parse(mission.districtId.Substring(2));
                int count = district == 1 ? 6 : district == 2 ? 10 : 8;
                int seed = mission.SeedForVariant(district == 1 ? 3101 : district == 2 ? 3202 : 3303, variant);
                int sign = StreetMissionGeometry.ForwardSign(mission, leg);
                Vector3 spawn = StreetMissionGeometry.Spawn(mission, leg), goal = StreetMissionGeometry.Doorstep(StreetMissionGeometry.GoalKind(mission, leg));
                foreach (StreetBackgroundRoute actor in StreetBackgroundPopulation.Routes(seed, count))
                {
                    CheckActorPath(spawn, StreetMissionGeometry.Approach(spawn, sign, StreetLayout.RoadHalfWidth), actor);
                    Vector3[] arrival = StreetMissionGeometry.Arrival(goal, sign, StreetLayout.RoadHalfWidth);
                    CheckActorPath(arrival[0], arrival, actor);
                }
            }
        }
        private static void CheckSidewalkPath(Vector3 start, Vector3[] points)
        {
            foreach (Vector3 end in points)
            {
                Assert.IsTrue(Mathf.Abs(start.x - end.x) < .001f || Mathf.Abs(start.z - end.z) < .001f, "Sidewalk guidance must not cut diagonally across furniture or the road.");
                if (Mathf.Abs(start.x - end.x) > .001f)
                    Assert.Greater(Mathf.Min(Mathf.Abs(start.z), Mathf.Abs(end.z)), StreetLayout.RoadHalfWidth + .5f);
                start = end;
            }
        }
        private static void CheckActorPath(Vector3 start, Vector3[] points, StreetBackgroundRoute actor)
        {
            foreach (Vector3 end in points)
            {
                for (int a = 0; a <= 24; a++)
                for (int b = 0; b <= 24; b++)
                {
                    Vector3 player = Vector3.Lerp(start, end, a / 24f);
                    Vector3 neighbour = Vector3.Lerp(actor.start, actor.end, b / 24f);
                    Assert.Greater(Vector3.Distance(player, neighbour), .65f, "Required route must not pass through non-colliding background people.");
                }
                start = end;
            }
        }
    }
}
