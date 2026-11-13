using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    // Test-only alternate executor. It does not own geometry, physics, networking or hardware.
    public sealed class AuditManipulatorStub : MonoBehaviour, IManipulatorDriver
    {
        public bool Simulation = true;
        public bool HoldCommandPosition, FaultOnTick;
        public ManipulatorStatus ReportedStatus = ManipulatorStatus.Idle;
        public int MoveCalls, ResetCalls, TickCalls;
        public Pose LastCommand;
        public bool IsConfigured => true;
        public bool IsSimulation => Simulation;
        public Pose CurrentToolPose { get; private set; }
        public Vector3 CurrentVelocity => Vector3.zero;
        public ManipulatorStatus Status => ReportedStatus;
        public string Error => ReportedStatus == ManipulatorStatus.Error ? "Audit executor fault" : "";
        public bool IsBusy => ReportedStatus == ManipulatorStatus.Moving;
        public bool TryMoveTo(Pose pose, float speed) { LastCommand = pose; if (!HoldCommandPosition) CurrentToolPose = pose; MoveCalls++; return true; }
        public bool TryJog(Vector3 velocity) => true;
        public bool TryReset(Pose pose) { CurrentToolPose = pose; ResetCalls++; return true; }
        public bool TrySetGripper(float openness) => true;
        public void Tick(float dt) { TickCalls++; if (FaultOnTick) ReportedStatus = ManipulatorStatus.Error; }
        public void Stop() { ReportedStatus = ManipulatorStatus.Idle; }
    }
}
