using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using StreetCrossing.Traffic;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetTurnShoulderTests
    {
        private static bool Contains(Vector2[] polygon, Vector2 p)
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < -.0001f) return false;
            }
            return true;
        }
        [Test]
        public void ExistingLargestTurningVehicleFootprintHasContinuousRoadCoverageAndCurbClearance()
        {
            StreetScenarioDefinition scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>("Assets/StreetCrossing/Scenarios/S03.asset");
            var aprons = StreetJunctionGeometry.TurnAprons(scenario);
            Assert.That(aprons.Count, Is.GreaterThan(0), "The right turn has an actual inner-corner shoulder outside the old road union.");
            Vector2 southOpening = StreetJunctionGeometry.CurbOpening(scenario, -1, aprons);
            Assert.That(southOpening.y, Is.GreaterThan(-4.75f), "The old -5.6m curb end cuts through the van's inner wheels.");
            foreach (Vector2[] apron in aprons)
            {
                foreach (Vector2 p in apron)
                    Assert.That(Mathf.Abs(p.x), Is.GreaterThan(scenario.conflictHalfWidth + .5f), "Do not expand the protected-crosswalk corridor.");
                Mesh surface = StreetJunctionGeometry.Surface(apron, .021f);
                try { foreach (Vector3 normal in surface.normals) Assert.That(normal.y, Is.GreaterThan(.99f)); }
                finally { Object.DestroyImmediate(surface); }
            }
            float junction = -9 * scenario.DirectionForLane(0);
            for (int routeIndex = scenario.StraightLaneCount; routeIndex < scenario.StraightLaneCount + 2; routeIndex++)
            {
                var path = new VehicleLanePath(scenario, routeIndex);
                for (float distance = path.StopDistance - 3; distance <= path.JunctionExit + 3; distance += .11f)
                for (int across = -2; across <= 2; across++)
                for (int along = -4; along <= 4; along++)
                {
                    Vector3 point = path.Position(distance) + path.RotationAt(distance) * new Vector3(across * .54f, 0, along * .6f);
                    bool covered = Mathf.Abs(point.z) <= scenario.roadHalfWidth + .0001f || Mathf.Abs(point.x - junction) <= StreetLayout.SideRoadHalfWidth + .0001f;
                    foreach (Vector2[] apron in aprons) covered |= Contains(apron, new Vector2(point.x, point.z));
                    Assert.That(covered, Is.True, "The unchanged maximum vehicle footprint leaves visible asphalt at " + point);
                    for (int side = -1; side <= 1; side += 2)
                        if (Mathf.Abs(point.z - side * (scenario.roadHalfWidth + .08f)) <= .09f)
                        {
                            Vector2 opening = StreetJunctionGeometry.CurbOpening(scenario, side, aprons);
                            Assert.That(point.x, Is.InRange(opening.x + .01f, opening.y - .01f));
                        }
                }
            }
            Assert.That(scenario.roadHalfWidth, Is.EqualTo(StreetLayout.RoadHalfWidth)); Assert.That(scenario.crosswalkHalfWidth, Is.EqualTo(1.8f));
        }
    }
}
