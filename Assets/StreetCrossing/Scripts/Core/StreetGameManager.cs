using System.Collections.Generic;
using System.Text;
using StreetCrossing.Evaluation;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using StreetCrossing.UI;
using UnityEngine;
using Neuralife.XR;

namespace StreetCrossing.Core
{
    public enum StreetGameState { Menu, Playing, Paused, SegmentComplete, Result }
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
        private int leftObservationRiskSignature, rightObservationRiskSignature;
        private const float CourseObservationMaximumAge = 10f;
        public int CourseIndex { get; private set; } = -1;
        public bool SidewalkLesson => CourseIndex == 0 || CourseIndex == 1;
        public bool CourseRiskAwareObservations => CourseIndex >= 2 && CourseIndex < StreetCourse.Stages.Count;
        public bool SidewalkPassed { get; private set; }
        private float signalDwell;
        private TrafficSignalView signals;
        public void StartCourse(int index)
        {
            if (index < 0 || index >= StreetCourse.Stages.Count || IsPlaying) return;
            StreetCourse lesson = StreetCourse.Stages[index];
            int district = System.Array.FindIndex(scenarios, item => item.scenarioId == lesson.District);
            if (district < 0) return;
            if (SelectedScenarioIndex != district) SelectScenario(district);
            State = StreetGameState.Menu;
            SelectMission(lesson.MissionIndex); SelectVariant(0);
            CourseIndex = index;
            StartPractice();
        }

