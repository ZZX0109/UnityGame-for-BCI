using System;
using UnityEngine;

namespace StreetCrossing.Core
{
    /// <summary>The approved four-lane main road; the side street stays two-lane.</summary>
    public static class StreetLayout
    {
        public const int StraightLaneCount = 4;
        public const float LaneWidth = 3.3f;
        public const float RoadHalfWidth = 6.6f;
        public const float SideRoadHalfWidth = 3.3f;
        public const float SidewalkOffset = 3.3f;
        public const float SpawnZ = -11.3f;
        public const float DestinationZ = 10.8f;

        public static float SidewalkZ(float twoLaneZ)
            => twoLaneZ == 0f ? 0f : twoLaneZ + Mathf.Sign(twoLaneZ) * SidewalkOffset;

        public static float LaneZ(int index, float halfLaneWidth = 1.65f)
        {
            if (index < 0 || index >= StraightLaneCount) throw new ArgumentOutOfRangeException(nameof(index));
            return (index * 2 - (StraightLaneCount - 1)) * halfLaneWidth;
        }
    }
}
