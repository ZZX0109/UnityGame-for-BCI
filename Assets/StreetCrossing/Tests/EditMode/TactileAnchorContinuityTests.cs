using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// 盲道锚点连续性：directional tactile anchors must span exactly 10.0 m along x and
    /// butt-joint with their x-neighbours on the same sidewalk, so the registered guide path
    /// mirrors the rendered 10 m straight pieces centred on x = segment * 10
    /// (StreetFurnitureBuilder.cs:45, butt-jointing at x = ±5/±15/±25) with neither gap nor
    /// overlap. The former 9.7 m footprints left 0.3 m registry gaps; this test pins the
    /// repaired invariant so it cannot regress.
    /// </summary>
    public sealed class TactileAnchorContinuityTests
    {
        private const float Delta = 0.0001f;
        private const float DesignedSegmentLength = 10f;
        private const float DesignedSegmentDepth = 0.34f;
        private const int SegmentsPerSide = 7;

        [Test]
        public void TactileAnchors_SpanExactlyTenMetres()
        {
            foreach (SpatialAnchor anchor in StreetSpatialAnchors.OfKind(AnchorKind.TactilePath))
                Assert.AreEqual(DesignedSegmentLength, anchor.Footprint.x, Delta,
                    $"{anchor.Id} must match the rendered 10 m straight-piece extent");
        }

        [Test]
        public void TactileAnchors_ButtJointWithXNeighbours()
        {
            foreach (SpatialAnchor anchor in StreetSpatialAnchors.OfKind(AnchorKind.TactilePath))
                Assert.AreEqual(DesignedSegmentDepth, anchor.Footprint.y, Delta,
                    $"{anchor.Id} must match the rendered 0.34 m paving depth");

            for (int side = -1; side <= 1; side += 2)
            {
                List<SpatialAnchor> row = new List<SpatialAnchor>();
                foreach (SpatialAnchor anchor in StreetSpatialAnchors.OfKind(AnchorKind.TactilePath))
                    if (anchor.Side == side)
                        row.Add(anchor);

                Assert.AreEqual(SegmentsPerSide, row.Count, $"side {side} tactile anchor count");
                row.Sort((a, b) => a.Position.x.CompareTo(b.Position.x));

                for (int i = 1; i < row.Count; i++)
                {
                    float previousEnd = row[i - 1].Position.x + row[i - 1].Footprint.x * .5f;
                    float nextStart = row[i].Position.x - row[i].Footprint.x * .5f;
                    Assert.AreEqual(previousEnd, nextStart, Delta,
                        $"{row[i - 1].Id} and {row[i].Id} must butt-joint on side {side}");
                }
            }
        }

        [Test]
        public void TactileAnchors_RunOnTheDesignedSidewalkLine()
        {
            float designedZ = StreetLayout.SidewalkZ(6.7f);
            Assert.AreEqual(10f, designedZ, Delta, "SidewalkZ(6.7) must stay the guide-line z");

            int[] counts = { 0, 0 };
            foreach (SpatialAnchor anchor in StreetSpatialAnchors.OfKind(AnchorKind.TactilePath))
            {
                int side = anchor.Side > 0 ? 1 : 0;
                counts[side]++;
                Assert.AreEqual(anchor.Side * designedZ, anchor.Position.z, Delta,
                    $"{anchor.Id} must sit on the Z(side, 6.7) guide line");
            }

            Assert.AreEqual(SegmentsPerSide * 2, counts[0] + counts[1], "total tactile anchor count");
        }
    }
}
