using System;
using UnityEngine;

namespace StreetCrossing.Traffic
{
    [CreateAssetMenu(menuName = "Street Crossing/Intersection Phase Table")]
    public sealed class IntersectionPhaseTable : ScriptableObject
    {
        [Serializable]
        public sealed class PhaseTiming
        {
            public TrafficPhase phase;
            [Min(0.1f)] public float duration;
            public bool vehicleEntry;
            public bool pedestrianEntry;
            public PhaseTiming(TrafficPhase p, float seconds, bool cars = false, bool people = false)
            { phase = p; duration = seconds; vehicleEntry = cars; pedestrianEntry = people; }
        }
        public PhaseTiming[] phases = {
            new PhaseTiming(TrafficPhase.VehicleGreen, 15, true),
            new PhaseTiming(TrafficPhase.VehicleAmber, 3),
            new PhaseTiming(TrafficPhase.AllRedBeforeWalk, 2),
            new PhaseTiming(TrafficPhase.Walk, 12, false, true),
            new PhaseTiming(TrafficPhase.WalkFlashing, 5),
            new PhaseTiming(TrafficPhase.AllRedAfterWalk, 2)
        };
        [Min(5)] public float clearanceWatchdogSeconds = 30;
        public void Validate()
        {
            if (phases == null || phases.Length != 6) throw new ArgumentException("Exactly six ordered phases are required.");
            for (int i = 0; i < phases.Length; i++)
            {
                PhaseTiming p = phases[i];
                if (p == null || (int)p.phase != i || p.duration <= 0 || float.IsNaN(p.duration) || float.IsInfinity(p.duration))
                    throw new ArgumentException("Phase order and finite positive durations are required.");
                if (p.vehicleEntry != (i == 0) || p.pedestrianEntry != (i == 3))
                    throw new ArgumentException("Prototype phases admit vehicles only on VehicleGreen and new pedestrians only on Walk.");
            }
            if (clearanceWatchdogSeconds < 5 || float.IsNaN(clearanceWatchdogSeconds) || float.IsInfinity(clearanceWatchdogSeconds))
                throw new ArgumentException("A finite clearance watchdog of at least five seconds is required.");
        }
    }
}
