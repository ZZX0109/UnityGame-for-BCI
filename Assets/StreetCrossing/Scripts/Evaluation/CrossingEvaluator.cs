using System;
using System.Collections.Generic;
using StreetCrossing.Traffic;
using UnityEngine;

namespace StreetCrossing.Evaluation
{
    [Flags]
    public enum CrossingRegion { None = 0, Approach = 1, Kerb = 2, Entry = 4, Conflict = 8, Exit = 16, Destination = 32 }
    public sealed class CrossingEvaluator
    {
        private readonly HashSet<int> nearMissVehicles = new HashSet<int>();
        private bool approachSeen, kerbSeen, entrySeen, active, crossed, validRoute, legalEntry;
        private bool finished;
        private bool lastOutsideWasNear;
        public bool VehicleConflictAtEntry { get; private set; }
        public TrafficPhase SignalAtEntry { get; private set; }
        public int Attempts { get; private set; }
        public int Violations { get; private set; }
        public int NearMisses => nearMissVehicles.Count;
        public int Collisions { get; private set; }
        public bool LeftObserved { get; private set; }
        public bool RightObserved { get; private set; }
        public bool SafetySuccess { get; private set; }
        public bool IsCrossing => active;
        public bool Finished => finished;
        public float WaitingSeconds { get; private set; }
        public string Feedback { get; private set; } = "先走到路沿，观察行人灯与左右车流。";
        public event Action<string, string> Event;
        public void Observe(bool left)
        {
            if (finished || (left ? LeftObserved : RightObserved)) return;
            if (left) LeftObserved = true; else RightObserved = true;
            Event?.Invoke(left ? "LeftLook" : "RightLook", "Camera heading held for observation threshold; not an eye-tracking measure.");
        }
        public void Tick(CrossingRegion region, TrafficPhase phase, bool withinCrosswalk, bool vehicleConflict, float dt)
        {
            if (finished) return;
            if ((region & CrossingRegion.Approach) != 0) approachSeen = true;
            if ((region & CrossingRegion.Kerb) != 0) { kerbSeen = true; WaitingSeconds += dt; }
            if ((region & CrossingRegion.Entry) != 0) entrySeen = true;
            bool roadway = (region & CrossingRegion.Conflict) != 0;
            if (!roadway) lastOutsideWasNear = (region & (CrossingRegion.Approach | CrossingRegion.Kerb | CrossingRegion.Entry)) != 0;
            if (roadway && !active)
            {
                active = true; crossed = false; Attempts++;
                SignalAtEntry = phase; legalEntry = phase == TrafficPhase.Walk;
                validRoute = lastOutsideWasNear && approachSeen && kerbSeen && entrySeen && withinCrosswalk;
                VehicleConflictAtEntry = vehicleConflict;
                Event?.Invoke("CrossingStarted", "phase=" + phase + ";route=" + validRoute + ";vehicleConflict=" + vehicleConflict);
                Event?.Invoke("SignalAtEntry", phase.ToString());
                if (vehicleConflict) Event?.Invoke("VehicleConflictAtEntry", "An already committed vehicle has not cleared.");
                if (!legalEntry) { Violations++; Feedback = "本次进入不是行人绿灯；安全退回等待区后可重新尝试。"; Event?.Invoke("SignalViolation", phase.ToString()); }
                else Feedback = "沿斑马线前往对面。继续观察，灯转闪烁时可完成已开始的过街。";
            }
            if (active && roadway && !withinCrosswalk && validRoute)
            { validRoute = false; Violations++; Feedback = "请沿斑马线过街。"; Event?.Invoke("RouteViolation", "Left the marked crosswalk."); }
            if (active && !roadway)
            {
                active = false;
                crossed = (region & CrossingRegion.Exit) != 0;
                Event?.Invoke("CrossingEnd", crossed ? "Far sidewalk reached." : "Returned to sidewalk.");
                if (!crossed) { entrySeen = false; Feedback = "已返回人行道。等待下一次行人绿灯，确认车流停止。"; }
            }
            if ((region & CrossingRegion.Destination) != 0 && crossed)
            {
                finished = true;
                SafetySuccess = legalEntry && validRoute && !VehicleConflictAtEntry && Collisions == 0 && Violations == 0;
                Feedback = SafetySuccess ? "安全抵达街角便利店。" : "已到达，但本次过街包含违规。请重新尝试安全通行。";
                Event?.Invoke(SafetySuccess ? "SafeArrival" : "UnsafeArrival", Feedback);
            }
        }
        public void NearMiss(int encounterId)
        {
            if (!finished && nearMissVehicles.Add(encounterId)) Event?.Invoke("NearMiss", "Moving closing vehicle encounter=" + encounterId);
        }
        public void Collision(int encounterId)
        {
            if (finished || Collisions > 0) return;
            Collisions = 1; finished = true; SafetySuccess = false;
            Feedback = "与车辆发生碰撞。暂停恢复后重新尝试：等待、观察，确认车辆停稳。";
            Event?.Invoke("Collision", "Physical collider contact with encounter=" + encounterId);
            if (active) { active = false; Event?.Invoke("CrossingEnd", "Collision."); }
        }
        public void Timeout()
        {
            if (finished) return;
            finished = true; SafetySuccess = false;
            Feedback = "练习时间已到。可以重新开始，无需抢时间。";
            Event?.Invoke("Timeout", Feedback);
        }
    }
}
