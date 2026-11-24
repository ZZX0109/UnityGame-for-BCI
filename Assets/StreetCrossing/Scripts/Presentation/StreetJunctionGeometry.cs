using System.Collections.Generic;
using StreetCrossing.Core;
using StreetCrossing.Traffic;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Build-time clearance envelope for the unchanged turning routes and largest authored vehicle.</summary>
    public static class StreetJunctionGeometry
    {
        public const float VehicleHalfLength = 2.4f;
        public const float VehicleHalfWidth = 1.08f;
        public const float Clearance = .12f;
        /// <summary>Finished tops of the two existing surfaces the apron must meet flush.</summary>
        public const float MainRoadTopY = -.010f;
        public const float SideStreetTopY = .0205f;
        /// <summary>Ramp budget: the 30.5 mm corner drop at 3.2% rise/run needs 0.953 m of seam.</summary>
        public const float MaxSlopeRiseRun = .032f;
        public const float TransitionMetres = (SideStreetTopY - MainRoadTopY) / MaxSlopeRiseRun;

        public static List<Vector2[]> TurnAprons(StreetScenarioDefinition scenario)
        {
            var result = new List<Vector2[]>();
            if (!scenario.turningTraffic) return result;
            float junction = -9 * scenario.DirectionForLane(0);
            for (int side = -1; side <= 1; side += 2)
            for (int edge = -1; edge <= 1; edge += 2)
            {
                var points = new List<Vector2>();
                for (int routeIndex = scenario.StraightLaneCount; routeIndex < scenario.StraightLaneCount + 2; routeIndex++)
                {
                    var path = new VehicleLanePath(scenario, routeIndex);
                    for (float distance = Mathf.Max(0, path.StopDistance - 3); distance <= path.JunctionExit + 3; distance += .08f)
                    {
                        Vector3 centre = path.Position(distance); Quaternion heading = path.RotationAt(distance);
                        var footprint = new List<Vector2>(4);
                        foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
                        {
                            Vector3 p = centre + heading * new Vector3(corner.x * (VehicleHalfWidth + Clearance), 0, corner.y * (VehicleHalfLength + Clearance));
                            footprint.Add(new Vector2(p.x, p.z));
                        }
                        // Only add road surface outside the existing main/side-road union.
                        footprint = Clip(footprint, 1, side * scenario.roadHalfWidth, side > 0);
                        footprint = Clip(footprint, 0, junction + edge * StreetLayout.SideRoadHalfWidth, edge > 0);
                        points.AddRange(footprint);
                    }
                }
                Vector2[] hull = Hull(points);
                if (hull.Length >= 3) result.Add(hull);
            }
            return result;
        }

        public static Vector2 CurbOpening(StreetScenarioDefinition scenario, int side, List<Vector2[]> aprons)
        {
            float junction = -9 * scenario.DirectionForLane(0);
            float min = junction - 3.4f, max = junction + 3.4f;
            foreach (Vector2[] apron in aprons)
                foreach (Vector2 point in apron)
                    if (point.y * side >= scenario.roadHalfWidth - .001f)
                    { min = Mathf.Min(min, point.x - .1f); max = Mathf.Max(max, point.x + .1f); }
            return new Vector2(min, max);
        }

        private static List<Vector2> Clip(List<Vector2> source, int axis, float boundary, bool greater)
        {
            var output = new List<Vector2>();
            for (int i = 0; i < source.Count; i++)
            {
                Vector2 p = source[i], q = source[(i + 1) % source.Count];
                bool inside = greater ? p[axis] >= boundary : p[axis] <= boundary;
                bool nextInside = greater ? q[axis] >= boundary : q[axis] <= boundary;
                if (inside) output.Add(p);
                if (inside != nextInside) output.Add(Vector2.Lerp(p, q, (boundary - p[axis]) / (q[axis] - p[axis])));
            }
            return output;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        private static Vector2[] Hull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var unique = new List<Vector2>();
            foreach (Vector2 p in points) if (unique.Count == 0 || (p - unique[unique.Count - 1]).sqrMagnitude > .00000001f) unique.Add(p);
            if (unique.Count < 3) return new Vector2[0];
            var hull = new List<Vector2>();
            foreach (Vector2 p in unique)
            { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= .000001f) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count;
            for (int i = unique.Count - 2; i >= 0; i--)
            { while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], unique[i]) <= .000001f) hull.RemoveAt(hull.Count - 1); hull.Add(unique[i]); }
            hull.RemoveAt(hull.Count - 1); return hull.ToArray();
        }

        /// <summary>
        /// Finds the hull corner shared by the main-road seam and the side-street seam, plus the unit
        /// ramp axis pointing along the main-road seam away from that corner. The covered corner then
        /// matches the higher side-street top while the exposed main-road seam drops 30.5 mm over 0.953 m.
        /// </summary>
        public static bool CornerRamp(Vector2[] polygon, float junction, float roadHalfWidth, out Vector2 corner, out Vector2 rampAxis)
        {
            corner = default; rampAxis = default;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length], c = polygon[(i - 1 + polygon.Length) % polygon.Length];
                bool onRoadEdge = Mathf.Abs(Mathf.Abs(a.y) - roadHalfWidth) < .001f;
                bool onMouthEdge = Mathf.Abs(Mathf.Abs(a.x - junction) - StreetLayout.SideRoadHalfWidth) < .001f;
                if (!onRoadEdge || !onMouthEdge) continue;
                corner = a;
                // The main-seam neighbour is the adjacent hull vertex that stays on the same road-edge line.
                if (Mathf.Abs(b.y - a.y) < .001f) rampAxis = new Vector2(Mathf.Sign(b.x - a.x), 0f);
                else if (Mathf.Abs(c.y - a.y) < .001f) rampAxis = new Vector2(Mathf.Sign(c.x - a.x), 0f);
                else return false;
                return true;
            }
            return false;
        }

        public static Mesh Surface(Vector2[] polygon, Vector2 corner, Vector2 rampAxis)
        {
            // The height field is linear on [0, TransitionMetres] and flat beyond, so the fan
            // triangulation needs the polygon split at the kink: a single triangle spanning it would
            // leave the apron up to ~11 mm proud of the road past the ramp. Both halves stay convex,
            // each is fan-triangulated, and both halves merge into one mesh with one collider.
            var vertices = new List<Vector3>(polygon.Length + 2); var uvs = new List<Vector2>(polygon.Length + 2);
            var triangles = new List<int>((polygon.Length - 2) * 3);
            float[] d = new float[polygon.Length];
            for (int i = 0; i < polygon.Length; i++) d[i] = Vector2.Dot(polygon[i] - corner, rampAxis);
            for (int half = 0; half < 2; half++)
            {
                bool lowHalf = half == 1;
                int baseIndex = vertices.Count, count = 0;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 p = polygon[i], q = polygon[(i + 1) % polygon.Length];
                    float di = d[i], dj = d[(i + 1) % polygon.Length];
                    bool inside = lowHalf ? di >= TransitionMetres : di <= TransitionMetres;
                    bool nextInside = lowHalf ? dj >= TransitionMetres : dj <= TransitionMetres;
                    if (inside) { AddVertex(vertices, uvs, p, lowHalf, corner, rampAxis); count++; }
                    if (inside != nextInside)
                    {
                        Vector2 crossing = Vector2.Lerp(p, q, (TransitionMetres - di) / (dj - di));
                        AddVertex(vertices, uvs, crossing, true, corner, rampAxis); // the kink sits exactly at the road top
                        count++;
                    }
                }
                for (int i = 0; i < count - 2; i++) { triangles.Add(baseIndex); triangles.Add(baseIndex + i + 2); triangles.Add(baseIndex + i + 1); }
            }
            Mesh mesh = new Mesh { name = "Route-derived turn apron" };
            mesh.vertices = vertices.ToArray(); mesh.uv = uvs.ToArray(); mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }

        /// <summary>Field height with UVs: the corner matches the covered side-street top, the ramp runs at 3.2%.</summary>
        private static void AddVertex(List<Vector3> vertices, List<Vector2> uvs, Vector2 point, bool lowHalf, Vector2 corner, Vector2 rampAxis)
        {
            float d = Vector2.Dot(point - corner, rampAxis);
            float drop = Mathf.Clamp01(d / TransitionMetres) * (SideStreetTopY - MainRoadTopY);
            float y = lowHalf ? MainRoadTopY : SideStreetTopY - drop;
            vertices.Add(new Vector3(point.x, y, point.y));
            uvs.Add(point / StreetWorld.AsphaltTileSizeMetres);
        }

        public static Mesh Surface(Vector2[] polygon, float height)
        {
            var vertices = new Vector3[polygon.Length]; var uv = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
            {
                vertices[i] = new Vector3(polygon[i].x, height, polygon[i].y);
                uv[i] = polygon[i] / StreetWorld.AsphaltTileSizeMetres;
            }
            var triangles = new int[(polygon.Length - 2) * 3];
            for (int i = 0; i < polygon.Length - 2; i++) { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 2; triangles[i * 3 + 2] = i + 1; }
            Mesh mesh = new Mesh { name = "Route-derived turn apron" }; mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