        private readonly List<StreetSegmentResult> segmentResults = new List<StreetSegmentResult>();
        private bool prewarming, longWaitNotified;
        private float missionStartedAt;
        private CrossingRegion region;
        private readonly StreetNoticeBoard notices = new StreetNoticeBoard();
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
        /// <summary>Fixed simulation step in seconds, exposed for render-time interpolation.</summary>
        public const float SimulationStep = Step;
        /// <summary>
        /// Fraction (0..1) of the current fixed step that has already been rendered but not yet
        /// simulated. Presentation code combines this with <see cref="WorldTime"/> to position
        /// visuals on the continuous render clock while the simulation itself stays deterministic.
        /// </summary>
        public float StepAlpha => Mathf.Clamp01(accumulator / Step);
        public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
        public PedestrianPlayerController Player => player;
        public VehicleTrafficManager Traffic => traffic;
        public IReadOnlyList<CrossingZone> Zones => world.Zones;
        public Vector3[] LeftRoadTargets => world.MissionForwardSign > 0 ? world.LeftRoadTargets : world.RightRoadTargets;
        public Vector3[] RightRoadTargets => world.MissionForwardSign > 0 ? world.RightRoadTargets : world.LeftRoadTargets;
        public string Notice => notices.Resolve(WorldTime);
        public string CourseObservationPrompt
        {
            get
            {
                if (!CourseRiskAwareObservations || Evaluation == null) return "";
                bool left = Evaluation.LeftObservationInvalidated, right = Evaluation.RightObservationInvalidated;
                if (!left && !right) return "";
                string reason = "";
                if (left) reason = "左侧" + ObservationInvalidationText(Evaluation.LeftObservationInvalidationReason);
                if (right) reason += (reason.Length > 0 ? "；" : "") + "右侧" + ObservationInvalidationText(Evaluation.RightObservationInvalidationReason);
                return reason + "，请留在人行道上重新观察。合理等待不扣分。";
            }
        }
        public int CourseObservationRiskSignature(bool left) => CourseRiskAwareObservations ? GetCourseObservationRiskSignature(left) : 0;
        public string LogPath => log?.Path;
        public string LogError => log?.Error;
        public event System.Action<string, string> GameEvent;
        public IReadOnlyList<StreetMissionDefinition> Missions => StreetMissionCatalog.ForDistrict(scenario.scenarioId);
        public StreetMissionDefinition Mission => Missions[SelectedMissionIndex];
        public int SelectedMissionIndex { get; private set; }
        public int SelectedVariant { get; private set; }
        public int CurrentSegmentIndex { get; private set; }
        public int SegmentCount => Mission.roundTrip ? 2 : 1;
        public IReadOnlyList<StreetSegmentResult> SegmentResults => segmentResults;
        public bool CrossingCompleted => Evaluation != null && Evaluation.HasCrossed;
        public bool MissionCompleted => State == StreetGameState.Result;
        public bool MissionSafetySuccess => MissionCompleted && segmentResults.Count == SegmentCount && segmentResults.TrueForAll(item => item.SafetySuccess);
        public bool PersistMissionProgress { get; set; } = true;
        public float MissionElapsedSeconds => Mathf.Max(0, WorldTime - missionStartedAt);
        public string DestinationName => world.MissionDestinationName;
        public Vector3 Destination => world.MissionDestination;
        public Vector3 NavigationTarget => world.NavigationTarget(PlayerPosition, CrossingCompleted);
        public int ForwardSign => world.MissionForwardSign;
        public int Stars
        {
            get
            {
                if (!MissionSafetySuccess) return 0;
                int stars = 3; foreach (StreetSegmentResult item in segmentResults) stars = Mathf.Min(stars, item.Stars);
                return stars;
            }
        }
        public string MissionSummary
        {
            get
            {
                var text = new StringBuilder();
                text.Append(Mission.title).Append("  ·  配置 ").Append(SelectedVariant + 1).Append("\n");
                foreach (StreetSegmentResult item in segmentResults) text.Append(item.Summary).Append("\n");
                text.Append("任务时长 ").Append(MissionElapsedSeconds.ToString("F1")).Append(" 秒。\n合理等待不扣分，没有抢时间奖励。观察记录来自镜头可见性。");
                return text.ToString();
            }
        }
        private const float Step = 0.02f;
        private void Awake()
        {
            if (scenario == null || uiFont == null) { Debug.LogError("Street scene is missing scenario or dedicated UI font.", this); enabled = false; return; }
            if (scenarios == null || scenarios.Length == 0) scenarios = new[] { scenario };
            HashSet<string> ids = new HashSet<string>();
            foreach (StreetScenarioDefinition entry in scenarios)
            { entry.Validate(); if (!ids.Add(entry.scenarioId)) throw new System.ArgumentException("Duplicate stable scenario id."); }
            Application.targetFrameRate = PicoXR.Requested ? -1 : 60;
            RebuildScenario(); ui = gameObject.AddComponent<StreetUIController>(); ui.Initialize(this, uiFont);
            SetCursor(false);
        }
        public void SelectScenario(int index)
        {
            if (index < 0 || index >= scenarios.Length || IsPlaying) return;
            CourseIndex = -1; Record("ScenarioChanged", scenarios[index].scenarioId); SelectedScenarioIndex = index; scenario = scenarios[index];
            State = StreetGameState.Menu; SelectedMissionIndex = SelectedVariant = CurrentSegmentIndex = 0;
            segmentResults.Clear(); notices.Reset(); attempt = 0; WorldTime = accumulator = 0; RebuildScenario(); SetCursor(false);
        }
        public void SelectMission(int index)
        {
            if (State != StreetGameState.Menu || index < 0 || index >= Missions.Count) return;
            SelectedMissionIndex = index; CurrentSegmentIndex = 0; segmentResults.Clear(); notices.Reset();
            Record("MissionSelected", Mission.missionId);
        }
        public void SelectVariant(int index)
        {
            if (State != StreetGameState.Menu || index < 0 || index > 2) return;
            SelectedVariant = index; Record("VariantSelected", "variant=" + index);
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
            signals = signalView.AddComponent<TrafficSignalView>(); signals.Initialize(this, world, scenario);
            Intersection.PhaseChanged += PhaseChanged; Intersection.ClearanceBlocked += ClearanceBlocked;
            environment.gameObject.AddComponent<StreetCrossing.Audio.StreetAudioController>().Initialize(this);
            Physics.SyncTransforms(); Record("SessionOpened", "Prototype scenario=" + scenario.scenarioId + ";seed=" + scenario.seed + ";not a traffic-law standard or medical assessment.");
        }
        private void ResetEvaluation()
        {
            if (Evaluation != null) Evaluation.Event -= OnEvaluation;
            Evaluation = new CrossingEvaluator(scenario.requireLeftObservation, scenario.requireRightObservation); Evaluation.Event += OnEvaluation;
            leftObservationRiskSignature = rightObservationRiskSignature = 0;
            observation = new ObservationTracker(scenario.observationDwellSeconds); observation.Observed += OnRoadObserved;
        }
        private void OnRoadObserved(bool left)
        {
            Evaluation?.Observe(left);
            if (!CourseRiskAwareObservations) return;
            int signature = GetCourseObservationRiskSignature(left);
            if (left) leftObservationRiskSignature = signature; else rightObservationRiskSignature = signature;
        }
        public void SetInputSource(IStreetInput source) { input = source ?? new LegacyStreetInput(); if (player != null) player.SetInputSource(input); }
        public void StartPractice()
        {
            if (!isActiveAndEnabled) return; ui?.CloseModal();
            SidewalkPassed = false; signalDwell = 0;
            attempt++; WorldTime = accumulator = 0; region = CrossingRegion.None; notices.Reset();
            segmentResults.Clear(); CurrentSegmentIndex = 0; longWaitNotified = false;
            world.ApplyMission(Mission, 0); ResetEvaluation(); player.ResetPose(world.MissionSpawn, world.MissionHeading);
            Intersection.Reset(); traffic.ResetWithSeed(Mission.SeedForVariant(scenario.seed, SelectedVariant));
            State = StreetGameState.Playing; Physics.SyncTransforms();
            // Advance the complete signal/vehicle simulation, never just change the displayed light.
            prewarming = true;
            try
            {
                int steps = Mathf.RoundToInt(Mission.prewarmSeconds / Step);
                for (int i = 0; i < steps; i++)
                {
                    WorldTime += Step;
                    Intersection.Tick(Step, traffic.HasUnclearedVehicles, false);
                    traffic.Tick(Step, WorldTime, false);
                }
            }
            finally { prewarming = false; }
            ResetEvaluation(); notices.Reset(); missionStartedAt = WorldTime;
            if (CourseIndex >= 0) notices.Set(StreetNoticeKind.Inspection, StreetCourse.Stages[CourseIndex].Instruction, WorldTime + 8);
            SetCursor(true); Record("AttemptStarted", "mission=" + Mission.missionId + ";revision=" + Mission.revision + ";variant=" + SelectedVariant + ";seed=" + Mission.SeedForVariant(scenario.seed, SelectedVariant) + ";prewarm=" + Mission.prewarmSeconds);
        }
        public void ContinueMission()
        {
            if (State != StreetGameState.SegmentComplete || CurrentSegmentIndex + 1 >= SegmentCount) return;
            CurrentSegmentIndex++; world.ApplyMission(Mission, CurrentSegmentIndex); ResetEvaluation();
            region = CrossingRegion.None; notices.Reset(); accumulator = 0;
            State = StreetGameState.Playing; SetCursor(true);
            Record("SegmentStarted", "segment=" + CurrentSegmentIndex + ";signalContinues=" + Intersection.Phase);
        }
        private void Update()
        {
            if (PicoXR.Requested && PicoXR.Ready && !PicoXR.HeadTracked) { Pause(); return; }
            if (Intersection == null) return;
            if (input.PausePressed && ui.HasModal) ui.CloseModal();
            else if (input.PausePressed) { if (State == StreetGameState.Playing) Pause(); else if (State == StreetGameState.Paused) Resume(); }
            if (input.RestartPressed && State != StreetGameState.Menu) RequestRestart();
            if (!IsPlaying) return;
            accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            while (accumulator >= Step && IsPlaying)
            {
                accumulator -= Step; WorldTime += Step; TickWorld(Step);
                if (!longWaitNotified && MissionElapsedSeconds >= scenario.timeoutSeconds && !Evaluation.Finished)
                {
                    longWaitNotified = true;
                    notices.Set(StreetNoticeKind.Inspection, "练习时间较长，可以继续观察和安全等待，也可暂停或重新开始。", WorldTime + 12);
                    Record("PracticeReminder", "Non-failing reminder; safe waiting remains available.");
                }
                if (Evaluation.Finished) Finish();
            }
        }
        private void TickWorld(float dt)
        {
            region = CrossingRegion.None;
            foreach (CrossingZone zone in world.Zones) if (zone.Contains(player.transform.position)) region |= zone.region;
            bool roadway = IsPlayerInRoadwayHazard;
            bool protectedOccupied = IsPlayerInProtectedCrosswalk;
            if (CourseRiskAwareObservations && Evaluation.MayRecordPreEntryObservation)
                RefreshCourseObservations();
            RoadObservationEvidence evidence = new RoadObservationEvidence {
                atKerb = IsAtKerb, beforeEntry = Evaluation.MayRecordPreEntryObservation, yaw = Mathf.DeltaAngle(world.MissionHeading, player.Heading), pitch = player.Pitch,
                leftTargetVisible = DirectionVisible(true),
                rightTargetVisible = DirectionVisible(false)
            };
            observation.Tick(evidence, dt);
            if (SidewalkLesson)
            {
                if (roadway) { FinishSidewalk(false); return; }
                if (CourseIndex == 0)
                {
                    signalDwell = IsAtKerb && signals.FacingFarPedestrianSignal(player.View, player.transform) ? signalDwell + dt : 0;
                    if (signalDwell >= 0.6f) { FinishSidewalk(true); return; }
                }
                else if (Evaluation.LeftObserved) { FinishSidewalk(true); return; }
            }
            Evaluation.Tick(region, Intersection.Phase, (region & CrossingRegion.CrossingRoute) != 0, traffic.HasUnclearedVehicles, dt);
            if (Evaluation.Finished) return;
            // Roadway hazard is used by evaluation/local vehicle sensing only. Signal priority is confined to the protected crossing.
            Intersection.Tick(dt, traffic.HasUnclearedVehicles, protectedOccupied);
            traffic.Tick(dt, WorldTime, protectedOccupied);
            if (Intersection.IsClearanceHeld) notices.Set(StreetNoticeKind.Clearance, "受保护横道正在清空，冲突车辆继续等待。");
            else { notices.Clear(StreetNoticeKind.Clearance); notices.Clear(StreetNoticeKind.ClearanceWatchdog); }
            bool lookingLeft = evidence.yaw >= -125 && evidence.yaw <= -45;
            bool lookingRight = evidence.yaw >= 45 && evidence.yaw <= 125;
            if (!roadway && evidence.atKerb && evidence.beforeEntry && Mathf.Abs(evidence.pitch) <= 35 &&
                ((lookingLeft && !evidence.leftTargetVisible && !Evaluation.LeftObserved) || (lookingRight && !evidence.rightTargetVisible && !Evaluation.RightObserved)))
                notices.Set(StreetNoticeKind.Occlusion, lookingLeft && world.StopPanelHidesNearLane(player.View.transform.position)
                    ? "公交站牌挡住了左侧近车道。沿人行道向斑马线口挪半步，再向左看，不要走进车道。"
                    : (lookingLeft ? "左侧" : "右侧") + "两条来车道还没看全。留在人行道上，转头看清来车。");
            else notices.Clear(StreetNoticeKind.Occlusion);
            if (!CourseRiskAwareObservations && IsAtKerb && Evaluation.MayRecordPreEntryObservation && observation.NeedsRefresh(10))
                notices.Set(StreetNoticeKind.ObservationRefresh, "距离上次观察已超过10秒。出发前请再次确认左右来车；观察记录不代表当前道路仍然安全。");
            else if (CourseRiskAwareObservations) UpdateCourseObservationNotice();
            else notices.Clear(StreetNoticeKind.ObservationRefresh);
        }
        private void RefreshCourseObservations()
        {
            if (Evaluation.LeftObserved)
            {
                if (observation.NeedsRefresh(true, CourseObservationMaximumAge))
                    InvalidateCourseObservation(true, "expired-after-10-seconds");
                else if (GetCourseObservationRiskSignature(true) != leftObservationRiskSignature)
                    InvalidateCourseObservation(true, "left-road-risk-changed");
            }
            if (Evaluation.RightObserved)
            {
                if (observation.NeedsRefresh(false, CourseObservationMaximumAge))
                    InvalidateCourseObservation(false, "expired-after-10-seconds");
                else if (GetCourseObservationRiskSignature(false) != rightObservationRiskSignature)
                    InvalidateCourseObservation(false, "right-road-risk-changed");
            }
        }
        private void InvalidateCourseObservation(bool left, string reason)
        {
            if (!observation.Invalidate(left)) return;
            Evaluation.InvalidateObservation(left, reason);
            UpdateCourseObservationNotice();
        }
        private void UpdateCourseObservationNotice()
        {
            if (CourseRiskAwareObservations && !string.IsNullOrEmpty(CourseObservationPrompt))
                notices.Set(StreetNoticeKind.ObservationRefresh, CourseObservationPrompt);
            else notices.Clear(StreetNoticeKind.ObservationRefresh);
        }
        private static string ObservationInvalidationText(string reason)
            => reason == "expired-after-10-seconds" ? "观察超过10秒" : "信号或该侧车流情况已变化";
        private int GetCourseObservationRiskSignature(bool left)
        {
            if (traffic == null || world == null || Intersection == null) return 0;
            bool worldLeft = world.MissionForwardSign > 0 ? left : !left;
            int direction = worldLeft ? 1 : -1;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)Intersection.Phase;
                for (int laneIndex = 0; laneIndex < scenario.StraightLaneCount; laneIndex++)
                {
                    if (scenario.DirectionForLane(laneIndex) != direction) continue;
                    // Preserve every relevant vehicle's risk band. Looking only at the
                    // closest/highest band can hide a faster follower behind a stopped car.
                    int band1 = 0, band2 = 0, band3 = 0, band4 = 0, band5 = 0, band6 = 0, band7 = 0;
                    foreach (VehicleController car in traffic.Active)
                    {
                        if (car.Lane.IsTurning || car.Lane.Index != laneIndex) continue;
                        switch (TrafficRiskBand(car.Lane, car.Motion))
                        {
                            case 1: band1++; break;
                            case 2: band2++; break;
                            case 3: band3++; break;
                            case 4: band4++; break;
                            case 5: band5++; break;
                            case 6: band6++; break;
                            case 7: band7++; break;
                        }
                    }
                    hash = hash * 31 + Mathf.Min(3, band1); hash = hash * 31 + Mathf.Min(3, band2);
                    hash = hash * 31 + Mathf.Min(3, band3); hash = hash * 31 + Mathf.Min(3, band4);
                    hash = hash * 31 + Mathf.Min(3, band5); hash = hash * 31 + Mathf.Min(3, band6);
                    hash = hash * 31 + Mathf.Min(3, band7);
                }
                // An already committed turn through the junction affects the crossing window from either approach.
                int turnRisk = 0;
                foreach (VehicleController car in traffic.Active)
                {
                    if (!car.Lane.IsTurning || !car.Lane.HasUnclearedCommittedVehicle(car.Motion)) continue;
                    turnRisk = car.Lane.OccupiesConflict(car.Motion) ? 2 : 1;
                    break;
                }
                return hash * 31 + turnRisk;
            }
        }
        private static int TrafficRiskBand(VehicleLanePath lane, VehicleKinematics motion)
        {
            if (lane.OccupiesConflict(motion)) return 7;
            if (motion.Committed) return lane.HasUnclearedCommittedVehicle(motion) ? 6 : 0;
            float distance = lane.ConflictEntry - motion.Front;
            if (distance < 0 || distance > 75) return 0;
            if (motion.Speed < 0.25f) return 5;
            float eta = distance / Mathf.Max(0.25f, motion.Speed);
            if (eta <= 4) return 4;
            if (eta <= 9) return 3;
            if (eta <= 18) return 2;
            return 1;
        }
        public bool DirectionVisible(bool left)
        {
            // Four lanes need evidence for both approaching lanes on the chosen side.
            // A visible car in the inner lane cannot prove that the occluded outer lane is visible.
            bool worldLeft = world.MissionForwardSign > 0 ? left : !left;
            int direction = worldLeft ? 1 : -1;
            for (int lane = 0; lane < scenario.StraightLaneCount; lane++)
            {
                if (scenario.DirectionForLane(lane) != direction) continue;
                bool visible = RoadObservationVisibility.TargetVisible(player.View, world.RoadTargetsForLane(lane), player.transform);
                if (!visible)
                    foreach (VehicleController car in traffic.Active)
                    {
                        if (car.Lane.Direction != direction || Mathf.Abs(car.transform.position.z - scenario.LaneZ(lane)) > scenario.laneOffset * .5f) continue;
                        float dx = car.transform.position.x - player.transform.position.x;
                        if ((worldLeft && dx > -2) || (!worldLeft && dx < 2)) continue;
                        Vector3 point = car.transform.position + Vector3.up * 0.9f;
                        if (RoadObservationVisibility.PointVisible(player.View, point, player.transform, car.transform)) { visible = true; break; }
                    }
                if (!visible) return false;
            }
            return true;
        }
        public void RegisterContact(int encounterId, float vehicleSpeed)
        {
            if (!IsPlaying || prewarming || Evaluation.Finished) return;
            Evaluation.Contact(encounterId, vehicleSpeed); if (Evaluation.Finished) Finish();
        }
        public void RegisterCollision(int encounterId) { RegisterContact(encounterId, 1); }
        private void FinishSidewalk(bool passed)
        {
            if (passed && PersistMissionProgress) StreetMissionProgress.CompleteStage(CourseIndex);
            SidewalkPassed = passed;
            State = StreetGameState.Result; SetCursor(false);
            Record("SidewalkLessonResult", "stage=" + (CourseIndex + 1) + ";passed=" + passed);
        }
        private void Finish()
        {
            if (State == StreetGameState.Result) return;
            if (SidewalkLesson) { FinishSidewalk(false); return; }
            segmentResults.Add(new StreetSegmentResult(CurrentSegmentIndex, Evaluation));
            if (CurrentSegmentIndex + 1 < SegmentCount && Evaluation.MovingCollisions == 0)
            {
                State = StreetGameState.SegmentComplete; SetCursor(false);
                Record("SegmentCompleted", segmentResults[segmentResults.Count - 1].Summary); return;
            }
            State = StreetGameState.Result; SetCursor(false);
            if (PersistMissionProgress && MissionSafetySuccess) StreetMissionProgress.RecordSuccess(Mission, SelectedVariant, Stars);
            if (CourseIndex >= 0 && PersistMissionProgress && MissionSafetySuccess) StreetMissionProgress.CompleteStage(CourseIndex);
            Record("AttemptResult", "mission=" + Mission.missionId + ";safe=" + MissionSafetySuccess + ";stars=" + Stars);
        }
        public void Pause() { if (State != StreetGameState.Playing) return; State = StreetGameState.Paused; SetCursor(false); Record("Paused", "Unified simulation frozen."); }
        public void Resume() { if (!isActiveAndEnabled || State != StreetGameState.Paused) return; State = StreetGameState.Playing; SetCursor(true); Record("Resumed", ""); }
        public void RequestRestart() { if (State == StreetGameState.Playing) Pause(); ui?.ShowRestartConfirmation(); }
        public void ReturnToMenu() { if (IsPlaying) Pause(); CourseIndex = -1; State = StreetGameState.Menu; SetCursor(false); Record("ReturnedToMenu", ""); }
        public void Inspect(StreetInspectable sign)
        {
            if (!IsPlaying || sign == null) return;
            notices.Set(StreetNoticeKind.Inspection, sign.Content, WorldTime + 5);
            Record("Inspect", sign.title);
        }
        private void PhaseChanged(TrafficPhase phase) { Record("TrafficPhase", phase.ToString()); }
        private void ClearanceBlocked(string message) { notices.Set(StreetNoticeKind.ClearanceWatchdog, "清空等待较长，保持全红；可暂停检查或重新开始。"); Record("ClearanceWatchdog", message); }
        private void OnEvaluation(string name, string detail)
        {
            switch (name)
            {
                case "SignalViolation": notices.Set(StreetNoticeKind.Violation, "当前不是行人绿灯，本次进入已记录违规。请安全返回人行道后重新练习。"); break;
                case "RouteViolation": notices.Set(StreetNoticeKind.Violation, "已离开合法过街路线。请安全返回人行道后重新练习。"); break;
                case "VehicleConflictAtEntry": notices.Set(StreetNoticeKind.Violation, "已有车辆尚未清空。请与车辆保持距离，本次进入存在冲突。"); break;
                case "ObservationRequirementMissing": notices.Set(StreetNoticeKind.Violation, "进入前未完成左右道路观察。请安全完成当前动作后重新练习。"); break;
                case "LeftLook": case "RightLook": UpdateCourseObservationNotice(); break;
                case "LeftObservationInvalidated": case "RightObservationInvalidated": UpdateCourseObservationNotice(); break;
                case "StationaryContact": notices.Set(StreetNoticeKind.Safety, "接触到已停稳车辆，请保持距离。静止接触与移动碰撞分别记录。", WorldTime + 4); break;
                case "NearMiss": notices.Set(StreetNoticeKind.Safety, "车辆正在危险接近，请注意来车并保持安全距离。", WorldTime + 3); break;
                case "CrossingEnd":
                    if (!Evaluation.Finished) { notices.Clear(StreetNoticeKind.Violation); notices.Clear(StreetNoticeKind.Safety); }
                    break;
                case "Collision": case "Timeout": notices.Set(StreetNoticeKind.Result, Evaluation.Feedback); break;
            }
            Record(name, detail); GameEvent?.Invoke(name, detail);
        }
        public void Record(string name, string detail) { log?.Write(name, detail, WorldTime, attempt, Intersection == null ? "" : Intersection.Phase.ToString(), PlayerPosition); }
        private static void SetCursor(bool locked) { if (PicoXR.Requested) locked = false; Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
        private void OnApplicationFocus(bool focus) { if (AutoPauseOnFocusLoss && !focus && State == StreetGameState.Playing) Pause(); }
        private void OnDisable() { if (State == StreetGameState.Playing) Pause(); }
        private void OnDestroy()
        {
            if (Intersection != null) { Intersection.PhaseChanged -= PhaseChanged; Intersection.ClearanceBlocked -= ClearanceBlocked; }
            traffic?.Dispose(); log?.Dispose(); world?.Dispose(); SetCursor(false);
        }
    }
}
