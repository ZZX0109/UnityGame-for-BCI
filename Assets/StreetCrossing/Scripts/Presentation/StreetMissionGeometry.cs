using System;
using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Real metre-scale doorsteps and orthogonal sidewalk routes; no traffic or scoring changes.</summary>
    public static class StreetMissionGeometry
    {
        public static Vector3 Doorstep(StreetDestinationKind kind)
        {
            switch (kind)
            {
                case StreetDestinationKind.Convenience: return new Vector3(0, 0, 11.9f);
                case StreetDestinationKind.Residence: return new Vector3(0, 0, -11.9f);
                case StreetDestinationKind.Bookshop: return new Vector3(9.6f, 0, 11.9f);
                case StreetDestinationKind.Pharmacy: return new Vector3(13, 0, 11.9f);
                case StreetDestinationKind.Cafe: return new Vector3(16.4f, 0, 11.9f);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        public static string Name(StreetDestinationKind kind)
        {
            switch (kind)
            {
                case StreetDestinationKind.Convenience: return "街角便利店";
                case StreetDestinationKind.Residence: return "梧桐里住宅入口";
                case StreetDestinationKind.Bookshop: return "时光书店";
                case StreetDestinationKind.Pharmacy: return "社区药房";
                case StreetDestinationKind.Cafe: return "街角咖啡";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        public static float CorridorZ(int side) => side > 0 ? 10.2f : -9.95f;
        public static int ForwardSign(StreetMissionDefinition mission, int segmentIndex)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (segmentIndex < 0 || segmentIndex >= (mission.roundTrip ? 2 : 1)) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            return (mission.reverse ? -1 : 1) * (segmentIndex == 0 ? 1 : -1);
        }
        public static StreetDestinationKind GoalKind(StreetMissionDefinition mission, int segmentIndex)
        {
            ForwardSign(mission, segmentIndex);
            return segmentIndex == 0 ? mission.destinationKind : StreetDestinationKind.Residence;
        }
        public static Vector3 Spawn(StreetMissionDefinition mission, int segmentIndex)
        {
            ForwardSign(mission, segmentIndex);
            Vector3 position = Doorstep(segmentIndex == 0 ? (mission.reverse ? StreetDestinationKind.Convenience : StreetDestinationKind.Residence) : mission.destinationKind);
            position.y = .02f; return position;
        }
        public static Vector3[] Approach(Vector3 spawn, int forward, float width)
            => new[] { new Vector3(spawn.x, 0, CorridorZ(-forward)), new Vector3(0, 0, CorridorZ(-forward)), new Vector3(0, 0, -forward * (width + .12f)) };
        public static Vector3[] Arrival(Vector3 destination, int forward, float width)
            => new[] { new Vector3(0, 0, forward * (width + .8f)), new Vector3(0, 0, CorridorZ(forward)), new Vector3(destination.x, 0, CorridorZ(forward)), destination };
    }
}
