using System;
using UnityEngine;

namespace ClawMachine
{
    public enum GraspState { Idle, Aiming, Approaching, Lowering = Approaching, ContactDescent, Closing, Lifting, Transporting, Releasing, Settling, Result, End, Resetting, Error }
    public enum GraspFailure { None, Misaligned, NoContact, Penetrating, Obstructed, UnstableGrip, Slipped, WrongTarget, MissedChute, Timeout, Cancelled, Configuration }

    // Single authority for transitions, attempt identity and once-only settlement.
    public sealed class ClawSession
    {
        public GraspState State { get; private set; } = GraspState.Idle;
        public int SessionId { get; private set; }
        public int AttemptId { get; private set; }
        public int AttemptLimit { get; private set; }
        public bool Paused { get; private set; }
        public bool Awarded { get; private set; }
        public bool Released { get; private set; }
        public GraspFailure Failure { get; private set; }
        public int Remaining => Mathf.Max(0, AttemptLimit - AttemptId);
        public event Action<GraspState> Changed;

        public void Reset(int limit, bool start)
        {
            Set(GraspState.Resetting);
            SessionId++; AttemptId = 0; AttemptLimit = Mathf.Max(1, limit);
            Paused = Awarded = Released = false; Failure = GraspFailure.None;
            Set(start ? GraspState.Aiming : GraspState.Idle);
        }
        public bool BeginAttempt()
        {
            if (Paused || State != GraspState.Aiming || Remaining == 0) return false;
            AttemptId++; Released = Awarded = false; Failure = GraspFailure.None;
            Set(GraspState.Approaching); return true;
        }
        public bool Advance(GraspState next)
        {
            // Attempt entry must go through BeginAttempt so IDs/counts cannot be bypassed.
            if (Paused || next == GraspState.Lowering || !IsLegal(State, next)) return false;
            Set(next); return true;
        }
        public static bool IsLegal(GraspState from, GraspState to)
        {
            switch (from)
            {
                case GraspState.Idle: return to == GraspState.Aiming;
                case GraspState.Aiming: return to == GraspState.Approaching;
                case GraspState.Approaching: return to == GraspState.ContactDescent;
                case GraspState.ContactDescent: return to == GraspState.Closing;
                case GraspState.Closing: return to == GraspState.Lifting;
                case GraspState.Lifting: return to == GraspState.Transporting;
                case GraspState.Transporting: return to == GraspState.Releasing;
                case GraspState.Releasing: return to == GraspState.Settling;
                case GraspState.Settling: return to == GraspState.Result;
                case GraspState.Result: return to == GraspState.Aiming || to == GraspState.End;
                default: return false;
            }
        }
        public void MarkReleased() { if (State == GraspState.Releasing) Released = true; }
        public void RecordFailure(GraspFailure reason) { if (Failure == GraspFailure.None) Failure = reason; }
        public bool TryAward(int session, int attempt, string prize, string target, bool containedAndStable)
        {
            if (Paused || State != GraspState.Settling || !Released || Awarded || !containedAndStable ||
                session != SessionId || attempt != AttemptId || string.IsNullOrEmpty(target) || prize != target) return false;
            Awarded = true; Failure = GraspFailure.None; return true;
        }
        public bool Retry()
        {
            if (Paused || State != GraspState.Result) return false;
            return Advance(Awarded || Remaining == 0 ? GraspState.End : GraspState.Aiming);
        }
        public void Pause(bool value) { Paused = value; }
        public void Fail(GraspFailure reason) { Failure = reason; Set(GraspState.Error); }
        private void Set(GraspState state) { State = state; Changed?.Invoke(state); }
    }

    public static class ClawMotion
    {
        public static Vector3 MachineDirection(Vector2 input) => new Vector3(input.x, 0, input.y).normalized * Mathf.Min(1, input.magnitude);
        public static Vector3 Clamp(Vector3 point, Bounds workspace)
        {
            return new Vector3(Mathf.Clamp(point.x, workspace.min.x, workspace.max.x),
                Mathf.Clamp(point.y, workspace.min.y, workspace.max.y), Mathf.Clamp(point.z, workspace.min.z, workspace.max.z));
        }
        public static Vector3 Accelerate(Vector3 velocity, Vector3 wanted, float acceleration, float dt)
            => Vector3.MoveTowards(velocity, wanted, Mathf.Max(0, acceleration) * Mathf.Max(0, dt));
    }
}
