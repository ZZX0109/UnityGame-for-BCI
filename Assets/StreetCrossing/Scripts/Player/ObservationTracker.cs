using System;
using UnityEngine;

namespace StreetCrossing.Player
{
    public sealed class ObservationTracker
    {
        private float leftTime, rightTime;
        public bool Left { get; private set; }
        public bool Right { get; private set; }
        public event Action<bool> Observed;
        public void Tick(float yawRelativeToCrosswalk, bool atKerb, float dt)
        {
            if (!atKerb) { leftTime = rightTime = 0; return; }
            float yaw = Mathf.DeltaAngle(0, yawRelativeToCrosswalk);
            leftTime = yaw <= -55 && yaw >= -120 ? leftTime + dt : 0;
            rightTime = yaw >= 55 && yaw <= 120 ? rightTime + dt : 0;
            if (!Left && leftTime >= 0.35f) { Left = true; Observed?.Invoke(true); }
            if (!Right && rightTime >= 0.35f) { Right = true; Observed?.Invoke(false); }
        }
    }
}
