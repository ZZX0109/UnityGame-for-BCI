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
            Assert.AreEqual(4, aprons.Count, "two sides x two edges hulls are expected");
            foreach (Vector2[] hull in aprons)
            {
                Assert.GreaterOrEqual(hull.Length, 3);
                foreach (Vector2 p in hull)
                    Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y), "hull points must be finite");
            }
            UnityEngine.Object.DestroyImmediate(scenario);
        }
    }
}
