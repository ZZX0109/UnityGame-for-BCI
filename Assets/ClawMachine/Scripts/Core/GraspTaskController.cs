using System.Collections.Generic;
using UnityEngine;

namespace ClawMachine
{
    // All planning and commands are world-space. No GantryDriver dependency or coroutine lifetime.
    public sealed class GraspTaskController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour driverComponent;
        [SerializeField] private ClawTaskSpace taskSpace;
        [SerializeField] private GripperController gripper;
        [SerializeField] private PrizePhysics[] prizes;
        [SerializeField] private PrizeChuteDetector chute;
        [SerializeField] private ClawChallengeDefinition challenge;
        [SerializeField] private float aimSpeed = 0.35f;
        [SerializeField] private float lowerSpeed = 0.25f;
        [SerializeField] private float liftSpeed = 0.32f;
        [SerializeField] private float closeDuration = 0.85f;
        private IManipulatorDriver driver;
        private ManipulatorSafetyGuard safety;
        private GraspPlan plan;
        private Pose commandedPose;
        private float phaseTime, noProgressTime;
        private Vector3 lastPosition;
        private Vector2 aimInput;
        private bool ready;
        private int selectedPrize;
        public ClawSession Session { get; } = new ClawSession();
        public ClawChallengeDefinition Challenge => challenge;
        public IManipulatorDriver Driver => driver ?? driverComponent as IManipulatorDriver;
        public ClawTaskSpace TaskSpace => taskSpace;
        public IReadOnlyList<PrizePhysics> Prizes => prizes;
        public PrizePhysics SelectedPrize => prizes != null && selectedPrize >= 0 && selectedPrize < prizes.Length ? prizes[selectedPrize] : null;
        public PrizePhysics Prize
        {
            get { if (!challenge) return SelectedPrize; if (prizes != null) foreach (var p in prizes) if (p && p.gameObject.activeSelf && p.PrizeId == challenge.targetId) return p; return SelectedPrize; }
        }
        public GripperController Gripper => gripper;
        public int ContactCount => gripper ? gripper.LastContacts : 0;
        public bool IsConfigured => driverComponent && Driver != null && Driver.IsConfigured && Driver.IsSimulation && taskSpace && taskSpace.IsConfigured &&
            gripper && gripper.IsConfigured && prizes != null && prizes.Length > 0 && chute && chute.IsConfigured && challenge && ChallengeHasBindings();
        public string Diagnostic { get; private set; } = "";
        private bool ChallengeHasBindings()
        {
            if (challenge.spawns == null || challenge.spawns.Length == 0) return false;
            bool target = false;
            foreach (PrizeSpawnDefinition spawn in challenge.spawns)
            {
                if (spawn == null || !spawn.graspProfile) return false;
                bool bound = false;
                foreach (PrizePhysics prize in prizes) if (prize && prize.IsConfigured && prize.SlotId == spawn.prizeId) { bound = true; break; }
                if (!bound) return false;
                if (spawn.prizeId == challenge.targetId) target = true;
            }
            return target;
        }
        private void Awake()
        {
            driver = driverComponent as IManipulatorDriver;
            ready = IsConfigured;
            if (!ready) { Diagnostic = "Scene references or challenge bindings are incomplete."; Session.Fail(GraspFailure.Configuration); return; }
            safety = new ManipulatorSafetyGuard(driver, taskSpace); gripper.SetCollisionContext(prizes); ResetChallenge(false);
        }
        public bool SetChallenge(ClawChallengeDefinition definition, bool start)
        {
            if (!definition) return false;
            challenge = definition; ready = IsConfigured;
            if (!ready) { Abort(GraspFailure.Configuration, "Challenge prize bindings are incomplete."); return false; }
            driver = Driver; safety = new ManipulatorSafetyGuard(driver, taskSpace); ResetChallenge(start); return true;
        }
        public bool SetDriver(MonoBehaviour simulator)
        {
            if (Session.State != GraspState.Idle && Session.State != GraspState.Error) return false;
            if (!(simulator is IManipulatorDriver replacement) || !replacement.IsConfigured || !replacement.IsSimulation) return false;
            driver?.Stop(); driverComponent = simulator; driver = replacement; safety = new ManipulatorSafetyGuard(driver, taskSpace); return true;
        }
        public bool StartChallenge() { if (!IsConfigured) return false; ready = true; if (safety == null) safety = new ManipulatorSafetyGuard(Driver, taskSpace); ResetChallenge(true); return Session.State == GraspState.Aiming; }
        public void SelectPrize(int index) { if (prizes != null && index >= 0 && index < prizes.Length && prizes[index] && prizes[index].gameObject.activeSelf) selectedPrize = index; }
        public void SetAim(Vector2 input) { aimInput = ClawTaskSpace.Finite(new Vector3(input.x, 0, input.y)) ? Vector2.ClampMagnitude(input, 1) : Vector2.zero; }
        public bool StartGrab()
        {
            if (!ready || !IsConfigured || !Session.BeginAttempt()) return false;
            aimInput = Vector2.zero; driver.Stop();
            foreach (PrizePhysics prize in prizes) if (prize && prize.gameObject.activeSelf) prize.BeginAttempt(Session.SessionId, Session.AttemptId);
            chute.Arm(Session.SessionId, Session.AttemptId);
            plan = GraspPlanner.Plan(taskSpace, driver.CurrentToolPose, prizes);
            if (!plan.IsSafe) { Abort(GraspFailure.Obstructed, "No safe approach clearance within the workspace."); return false; }
            if (!plan.HasSurface) Session.RecordFailure(GraspFailure.NoContact);
            Move(plan.SafeApproach, lowerSpeed); return true;
        }
        private void Move(Pose target, float speed)
        {
            phaseTime = noProgressTime = 0; lastPosition = driver.CurrentToolPose.position; commandedPose = target;
            if (!safety.Move(target, speed)) Abort(GraspFailure.Configuration, "Simulator rejected a safe task-space pose.");
        }
        private bool Arrived => !driver.IsBusy && Vector3.Distance(driver.CurrentToolPose.position, commandedPose.position) <= 0.008f && Quaternion.Angle(driver.CurrentToolPose.rotation, commandedPose.rotation) <= 2;
        private bool IsAutomaticMotion => Session.State == GraspState.Approaching || Session.State == GraspState.ContactDescent || Session.State == GraspState.Lifting || Session.State == GraspState.Transporting;
        private void FixedUpdate()
        {
            if (!ready || Session.Paused || Session.State == GraspState.Error) return;
            if (!IsConfigured || !driverComponent.enabled || !gripper.enabled) { Abort(GraspFailure.Configuration, "A required scene reference was lost or disabled."); return; }
            if (driver.Status == ManipulatorStatus.Error) { Abort(GraspFailure.Configuration, "Executor error: " + driver.Error); return; }
            float dt = Time.fixedDeltaTime;
            if (Session.State == GraspState.Aiming) safety.Jog(taskSpace.AimVelocity(aimInput, aimSpeed));
            if ((Session.State == GraspState.ContactDescent || Session.State == GraspState.Approaching) && driver.IsBusy)
            {
                Vector3 delta = (Session.State == GraspState.Approaching ? plan.SafeApproach.position : plan.ContactPose.position) - driver.CurrentToolPose.position;
                float speed = Session.State == GraspState.Approaching ? lowerSpeed : plan.Candidate ? plan.Candidate.Profile.contactDescentSpeed : 0.06f;
                if (Vector3.Dot(delta, taskSpace.Up) < 0 && !gripper.CanDescend(Vector3.ClampMagnitude(delta, speed * dt), prizes))
                {
                    driver.Stop(); commandedPose = driver.CurrentToolPose;
                    if (!plan.Candidate || Vector3.Distance(plan.ContactPose.position, driver.CurrentToolPose.position) > 0.035f) Session.RecordFailure(GraspFailure.Obstructed);
                }
            }
            driver.Tick(dt);
            if (driver.Status == ManipulatorStatus.Error) { Abort(GraspFailure.Configuration, "Executor error: " + driver.Error); return; }
            gripper.CheckGrip();
            if (gripper.Slipped) Session.RecordFailure(GraspFailure.Slipped);
            phaseTime += dt;
            if (IsAutomaticMotion && !Arrived)
            {
                float progress = Vector3.Distance(driver.CurrentToolPose.position, lastPosition);
                noProgressTime = progress < 0.00004f ? noProgressTime + dt : 0; lastPosition = driver.CurrentToolPose.position;
                if (noProgressTime > 2 || phaseTime > 14) { Abort(GraspFailure.Timeout, "Simulator motion made no progress."); return; }
            }
            switch (Session.State)
            {
                case GraspState.Approaching:
                    if (Arrived)
                    {
                        driver.Stop(); plan = GraspPlanner.Plan(taskSpace, driver.CurrentToolPose, prizes);
                        if (!plan.IsSafe) { Abort(GraspFailure.Obstructed, "Prize moved outside safe approach clearance."); return; }
                        Session.Advance(GraspState.ContactDescent); Move(plan.ContactPose, plan.Candidate ? plan.Candidate.Profile.contactDescentSpeed : 0.06f);
                    }
                    break;
                case GraspState.ContactDescent:
                    if (Arrived) { Session.Advance(GraspState.Closing); phaseTime = 0; }
                    break;
                case GraspState.Closing:
                    driver.TrySetGripper(1 - Mathf.Clamp01(phaseTime / closeDuration));
                    if (phaseTime >= closeDuration)
                    {
                        Session.RecordFailure(gripper.TryGrip(plan.Candidate));
                        if (gripper.IsHolding && plan.Candidate.PrizeId != challenge.targetId) Session.RecordFailure(GraspFailure.WrongTarget);
                        Session.Advance(GraspState.Lifting); Move(taskSpace.PoseAtHeight(driver.CurrentToolPose.position, taskSpace.LocalBounds.max.y), liftSpeed);
                    }
                    break;
                case GraspState.Lifting:
                    if (Arrived) { Session.Advance(GraspState.Transporting); Move(taskSpace.ReleasePose, aimSpeed); }
                    break;
                case GraspState.Transporting:
                    if (Arrived) { Session.Advance(GraspState.Releasing); phaseTime = 0; gripper.Release(true); Session.MarkReleased(); }
                    break;
                case GraspState.Releasing:
                    driver.TrySetGripper(Mathf.Clamp01(phaseTime / 0.4f));
                    if (phaseTime >= 0.4f) { Session.Advance(GraspState.Settling); phaseTime = 0; }
                    break;
                case GraspState.Settling:
                    PrizePhysics target = Prize;
                    bool stable = chute.Observe(target, dt);
                    if (target) Session.TryAward(target.SessionId, target.AttemptId, target.PrizeId, challenge.targetId, stable);
                    if (Session.Awarded || phaseTime >= (target && target.Profile ? target.Profile.settleTimeout : 3))
                    {
                        if (!Session.Awarded) Session.RecordFailure(GraspFailure.MissedChute);
                        Session.Advance(GraspState.Result); driver.Stop();
                    }
                    break;
            }
        }
        public void Retry()
        {
            if (!ready || !Session.Retry()) return;
            gripper.ResetGrip(); aimInput = Vector2.zero; phaseTime = noProgressTime = 0;
            if (Session.State == GraspState.Aiming)
            {
                // Explicit policy: every failed attempt restores the same seeded full layout, preserving used attempts.
                RestorePrizes(); chute.Clear(); driver.Stop(); driver.TryReset(taskSpace.HomePose); Physics.SyncTransforms();
            }
        }
        private void RestorePrizes()
        {
            foreach (PrizePhysics prize in prizes)
            {
                if (!prize) continue;
                PrizeSpawnDefinition found = null;
                foreach (PrizeSpawnDefinition spawn in challenge.spawns) if (spawn.prizeId == prize.SlotId) { found = spawn; break; }
                prize.gameObject.SetActive(found != null);
                if (found != null) prize.Configure(found, challenge.seed);
            }
            selectedPrize = 0;
            for (int i = 0; i < prizes.Length; i++) if (prizes[i] && prizes[i].gameObject.activeSelf && prizes[i].PrizeId == challenge.targetId) selectedPrize = i;
        }
        public void ResetChallenge(bool start)
        {
            if (!IsConfigured) return;
            driver = Driver; if (safety == null) safety = new ManipulatorSafetyGuard(driver, taskSpace);
            safety.ResetStop(); driver.Stop(); gripper.SetCollisionContext(prizes); gripper.ResetGrip(); chute.Clear(); RestorePrizes();
            Session.Reset(challenge.attemptLimit, start);
            if (!driver.TryReset(taskSpace.HomePose)) { Abort(GraspFailure.Configuration, "Simulator reset was rejected."); return; }
            phaseTime = noProgressTime = 0; aimInput = Vector2.zero; plan = default(GraspPlan); Diagnostic = ""; Physics.SyncTransforms();
        }
        public void Cancel(bool returnToMenu) { if (ready) ResetChallenge(!returnToMenu); }
        public void EmergencyStop() { safety?.EmergencyStop(); Abort(GraspFailure.Cancelled, "Simulation stopped by user."); }
        private void Abort(GraspFailure failure, string message)
        {
            driver?.Stop(); if (gripper) gripper.ResetGrip(); if (chute) chute.Clear();
            if (prizes != null) foreach (var prize in prizes) if (prize && prize.IsConfigured) prize.Restore();
            Diagnostic = message; Session.Fail(failure);
        }
        public void SetPaused(bool value) { Session.Pause(value); aimInput = Vector2.zero; }
        private void OnDisable()
        {
            aimInput = Vector2.zero; driver?.Stop(); if (gripper) gripper.ResetGrip(); if (chute) chute.Clear();
            if (prizes != null) foreach (var prize in prizes) if (prize && prize.IsConfigured) prize.Restore();
            Session.Reset(challenge ? challenge.attemptLimit : 1, false);
        }
        private void OnApplicationQuit() { driver?.Stop(); if (gripper) gripper.Release(false); if (prizes != null) foreach (var prize in prizes) if (prize) prize.ClearVelocity(); }
    }
}
