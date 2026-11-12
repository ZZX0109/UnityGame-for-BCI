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
        public Font uiFont;
        private StreetWorld world;
        private VehicleTrafficManager traffic;
        private PedestrianPlayerController player;
        private ObservationTracker observation;
        private StreetEventLog log;
        private IStreetInput input = new LegacyStreetInput();
        private float accumulator;
        private int attempt;
        private CrossingRegion region;
        public IntersectionController Intersection { get; private set; }
        public CrossingEvaluator Evaluation { get; private set; }
        public StreetGameState State { get; private set; } = StreetGameState.Menu;
        public bool IsPlaying => State == StreetGameState.Playing;
        public float WorldTime { get; private set; }
        public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
        public PedestrianPlayerController Player => player;
        public VehicleTrafficManager Traffic => traffic;
        public string Notice { get; private set; } = "";
        public string LogPath => log?.Path;
        public string LogError => log?.Error;
        public int Stars => Evaluation.SafetySuccess ? 1 + (Evaluation.LeftObserved && Evaluation.RightObserved ? 1 : 0) + (Evaluation.NearMisses == 0 ? 1 : 0) : 0;
        private const float Step = 0.02f;
        private void Awake()
        {
            if (scenario == null || uiFont == null) { Debug.LogError("Street scene is missing S01 or the dedicated UI font.", this); enabled = false; return; }
            scenario.Validate(); Application.targetFrameRate = 60;
            Intersection = new IntersectionController(scenario.phaseTable);
            world = new StreetWorld(transform, scenario.surfaceMaterial, uiFont); world.Build(scenario);
            GameObject rig = new GameObject("Pedestrian Player", typeof(CharacterController)); rig.transform.SetParent(transform, false);
            player = rig.AddComponent<PedestrianPlayerController>(); player.Initialize(this, scenario, input);
            log = new StreetEventLog(scenario.scenarioId);
            ResetEvaluation();
            traffic = new VehicleTrafficManager(scenario, Intersection, this, world.TrafficRoot, world, rig.GetComponent<CharacterController>());
            GameObject signalView = new GameObject("Intersection Signals"); signalView.transform.SetParent(transform, false);
            signalView.AddComponent<TrafficSignalView>().Initialize(this, world, scenario);
            Intersection.PhaseChanged += PhaseChanged;
            Intersection.ClearanceBlocked += ClearanceBlocked;
            gameObject.AddComponent<StreetUIController>().Initialize(this, uiFont);
            gameObject.AddComponent<StreetCrossing.Audio.StreetAudioController>().Initialize(this);
            SetCursor(false); Record("SessionOpened", "S01 prototype; game timing is not a real-world traffic standard.");
        }
        private void ResetEvaluation()
        {
            if (Evaluation != null) Evaluation.Event -= OnEvaluation;
            Evaluation = new CrossingEvaluator(); Evaluation.Event += OnEvaluation;
            observation = new ObservationTracker(); observation.Observed += Evaluation.Observe;
        }
        public void SetInputSource(IStreetInput source)
        {
            input = source ?? new LegacyStreetInput(); if (player != null) player.SetInputSource(input);
        }
        public void StartPractice()
        {
            if (!isActiveAndEnabled) return;
            GetComponent<StreetUIController>()?.CloseModal();
            attempt++; WorldTime = accumulator = 0; region = CrossingRegion.None; Notice = "";
            ResetEvaluation(); player.ResetPose(); Intersection.Reset(); traffic.Reset();
            State = StreetGameState.Playing; SetCursor(true);
            Physics.SyncTransforms(); Record("AttemptStarted", "seed=" + scenario.seed);
        }
        private void Update()
        {
            if (Intersection == null) return;
            if (input.PausePressed && GetComponent<StreetUIController>().HasModal) GetComponent<StreetUIController>().CloseModal();
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
            bool occupied = (region & CrossingRegion.Conflict) != 0;
            bool withinCrosswalk = Mathf.Abs(player.transform.position.x) <= scenario.crosswalkHalfWidth;
            bool atKerb = (region & CrossingRegion.Kerb) != 0 && !occupied;
            observation.Tick(player.Heading, atKerb, dt);
            Evaluation.Tick(region, Intersection.Phase, withinCrosswalk, traffic.HasUnclearedVehicles, dt);
            if (Evaluation.Finished) return;
            Intersection.Tick(dt, traffic.HasUnclearedVehicles, occupied);
            traffic.Tick(dt, WorldTime, occupied);
            if (Intersection.IsClearanceHeld) Notice = "横道正在清空。车辆会继续等待；请安全完成过街。";
            else if (Notice.StartsWith("横道")) Notice = "";
        }
        public void RegisterCollision(int encounterId)
        {
            if (!IsPlaying || Evaluation.Finished) return;
            Evaluation.Collision(encounterId); Finish();
        }
        private void Finish()
        {
            if (State == StreetGameState.Result) return;
            State = StreetGameState.Result; SetCursor(false);
            if (Evaluation.SafetySuccess) StreetPreferences.SaveStars(scenario.scenarioId, Stars);
            Record("AttemptResult", "safe=" + Evaluation.SafetySuccess + ";stars=" + Stars);
        }
        public void Pause() { if (!IsPlaying) return; State = StreetGameState.Paused; SetCursor(false); Record("Paused", "World clock, signals, traffic and movement frozen."); }
        public void Resume() { if (!isActiveAndEnabled || State != StreetGameState.Paused) return; State = StreetGameState.Playing; SetCursor(true); Record("Resumed", ""); }
        public void RequestRestart()
        {
            if (State == StreetGameState.Playing) Pause();
            GetComponent<StreetUIController>()?.ShowRestartConfirmation();
        }
        public void ReturnToMenu() { if (IsPlaying) Pause(); State = StreetGameState.Menu; SetCursor(false); Record("ReturnedToMenu", ""); }
        public void Inspect(string objectName) { Notice = "环境标识：" + objectName + "。目的地是对面的街角便利店。"; Record("Inspect", objectName); }
        private void PhaseChanged(TrafficPhase phase) { Record("TrafficPhase", phase.ToString()); }
        private void ClearanceBlocked(string message) { Notice = "清空等待时间较长。保持全红；可暂停检查或重新开始。"; Record("ClearanceWatchdog", message); }
        private void OnEvaluation(string name, string detail) { Record(name, detail); }
        public void Record(string name, string detail) { log?.Write(name, detail, WorldTime, attempt, Intersection == null ? "" : Intersection.Phase.ToString(), PlayerPosition); }
        private static void SetCursor(bool locked) { Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
        private void OnApplicationFocus(bool focus) { if (!focus && IsPlaying) Pause(); }
        private void OnDisable() { if (IsPlaying) Pause(); }
        private void OnDestroy()
        {
            if (Intersection != null) { Intersection.PhaseChanged -= PhaseChanged; Intersection.ClearanceBlocked -= ClearanceBlocked; }
            traffic?.Dispose(); log?.Dispose(); world?.Dispose(); SetCursor(false);
        }
    }
}
