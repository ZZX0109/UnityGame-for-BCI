using System;
using System.Collections.Generic;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Core
{
    /// <summary>Facility families tracked by the unified spatial anchor registry.</summary>
    public enum AnchorKind
    {
        BusStop,
        Bench,
        TreePit,
        BuildingEntrance,
        NpcWaitingPoint,
        NpcWalkingBand,
        SafeObservationArea,
        TactilePath,
        CrossingEntrance,
        Kerb,
    }

    /// <summary>
    /// One planar anchor. <see cref="Position"/> is metre-space with y ignored (the scene keeps
    /// furniture ground level); <see cref="Footprint"/> is the axis-aligned envelope in metres,
    /// x along the street and y along the z axis, with 0 meaning a point or line anchor;
    /// <see cref="Side"/> is the sidewalk the anchor belongs to (+1 north, -1 south).
    /// </summary>
    public readonly struct SpatialAnchor
    {
        public readonly string Id;
        public readonly AnchorKind Kind;
        public readonly Vector3 Position;
        public readonly Vector2 Footprint;
        public readonly int Side;

        public SpatialAnchor(string id, AnchorKind kind, int side, Vector3 position, Vector2 footprint)
        {
            Id = id;
            Kind = kind;
            Side = side;
            Position = position;
            Footprint = footprint;
        }

        /// <summary>AABB lower corner on the x/z plane (Vector2.y is the z axis).</summary>
        public Vector2 Min => new Vector2(Position.x - Footprint.x * .5f, Position.z - Footprint.y * .5f);

        /// <summary>AABB upper corner on the x/z plane (Vector2.y is the z axis).</summary>
        public Vector2 Max => new Vector2(Position.x + Footprint.x * .5f, Position.z + Footprint.y * .5f);

        /// <summary>
        /// Axis-aligned gap between the two envelopes on the x/z plane: positive when separated,
        /// zero when touching, negative when overlapping. Points and line anchors degenerate to
        /// the distance from their centre line.
        /// </summary>
        public float PlanarGap(SpatialAnchor other)
        {
            float dx = Mathf.Max(other.Min.x - Max.x, Min.x - other.Max.x);
            float dz = Mathf.Max(other.Min.y - Max.y, Min.y - other.Max.y);
            return Mathf.Max(dx, dz);
        }
    }

    /// <summary>
    /// Unified spatial anchors for the street scene (公交站、长椅、树池、建筑入口、NPC 站位、
    /// 安全观察区、盲道路径) plus the query API for corridor occupancy, nearest-facility
    /// distance and clearance validation.
    ///
    /// Derivation policy (task rule: no independent hardcoded coordinates):
    /// - values that StreetLayout / StreetMissionGeometry already expose are derived by calling
    ///   them directly (same StreetCrossing.Runtime asmdef, so the anchors cannot drift from the
    ///   mission geometry);
    /// - values that only exist as build-site literals in Presentation (StreetWorld.cs,
    ///   StreetFurnitureBuilder.cs, StreetBackgroundPopulation.cs) are restated once here with a
    ///   file:line citation; this registry is the canonical query surface from now on.
    ///
    /// Deliberately not registered (not in the task's facility list, or profile-dependent):
    /// litter bins (x = 25 fixed on BOTH sides, StreetWorld.cs:697 — asymmetric on purpose),
    /// utility cabinets (StreetWorld.cs:701-702), cycle stands (StreetWorld.cs:696),
    /// lamps (StreetWorld.cs:310), and bollards (x = slot * StreetVisualProfile.furnishingSpacing,
    /// runtime-dependent in 12..24 — no fixed coordinate exists to anchor, StreetWorld.cs:705).
    /// </summary>
    public static class StreetSpatialAnchors
    {
        /// <summary>Kerb line: the sidewalk starts at |z| = RoadHalfWidth = 6.6 (StreetLayout.cs:11).</summary>
        public static readonly float SidewalkInnerZ = StreetLayout.RoadHalfWidth;

        /// <summary>Building row: StreetWorld.cs:308 places shop buildings at SidewalkZ(side * 11.3) = ±14.6.</summary>
        public static readonly float SidewalkOuterZ = StreetLayout.SidewalkZ(11.3f);

        private static readonly Dictionary<AnchorKind, List<SpatialAnchor>> ByKind;

        /// <summary>Every registered anchor, deterministically ordered (build order below).</summary>
        public static IReadOnlyList<SpatialAnchor> All { get; }

        static StreetSpatialAnchors()
        {
            List<SpatialAnchor> anchors = BuildAll();
            All = anchors;
            ByKind = new Dictionary<AnchorKind, List<SpatialAnchor>>();
            foreach (AnchorKind kind in Enum.GetValues(typeof(AnchorKind))) ByKind[kind] = new List<SpatialAnchor>();
            foreach (SpatialAnchor anchor in anchors) ByKind[anchor.Kind].Add(anchor);
        }

        /// <summary>All anchors of one kind (empty list when the kind is not represented).</summary>
        public static IReadOnlyList<SpatialAnchor> OfKind(AnchorKind kind) => ByKind[kind];

        /// <summary>Closest anchor of the given kind to a planar position; default when none exist.</summary>
        public static SpatialAnchor Nearest(AnchorKind kind, Vector3 position)
        {
            SpatialAnchor best = default;
            float bestDistanceSq = float.MaxValue;
            foreach (SpatialAnchor anchor in OfKind(kind))
            {
                float dx = anchor.Position.x - position.x;
                float dz = anchor.Position.z - position.z;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    best = anchor;
                }
            }
            return best;
        }

        /// <summary>
        /// Nearest-facility distance (设施最近距离): planar distance to the closest anchor of the
        /// given kind, or Mathf.Infinity when the kind is not represented.
        /// </summary>
        public static float NearestDistance(AnchorKind kind, Vector3 position)
        {
            float bestDistanceSq = float.MaxValue;
            foreach (SpatialAnchor anchor in OfKind(kind))
            {
                float dx = anchor.Position.x - position.x;
                float dz = anchor.Position.z - position.z;
                bestDistanceSq = Mathf.Min(bestDistanceSq, dx * dx + dz * dz);
            }
            return bestDistanceSq == float.MaxValue ? Mathf.Infinity : Mathf.Sqrt(bestDistanceSq);
        }

        /// <summary>
        /// Corridor occupancy test (走廊占用判定): true when a disc of the given radius around
        /// <paramref name="position"/> reaches either mission walking corridor line
        /// z = StreetMissionGeometry.CorridorZ(±1) = +10.2 / -9.95 (StreetMissionGeometry.cs:34).
        /// z-only by design: the approach/arrival corridors run the full sidewalk length
        /// (StreetMissionGeometry.cs:52-55).
        /// </summary>
        public static bool CorridorOccupies(Vector3 position, float radius)
        {
            float dz = Mathf.Min(
                Mathf.Abs(position.z - StreetMissionGeometry.CorridorZ(1)),
                Mathf.Abs(position.z - StreetMissionGeometry.CorridorZ(-1)));
            return dz <= radius;
        }

        /// <summary>
        /// True when the planar point lies inside the public sidewalk band: kerb line ±6.6
        /// (StreetLayout.cs:11) out to the building row ±14.6 (StreetWorld.cs:308).
        /// </summary>
        public static bool IsOnSidewalk(Vector3 position)
            => Mathf.Abs(position.z) >= SidewalkInnerZ && Mathf.Abs(position.z) <= SidewalkOuterZ;

        /// <summary>
        /// Clearance validation (清障验证): true when the planar gap between the two anchor
        /// envelopes is at least <paramref name="minGap"/> (see <see cref="SpatialAnchor.PlanarGap"/>).
        /// </summary>
        public static bool ClearanceCheck(SpatialAnchor a, SpatialAnchor b, float minGap)
            => a.PlanarGap(b) >= minGap;

        private static List<SpatialAnchor> BuildAll()
        {
            var anchors = new List<SpatialAnchor>();

            // ---- Bus stops ----------------------------------------------------
            // Regular street-side shelter: StreetWorld.cs:338 builds it at (7, 0, roadHalfWidth + 2)
            // with a 3.6 × 1.7 shelter roof (StreetWorld.cs:738) and a sign pole 2.2 m west of the
            // shelter centre (StreetWorld.cs:745 → pole x = 4.8). Built unconditionally.
            anchors.Add(new SpatialAnchor("busStop.street", AnchorKind.BusStop, 1,
                new Vector3(7f, 0f, StreetLayout.RoadHalfWidth + 2f), new Vector2(3.6f, 1.7f)));
            // S03 corner stop, built only when scenario.kerbOcclusion is set: StreetWorld.cs:563
            // places it at (-2.15, 0, -(roadHalfWidth + 1.48)); the tested obstacle envelope is the
            // 2.85 × 2.45 canopy (StreetWorld.cs:568).
            anchors.Add(new SpatialAnchor("busStop.corner", AnchorKind.BusStop, -1,
                new Vector3(-2.15f, 0f, -(StreetLayout.RoadHalfWidth + 1.48f)), new Vector2(2.85f, 2.45f)));

            // ---- Bench ----------------------------------------------------------
            // StreetWorld.cs:693: neighbourhood bench bounds at (6.8, .5, SidewalkZ(-7.5)), 2.25 × 0.65.
            anchors.Add(new SpatialAnchor("bench.south", AnchorKind.Bench, -1,
                new Vector3(6.8f, 0f, StreetLayout.SidewalkZ(-7.5f)), new Vector2(2.25f, 0.65f)));

            // ---- Tree pits --------------------------------------------------------
            // StreetWorld.cs:310: trees for x in {-26,-13,13,26} (the |x| > 4 filter drops x = 0) on
            // both sides at SidewalkZ(side * 5.5) = ±8.8; the kept planter collider is a 1.4 × 1.4 box
            // (StreetWorld.cs:753). The S03 turning-traffic variant may skip the x = -13 slot on one
            // side (the |x + 9 * DirectionForLane(0)| > 6 filter in StreetWorld.cs:310); the registry
            // keeps the full-configuration slots. StreetFurnitureBuilder.cs:73 adds 1.10 m mulch in
            // the same pits (visual only).
            int[] treePitXs = { -26, -13, 13, 26 };
            foreach (int x in treePitXs)
                for (int side = -1; side <= 1; side += 2)
                    anchors.Add(new SpatialAnchor($"treePit.{(side > 0 ? "n" : "s")}.{x}", AnchorKind.TreePit, side,
                        new Vector3(x, 0f, StreetLayout.SidewalkZ(side * 5.5f)), new Vector2(1.4f, 1.4f)));

            // ---- Building entrances ------------------------------------------------
            // All five mission doorsteps come straight from StreetMissionGeometry.Doorstep
            // (StreetMissionGeometry.cs:14-18) so this registry cannot drift from the mission
            // spawn/arrival points. The brief's "doorstep 4 处" wording is stale: the code defines
            // five destination kinds (StreetMissionDefinition.cs:6). The 1.2 × 1.0 footprint is the
            // Destination zone size from StreetWorld.cs:384. Anchors mark the doorstep point, not
            // the facade plane (facade row z = SidewalkZ(9.5) = 12.8 vs doorstep z = ±11.9).
            StreetDestinationKind[] destinationKinds =
            {
                StreetDestinationKind.Convenience, StreetDestinationKind.Residence, StreetDestinationKind.Bookshop,
                StreetDestinationKind.Pharmacy, StreetDestinationKind.Cafe,
            };
            foreach (StreetDestinationKind kind in destinationKinds)
            {
                Vector3 doorstep = StreetMissionGeometry.Doorstep(kind);
                anchors.Add(new SpatialAnchor($"entrance.{kind}", AnchorKind.BuildingEntrance,
                    doorstep.z > 0f ? 1 : -1, new Vector3(doorstep.x, 0f, doorstep.z), new Vector2(1.2f, 1f)));
            }

            // ---- NPC waiting spots (候车) ------------------------------------------
            // StreetBackgroundPopulation.cs:112 spawns waiting NPCs at
            // (6.0 + (i / 4) * 0.66, 0, SidewalkZ(4.65)) — always the north side, because waiting
            // requires i % 4 == 0 (even i → side +1 at line 104). Registered spots:
            // x = 6.0 / 6.66 / 7.32 / 7.98, z = 7.95, treated as point anchors.
            float waitingZ = StreetLayout.SidewalkZ(4.65f);
            for (int i = 0; i < 4; i++)
                anchors.Add(new SpatialAnchor($"npcWaiting.{i}", AnchorKind.NpcWaitingPoint, 1,
                    new Vector3(6f + i * 0.66f, 0f, waitingZ), Vector2.zero));

            // ---- NPC walking bands (通行区) ----------------------------------------
            // StreetBackgroundPopulation.cs:105-110: three walking bands with side-shared x ranges
            // band0 4.8..9.5, band1 10.4..14.7, band2 28.2..34.5 and per-side depth references
            // band0 north 4.15 / south 5.1, band1 7.55, band2 6.0 (z = SidewalkZ(side * ref)).
            // Rows beyond the first drift +0.55 m per sixth NPC (line 110) except band0 north, which
            // stays on the base line; anchors register the row-0 band centre lines (z-extent 0).
            float[] bandMinX = { 4.8f, 10.4f, 28.2f };
            float[] bandMaxX = { 9.5f, 14.7f, 34.5f };
            float[] bandNorthRef = { 4.15f, 7.55f, 6f };
            float[] bandSouthRef = { 5.1f, 7.55f, 6f };
            for (int band = 0; band < 3; band++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float reference = side > 0 ? bandNorthRef[band] : bandSouthRef[band];
                    anchors.Add(new SpatialAnchor($"npcBand.{band}.{(side > 0 ? "n" : "s")}", AnchorKind.NpcWalkingBand, side,
                        new Vector3((bandMinX[band] + bandMaxX[band]) * .5f, 0f, StreetLayout.SidewalkZ(side * reference)),
                        new Vector2(bandMaxX[band] - bandMinX[band], 0f)));
                }

            // ---- Safe observation areas (安全观察区) --------------------------------
            // StreetWorld.cs:433-434: tactile waiting paving centred (0, .008, side * (roadHalfWidth + .55))
            // = ±7.15, footprint 3.6 × 0.65 (3.6 = crosswalkHalfWidth × 2).
            for (int side = -1; side <= 1; side += 2)
                anchors.Add(new SpatialAnchor($"observation.{(side > 0 ? "n" : "s")}", AnchorKind.SafeObservationArea, side,
                    new Vector3(0f, 0f, side * (StreetLayout.RoadHalfWidth + 0.55f)), new Vector2(3.6f, 0.65f)));

            // ---- Tactile guide path (盲道) ------------------------------------------
            // StreetFurnitureBuilder.cs:18,22,45: directional paving at Z(side, 6.7) =
            // SidewalkZ(side * 6.7) = ±10.0, seven 9.7 × 0.34 segments per side centred on
            // x = segment * 10 for segment in -3..3. The S03 turning-traffic variant replaces the
            // segment at x = -10 with three bridging spans on the same line (StreetFurnitureBuilder.cs:23-44).
            float tactileZ = StreetLayout.SidewalkZ(6.7f);
            for (int segment = -3; segment <= 3; segment++)
                for (int side = -1; side <= 1; side += 2)
                    anchors.Add(new SpatialAnchor($"tactile.{segment}.{(side > 0 ? "n" : "s")}", AnchorKind.TactilePath, side,
                        new Vector3(segment * 10f, 0f, side * tactileZ), new Vector2(9.7f, 0.34f)));

            // ---- Crossing entrance / kerb -------------------------------------------
            // StreetWorld.cs:380: CrossingRegion.Entry z = ∓width = ±6.6 with 3.6 × 0.35 crosswalk
            // stripes (StreetWorld.cs:315, crosswalkHalfWidth × 2 wide); StreetWorld.cs:379:
            // CrossingRegion.Kerb z = ∓(width + .45) = ±7.05. These are road-class anchors: they sit
            // on the kerb line by definition and are whitelisted in the carriageway tests.
            for (int side = -1; side <= 1; side += 2)
            {
                anchors.Add(new SpatialAnchor($"crossingEntrance.{(side > 0 ? "n" : "s")}", AnchorKind.CrossingEntrance, side,
                    new Vector3(0f, 0f, side * StreetLayout.RoadHalfWidth), new Vector2(3.6f, 0.35f)));
                anchors.Add(new SpatialAnchor($"kerb.{(side > 0 ? "n" : "s")}", AnchorKind.Kerb, side,
                    new Vector3(0f, 0f, side * (StreetLayout.RoadHalfWidth + 0.45f)), Vector2.zero));
            }

            return anchors;
        }
    }
}
