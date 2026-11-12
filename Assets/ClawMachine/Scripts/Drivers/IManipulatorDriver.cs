using UnityEngine;

namespace ClawMachine
{
    public enum ManipulatorStatus { Idle, Moving, Error }
    // Simulator contract only. Hardware transport and hardware safety do not belong here.
    public interface IManipulatorDriver
    {
        Pose CurrentToolPose { get; }
        bool IsBusy { get; }
        bool TryMoveTo(Pose targetWorldPose, float maxSpeedMetersPerSecond);
        bool TrySetGripper(float openness01);
        void Stop();
    }
}
