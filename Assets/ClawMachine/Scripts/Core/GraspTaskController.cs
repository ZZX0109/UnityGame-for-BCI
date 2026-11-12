using UnityEngine;

namespace ClawMachine
{
    // Fixed-step state machine. No coroutines can survive cancellation or write into an old attempt.
    public sealed class GraspTaskController : MonoBehaviour
    {
        [SerializeField] private GantryDriver gantry;
        [SerializeField] private GripperController gripper;
        [SerializeField] private PrizePhysics prize;
        [SerializeField] private PrizeChuteDetector chute;
        [SerializeField] private ClawChallengeDefinition challenge;
        [SerializeField] private Transform dropPose;
        [SerializeField] private float aimSpeed = 0.35f;
        [SerializeField] private float lowerSpeed = 0.25f;
        [SerializeField] private float liftSpeed = 0.32f;
        [SerializeField] private float closeDuration = 0.5f;
        private IManipulatorDriver driver;
        private float phaseTime, noProgressTime;
        private Vector3 lastPosition;
        private Vector2 aimInput;
        private bool ready;
        public ClawSession Session { get; } = new ClawSession();
        public ClawChallengeDefinition Challenge => challenge;
        public PrizePhysics Prize => prize;
        public GantryDriver Gantry => gantry;
        public bool IsConfigured => gantry && gantry.IsConfigured && gripper && gripper.IsConfigured &&
            prize && prize.IsConfigured && chute && chute.IsConfigured && challenge && challenge.graspProfile && dropPose;
        public int ContactCount => gripper ? gripper.LastContacts : 0;
        public string Diagnostic { get; private set; } = "";

        private void Awake()
        {
            driver = gantry;
            ready = IsConfigured;
            if (!ready) { Diagnostic = "Scene references are incomplete."; Session.Fail(GraspFailure.Configuration); return; }
            ResetChallenge(false);
        }
        public bool StartChallenge()
        {
            if (!ready || !IsConfigured) return false;
            ResetChallenge(true); return true;
        }
        public void SetAim(Vector2 input) { aimInput = Vector2.ClampMagnitude(input, 1); }
        public bool StartGrab()
        {
            if (!ready || !Session.BeginAttempt()) return false;
            aimInput = Vector2.zero; driver.Stop();
            prize.BeginAttempt(Session.SessionId, Session.AttemptId); chute.Arm(Session.SessionId, Session.AttemptId);
            Vector3 local = gantry.LocalPosition;
            float contactY = gantry.ToLocal(prize.Body.worldCenterOfMass).y;
            local.y = Mathf.Clamp(contactY, gantry.Workspace.min.y, gantry.Workspace.max.y);
            Move(local, lowerSpeed); return true;
        }
        private void Move(Vector3 target, float speed)
        {
            phaseTime = noProgressTime = 0; lastPosition = driver.CurrentToolPose.position;
            if (!driver.TryMoveTo(gantry.WorldPose(target), speed)) Abort(GraspFailure.Configuration, "Rejected tool pose or workspace limit.");
        }
        private void FixedUpdate()
        {
            if (!ready || Session.Paused || Session.State == GraspState.Error) return;
            if (!IsConfigured) { Abort(GraspFailure.Configuration, "A required scene reference was lost."); return; }
            float dt = Time.fixedDeltaTime;
            if (Session.State == GraspState.Aiming) gantry.Aim(aimInput, aimSpeed, dt);
            gantry.Step(dt);
            gripper.CheckGrip();
            if (gripper.Slipped) Session.RecordFailure(GraspFailure.Slipped);
            phaseTime += dt;
            if (driver.IsBusy && (Session.State == GraspState.Lowering || Session.State == GraspState.Lifting || Session.State == GraspState.Transporting))
            {
                float progress = Vector3.Distance(driver.CurrentToolPose.position, lastPosition);
                noProgressTime = progress < 0.00005f ? noProgressTime + dt : 0;
                lastPosition = driver.CurrentToolPose.position;
                if (noProgressTime > 2 || phaseTime > 12) Abort(GraspFailure.Timeout, "Motion made no progress.");
            }
            switch (Session.State)
            {
                case GraspState.Lowering:
                    if (!driver.IsBusy) { Session.Advance(GraspState.Closing); phaseTime = 0; }
                    break;
                case GraspState.Closing:
                    driver.TrySetGripper(1 - Mathf.Clamp01(phaseTime / closeDuration));
                    if (phaseTime >= closeDuration)
                    {
                        driver.TrySetGripper(0); Session.RecordFailure(gripper.TryGrip(prize));
                        Session.Advance(GraspState.Lifting);
                        Vector3 p = gantry.LocalPosition; p.y = gantry.Workspace.max.y; Move(p, liftSpeed);
                    }
                    break;
                case GraspState.Lifting:
                    if (!driver.IsBusy)
                    {
                        Session.Advance(GraspState.Transporting);
                        Vector3 p = gantry.ToLocal(dropPose.position); p.y = gantry.Workspace.max.y;
                        Move(p, aimSpeed);
                    }
                    break;
                case GraspState.Transporting:
                    if (!driver.IsBusy) { Session.Advance(GraspState.Releasing); phaseTime = 0; gripper.Release(true); Session.MarkReleased(); }
                    break;
                case GraspState.Releasing:
                    driver.TrySetGripper(Mathf.Clamp01(phaseTime / 0.35f));
                    if (phaseTime >= 0.35f) { Session.Advance(GraspState.Settling); phaseTime = 0; }
                    break;
                case GraspState.Settling:
                    bool stable = chute.Observe(prize, dt);
                    Session.TryAward(prize.SessionId, prize.AttemptId, prize.PrizeId, challenge.targetId, stable);
                    if (Session.Awarded || phaseTime >= challenge.graspProfile.settleTimeout)
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
            gripper.ResetGrip(); aimInput = Vector2.zero; phaseTime = 0;
            if (Session.State == GraspState.Aiming) { chute.Clear(); driver.Stop(); }
        }
        public void ResetChallenge(bool start)
        {
            if (!IsConfigured) return;
            driver = gantry; driver.Stop(); gripper.ResetGrip(); chute.Clear();
            prize.ResetPrize(challenge); Session.Reset(challenge.attemptLimit, start);
            Vector3 home = gantry.Workspace.center; home.y = gantry.Workspace.max.y;
            gantry.ResetPose(home); phaseTime = noProgressTime = 0; aimInput = Vector2.zero; Diagnostic = "";
            Physics.SyncTransforms();
        }
        public void Cancel(bool returnToMenu)
        {
            if (!ready) return;
            ResetChallenge(!returnToMenu);
        }
        private void Abort(GraspFailure failure, string message)
        {
            driver?.Stop(); if (gripper) gripper.ResetGrip(); if (prize && challenge) prize.ResetPrize(challenge);
            if (chute) chute.Clear(); Diagnostic = message; Session.Fail(failure);
        }
        public void SetPaused(bool value) { Session.Pause(value); aimInput = Vector2.zero; }
        private void OnDisable()
        {
            aimInput = Vector2.zero; driver?.Stop(); if (gripper) gripper.ResetGrip();
            if (prize && challenge && prize.IsConfigured) prize.ResetPrize(challenge);
            if (chute) chute.Clear(); Session.Reset(challenge ? challenge.attemptLimit : 1, false);
        }
        private void OnApplicationQuit() { driver?.Stop(); if (gripper) gripper.Release(false); if (prize) prize.ClearVelocity(); }
    }
}
