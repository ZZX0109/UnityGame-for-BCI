using System.Collections.Generic;
using StreetCrossing.Evaluation;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using StreetCrossing.UI;
using UnityEngine;

namespace StreetCrossing.Core
{
    public enum StreetGameState { Menu, Playing, Paused, Result }
    public sealed class StreetGameManager : MonoBehaviour
    {
        public StreetScenarioDefinition scenario;
        public StreetScenarioDefinition[] scenarios;
        public Font uiFont;
        private StreetWorld world;
        private VehicleTrafficManager traffic;
        private PedestrianPlayerController player;
        private ObservationTracker observation;
        private StreetEventLog log;
        private Transform environment;
        private StreetUIController ui;
        private IStreetInput input = new LegacyStreetInput();
        private float accumulator;
        private int attempt;
        private CrossingRegion region;
        public IntersectionController Intersection { get; private set; }
        public CrossingEvaluator Evaluation { get; private set; }
        public StreetGameState State { get; private set; } = StreetGameState.Menu;
        public bool AutoPauseOnFocusLoss { get; set; } = true;
        public bool IsPlaying => State == StreetGameState.Playing && isActiveAndEnabled;
        public bool IsPlayerInRoadwayHazard => (region & CrossingRegion.RoadwayHazard) != 0;
        public bool IsPlayerInProtectedCrosswalk => (region & CrossingRegion.ProtectedCrosswalk) != 0;
        public bool IsAtKerb => (region & CrossingRegion.Kerb) != 0 && !IsPlayerInRoadwayHazard;
        public int SelectedScenarioIndex { get; private set; }
        public IReadOnlyList<StreetScenarioDefinition> Scenarios => scenarios;
        public float WorldTime { get; private set; }
        public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
        public PedestrianPlayerController Player => player;
        public VehicleTrafficManager Traffic => traffic;
        public IReadOnlyList<CrossingZone> Zones => world.Zones;
        public Vector3[] LeftRoadTargets => world.LeftRoadTargets;
        public Vector3[] RightRoadTargets => world.RightRoadTargets;
        public string Notice { get; private set; } = "";
        public string LogPath => log?.Path;
        public string LogError => log?.Error;
        public event System.Action<string, string> GameEvent;
        public int Stars => Evaluation.SafetySuccess ? 1 + (Evaluation.LeftObserved && Evaluation.RightObserved ? 1 : 0) + (Evaluation.NearMisses == 0 && Evaluation.StationaryContacts == 0 ? 1 : 0) : 0;
        private const float Step = 0.02f;
        private void Awake()
        {
            if (scenario == null || uiFont == null) { Debug.LogError("Street scene is missing scenario or dedicated UI font.", this); enabled = false; return; }
            if (scenarios == null || scenarios.Length == 0) scenarios = new[] { scenario };
            HashSet<string> ids = new HashSet<string>();
            foreach (StreetScenarioDefinition entry in scenarios)
            { entry.Validate(); if (!ids.Add(entry.scenarioId)) throw new System.ArgumentException("Duplicate stable scenario id."); }
            Application.targetFrameRate = 60;
            RebuildScenario(); ui = gameObject.AddComponent<StreetUIController>(); ui.Initialize(this, uiFont);
            SetCursor(false);
        }
        public void SelectScenario(int index)
        {
            if (index < 0 || index >= scenarios.Length || IsPlaying) return;
            Record("ScenarioChanged", scenarios[index].scenarioId); SelectedScenarioIndex = index; scenario = scenarios[index];
            State = StreetGameState.Menu; Notice = ""; attempt = 0; WorldTime = accumulator = 0; RebuildScenario(); SetCursor(false);
        }
        private void RebuildScenario()
        {
            if (Intersection != null) { Intersection.PhaseChanged -= PhaseChanged; Intersection.ClearanceBlocked -= ClearanceBlocked; }
            traffic?.Dispose(); log?.Dispose(); world?.Dispose();
            if (environment != null) { environment.gameObject.SetActive(false); Destroy(environment.gameObject); }
            environment = new GameObject(scenario.scenarioId + " Street Environment").transform; environment.SetParent(transform, false);
            Intersection = new IntersectionController(scenario.phaseTable);
            world = new StreetWorld(environment, scenario.surfaceMaterial, uiFont); world.Build(scenario);
            GameObject rig = new GameObject("Pedestrian Player", typeof(CharacterController)); rig.transform.SetParent(environment, false);
            player = rig.AddComponent<PedestrianPlayerController>(); player.Initialize(this, scenario, input);
            log = new StreetEventLog(scenario.scenarioId); region = CrossingRegion.None; ResetEvaluation();
            traffic = new VehicleTrafficManager(scenario, Intersection, this, world.TrafficRoot, world, rig.GetComponent<CharacterController>());
            GameObject signalView = new GameObject("Intersection Signals"); signalView.transform.SetParent(environment, false);
            signalView.AddComponent<TrafficSignalView>().Initialize(this, world, scenario);
            Intersection.PhaseChanged += PhaseChanged; Intersection.ClearanceBlocked += ClearanceBlocked;
            environment.gameObject.AddComponent<StreetCrossing.Audio.StreetAudioController>().Initialize(this);
            Physics.SyncTransforms(); Record("SessionOpened", "Prototype scenario=" + scenario.scenarioId + ";seed=" + scenario.seed + ";not a traffic-law standard or medical assessment.");
        }
        private void ResetEvaluation()
        {
            if (Evaluation != null) Evaluation.Event -= OnEvaluation;
            Evaluation = new CrossingEvaluator(scenario.requireLeftObservation, scenario.requireRightObservation); Evaluation.Event += OnEvaluation;
            observation = new ObservationTracker(scenario.observationDwellSeconds); observation.Observed += Evaluation.Observe;
        }
        public void SetInputSource(IStreetInput source) { input = source ?? new LegacyStreetInput(); if (player != null) player.SetInputSource(input); }
        public void StartPractice()
        {
            if (!isActiveAndEnabled) return; ui?.CloseModal();
            attempt++; WorldTime = accumulator = 0; region = CrossingRegion.None; Notice = "";
            ResetEvaluation(); player.ResetPose(); Intersection.Reset(); traffic.Reset();
            State = StreetGameState.Playing; SetCursor(true); Physics.SyncTransforms(); Record("AttemptStarted", "seed=" + scenario.seed);
        }
        private void Update()
        {
            if (Intersection == null) return;
            if (input.PausePressed && ui.HasModal) ui.CloseModal();
            else if (input.PausePressed) { if (State == StreetGameState.Playing) Pause(); else if (State == StreetGameState.Paused) Resume(); }
            if (input.RestartPressed && State != StreetGameState.Menu) RequestRestart();
            if (!IsPlaying) return;
            accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            while (accumulator >= Step && IsPlaying)
            {
                accumulator -= Step; WorldTime += Step; TickWorld(Step);
                if (WorldTime >= scenario.timeoutSeconds && !Evaluation.Finished) Evaluation.Timeout();
                if (Evaluation.Finished) Finish();
            }
        }
        private void TickWorld(float dt)
        {
            region = CrossingRegion.None;
            foreach (CrossingZone zone in world.Zones) if (zone.Contains(player.transform.position)) region |= zone.region;
            bool roadway = IsPlayerInRoadwayHazard;
            bool protectedOccupied = IsPlayerInProtectedCrosswalk;
            RoadObservationEvidence evidence = new RoadObservationEvidence {
                atKerb = IsAtKerb, beforeEntry = Evaluation.MayRecordPreEntryObservation, yaw = player.Heading, pitch = player.Pitch,
                leftTargetVisible = DirectionVisible(true),
                rightTargetVisible = DirectionVisible(false)
            };
            observation.Tick(evidence, dt);
            Evaluation.Tick(region, Intersection.Phase, (region & CrossingRegion.CrossingRoute) != 0, traffic.HasUnclearedVehicles, dt);
            if (Evaluation.Finished) return;
            // Roadway hazard is used by evaluation/local vehicle sensing only. Signal priority is confined to the protected crossing.
            Intersection.Tick(dt, traffic.HasUnclearedVehicles, protectedOccupied);
            traffic.Tick(dt, WorldTime, protectedOccupied);
            if (Intersection.IsClearanceHeld && !Notice.StartsWith("清空等待")) Notice = "受保护横道正在清空，冲突车辆继续等待。";
            else if (Notice.StartsWith("受保护横道")) Notice = "";
            if (!roadway && IsAtKerb && scenario.kerbOcclusion && !evidence.leftTargetVisible && !Evaluation.LeftObserved)
                Notice = "左侧视线可能被遮挡。请在人行道内调整站位，确认能看到驶近道路。";
        }
        public bool DirectionVisible(bool left)
        {
            if (RoadObservationVisibility.TargetVisible(player.View, left ? world.LeftRoadTargets : world.RightRoadTargets, player.transform)) return true;
            foreach (VehicleController car in traffic.Active)
            {
                if (Mathf.Abs(car.transform.position.z) > scenario.roadHalfWidth + 1 || (left ? car.Lane.Direction <= 0 : car.Lane.Direction >= 0)) continue;
                float dx = car.transform.position.x - player.transform.position.x;
                if ((left && dx > -2) || (!left && dx < 2)) continue;
                Vector3 point = car.transform.position + Vector3.up * 0.9f;
                if (RoadObservationVisibility.TargetVisible(player.View, new[] { point }, player.transform, car.transform)) return true;
            }
            return false;
        }
        public void RegisterContact(int encounterId, float vehicleSpeed)
        {
            if (!IsPlaying || Evaluation.Finished) return;
            Evaluation.Contact(encounterId, vehicleSpeed); if (Evaluation.Finished) Finish();
        }
        public void RegisterCollision(int encounterId) { RegisterContact(encounterId, 1); }
        private void Finish()
        {
            if (State == StreetGameState.Result) return;
            State = StreetGameState.Result; SetCursor(false);
            if (Evaluation.SafetySuccess) StreetPreferences.SaveStars(scenario.scenarioId, Stars);
            Record("AttemptResult", "safe=" + Evaluation.SafetySuccess + ";stars=" + Stars);
        }
        public void Pause() { if (State != StreetGameState.Playing) return; State = StreetGameState.Paused; SetCursor(false); Record("Paused", "Unified simulation frozen."); }
        public void Resume() { if (!isActiveAndEnabled || State != StreetGameState.Paused) return; State = StreetGameState.Playing; SetCursor(true); Record("Resumed", ""); }
        public void RequestRestart() { if (State == StreetGameState.Playing) Pause(); ui?.ShowRestartConfirmation(); }
        public void ReturnToMenu() { if (IsPlaying) Pause(); State = StreetGameState.Menu; SetCursor(false); Record("ReturnedToMenu", ""); }
        public void Inspect(string objectName) { Notice = "查看道路标识与目的地，确认视线清晰后再进入横道。"; Record("Inspect", objectName); }
        private void PhaseChanged(TrafficPhase phase) { Record("TrafficPhase", phase.ToString()); }
        private void ClearanceBlocked(string message) { Notice = "清空等待较长，保持全红；可暂停检查或重新开始。"; Record("ClearanceWatchdog", message); }
        private void OnEvaluation(string name, string detail) { Record(name, detail); GameEvent?.Invoke(name, detail); }
        public void Record(string name, string detail) { log?.Write(name, detail, WorldTime, attempt, Intersection == null ? "" : Intersection.Phase.ToString(), PlayerPosition); }
        private static void SetCursor(bool locked) { Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
        private void OnApplicationFocus(bool focus) { if (AutoPauseOnFocusLoss && !focus && State == StreetGameState.Playing) Pause(); }
        private void OnDisable() { if (State == StreetGameState.Playing) Pause(); }
        private void OnDestroy()
        {
            if (Intersection != null) { Intersection.PhaseChanged -= PhaseChanged; Intersection.ClearanceBlocked -= ClearanceBlocked; }
            traffic?.Dispose(); log?.Dispose(); world?.Dispose(); SetCursor(false);
        }
    }
}
