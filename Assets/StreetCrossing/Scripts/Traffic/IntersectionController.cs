using System;

namespace StreetCrossing.Traffic
{
    /// <summary>Single authority. Only the game clock calls Tick; views never keep their own phase timers.</summary>
    public sealed class IntersectionController
    {
        private readonly IntersectionPhaseTable table;
        private float elapsed;
        private float blockedTime;
        private bool watchdogReported;
        private bool pedestrianOccupied;
        public TrafficPhase Phase { get; private set; }
        public float Remaining => Math.Max(0, table.phases[(int)Phase].duration - elapsed);
        public bool IsClearanceHeld { get; private set; }
        public bool CanEnterVehicles => table.phases[(int)Phase].vehicleEntry && !pedestrianOccupied;
        public bool CanEnterPedestrians => table.phases[(int)Phase].pedestrianEntry;
        public event Action<TrafficPhase> PhaseChanged;
        public event Action<string> ClearanceBlocked;
        public IntersectionController(IntersectionPhaseTable definition) { table = definition; table.Validate(); Reset(); }
        public void Reset()
        {
            Phase = TrafficPhase.VehicleGreen; elapsed = blockedTime = 0;
            pedestrianOccupied = IsClearanceHeld = watchdogReported = false;
            PhaseChanged?.Invoke(Phase);
        }
        public void SetPedestrianOccupancy(bool occupied) { pedestrianOccupied = occupied; }
        public void Tick(float seconds, bool vehicleConflictOccupied, bool pedestrianConflictOccupied)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            pedestrianOccupied = pedestrianConflictOccupied;
            // Small deterministic world steps also make boundary timing independent of the rendering frame rate.
            float budget = seconds;
            while (budget > 0.000001f)
            {
                float duration = table.phases[(int)Phase].duration;
                float advance = Math.Min(budget, Math.Max(0, duration - elapsed));
                elapsed += advance; budget -= advance;
                if (elapsed + 0.00001f < duration) break;
                bool blocked = (Phase == TrafficPhase.AllRedBeforeWalk && vehicleConflictOccupied)
                    || (Phase == TrafficPhase.AllRedAfterWalk && (pedestrianConflictOccupied || vehicleConflictOccupied));
                IsClearanceHeld = blocked;
                if (blocked)
                {
                    blockedTime += budget; budget = 0;
                    if (!watchdogReported && blockedTime >= table.clearanceWatchdogSeconds)
                    {
                        watchdogReported = true;
                        ClearanceBlocked?.Invoke("Clearance occupied: keeping all signals red. Pause or restart to recover.");
                    }
                    break;
                }
                Phase = (TrafficPhase)(((int)Phase + 1) % 6);
                elapsed = blockedTime = 0; watchdogReported = false;
                PhaseChanged?.Invoke(Phase);
            }
        }
    }
}
