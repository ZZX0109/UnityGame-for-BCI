using System;
using System.Collections.Generic;
using StreetCrossing.Traffic;

namespace StreetCrossing.Evaluation
{
    [Flags]
    public enum CrossingRegion
    {
        None = 0, Approach = 1, Kerb = 2, Entry = 4,
        RoadwayHazard = 8, Exit = 16, Destination = 32,
        ProtectedCrosswalk = 64, CrossingRoute = 128
    }
    public enum VehicleContactKind { StationaryContact, MovingTrafficAccident }
    public sealed class CrossingEvaluator
    {
        private readonly HashSet<int> nearMissVehicles = new HashSet<int>();
        private readonly HashSet<int> stationaryVehicles = new HashSet<int>();
        private readonly bool requireLeft, requireRight;
        private bool approachSeen, kerbSeen, entrySeen, active, crossed, validRoute, legalEntry, finished, lastOutsideWasNear;
        private bool leftAtEntry, rightAtEntry;
        public bool VehicleConflictAtEntry { get; private set; }
        public TrafficPhase SignalAtEntry { get; private set; }
        public int Attempts { get; private set; }
        public int Violations { get; private set; }
        public int NearMisses => nearMissVehicles.Count;
        public int StationaryContacts => stationaryVehicles.Count;
        public int MovingCollisions { get; private set; }
        public int Collisions => MovingCollisions;
        public bool LeftObserved { get; private set; }
        public bool RightObserved { get; private set; }
        public bool ObservationRequirementsSatisfied => (!requireLeft || leftAtEntry) && (!requireRight || rightAtEntry);
        public bool SafetySuccess { get; private set; }
        public bool IsCrossing => active;
        public bool Finished => finished;
        public bool MayRecordPreEntryObservation => Attempts == 0 && !finished;
        public float WaitingSeconds { get; private set; }
        public string Feedback { get; private set; } = "先走到路沿，观察行人灯与左右车流。";
        public event Action<string, string> Event;
        public CrossingEvaluator(bool requireLeftObservation = true, bool requireRightObservation = true)
        { requireLeft = requireLeftObservation; requireRight = requireRightObservation; }
        /// <summary>Called only by the visibility-gated kerb observation tracker; late observations are rejected here too.</summary>
        public void Observe(bool left)
        {
            if (!MayRecordPreEntryObservation || (left ? LeftObserved : RightObserved)) return;
            if (left) LeftObserved = true; else RightObserved = true;
            Event?.Invoke(left ? "LeftLook" : "RightLook", "Visible road target held at kerb before entry; camera visibility, not eye gaze.");
        }
        public void Tick(CrossingRegion region, TrafficPhase phase, bool withinRoute, bool vehicleConflict, float dt)
        {
            if (finished) return;
            if ((region & CrossingRegion.Approach) != 0) approachSeen = true;
            if ((region & CrossingRegion.Kerb) != 0) { kerbSeen = true; WaitingSeconds += Math.Max(0, dt); }
            if ((region & CrossingRegion.Entry) != 0) entrySeen = true;
            bool roadway = (region & CrossingRegion.RoadwayHazard) != 0;
            if (!roadway) lastOutsideWasNear = (region & (CrossingRegion.Approach | CrossingRegion.Kerb | CrossingRegion.Entry)) != 0;
            if (roadway && !active)
            {
                active = true; crossed = false; Attempts++;
                SignalAtEntry = phase; legalEntry = phase == TrafficPhase.Walk;
                validRoute = lastOutsideWasNear && approachSeen && kerbSeen && entrySeen && withinRoute;
                VehicleConflictAtEntry = vehicleConflict; leftAtEntry = LeftObserved; rightAtEntry = RightObserved;
                Event?.Invoke("CrossingStarted", "phase=" + phase + ";route=" + validRoute + ";vehicleConflict=" + vehicleConflict + ";leftBefore=" + leftAtEntry + ";rightBefore=" + rightAtEntry);
                Event?.Invoke("SignalAtEntry", phase.ToString());
                if (vehicleConflict) Event?.Invoke("VehicleConflictAtEntry", "An already committed vehicle has not cleared.");
                if (!legalEntry) { Violations++; Feedback = "本次进入不是行人绿灯。请安全返回人行道，再重新练习。"; Event?.Invoke("SignalViolation", phase.ToString()); }
                else if (!ObservationRequirementsSatisfied) { Feedback = "进入前未完成要求的可见道路观察。请安全完成当前动作后重新练习。"; Event?.Invoke("ObservationRequirementMissing", "Required visible left/right road targets were not inspected before entry."); }
                else Feedback = "沿斑马线前往对面。继续观察；闪烁时可完成已经开始的过街。";
                if (!validRoute) { Violations++; Event?.Invoke("RouteViolation", "Entered roadway outside the prepared legal crossing route."); }
            }
            if (active && roadway && !withinRoute && validRoute)
            { validRoute = false; Violations++; Feedback = "已离开合法过街路线，请安全返回人行道。"; Event?.Invoke("RouteViolation", "Left the marked crosswalk route."); }
            if (active && !roadway)
            {
                active = false; crossed = (region & CrossingRegion.Exit) != 0;
                Event?.Invoke("CrossingEnd", crossed ? "Far sidewalk reached." : "Returned to sidewalk.");
                if (!crossed) { entrySeen = false; Feedback = "已返回人行道。重新开始可重置本次违规与观察要求。"; }
            }
            if ((region & CrossingRegion.Destination) != 0 && crossed)
            {
                finished = true;
                SafetySuccess = legalEntry && validRoute && !VehicleConflictAtEntry && MovingCollisions == 0 && Violations == 0 && ObservationRequirementsSatisfied;
                Feedback = SafetySuccess ? "已按要求安全抵达目的地。" : !ObservationRequirementsSatisfied ? "已到达，但进入前的左右可见道路观察没有完成。" : "已到达，但本次包含不安全过街行为。请重新练习。";
                Event?.Invoke(SafetySuccess ? "SafeArrival" : "UnsafeArrival", Feedback);
            }
        }
        public void NearMiss(int encounterId)
        { if (!finished && nearMissVehicles.Add(encounterId)) Event?.Invoke("NearMiss", "Moving closing vehicle encounter=" + encounterId); }
        public void Contact(int encounterId, float vehicleSpeed)
        {
            if (finished) return;
            if (vehicleSpeed <= 0.25f)
            {
                if (stationaryVehicles.Add(encounterId)) { Feedback = "接触到已停稳车辆。请保持距离；本次不是移动交通事故。"; Event?.Invoke("StationaryContact", "Resting vehicle contact encounter=" + encounterId); }
                return;
            }
            MovingCollisions = 1; finished = true; SafetySuccess = false;
            Feedback = "发生移动交通碰撞。练习已暂停，请重新开始并确认车流停稳。";
            Event?.Invoke("Collision", "Physical moving vehicle contact encounter=" + encounterId + ";speed=" + vehicleSpeed);
            if (active) { active = false; Event?.Invoke("CrossingEnd", "Moving traffic accident."); }
        }
        public void Collision(int encounterId) { Contact(encounterId, 1); }
        public void Timeout()
        {
            if (finished) return; finished = true; SafetySuccess = false;
            Feedback = "练习时间已到。可以重新开始，无需抢时间。"; Event?.Invoke("Timeout", Feedback);
        }
    }
}
