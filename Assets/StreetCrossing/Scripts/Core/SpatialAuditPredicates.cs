using UnityEngine;

namespace StreetCrossing.Core
{
    /// <summary>
    /// P0-§3 pure static predicates for the expanded spatial audit (checks i/j).
    /// Kept in the runtime assembly so EditMode tests can cover them directly;
    /// the Editor audit only forwards world AABBs into these helpers.
    /// </summary>
    public static class SpatialAuditPredicates
    {
        /// <summary>
        /// check i (carriageway_intrusion): a world AABB counts as an intruder when
        /// it is tall enough to be a body (not flat road paint), starts near ground
        /// level, and its footprint overlaps the rectangle |x| &lt;= halfLength,
        /// |z| &lt;= halfWidth (the StreetLayout.RoadHalfWidth band). Vector2.y in
        /// hull helpers maps to world z throughout this class.
        /// </summary>
        public static bool IntrudesIntoCarriageway(Bounds box, float halfLength, float halfWidth)
        {
            if (box.size.y < 0.1f) return false;   // flat decals / road paint are not obstacles
            if (box.min.y > 0.2f) return false;    // parts mounted high above the road
            return box.min.x <= halfLength && box.max.x >= -halfLength
                && box.min.z <= halfWidth && box.max.z >= -halfWidth;
        }

        /// <summary>
        /// check j (turn_envelope_block): planar overlap test between a world AABB
        /// (x/z only) and one route-derived turning-apron hull polygon. The hull
        /// already includes the vehicle footprint plus the clearance sweep
        /// (StreetJunctionGeometry.TurnAprons). True when a hull vertex lies inside
        /// the box, a box corner lies inside the hull, or any hull edge crosses any
        /// box edge.
        /// </summary>
        public static bool OverlapsPlanar(Bounds box, Vector2[] hull)
        {
            if (hull == null || hull.Length < 3) return false;
            Vector2 c = new Vector2(box.min.x, box.min.z);
            Vector2 d = new Vector2(box.max.x, box.min.z);
            Vector2 e = new Vector2(box.max.x, box.max.z);
            Vector2 f = new Vector2(box.min.x, box.max.z);
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 p = hull[i];
                if (p.x >= box.min.x && p.x <= box.max.x && p.y >= box.min.z && p.y <= box.max.z) return true;
                Vector2 q = hull[(i + 1) % hull.Length];
                if (SegmentsCross(p, q, c, d) || SegmentsCross(p, q, d, e)
                    || SegmentsCross(p, q, e, f) || SegmentsCross(p, q, f, c)) return true;
            }
            return PointInHull(c, hull) || PointInHull(d, hull)
                || PointInHull(e, hull) || PointInHull(f, hull);
        }

        /// <summary>Ray-casting point-in-polygon test on the x/z plane.</summary>
        public static bool PointInHull(Vector2 p, Vector2[] hull)
        {
            bool inside = false;
            for (int i = 0, j = hull.Length - 1; i < hull.Length; j = i++)
            {
                if ((hull[i].y > p.y) != (hull[j].y > p.y))
                {
                    float x = (hull[j].x - hull[i].x) * (p.y - hull[i].y) / (hull[j].y - hull[i].y) + hull[i].x;
                    if (p.x < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Strict 2D segment intersection via orientation signs.</summary>
        public static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(c, d, a);
            float d2 = Cross(c, d, b);
            float d3 = Cross(a, b, c);
            float d4 = Cross(a, b, d);
            return (d1 > 0f) != (d2 > 0f) && (d3 > 0f) != (d4 > 0f);
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b)
            => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }
}
