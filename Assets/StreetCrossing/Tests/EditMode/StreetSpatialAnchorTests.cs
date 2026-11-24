using System;
using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// EditMode verification of the unified StreetSpatialAnchors registry: every anchor falls
    /// within the sidewalk band, none sits on the carriageway, the waiting spots clear the
    /// walking bands by their designed 0.5 m margin, the corridor query follows
    /// StreetMissionGeometry.CorridorZ, and the bus stops keep clear of the doorsteps.
    /// </summary>
    public sealed class StreetSpatialAnchorTests
    {
        private const float Delta = 0.0001f;

        // By-design centre-line clearance between the north waiting row (z = SidewalkZ(4.65) = 7.95,
        // StreetBackgroundPopulation.cs:112) and the band-0 north walking line (z = SidewalkZ(4.15)
        // = 7.45, StreetBackgroundPopulation.cs:108). The 0.5 m gap is intentional — line 109
        // documents the per-row 0.55 m stagger as the body-width spacing mechanism — so the
        // assertion pins exactly 0.5 m instead of a generic 1.0 m "comfort" threshold.
        private const float DesignedWaitingClearance = 0.5f;

        private static readonly AnchorKind[] RoadClassKinds = { AnchorKind.CrossingEntrance, AnchorKind.Kerb };

        [Test]
        public void Anchors_HaveUniqueIdsAndCoverEveryKind()
        {
            var ids = new HashSet<string>();
            foreach (SpatialAnchor anchor in StreetSpatialAnchors.All)
                Assert.IsTrue(ids.Add(anchor.Id), $"duplicate anchor id {anchor.Id}");
            foreach (AnchorKind kind in Enum.GetValues(typeof(AnchorKind)))
                Assert.Greater(StreetSpatialAnchors.OfKind(kind).Count, 0, $"{kind} has no anchors");
        }

        [Test]
        public void Anchors_FallWithinSidewalkBounds()
        {
            // Boundary semantics of the query itself: kerb line ±6.6 inclusive, building row ±14.6.
            Assert.IsTrue(StreetSpatialAnchors.IsOnSidewalk(new Vector3(0f, 0f, StreetLayout.RoadHalfWidth)));
            Assert.IsTrue(StreetSpatialAnchors.IsOnSidewalk(new Vector3(0f, 0f, StreetLayout.SidewalkZ(11.3f))));
            Assert.IsFalse(StreetSpatialAnchors.IsOnSidewalk(Vector3.zero));
            Assert.IsFalse(StreetSpatialAnchors.IsOnSidewalk(new Vector3(0f, 0f, StreetLayout.RoadHalfWidth - 0.1f)));
            Assert.IsFalse(StreetSpatialAnchors.IsOnSidewalk(new Vector3(0f, 0f, StreetLayout.SidewalkZ(11.3f) + 0.1f)));

            foreach (SpatialAnchor anchor in StreetSpatialAnchors.All)
            {
                bool roadClass = Array.IndexOf(RoadClassKinds, anchor.Kind) >= 0;
                Assert.IsTrue(roadClass || StreetSpatialAnchors.IsOnSidewalk(anchor.Position),
                    $"{anchor.Id} is not on the sidewalk");

                // The envelope must stay inside the sidewalk band too — except the crossing
                // entrance gate, which straddles the kerb line by design.
                if (anchor.Kind == AnchorKind.CrossingEntrance) continue;
                float minAbsZ = Mathf.Abs(anchor.Position.z) - anchor.Footprint.y * .5f;
                float maxAbsZ = Mathf.Abs(anchor.Position.z) + anchor.Footprint.y * .5f;
                Assert.GreaterOrEqual(minAbsZ, StreetLayout.RoadHalfWidth - Delta, anchor.Id);
                Assert.LessOrEqual(maxAbsZ, StreetLayout.SidewalkZ(11.3f) + Delta, anchor.Id);
            }
        }

        [Test]
        public void Anchors_StayOutOfCarriageway()
        {
            foreach (SpatialAnchor anchor in StreetSpatialAnchors.All)
            {
                bool roadClass = Array.IndexOf(RoadClassKinds, anchor.Kind) >= 0;
                if (roadClass)
                {
                    // Crossing gates and kerb markers sit on the kerb line itself (±6.6 / ±7.05).
                    Assert.LessOrEqual(Mathf.Abs(anchor.Position.z), StreetLayout.RoadHalfWidth + 0.45f + Delta,
                        $"{anchor.Id} drifted away from the kerb line");
                }
                else
                {
                    Assert.GreaterOrEqual(Mathf.Abs(anchor.Position.z), StreetLayout.RoadHalfWidth,
                        $"{anchor.Id} sits inside the carriageway");
                }
            }
        }

        [Test]
        public void WaitingSpots_ClearWalkingBandsByDesignedMargin()
        {
            IReadOnlyList<SpatialAnchor> bands = StreetSpatialAnchors.OfKind(AnchorKind.NpcWalkingBand);
            Assert.Greater(bands.Count, 0);
            foreach (SpatialAnchor waiting in StreetSpatialAnchors.OfKind(AnchorKind.NpcWaitingPoint))
            {
                float gap = float.MaxValue;
                foreach (SpatialAnchor band in bands) gap = Mathf.Min(gap, waiting.PlanarGap(band));
                Assert.GreaterOrEqual(gap, DesignedWaitingClearance - Delta,
                    $"{waiting.Id} conflicts with the walking bands");
                Assert.LessOrEqual(gap, DesignedWaitingClearance + Delta,
                    $"{waiting.Id} no longer matches the designed 0.5 m band clearance");
            }
        }

        [Test]
        public void CorridorOccupies_FollowsMissionCorridorZ()
        {
            float north = StreetMissionGeometry.CorridorZ(1);  // +10.2 (StreetMissionGeometry.cs:34)
            float south = StreetMissionGeometry.CorridorZ(-1); // -9.95
            Assert.IsTrue(StreetSpatialAnchors.CorridorOccupies(new Vector3(0f, 0f, north), 0.25f));
            Assert.IsTrue(StreetSpatialAnchors.CorridorOccupies(new Vector3(0f, 0f, south), 0.25f));
            // Half a metre road-side of the corridor line is outside a 0.25 m radius...
            Assert.IsFalse(StreetSpatialAnchors.CorridorOccupies(new Vector3(0f, 0f, north - 0.5f), 0.25f));
            Assert.IsFalse(StreetSpatialAnchors.CorridorOccupies(new Vector3(0f, 0f, south + 0.5f), 0.25f));
            // ...but within a 0.5 m radius (boundary inclusive).
            Assert.IsTrue(StreetSpatialAnchors.CorridorOccupies(new Vector3(0f, 0f, north - 0.5f), 0.5f));
        }

        [Test]
        public void BusStops_KeepTwoMetresFromDoorsteps()
        {
            foreach (SpatialAnchor stop in StreetSpatialAnchors.OfKind(AnchorKind.BusStop))
                foreach (StreetDestinationKind kind in Enum.GetValues(typeof(StreetDestinationKind)))
                {
                    Vector3 doorstep = StreetMissionGeometry.Doorstep(kind);
                    float distance = Vector2.Distance(
                        new Vector2(stop.Position.x, stop.Position.z),
                        new Vector2(doorstep.x, doorstep.z));
                    Assert.GreaterOrEqual(distance, 2f, $"{stop.Id} is too close to the {kind} doorstep");
                }
        }

        [Test]
        public void BuildingEntrances_TrackMissionGeometryDoorsteps()
        {
            foreach (StreetDestinationKind kind in Enum.GetValues(typeof(StreetDestinationKind)))
            {
                Vector3 doorstep = StreetMissionGeometry.Doorstep(kind);
                SpatialAnchor nearest = StreetSpatialAnchors.Nearest(AnchorKind.BuildingEntrance, doorstep);
                Assert.AreEqual(doorstep.x, nearest.Position.x, Delta, kind.ToString());
                Assert.AreEqual(doorstep.z, nearest.Position.z, Delta, kind.ToString());
            }
        }

        [Test]
        public void Nearest_BusStopFromWaitingRowIsTheStreetShelter()
        {
            Vector3 waiting = new Vector3(6f, 0f, StreetLayout.SidewalkZ(4.65f)); // z = 7.95, north waiting row
            SpatialAnchor nearest = StreetSpatialAnchors.Nearest(AnchorKind.BusStop, waiting);
            Assert.AreEqual("busStop.street", nearest.Id);
            Assert.AreEqual(Mathf.Sqrt(1f + 0.65f * 0.65f),
                StreetSpatialAnchors.NearestDistance(AnchorKind.BusStop, waiting), Delta);
        }
    }
}
