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

        // ------------------------------------------------------------------
        // P0-§3 round 2: pure predicates behind checks a-h, k and l. All are
        // side-effect free so EditMode tests cover every branch without
        // loading a scene or touching the physics world.
        // ------------------------------------------------------------------

        /// <summary>
        /// check a (render_bounds_abnormal): true when every world-AABB axis
        /// sits inside [minBand, maxBand]. bandRatio reports the worst per-axis
        /// excursion factor: exactly 1 when in band, &gt;1 when out.
        /// </summary>
        public static bool IsWithinSizeBand(Vector3 size, Vector3 minBand, Vector3 maxBand, out float bandRatio)
        {
            bandRatio = Mathf.Max(AxisExcursion(size.x, minBand.x, maxBand.x),
                         Mathf.Max(AxisExcursion(size.y, minBand.y, maxBand.y),
                                   AxisExcursion(size.z, minBand.z, maxBand.z)));
            return bandRatio <= 1f;
        }

        /// <summary>Per-axis band excursion; degenerate bands (max &lt;= min) count as unconstrained.</summary>
        private static float AxisExcursion(float value, float min, float max)
        {
            if (max > min)
            {
                if (value > max) return value / Mathf.Max(max, 1e-6f);
                if (value < min) return min / Mathf.Max(value, 1e-6f);
            }
            return 1f;
        }

        /// <summary>check b (tipped_over): a vertical prop lies down when its height collapses under uprightRatio x footprint.</summary>
        public static bool IsTippedOver(Vector3 size, float uprightRatio)
        {
            return size.y < uprightRatio * Mathf.Max(size.x, size.z);
        }

        /// <summary>check c (floating): AABB bottom drifted above the ground plane by more than threshold.</summary>
        public static bool IsFloating(float groundGap, float threshold) => groundGap > threshold;

        /// <summary>check d (sunk): AABB bottom pushed below the ground plane by more than threshold.</summary>
        public static bool IsSunk(float groundGap, float threshold) => groundGap < -threshold;

        /// <summary>check e (category_overlap): intersection volume of two world AABBs; 0 when disjoint.</summary>
        public static float SharedVolume(Bounds a, Bounds b)
        {
            Vector3 d = Vector3.Min(a.max, b.max) - Vector3.Max(a.min, b.min);
            if (d.x <= 0f || d.y <= 0f || d.z <= 0f) return 0f;
            return d.x * d.y * d.z;
        }

        /// <summary>Axis-aligned box volume; Unity's Bounds has no volume property.</summary>
        public static float VolumeOf(Bounds b) => b.size.x * b.size.y * b.size.z;

        /// <summary>
        /// check f (collider_offset): true when the merged renderer and collider
        /// AABBs of one hierarchy drift apart by more than distanceLimit or
        /// differ in size by more than ratioLimit on any axis.
        /// </summary>
        public static bool ColliderOffsetExceeds(Vector3 rendererCenter, Vector3 rendererSize,
            Vector3 colliderCenter, Vector3 colliderSize, float distanceLimit, float ratioLimit,
            out float distance, out float ratio)
        {
            distance = Vector3.Distance(rendererCenter, colliderCenter);
            ratio = Mathf.Max(AxisSizeRatio(rendererSize.x, colliderSize.x),
                     Mathf.Max(AxisSizeRatio(rendererSize.y, colliderSize.y),
                               AxisSizeRatio(rendererSize.z, colliderSize.z)));
            return distance > distanceLimit || ratio > ratioLimit;
        }

        /// <summary>Per-axis renderer/collider size ratio; degenerate on both sides counts as equal.</summary>
        private static float AxisSizeRatio(float rendererAxis, float colliderAxis)
        {
            float lo = Mathf.Min(rendererAxis, colliderAxis);
            float hi = Mathf.Max(rendererAxis, colliderAxis);
            return lo <= 1e-6f ? (hi <= 1e-6f ? 1f : hi) : hi / lo;
        }

        /// <summary>check g (duplicate_collider): intersection-over-union of two world AABBs; touching boxes score 0.</summary>
        public static float Iou(Bounds a, Bounds b)
        {
            float shared = SharedVolume(a, b);
            if (shared <= 0f) return 0f;
            float union = VolumeOf(a) + VolumeOf(b) - shared;
            return union > 1e-10f ? shared / union : 0f;
        }

        /// <summary>
        /// check h (corridor_blocker): true when the box footprint overlaps the
        /// doorstep corridor band |z - corridorZ| &lt; halfBand within the strip
        /// x in [minX, maxX]; depth is the penetrated band width along z.
        /// </summary>
        public static bool BlocksDoorstepCorridor(Bounds box, float corridorZ, float halfBand, float minX, float maxX, out float depth)
        {
            depth = 0f;
            float xOverlap = Mathf.Min(box.max.x, maxX) - Mathf.Max(box.min.x, minX);
            if (xOverlap <= 0f) return false;
            float zOverlap = Mathf.Min(box.max.z, corridorZ + halfBand) - Mathf.Max(box.min.z, corridorZ - halfBand);
            if (zOverlap <= 0f) return false;
            depth = zOverlap;
            return true;
        }

        /// <summary>check k (mission_path_block): a sampled route point (x, z) lies inside the collider footprint.</summary>
        public static bool PathPointBlocked(Bounds box, Vector2 point)
        {
            return point.x >= box.min.x && point.x <= box.max.x
                && point.y >= box.min.z && point.y <= box.max.z;
        }

        /// <summary>check l (signal_occluded): slab test of the segment from→to against a world AABB.</summary>
        public static bool SegmentIntersectsAabb(Vector3 from, Vector3 to, Bounds box)
        {
            Vector3 dir = to - from;
            float tMin = 0f, tMax = 1f;
            return SlabWithin(box.min.x, box.max.x, from.x, dir.x, ref tMin, ref tMax)
                && SlabWithin(box.min.y, box.max.y, from.y, dir.y, ref tMin, ref tMax)
                && SlabWithin(box.min.z, box.max.z, from.z, dir.z, ref tMin, ref tMax);
        }

        /// <summary>Slab-method step: clips the segment parameter window to one axis slab.</summary>
        private static bool SlabWithin(float min, float max, float origin, float dir, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(dir) < 1e-8f) return origin >= min && origin <= max;
            float inv = 1f / dir;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2) { float swap = t1; t1 = t2; t2 = swap; }
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
