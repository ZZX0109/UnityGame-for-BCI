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
            float junction = -9 * scenario.DirectionForLane(0);
            foreach (Vector2[] apron in aprons)
            {
                foreach (Vector2 p in apron)
                    Assert.That(Mathf.Abs(p.x), Is.GreaterThan(scenario.conflictHalfWidth + .5f), "Do not expand the protected-crosswalk corridor.");
                Assert.That(StreetJunctionGeometry.CornerRamp(apron, junction, scenario.roadHalfWidth, out Vector2 corner, out Vector2 axis), Is.True,
                    "Every apron must share a corner with the main-road and side-street seams.");
                Mesh surface = StreetJunctionGeometry.Surface(apron, corner, axis);
                try
                {
                    Vector3[] vertices = surface.vertices; Vector2[] uvs = surface.uv; int[] triangles = surface.triangles;
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        Assert.That(cross.y, Is.GreaterThan(0f), "Every apron triangle must wind upward.");
                        Assert.That(cross.magnitude * .5f, Is.GreaterThan(1e-8f), "Zero-area apron triangles are not allowed.");
                        Assert.That(Mathf.Sqrt(cross.x * cross.x + cross.z * cross.z) / cross.y,
                            Is.LessThanOrEqualTo(StreetJunctionGeometry.MaxSlopeRiseRun + 1e-5f), "The ramp slope must stay within 3.2%.");
                    }
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector2 xz = new Vector2(vertices[i].x, vertices[i].z);
                        // The split-and-merge triangulation reorders vertices, so match by XZ, not by index:
                        // every mesh vertex must sit on a hull edge (XZ unchanged) and obey the height field.
                        float best = float.MaxValue;
                        for (int k = 0; k < apron.Length; k++)
                        {
                            Vector2 e = apron[k], f = apron[(k + 1) % apron.Length];
                            Vector2 ab = f - e, t = xz - e;
                            float parameter = Mathf.Clamp01(Vector2.Dot(t, ab) / ab.sqrMagnitude);
                            best = Mathf.Min(best, Vector2.Distance(xz, e + ab * parameter));
                        }
                        Assert.That(best, Is.LessThanOrEqualTo(1e-4f), "The height field must not move the XZ envelope.");
                        Assert.That(Mathf.Abs(uvs[i].x - xz.x / StreetWorld.AsphaltTileSizeMetres)
                                + Mathf.Abs(uvs[i].y - xz.y / StreetWorld.AsphaltTileSizeMetres),
                            Is.LessThanOrEqualTo(1e-4f), "UVs stay world XZ / 3 m.");
                        Assert.That(vertices[i].y, Is.InRange(StreetJunctionGeometry.MainRoadTopY - .001f, StreetJunctionGeometry.SideStreetTopY + .001f),
                            "The apron must live between the two existing road tops.");
                        float rampDistance = Vector2.Dot(xz - corner, axis);
                        // The side-street seam line stays flush along its whole length: height must not
                        // decay with distance from the corner (a radial cone would re-tilt this edge).
                        if (Mathf.Abs(Mathf.Abs(xz.x - junction) - StreetLayout.SideRoadHalfWidth) < .001f)
                            Assert.That(vertices[i].y, Is.EqualTo(StreetJunctionGeometry.SideStreetTopY).Within(.001f),
                                "The entire side-street seam line must sit flush with the side-street top.");
                        if (rampDistance >= StreetJunctionGeometry.TransitionMetres)
                            Assert.That(vertices[i].y, Is.EqualTo(StreetJunctionGeometry.MainRoadTopY).Within(.001f),
                                "Vertices beyond the ramp must sit flush with the road top.");
                        else if (rampDistance <= 0f)
                            Assert.That(vertices[i].y, Is.EqualTo(StreetJunctionGeometry.SideStreetTopY).Within(.001f),
                                "Corner-region vertices must sit flush with the covered side-street top.");
                        else
                            Assert.That(vertices[i].y, Is.EqualTo(StreetJunctionGeometry.SideStreetTopY - rampDistance / StreetJunctionGeometry.TransitionMetres * (StreetJunctionGeometry.SideStreetTopY - StreetJunctionGeometry.MainRoadTopY)).Within(.001f),
                                "Ramp vertices must follow the 3.2% linear field.");
                    }
                }
                finally { Object.DestroyImmediate(surface); }
            }
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
