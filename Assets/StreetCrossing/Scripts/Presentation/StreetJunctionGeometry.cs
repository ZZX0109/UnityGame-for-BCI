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

        public static Mesh Surface(Vector2[] polygon, float height)
        {
            var vertices = new Vector3[polygon.Length]; var uv = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++) { vertices[i] = new Vector3(polygon[i].x, height, polygon[i].y); uv[i] = polygon[i] * .35f; }
            var triangles = new int[(polygon.Length - 2) * 3];
            for (int i = 0; i < polygon.Length - 2; i++) { triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 2; triangles[i * 3 + 2] = i + 1; }
            Mesh mesh = new Mesh { name = "Route-derived turn apron" }; mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
