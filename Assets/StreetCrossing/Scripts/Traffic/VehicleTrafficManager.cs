using System.Collections.Generic;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Traffic
{
    public sealed class VehicleTrafficManager
    {
        private readonly StreetScenarioDefinition scenario;
        private readonly IntersectionController intersection;
        private readonly StreetGameManager game;
        private readonly Transform root;
        private readonly StreetWorld world;
        private readonly CharacterController player;
        private readonly List<VehicleController> pool = new List<VehicleController>();
        private readonly List<VehicleController> active = new List<VehicleController>();
        private readonly VehicleLanePath[] lanes;
        private readonly float[] nextSpawn = new float[2];
        private TrafficSpawnSequence sequence;
        private int encounterId;
        public IReadOnlyList<VehicleController> Active => active;
        public bool HasUnclearedVehicles
        {
            get
            {
                foreach (VehicleController car in active)
                    if (car.Lane.HasUnclearedCommittedVehicle(car.Motion)) return true;
                return false;
            }
        }
        public VehicleTrafficManager(StreetScenarioDefinition settings, IntersectionController signal, StreetGameManager owner, Transform trafficRoot, StreetWorld geometry, CharacterController pedestrian)
        {
            scenario = settings; intersection = signal; game = owner; root = trafficRoot; world = geometry; player = pedestrian;
            lanes = new[] { new VehicleLanePath(settings, 0), new VehicleLanePath(settings, 1) };
            intersection.PhaseChanged += OnPhase;
            Reset();
        }
        public void Dispose() { intersection.PhaseChanged -= OnPhase; }
        public void Reset()
        {
            foreach (VehicleController car in pool) car.gameObject.SetActive(false);
            active.Clear(); sequence = new TrafficSpawnSequence(scenario.seed); encounterId = 0;
            for (int lane = 0; lane < 2; lane++)
            {
                Spawn(lane, 12 + lane * 5);
                nextSpawn[lane] = sequence.Between(scenario.minimumSpawnInterval, scenario.maximumSpawnInterval);
            }
        }
        private void OnPhase(TrafficPhase phase)
        {
            if (phase != TrafficPhase.VehicleAmber) return;
            foreach (VehicleController car in active) car.Motion.DecideAmber(car.Lane.StopDistance, scenario.comfortableBraking, scenario.reactionTime);
        }
        public void Tick(float dt, float time, bool pedestrianOccupied)
        {
            if (!game.IsPlaying) return;
            intersection.SetPedestrianOccupancy(pedestrianOccupied);
            for (int laneIndex = 0; laneIndex < 2; laneIndex++)
            {
                active.Sort((a, b) => b.Motion.Distance.CompareTo(a.Motion.Distance));
                float leaderRear = float.PositiveInfinity;
                foreach (VehicleController car in active)
                {
                    if (car.Lane.Index != laneIndex) continue;
                    Vector3 old = car.transform.position;
                    car.Motion.Step(dt, car.Lane.StopDistance, intersection.CanEnterVehicles, leaderRear, scenario.queueGap,
                        scenario.acceleration, scenario.comfortableBraking, scenario.reactionTime);
                    car.ApplyPosition(); Physics.SyncTransforms(); leaderRear = car.Motion.Rear;
                    if (car.SweptContact(player, old)) { game.RegisterCollision(car.EncounterId); if (!game.IsPlaying) return; }
                    Vector3 delta = player.transform.position - car.transform.position;
                    float ahead = delta.x * car.Lane.Direction;
                    float edgeDistance = Mathf.Max(0, Mathf.Abs(delta.x) - VehicleKinematics.HalfLength);
                    bool playerInRoad = Mathf.Abs(player.transform.position.z) < scenario.roadHalfWidth;
                    // Moving, closing and within the same lateral corridor; stopped queues are never near misses.
                    if (playerInRoad && car.Motion.Speed > 1 && ahead > 0 && Mathf.Abs(delta.z) < 1.3f
                        && edgeDistance < scenario.nearMissDistance && edgeDistance / car.Motion.Speed < 1.1f)
                        game.Evaluation.NearMiss(car.EncounterId);
                }
                if (time >= nextSpawn[laneIndex])
                {
                    if (active.Count < scenario.maximumVehicles && LaneCount(laneIndex) < (scenario.maximumVehicles + (laneIndex == 0 ? 1 : 0)) / 2 && SpawnIsClear(laneIndex))
                    { Spawn(laneIndex, 0); nextSpawn[laneIndex] = time + sequence.Between(scenario.minimumSpawnInterval, scenario.maximumSpawnInterval); }
                    else nextSpawn[laneIndex] = time + 0.25f;
                }
            }
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Motion.Rear > active[i].Lane.Length)
                { game.Record("VehicleRecycled", active[i].EncounterId.ToString()); active[i].gameObject.SetActive(false); active.RemoveAt(i); }
        }
        private int LaneCount(int lane)
        {
            int count = 0; foreach (VehicleController car in active) if (car.Lane.Index == lane) count++; return count;
        }
        private bool SpawnIsClear(int lane)
        {
            foreach (VehicleController car in active)
                if (car.Lane.Index == lane && car.Motion.Rear < VehicleKinematics.HalfLength + scenario.queueGap + scenario.maximumSpeed * scenario.reactionTime) return false;
            return true;
        }
        private void Spawn(int lane, float distance)
        {
            VehicleController car = pool.Find(v => !v.gameObject.activeSelf);
            if (car == null)
            {
                GameObject go = Object.Instantiate(scenario.vehiclePrefab, root); go.name = "S01 sedan (pooled)";
                car = go.GetComponent<VehicleController>(); world.DetailVehicle(go); pool.Add(car);
            }
            float speed = sequence.Between(scenario.minimumSpeed, scenario.maximumSpeed);
            world.ColorVehicle(car.gameObject, sequence.NextColor());
            car.Initialize(lanes[lane], ++encounterId, distance, speed); active.Add(car);
            game.Record("VehicleSpawn", "id=" + encounterId + ";lane=" + lane + ";speed=" + speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
