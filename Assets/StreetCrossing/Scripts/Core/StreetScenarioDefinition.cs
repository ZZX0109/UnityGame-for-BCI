using System;
using StreetCrossing.Traffic;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Core
{
    [CreateAssetMenu(menuName = "Street Crossing/Scenario")]
    public sealed class StreetScenarioDefinition : ScriptableObject
    {
        public string scenarioId = "S01";
        public StreetVisualProfile visualProfile;
        public string title = "S01 普通斑马线";
        [TextArea] public string description = "等待行人绿灯，观察双向车流后安全过街。";
        public bool requireLeftObservation = true;
        public bool requireRightObservation = true;
        public float observationDwellSeconds = 0.35f;
        public bool turningTraffic;
        public bool kerbOcclusion;
        [Range(0, 1)] public float turningShare = 0.4f;
        public int initialVehiclesPerStraightLane = 1;
        public string destinationName = "街角便利店";
        public int seed = 3101;
        public IntersectionPhaseTable phaseTable;
        public Material surfaceMaterial;
        public GameObject vehiclePrefab;
        [Header("Road coordinates in metres; +Z is the crossing direction")]
        public bool rightHandTraffic = true;
        public float roadHalfWidth = 3.3f;
        public float laneOffset = 1.65f;
        public float roadHalfLength = 42;
        public float crosswalkHalfWidth = 1.8f;
        public float conflictHalfWidth = 2.3f;
        public float stopLineOffset = 4.6f;
        public Vector3 playerSpawn = new Vector3(0, 0.02f, -8);
        public Vector3 destination = new Vector3(0, 0, 7.5f);
        [Header("S01 repeatable traffic")]
        [Range(2, 12)] public int maximumVehicles = 2;
        public float minimumSpeed = 6;
        public float maximumSpeed = 7;
        public float minimumSpawnInterval = 7;
        public float maximumSpawnInterval = 9;
        public float acceleration = 1.7f;
        public float comfortableBraking = 3;
        public float reactionTime = 0.6f;
        public float queueGap = 2.5f;
        public float walkingSpeed = 1.35f;
        public float comfortableSpeed = 1.05f;
        public float fastWalkingSpeed = 1.85f;
        public float nearMissDistance = 1.8f;
        public float timeoutSeconds = 240;
        public float SafeCrossingSeconds => roadHalfWidth * 2 / comfortableSpeed + 2;
        public int DirectionForLane(int lane) => ((lane == 0) == rightHandTraffic) ? 1 : -1;
        public void Validate()
        {
            if ((scenarioId != "S01" && scenarioId != "S02" && scenarioId != "S03") || phaseTable == null || surfaceMaterial == null || vehiclePrefab == null)
                throw new ArgumentException("Scenario requires its phase table, referenced surface material and vehicle prefab.");
            if (visualProfile == null) throw new ArgumentException("Scenario requires its explicit visual profile.");
            visualProfile.Validate(scenarioId);
            phaseTable.Validate();
            float[] values = { observationDwellSeconds, turningShare, roadHalfWidth, laneOffset, roadHalfLength, crosswalkHalfWidth, conflictHalfWidth, stopLineOffset, minimumSpeed, maximumSpeed, minimumSpawnInterval, maximumSpawnInterval, acceleration, comfortableBraking, reactionTime, queueGap, walkingSpeed, comfortableSpeed, fastWalkingSpeed, nearMissDistance, timeoutSeconds, playerSpawn.x, playerSpawn.y, playerSpawn.z, destination.x, destination.y, destination.z };
            foreach (float value in values) if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Scenario numeric values must be finite.");
            if (observationDwellSeconds < 0.2f || observationDwellSeconds > 2 || turningShare < 0 || turningShare > 1
                || initialVehiclesPerStraightLane < 1 || initialVehiclesPerStraightLane * 2 + (turningTraffic ? 2 : 0) > maximumVehicles
                || (scenarioId == "S03" && (!turningTraffic || !kerbOcclusion || maximumVehicles < 6 || turningShare <= 0))
                || (scenarioId == "S02" && maximumVehicles < 6) || roadHalfWidth < 3 || laneOffset <= 0 || laneOffset >= roadHalfWidth || crosswalkHalfWidth < 1.5f
                || conflictHalfWidth < crosswalkHalfWidth || stopLineOffset < conflictHalfWidth + 1
                || roadHalfLength < stopLineOffset + 20 || minimumSpeed <= 0 || maximumSpeed < minimumSpeed
                || maximumVehicles < 2 || maximumVehicles > 12 || minimumSpawnInterval <= 0 || maximumSpawnInterval < minimumSpawnInterval
                || acceleration <= 0 || comfortableBraking <= 0 || reactionTime < 0 || queueGap < 1
                || nearMissDistance <= 0 || walkingSpeed <= 0 || comfortableSpeed <= 0 || fastWalkingSpeed < walkingSpeed || timeoutSeconds < 30)
                throw new ArgumentException("Invalid scenario geometry, movement or traffic settings.");
            if (Mathf.Abs(playerSpawn.x) > crosswalkHalfWidth || playerSpawn.z >= -roadHalfWidth - 1 || playerSpawn.z < -9.5f || playerSpawn.y < 0 || playerSpawn.y > 0.3f
                || Mathf.Abs(destination.x) > crosswalkHalfWidth || destination.z <= roadHalfWidth + 1 || destination.z > 8.5f)
                throw new ArgumentException("Scenario spawn/destination must connect the approach and opposite shop sidewalk.");
            if (VehicleKinematics.StoppingDistance(maximumSpeed * 1.05f, comfortableBraking, reactionTime) + VehicleKinematics.HalfLength + 1 > roadHalfLength - stopLineOffset)
                throw new ArgumentException("Spawn approach is too short for the configured stopping distance.");
            if (phaseTable.phases[(int)TrafficPhase.Walk].duration < SafeCrossingSeconds)
                throw new ArgumentException("Walk must include a complete comfortable-speed crossing window plus margin.");
        }
    }
}
