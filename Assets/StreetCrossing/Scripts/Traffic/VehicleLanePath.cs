using System.Collections.Generic;
using UnityEngine;
using StreetCrossing.Core;

namespace StreetCrossing.Traffic
{
    public enum VehicleRouteKind { StraightNear, StraightFar, RightTurn, LeftTurn }
    /// <summary>Arc-length sampled world route. The two S03 curves have actual continuously changing headings.</summary>
    public sealed class VehicleLanePath
    {
        private readonly Vector3[] points;
        private readonly float[] cumulative;
        public int Index { get; }
        public int Direction { get; }
        public VehicleRouteKind Kind { get; }
        public bool IsTurning => Kind == VehicleRouteKind.RightTurn || Kind == VehicleRouteKind.LeftTurn;
        public float HalfLength { get; }
        public float Z { get; }
        public float StopDistance { get; }
        public float ConflictEntry { get; }
        public float ConflictExit { get; }
        public float JunctionEntry { get; }
        public float JunctionExit { get; }
        public float Length => cumulative[cumulative.Length - 1];
        public VehicleLanePath(StreetScenarioDefinition scenario, int index)
        {
            Index = index; Kind = (VehicleRouteKind)index; HalfLength = scenario.roadHalfLength;
            Direction = scenario.DirectionForLane(index == 1 ? 1 : 0);
            Z = index == 1 ? scenario.laneOffset : -scenario.laneOffset;
            List<Vector3> route = new List<Vector3>();
            float junctionX = -9 * scenario.DirectionForLane(0);
            if (!IsTurning)
            {
                route.Add(new Vector3(-Direction * HalfLength, 0, Z)); route.Add(new Vector3(Direction * HalfLength, 0, Z));
            }
            else
            {
                // South approach/right turn and north approach/left turn merge into the near main-road lane.
                float approachSide = index == 2 ? -Direction : Direction;
                float approachX = junctionX + (index == 2 ? 1.65f : -1.65f) * Direction;
                Vector3 start = new Vector3(approachX, 0, approachSide * 36);
                Vector3 begin = new Vector3(approachX, 0, approachSide * 8);
                Vector3 end = new Vector3(-3.6f * Direction, 0, Z);
                Vector3 control1 = begin + new Vector3(0, 0, -approachSide * (index == 2 ? 3.5f : 5.3f));
                Vector3 control2 = end - new Vector3(Direction * (index == 2 ? 2.1f : 3.9f), 0, 0);
                route.Add(start); route.Add(begin);
                for (int step = 1; step <= 40; step++)
                {
                    float t = step / 40f, a = 1 - t;
                    route.Add(a * a * a * begin + 3 * a * a * t * control1 + 3 * a * t * t * control2 + t * t * t * end);
                }
                route.Add(new Vector3(Direction * HalfLength, 0, Z));
            }
            points = route.ToArray(); cumulative = new float[points.Length];
            for (int i = 1; i < points.Length; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            if (!IsTurning)
            {
                StopDistance = HalfLength - scenario.stopLineOffset;
                ConflictEntry = HalfLength - scenario.conflictHalfWidth;
                ConflictExit = HalfLength + scenario.conflictHalfWidth;
                float firstX = Direction > 0 ? junctionX - 5 : junctionX + 5;
                float lastX = Direction > 0 ? junctionX + 5 : junctionX - 5;
                JunctionEntry = NearestDistance(new Vector3(firstX, 0, Z)); JunctionExit = NearestDistance(new Vector3(lastX, 0, Z));
            }
            else
            {
                StopDistance = cumulative[1] - 1.0f;
                ConflictEntry = NearestDistance(new Vector3(-scenario.conflictHalfWidth * Direction, 0, Z));
                ConflictExit = NearestDistance(new Vector3(scenario.conflictHalfWidth * Direction, 0, Z));
                JunctionEntry = StopDistance; JunctionExit = cumulative[cumulative.Length - 2] + 1;
            }
        }
        public Vector3 Position(float distance)
        {
            if (distance <= 0) return points[0] + (points[1] - points[0]).normalized * distance;
            for (int i = 1; i < points.Length; i++) if (distance <= cumulative[i])
                return Vector3.Lerp(points[i - 1], points[i], (distance - cumulative[i - 1]) / Mathf.Max(0.001f, cumulative[i] - cumulative[i - 1]));
            return points[points.Length - 1] + (points[points.Length - 1] - points[points.Length - 2]).normalized * (distance - Length);
        }
        public Quaternion RotationAt(float distance)
        {
            Vector3 tangent = Position(distance + 0.12f) - Position(distance - 0.12f);
            return tangent.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(tangent) : Quaternion.identity;
        }
        public Quaternion Rotation => RotationAt(0);
        public float NearestDistance(Vector3 position)
        {
            float best = float.PositiveInfinity, result = 0;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 segment = points[i] - points[i - 1];
                float t = Mathf.Clamp01(Vector3.Dot(position - points[i - 1], segment) / Mathf.Max(0.0001f, segment.sqrMagnitude));
                float sqr = (position - (points[i - 1] + segment * t)).sqrMagnitude;
                if (sqr < best) { best = sqr; result = Mathf.Lerp(cumulative[i - 1], cumulative[i], t); }
            }
            return result;
        }
        public bool HasUnclearedCommittedVehicle(VehicleKinematics vehicle) => (vehicle.Committed && vehicle.Rear < ConflictExit) || OccupiesConflict(vehicle);
        public bool OccupiesConflict(VehicleKinematics vehicle) => vehicle.Front > ConflictEntry && vehicle.Rear < ConflictExit;
        public bool OccupiesJunction(VehicleKinematics vehicle) => vehicle.Front > JunctionEntry && vehicle.Rear < JunctionExit;
    }
}
