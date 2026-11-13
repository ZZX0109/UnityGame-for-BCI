using System.Collections.Generic;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Traffic
{
    /// <summary>Deterministic routes, local obstacle braking and one explicit reservation for S03's merge junction.</summary>
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
        private readonly float[] nextSpawn;
        private TrafficSpawnSequence sequence;
        private int encounterId, turnOwner;
        public IReadOnlyList<VehicleController> Active => active;
        public IReadOnlyList<VehicleLanePath> Routes => lanes;
        public int TurningReservation => turnOwner;
        public bool HasUnclearedVehicles
        {
            get { foreach (VehicleController car in active) if (car.Lane.HasUnclearedCommittedVehicle(car.Motion)) return true; return false; }
        }
        public VehicleTrafficManager(StreetScenarioDefinition settings, IntersectionController signal, StreetGameManager owner, Transform trafficRoot, StreetWorld geometry, CharacterController pedestrian)
        {
            scenario = settings; intersection = signal; game = owner; root = trafficRoot; world = geometry; player = pedestrian;
            lanes = new VehicleLanePath[settings.turningTraffic ? 4 : 2]; nextSpawn = new float[lanes.Length];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = new VehicleLanePath(settings, i);
            intersection.PhaseChanged += OnPhase; Reset();
        }
        public void Dispose() { intersection.PhaseChanged -= OnPhase; }
        public void Reset()
        {
            foreach (VehicleController car in pool) car.gameObject.SetActive(false);
            active.Clear(); sequence = new TrafficSpawnSequence(scenario.seed); encounterId = turnOwner = 0;
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                int count = lane < 2 ? scenario.initialVehiclesPerStraightLane : 1;
                for (int i = count - 1; i >= 0; i--) Spawn(lane, 9 + i * 12 + (lane % 2) * 4);
                nextSpawn[lane] = SpawnInterval(lane);
            }
        }
        private float SpawnInterval(int lane)
        {
            float interval = sequence.Between(scenario.minimumSpawnInterval, scenario.maximumSpawnInterval);
            return lane < 2 ? interval : interval / Mathf.Max(0.15f, scenario.turningShare);
        }
        private void OnPhase(TrafficPhase phase)
        {
            if (phase != TrafficPhase.VehicleAmber) return;
            foreach (VehicleController car in active)
                if (!car.Lane.IsTurning || car.EncounterId == turnOwner)
                    car.Motion.DecideAmber(car.Lane.StopDistance, scenario.comfortableBraking, scenario.reactionTime);
        }
        private void UpdateTurnReservation()
        {
            VehicleController owner = active.Find(car => car.EncounterId == turnOwner);
            if (owner == null || (!intersection.CanEnterVehicles && !owner.Motion.Committed) || owner.Motion.Rear > owner.Lane.JunctionExit || (!owner.Motion.Committed && owner.Motion.Speed < 0.04f && owner.Motion.Front < owner.Lane.StopDistance - 0.15f)) turnOwner = 0;
            if (turnOwner != 0 || !intersection.CanEnterVehicles || !scenario.turningTraffic) return;
            active.Sort((a, b) => a.EncounterId.CompareTo(b.EncounterId));
            foreach (VehicleController turn in active)
            {
                if (!turn.Lane.IsTurning || turn.Motion.Rear > turn.Lane.JunctionExit) continue;
                float distance = turn.Lane.StopDistance - turn.Motion.Front;
                if (distance > VehicleKinematics.StoppingDistance(turn.Motion.Speed, scenario.comfortableBraking, scenario.reactionTime) + 4) continue;
                bool clear = true;
                foreach (VehicleController car in active)
                {
                    if (car == turn || car.Lane.IsTurning) continue;
                    if (car.Lane.OccupiesJunction(car.Motion)) { clear = false; break; }
                    float beforeJunction = car.Lane.JunctionEntry - car.Motion.Front;
                    if (beforeJunction >= 0 && beforeJunction < VehicleKinematics.StoppingDistance(car.Motion.Speed, scenario.comfortableBraking, scenario.reactionTime) + 1)
                    { clear = false; break; }
                }
                if (!clear) continue;
                turnOwner = turn.EncounterId; game.Record("TurnReservation", "id=" + turnOwner + ";route=" + turn.Lane.Kind); break;
            }
        }
        public void Tick(float dt, float time, bool protectedCrosswalkOccupied)
        {
            if (!game.IsPlaying) return;
            intersection.SetPedestrianOccupancy(protectedCrosswalkOccupied); UpdateTurnReservation();
            active.Sort((a, b) => { int lane = a.Lane.Index.CompareTo(b.Lane.Index); return lane != 0 ? lane : b.Motion.Distance.CompareTo(a.Motion.Distance); });
            for (int laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
            {
                float leaderRear = float.PositiveInfinity;
                foreach (VehicleController car in active)
                {
                    if (car.Lane.Index != laneIndex) continue;
                    Vector3 old = car.transform.position; float oldSpeed = car.Motion.Speed;
                    float obstacleRear = leaderRear;
                    foreach (VehicleController other in active)
                    {
                        if (other == car || other.Lane.Index == car.Lane.Index) continue;
                        obstacleRear = Mathf.Min(obstacleRear, VehicleFollowing.ProjectedRear(car.Lane, car.Motion, other.transform.position, other.transform.forward,
                            other.Motion.BodyHalfLength, other.HalfWidth, car.HalfWidth));
                    }
                    // A turning reservation is separate from the protected crosswalk. Straight cars yield before its merge junction.
                    if (scenario.turningTraffic && turnOwner != 0 && !car.Lane.IsTurning && car.Motion.Front < car.Lane.JunctionEntry)
                        obstacleRear = Mathf.Min(obstacleRear, car.Lane.JunctionEntry + scenario.queueGap - 0.2f);
                    // A pedestrian outside the protected crossing affects only the vehicle whose swept corridor they occupy.
                    float playerDistance = car.Lane.NearestDistance(player.transform.position);
                    float lateral = Vector3.Distance(car.Lane.Position(playerDistance), new Vector3(player.transform.position.x, 0, player.transform.position.z));
                    if (lateral < car.HalfWidth + player.radius + 0.15f && playerDistance > car.Motion.Front - 0.25f)
                        obstacleRear = Mathf.Min(obstacleRear, playerDistance - player.radius + scenario.queueGap);
                    bool mayEnter = intersection.CanEnterVehicles && (!car.Lane.IsTurning || car.EncounterId == turnOwner);
                    float curveSpeed = car.Lane.IsTurning && car.Motion.Distance >= car.Lane.StopDistance - 2 && car.Motion.Distance < car.Lane.JunctionExit + 2 ? 3.1f : -1;
                    car.Motion.Step(dt, car.Lane.StopDistance, mayEnter, obstacleRear, scenario.queueGap,
                        scenario.acceleration * car.AccelerationMultiplier, scenario.comfortableBraking, scenario.reactionTime, curveSpeed);
                    car.ApplyPosition(); Physics.SyncTransforms(); leaderRear = car.Motion.Rear;
                    if (car.SweptContact(player, old)) { game.RegisterContact(car.EncounterId, Mathf.Max(oldSpeed, car.Motion.Speed)); if (!game.IsPlaying) return; }
                    Vector3 delta = player.transform.position - car.transform.position;
                    Vector3 forward = car.transform.forward;
                    float closing = Vector3.Dot(new Vector3(delta.x, 0, delta.z), forward);
                    float edgeDistance = Mathf.Max(0, delta.magnitude - car.Motion.BodyHalfLength);
                    bool inRoad = game.IsPlayerInRoadwayHazard;
                    if (inRoad && car.Motion.Speed > 1 && closing > 0 && lateral < car.HalfWidth + 0.8f
                        && edgeDistance < scenario.nearMissDistance && edgeDistance / car.Motion.Speed < 1.1f)
                        game.Evaluation.NearMiss(car.EncounterId);
                }
                if (time >= nextSpawn[laneIndex])
                {
                    int quota = Mathf.Max(1, scenario.maximumVehicles / lanes.Length + (laneIndex < scenario.maximumVehicles % lanes.Length ? 1 : 0));
                    if (active.Count < scenario.maximumVehicles && LaneCount(laneIndex) < quota && SpawnIsClear(laneIndex))
                    { Spawn(laneIndex, 0); nextSpawn[laneIndex] = time + SpawnInterval(laneIndex); }
                    else nextSpawn[laneIndex] = time + 0.25f;
                }
            }
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Motion.Rear > active[i].Lane.Length)
                { game.Record("VehicleRecycled", active[i].EncounterId.ToString()); if (turnOwner == active[i].EncounterId) turnOwner = 0; active[i].gameObject.SetActive(false); active.RemoveAt(i); }
        }
        private int LaneCount(int lane) { int count = 0; foreach (VehicleController car in active) if (car.Lane.Index == lane) count++; return count; }
        private bool SpawnIsClear(int lane)
        {
            foreach (VehicleController car in active) if (car.Lane.Index == lane && car.Motion.Rear < 3 + scenario.queueGap + scenario.maximumSpeed * scenario.reactionTime) return false;
            return true;
        }
        private void Spawn(int lane, float distance)
        {
            VehicleController car = pool.Find(v => !v.gameObject.activeSelf);
            if (car == null)
            {
                GameObject go = Object.Instantiate(scenario.vehiclePrefab, root); go.name = "Street vehicle (pooled)";
                car = go.GetComponent<VehicleController>(); pool.Add(car);
            }
            int variant = scenario.scenarioId == "S01" ? 0 : sequence.NextVariant();
            float speed = sequence.Between(scenario.minimumSpeed, scenario.maximumSpeed) * (variant == 2 ? 0.86f : variant == 1 ? 1.05f : 1);
            world.DetailVehicle(car.gameObject, variant); world.ColorVehicle(car.gameObject, sequence.NextColor());
            car.Initialize(lanes[lane], ++encounterId, distance, speed, variant); active.Add(car);
            game.Record("VehicleSpawn", "id=" + encounterId + ";route=" + lanes[lane].Kind + ";variant=" + variant + ";speed=" + speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
