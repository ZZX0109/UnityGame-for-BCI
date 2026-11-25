using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// P0-§3 expansion coverage: LampPost anchor registration plus the pure static
    /// predicates behind carriageway_intrusion / turn_envelope_block. No scene
    /// loads; scenarios come from ScriptableObject.CreateInstance so the field
    /// defaults apply. Validate() is never called because it throws without
    /// assigned asset references (StreetScenarioDefinition.cs:56-83).
    /// </summary>
    public sealed class StreetSpatialAuditCoverageTests
    {
        private const float Delta = 0.0001f;

        [Test]
        public void LampPost_Anchors_AreRegistered()
        {
            Assert.Greater(StreetSpatialAnchors.OfKind(AnchorKind.LampPost).Count, 0,
                "P0-§3: LampPost anchors must be registered (lamp sites, StreetWorld.cs:779-781).");
        }

        [Test]
        public void LampPost_Anchors_FallWithinSidewalkBand()
        {
            float outerZ = Mathf.Abs(StreetLayout.SidewalkZ(11.3f));
            foreach (SpatialAnchor anchor in StreetSpatialAnchors.OfKind(AnchorKind.LampPost))
            {
                float inner = Mathf.Abs(anchor.Position.z) - anchor.Footprint.y * 0.5f;
                float outer = Mathf.Abs(anchor.Position.z) + anchor.Footprint.y * 0.5f;
                Assert.GreaterOrEqual(inner, StreetLayout.RoadHalfWidth - Delta,
                    $"anchor {anchor.Id} must stay outside the carriageway");
                Assert.LessOrEqual(outer, outerZ + Delta,
                    $"anchor {anchor.Id} must stay inside the sidewalk band");
            }
        }

        [Test]
        public void CarriagewayIntrusion_FlatPaintExempt_BodyFlagged()
        {
            // flat road paint: exempt by the size.y guard
            Bounds paint = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(4f, 0.02f, 2f));
            Assert.IsFalse(SpatialAuditPredicates.IntrudesIntoCarriageway(paint, 42f, StreetLayout.RoadHalfWidth));
            // a raised body standing inside the carriageway: intrusion
            Bounds body = new Bounds(new Vector3(3f, 0.5f, 2f), new Vector3(0.3f, 1f, 0.3f));
            Assert.IsTrue(SpatialAuditPredicates.IntrudesIntoCarriageway(body, 42f, StreetLayout.RoadHalfWidth));
            // the same body beyond the road end: no intrusion
            Bounds far = new Bounds(new Vector3(50f, 0.5f, 2f), new Vector3(0.3f, 1f, 0.3f));
            Assert.IsFalse(SpatialAuditPredicates.IntrudesIntoCarriageway(far, 42f, StreetLayout.RoadHalfWidth));
            // a body mounted high above the road (overhang): exempt
            Bounds high = new Bounds(new Vector3(3f, 5f, 2f), new Vector3(0.3f, 1f, 0.3f));
            Assert.IsFalse(SpatialAuditPredicates.IntrudesIntoCarriageway(high, 42f, StreetLayout.RoadHalfWidth));
        }

        [Test]
        public void OverlapsPlanar_HullVsBoxAllGeometryCases()
        {
            var square = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
            // box fully inside the hull: corner-inside branch
            Bounds inside = new Bounds(Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f));
            Assert.IsTrue(SpatialAuditPredicates.OverlapsPlanar(inside, square));
            // hull vertex inside the box: vertex branch
            Bounds contains = new Bounds(Vector3.zero, new Vector3(3f, 1f, 3f));
            Assert.IsTrue(SpatialAuditPredicates.OverlapsPlanar(contains, square));
            // edge crossing only: vertex outside, corner outside
            Bounds ring = new Bounds(new Vector3(1.5f, 0f, 0f), new Vector3(1f, 1f, 0.5f));
            Assert.IsTrue(SpatialAuditPredicates.OverlapsPlanar(ring, square));
            // fully separated
            Bounds away = new Bounds(new Vector3(5f, 0f, 5f), new Vector3(1f, 1f, 1f));
            Assert.IsFalse(SpatialAuditPredicates.OverlapsPlanar(away, square));
            // degenerate hull never blocks
            Assert.IsFalse(SpatialAuditPredicates.OverlapsPlanar(inside, new[] { Vector2.zero }));
        }

        [Test]
        public void TurnAprons_EmptyWhenTurningDisabled()
        {
            StreetScenarioDefinition scenario = ScriptableObject.CreateInstance<StreetScenarioDefinition>();
            Assert.IsFalse(scenario.turningTraffic, "field default must keep turning traffic off");
            Assert.AreEqual(0, StreetJunctionGeometry.TurnAprons(scenario).Count,
                "check j must no-op gracefully when no turn routes exist");
            Object.DestroyImmediate(scenario);
        }

        [Test]
        public void TurnAprons_HullsExistWhenTurningEnabled()
        {
            StreetScenarioDefinition scenario = ScriptableObject.CreateInstance<StreetScenarioDefinition>();
            scenario.turningTraffic = true;
            var aprons = StreetJunctionGeometry.TurnAprons(scenario);
            Assert.That(aprons.Count, Is.InRange(1, 4), "Only quadrants where a swept vehicle leaves the existing carriageways need a shoulder.");
            foreach (Vector2[] hull in aprons)
            {
                Assert.GreaterOrEqual(hull.Length, 3);
                foreach (Vector2 p in hull)
                    Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y), "hull points must be finite");
            }
            UnityEngine.Object.DestroyImmediate(scenario);
        }

        // ------------------------------------------------------------------
        // P0-§3 round 2: pure predicate coverage for checks a-l. Every test is
        // scene-free and physics-free; route geometry uses StreetMissionGeometry
        // accessors fed with synthetic spawn/destination values.
        // ------------------------------------------------------------------

        [Test]
        public void SizeBand_AcceptsInBandRejectsOversizeAndUnderSize()
        {
            Vector3 minBand = new Vector3(1f, 2f, 1f);
            Vector3 maxBand = new Vector3(4f, 6f, 4f);
            float ratio;
            Assert.IsTrue(SpatialAuditPredicates.IsWithinSizeBand(new Vector3(2f, 3f, 2f), minBand, maxBand, out ratio));
            Assert.AreEqual(1f, ratio, Delta, "in-band sizes must report excursion 1");
            Assert.IsFalse(SpatialAuditPredicates.IsWithinSizeBand(new Vector3(5f, 3f, 2f), minBand, maxBand, out ratio));
            Assert.AreEqual(1.25f, ratio, Delta, "oversize x reports 5/4");
            Assert.IsFalse(SpatialAuditPredicates.IsWithinSizeBand(new Vector3(0.5f, 3f, 2f), minBand, maxBand, out ratio));
            Assert.AreEqual(2f, ratio, Delta, "undersize x reports min/value");
            Vector3 degenerateMin = new Vector3(2f, 2f, 1f);
            Vector3 degenerateMax = new Vector3(2f, 6f, 4f);
            Assert.IsTrue(SpatialAuditPredicates.IsWithinSizeBand(new Vector3(50f, 3f, 2f), degenerateMin, degenerateMax, out ratio),
                "degenerate band axis (max <= min) is unconstrained");
        }

        [Test]
        public void TippedOver_UprightAndLyingDownBranches()
        {
            Assert.IsFalse(SpatialAuditPredicates.IsTippedOver(new Vector3(0.3f, 4.5f, 0.3f), 0.7f),
                "vertical-dominant lamp post stands upright");
            Assert.IsTrue(SpatialAuditPredicates.IsTippedOver(new Vector3(4.5f, 0.3f, 0.3f), 0.7f),
                "fallen lamp post collapses y under footprint");
            Assert.IsFalse(SpatialAuditPredicates.IsTippedOver(new Vector3(1f, 0.7f, 1f), 0.7f),
                "exact boundary is strict: y == ratio x footprint is not tipped");
        }

        [Test]
        public void FloatingAndSunk_ThresholdBranches()
        {
            Assert.IsTrue(SpatialAuditPredicates.IsFloating(0.2f, 0.05f));
            Assert.IsFalse(SpatialAuditPredicates.IsSunk(0.2f, 0.05f));
            Assert.IsFalse(SpatialAuditPredicates.IsFloating(0f, 0.05f), "resting on ground is neither");
            Assert.IsFalse(SpatialAuditPredicates.IsSunk(0f, 0.05f));
            Assert.IsTrue(SpatialAuditPredicates.IsSunk(-0.2f, 0.05f));
            Assert.IsFalse(SpatialAuditPredicates.IsFloating(0.05f, 0.05f), "threshold comparison is strict");
        }

        [Test]
        public void SharedVolume_OverlapDisjointAndTouchingBranches()
        {
            Bounds a = new Bounds(Vector3.zero, Vector3.one);
            Bounds b = new Bounds(new Vector3(0.5f, 0f, 0f), Vector3.one);
            Assert.AreEqual(0.5f, SpatialAuditPredicates.SharedVolume(a, b), Delta);
            Bounds far = new Bounds(new Vector3(10f, 0f, 0f), Vector3.one);
            Assert.AreEqual(0f, SpatialAuditPredicates.SharedVolume(a, far), Delta);
            Bounds touching = new Bounds(new Vector3(1f, 0f, 0f), Vector3.one);
            Assert.AreEqual(0f, SpatialAuditPredicates.SharedVolume(a, touching), Delta,
                "face-touching boxes share zero volume");
        }

        [Test]
        public void VolumeOf_UnitAndDegenerateBoxes()
        {
            Assert.AreEqual(1f, SpatialAuditPredicates.VolumeOf(new Bounds(Vector3.zero, Vector3.one)), Delta);
            Assert.AreEqual(0f, SpatialAuditPredicates.VolumeOf(new Bounds(Vector3.zero, Vector3.zero)), Delta);
            Assert.AreEqual(0.5f, SpatialAuditPredicates.VolumeOf(new Bounds(Vector3.zero, new Vector3(1f, 2f, 0.25f))), Delta);
        }

        [Test]
        public void ColliderOffset_DistanceRatioAndAlignedBranches()
        {
            float distance, ratio;
            Assert.IsFalse(SpatialAuditPredicates.ColliderOffsetExceeds(
                Vector3.zero, Vector3.one, Vector3.zero, Vector3.one, 0.35f, 1.8f, out distance, out ratio));
            Assert.AreEqual(0f, distance, Delta);
            Assert.AreEqual(1f, ratio, Delta);
            Assert.IsTrue(SpatialAuditPredicates.ColliderOffsetExceeds(
                Vector3.zero, Vector3.one, new Vector3(0.5f, 0f, 0f), Vector3.one, 0.35f, 1.8f, out distance, out ratio),
                "0.5 m centre drift exceeds the 0.35 m limit");
            Assert.AreEqual(0.5f, distance, Delta);
            Assert.IsTrue(SpatialAuditPredicates.ColliderOffsetExceeds(
                Vector3.zero, Vector3.one, Vector3.zero, new Vector3(2.5f, 1f, 1f), 0.35f, 1.8f, out distance, out ratio),
                "2.5x size ratio exceeds the 1.8 limit even when centred");
            Assert.AreEqual(2.5f, ratio, Delta);
            Assert.IsFalse(SpatialAuditPredicates.ColliderOffsetExceeds(
                Vector3.zero, Vector3.one, new Vector3(0.1f, 0f, 0f), Vector3.one, 0.35f, 1.8f, out distance, out ratio),
                "small drift inside both limits stays silent");
        }

        [Test]
        public void Iou_IdenticalHalfOverlapAndDisjoint()
        {
            Bounds a = new Bounds(Vector3.zero, Vector3.one);
            Assert.AreEqual(1f, SpatialAuditPredicates.Iou(a, new Bounds(Vector3.zero, Vector3.one)), Delta);
            Assert.AreEqual(1f / 3f, SpatialAuditPredicates.Iou(a, new Bounds(new Vector3(0.5f, 0f, 0f), Vector3.one)), Delta,
                "shared 0.5 over union 1.5");
            Assert.AreEqual(0f, SpatialAuditPredicates.Iou(a, new Bounds(new Vector3(10f, 0f, 0f), Vector3.one)), Delta);
        }

        [Test]
        public void CorridorBlocker_BandStripAndDepthBranches()
        {
            float corridorZ = StreetMissionGeometry.CorridorZ(1);
            float halfBand = 0.40f + 0.325f;
            float depth;
            Bounds blocker = new Bounds(new Vector3(5f, 0.5f, corridorZ), new Vector3(2f, 1f, 0.8f));
            Assert.IsTrue(SpatialAuditPredicates.BlocksDoorstepCorridor(blocker, corridorZ, halfBand, -38f, 38f, out depth));
            Assert.AreEqual(0.8f, depth, Delta, "box fully inside the band penetrates its full z size");
            Bounds outside = new Bounds(new Vector3(5f, 0.5f, 12.5f), new Vector3(2f, 1f, 0.8f));
            Assert.IsFalse(SpatialAuditPredicates.BlocksDoorstepCorridor(outside, corridorZ, halfBand, -38f, 38f, out depth));
            Assert.AreEqual(0f, depth, Delta);
            Bounds beyondStrip = new Bounds(new Vector3(50f, 0.5f, corridorZ), new Vector3(2f, 1f, 0.8f));
            Assert.IsFalse(SpatialAuditPredicates.BlocksDoorstepCorridor(beyondStrip, corridorZ, halfBand, -38f, 38f, out depth),
                "band hit outside the x strip is ignored");
            Bounds partial = new Bounds(new Vector3(5f, 0.5f, 11.0f), new Vector3(2f, 1f, 0.8f));
            Assert.IsTrue(SpatialAuditPredicates.BlocksDoorstepCorridor(partial, corridorZ, halfBand, -38f, 38f, out depth));
            Assert.AreEqual(0.325f, depth, Delta, "partial intrusion reports only the penetrated band width");
        }

        private static bool RouteBlocked(Bounds blocker, Vector3[] route, float step)
        {
            for (int i = 0; i < route.Length - 1; i++)
            {
                Vector3 from = route[i];
                Vector3 to = route[i + 1];
                float length = Vector3.Distance(from, to);
                int samples = Mathf.CeilToInt(length / step);
                for (int s = 0; s <= samples; s++)
                {
                    Vector3 p = Vector3.Lerp(from, to, samples == 0 ? 0f : (float)s / samples);
                    if (SpatialAuditPredicates.PathPointBlocked(blocker, new Vector2(p.x, p.z))) return true;
                }
            }
            return SpatialAuditPredicates.PathPointBlocked(blocker, new Vector2(route[route.Length - 1].x, route[route.Length - 1].z));
        }

        [Test]
        public void MissionPath_SyntheticRouteBlockedAndClear()
        {
            Vector3 spawn = new Vector3(0f, 0.02f, -11.3f);
            Vector3 destination = StreetMissionGeometry.Doorstep(StreetDestinationKind.Cafe);
            Vector3[] approach = StreetMissionGeometry.Approach(spawn, 1, 6.6f);
            Vector3[] arrival = StreetMissionGeometry.Arrival(destination, 1, 6.6f);
            Vector3[] route = new Vector3[approach.Length + arrival.Length];
            approach.CopyTo(route, 0);
            arrival.CopyTo(route, approach.Length);
            Assert.AreEqual(7, route.Length, "3 approach + 4 arrival waypoints");
            Assert.AreEqual(-6.72f, route[2].z, Delta, "approach ends width+0.12 past the carriageway edge");
            Bounds building = new Bounds(new Vector3(0f, 3.5f, route[2].z), new Vector3(10.3f, 7f, 4f));
            Assert.IsTrue(RouteBlocked(building, route, 0.25f), "sampled route must hit the synthetic building collider");
            Bounds clearBench = new Bounds(new Vector3(8f, 0.5f, 3f), new Vector3(2f, 1f, 0.6f));
            Assert.IsFalse(RouteBlocked(clearBench, route, 0.25f), "furniture off the route never blocks sampling");
            Assert.IsFalse(SpatialAuditPredicates.PathPointBlocked(building, new Vector2(0f, StreetMissionGeometry.CorridorZ(-1))),
                "waypoint on the far corridor is outside the building footprint");
        }

        [Test]
        public void SignalSightLine_HitMissAndBeyondEnd()
        {
            Vector3 eye = new Vector3(-1.8f, 1.65f, -7.05f);
            Vector3 head = new Vector3(1.8f, 3.2f, -7.05f);
            Bounds occluder = new Bounds(new Vector3(0f, 2.4f, -7.05f), new Vector3(0.5f, 1.5f, 0.4f));
            Assert.IsTrue(SpatialAuditPredicates.SegmentIntersectsAabb(eye, head, occluder));
            Bounds above = new Bounds(new Vector3(0f, 5.5f, -7.05f), new Vector3(0.5f, 1.5f, 0.4f));
            Assert.IsFalse(SpatialAuditPredicates.SegmentIntersectsAabb(eye, head, above),
                "segment passing under the occluder is clear");
            Bounds behind = new Bounds(new Vector3(5f, 3f, -7.05f), new Vector3(0.5f, 1.5f, 0.4f));
            Assert.IsFalse(SpatialAuditPredicates.SegmentIntersectsAabb(eye, head, behind),
                "slab window clips segments that only reach past the end");
        }
    }
}
