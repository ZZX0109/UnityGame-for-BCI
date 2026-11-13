using UnityEngine;

namespace ClawMachine
{
    public enum ManipulatorStatus { Idle, Moving, Error }
    // Simulator contract only. Hardware transport and hardware safety do not belong here.
    public interface IManipulatorDriver
    {
        bool IsConfigured { get; }
        bool IsSimulation { get; }
        Pose CurrentToolPose { get; }
        Vector3 CurrentVelocity { get; }
        ManipulatorStatus Status { get; }
        string Error { get; }
        bool IsBusy { get; }
        bool TryMoveTo(Pose targetWorldPose, float maxSpeedMetersPerSecond);
        bool TryJog(Vector3 worldVelocity);
        bool TryReset(Pose worldPose);
        bool TrySetGripper(float openness01);
        void Tick(float deltaTime);
        void Stop();
    }
}
