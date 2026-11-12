using UnityEngine;
using StreetCrossing.Core;

namespace StreetCrossing.Traffic
{
    public sealed class VehicleLanePath
    {
        public int Index { get; }
        public int Direction { get; }
        public float HalfLength { get; }
        public float Z { get; }
        public float StopDistance { get; }
        public float ConflictEntry { get; }
        public float ConflictExit { get; }
        public float Length => HalfLength * 2;
        public VehicleLanePath(StreetScenarioDefinition scenario, int index)
        {
            Index = index; Direction = scenario.DirectionForLane(index); HalfLength = scenario.roadHalfLength;
            Z = index == 0 ? -scenario.laneOffset : scenario.laneOffset;
            StopDistance = HalfLength - scenario.stopLineOffset;
            ConflictEntry = HalfLength - scenario.conflictHalfWidth;
            ConflictExit = HalfLength + scenario.conflictHalfWidth;
        }
        public Vector3 Position(float distance) => new Vector3(Direction * (distance - HalfLength), 0, Z);
        public Quaternion Rotation => Quaternion.Euler(0, Direction * 90, 0);
        public bool HasUnclearedCommittedVehicle(VehicleKinematics vehicle) => (vehicle.Committed && vehicle.Rear < ConflictExit) || OccupiesConflict(vehicle);
        public bool OccupiesConflict(VehicleKinematics vehicle) => vehicle.Front > ConflictEntry && vehicle.Rear < ConflictExit;
    }
}
