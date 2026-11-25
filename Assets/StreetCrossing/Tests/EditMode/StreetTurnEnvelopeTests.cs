using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using StreetCrossing.Traffic;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// §6.2 static lock for the unchanged S03 turning envelope. Re-derives the two
    /// Bézier turning routes from <see cref="VehicleLanePath"/> and measures the
    /// clearance-inclusive swept envelope (same footprint construction as
    /// <see cref="StreetJunctionGeometry.TurnAprons"/>) against
    /// (a) the two chamfered corner-building prisms placed by
    /// StreetWorld.BuildFurnitureBounds and (b) the junction-throat kerb faces.
    /// Complements the in-scene physics overlap test
    /// StreetGeometryClearanceTests.S03TurnEnvelopeDoesNotPenetrateCurbsFurnitureOrBuildings
    /// without loading the scene, so a regression fails fast in EditMode.
    /// </summary>
    public class StreetTurnEnvelopeTests
    {
        private const string ScenarioPath = "Assets/StreetCrossing/Scenarios/S03.asset";
        private const float SampleStep = .08f;

        // Corner-building placement mirrored from StreetWorld.BuildFurnitureBounds
        // (the turning-traffic "Corner building bounds" fixtures). If that builder
        // moves these fixtures, update the mirrors and re-audit; the PlayMode
        // overlap test remains the authoritative in-scene guard.
        private const float CornerWidth = 6.6f;
        private const float CornerDepth = 4f;
        private const float CornerJunctionOffset = 8.4f;
        private const float CornerSidewalkZ = 12f;
        private const float NorthCornerHeight = 10.2f;
        private const float SouthCornerHeight = 8.8f;
        private const float NorthCornerCut = 1.15f;
        private const float SouthCornerCut = .7f;

        // Documented tightest margin anywhere on the turning envelope: the left-turn
        // outer rear corner against the junction-throat kerb face (measured 0.41 m).
        private const float ThroatKerbMarginFloor = .4f;

        private static readonly Vector2[] CornerSigns =
            { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) };

        private static StreetScenarioDefinition LoadS03()
        {
            var scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>(ScenarioPath);
            Assert.NotNull(scenario, "S03 scenario asset missing at " + ScenarioPath);
            Assert.IsTrue(scenario.turningTraffic, "S03 must keep turning traffic for this envelope lock.");
            return scenario;
        }

        /// <summary>World XZ outline of one corner building, from the same production mesh.</summary>
        private static List<Vector2> CornerFootprint(int side, float junctionX, float direction)
        {
            float height = side > 0 ? NorthCornerHeight : SouthCornerHeight;
            float cut = side > 0 ? NorthCornerCut : SouthCornerCut;
            Mesh mesh = StreetDetailMesh.ChamferedCorner(new Vector3(CornerWidth, height, CornerDepth), cut, side > 0);
            try
            {
                var centre = new Vector3(junctionX - CornerJunctionOffset * direction, 0,
                    StreetLayout.SidewalkZ(side * CornerSidewalkZ));
                var rotation = Quaternion.Euler(0, side > 0 ? 0 : 180, 0);
                var points = new List<Vector2>();
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 world = centre + rotation * new Vector3(v.x, 0, v.z);
                    var p = new Vector2(world.x, world.z);
                    if (!points.Exists(q => (q - p).sqrMagnitude < .000001f)) points.Add(p);
                }
                return Hull(points);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static IEnumerable<Vector2> EnvelopeCorners(VehicleLanePath path, float distance, float halfWidth, float halfLength)
        {
            Vector3 centre = path.Position(distance);
            Quaternion heading = path.RotationAt(distance);
            foreach (Vector2 sign in CornerSigns)
            {
                Vector3 p = centre + heading * new Vector3(sign.x * halfWidth, 0, sign.y * halfLength);
                yield return new Vector2(p.x, p.z);
            }
        }

        [Test]
        public void S03CornerBuildingFootprintsMatchDocumentedPrisms()
        {
            StreetScenarioDefinition scenario = LoadS03();
            float direction = scenario.DirectionForLane(0);
            float junctionX = -9 * direction;
            for (int side = -1; side <= 1; side += 2)
            {
                List<Vector2> footprint = CornerFootprint(side, junctionX, direction);
                Assert.AreEqual(5, footprint.Count, "Corner footprint must stay the five-sided chamfered outline.");

                float expectedCentreX = junctionX - CornerJunctionOffset * direction;
                float expectedCentreZ = StreetLayout.SidewalkZ(side * CornerSidewalkZ);
                float xMin = float.PositiveInfinity, xMax = float.NegativeInfinity;
                float zMin = float.PositiveInfinity, zMax = float.NegativeInfinity;
                foreach (Vector2 p in footprint)
                {
                    xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                    zMin = Mathf.Min(zMin, p.y); zMax = Mathf.Max(zMax, p.y);
                }
                Assert.AreEqual(expectedCentreX - CornerWidth * .5f, xMin, .001f, "corner prism xMin");
                Assert.AreEqual(expectedCentreX + CornerWidth * .5f, xMax, .001f, "corner prism xMax");
                Assert.AreEqual(expectedCentreZ - CornerDepth * .5f, zMin, .001f, "corner prism zMin");
                Assert.AreEqual(expectedCentreZ + CornerDepth * .5f, zMax, .001f, "corner prism zMax");

                float cut = side > 0 ? NorthCornerCut : SouthCornerCut;
                float shortest = float.PositiveInfinity;
                for (int i = 0, j = footprint.Count - 1; i < footprint.Count; j = i++)
                    shortest = Mathf.Min(shortest, Vector2.Distance(footprint[j], footprint[i]));
                Assert.AreEqual(cut * Mathf.Sqrt(2f), shortest, .001f,
                    "The shortest outline edge must remain the junction-facing chamfer.");
            }
        }

        [Test]
        public void S03TurningEnvelopeKeepsClearanceFromCornerBuildings()
        {
            StreetScenarioDefinition scenario = LoadS03();
            float direction = scenario.DirectionForLane(0);
            float junctionX = -9 * direction;
            var prisms = new Dictionary<int, List<Vector2>>
            {
                { 1, CornerFootprint(1, junctionX, direction) },
                { -1, CornerFootprint(-1, junctionX, direction) },
            };
            foreach (List<Vector2> prism in prisms.Values)
                Assert.AreEqual(5, prism.Count, "Test setup: prism outline incomplete.");

            float halfWidth = StreetJunctionGeometry.VehicleHalfWidth + StreetJunctionGeometry.Clearance;
            float halfLength = StreetJunctionGeometry.VehicleHalfLength + StreetJunctionGeometry.Clearance;
            for (int route = scenario.StraightLaneCount; route < scenario.StraightLaneCount + 2; route++)
            {
                var path = new VehicleLanePath(scenario, route);
                int samples = 0;
                foreach (KeyValuePair<int, List<Vector2>> prism in prisms)
                {
                    float minGap = float.PositiveInfinity;
                    float minDistance = 0;
                    Vector2 minCorner = default;
                    for (float distance = 0; distance <= path.Length; distance += SampleStep)
                    {
                        samples++;
                        foreach (Vector2 corner in EnvelopeCorners(path, distance, halfWidth, halfLength))
                        {
                            float gap = DistanceToPolygon(corner, prism.Value);
                            if (gap < minGap) { minGap = gap; minDistance = distance; minCorner = corner; }
                        }
                    }
                    Assert.IsTrue(minGap >= StreetJunctionGeometry.Clearance - .0001f,
                        path.Kind + " turning envelope comes within " + minGap.ToString("F3") +
                        " m of the " + (prism.Key > 0 ? "north" : "south") + " corner building at " +
                        minCorner + " (route distance " + minDistance.ToString("F2") + ").");
                    TestContext.Out.WriteLine(path.Kind + " vs " + (prism.Key > 0 ? "north" : "south") +
                        " corner building: min gap " + minGap.ToString("F3") + " m at route distance " +
                        minDistance.ToString("F2") + ".");
                }
                Assert.Greater(samples, 200, "Degenerate sweep: envelope was not actually sampled.");
            }
        }

        [Test]
        public void S03TurningEnvelopeKeepsThroatKerbMargin()
        {
            StreetScenarioDefinition scenario = LoadS03();
            float direction = scenario.DirectionForLane(0);
            float junctionX = -9 * direction;
            var aprons = StreetJunctionGeometry.TurnAprons(scenario);
            float northEnd = scenario.roadHalfWidth, southEnd = -scenario.roadHalfWidth;
            foreach (var hull in aprons)
                foreach (var point in hull)
                {
                    northEnd = Mathf.Max(northEnd, point.y);
                    southEnd = Mathf.Min(southEnd, point.y);
                }
            float westFace = junctionX - StreetLayout.SideRoadHalfWidth;
            float eastFace = junctionX + StreetLayout.SideRoadHalfWidth;

            float halfWidth = StreetJunctionGeometry.VehicleHalfWidth + StreetJunctionGeometry.Clearance;
            float halfLength = StreetJunctionGeometry.VehicleHalfLength + StreetJunctionGeometry.Clearance;
            for (int route = scenario.StraightLaneCount; route < scenario.StraightLaneCount + 2; route++)
            {
                var path = new VehicleLanePath(scenario, route);
                float minMargin = float.PositiveInfinity;
                float minDistance = 0;
                Vector2 minCorner = default;
                for (float distance = 0; distance <= path.Length; distance += SampleStep)
                {
                    foreach (Vector2 corner in EnvelopeCorners(path, distance, halfWidth, halfLength))
                    {
                        // The throat kerb faces run along the side-street edges outside the main road.
                        if (corner.y <= northEnd && corner.y >= southEnd) continue;
                        float margin = Mathf.Min(corner.x - westFace, eastFace - corner.x);
                        if (margin < minMargin) { minMargin = margin; minDistance = distance; minCorner = corner; }
                    }
                }
                Assert.IsFalse(float.IsInfinity(minMargin), "The route must enter the straight throat.");
                Assert.IsTrue(minMargin >= ThroatKerbMarginFloor,
                    path.Kind + " turning envelope leaves only " + minMargin.ToString("F3") +
                    " m to a junction-throat kerb face at " + minCorner + " (route distance " +
                    minDistance.ToString("F2") + "); documented minimum is 0.41 m.");
                TestContext.Out.WriteLine(path.Kind + " throat kerb margin: " + minMargin.ToString("F3") +
                    " m at route distance " + minDistance.ToString("F2") + ".");
            }
        }

        private static float DistanceToPolygon(Vector2 p, List<Vector2> polygon)
        {
            if (Contains(polygon, p)) return 0;
            float best = float.PositiveInfinity;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                best = Mathf.Min(best, DistanceToSegment(p, polygon[j], polygon[i]));
            return best;
        }

        private static bool Contains(List<Vector2> polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            }
            return inside;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
            return Vector2.Distance(p, a + segment * t);
        }

        private static List<Vector2> Hull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            foreach (Vector2 p in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                Vector2 p = points[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float Cross(Vector2 origin, Vector2 a, Vector2 b)
            => (a.x - origin.x) * (b.y - origin.y) - (a.y - origin.y) * (b.x - origin.x);
    }
}
