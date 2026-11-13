using UnityEngine;

namespace ClawMachine
{
    // Task-space geometry is independent of rail/joint/robot implementation.
    public sealed class ClawTaskSpace : MonoBehaviour
    {
        [SerializeField] private Transform machineFrame;
        [SerializeField] private BoxCollider workspaceVolume;
        [SerializeField] private Transform releasePoint;
        public bool IsConfigured => machineFrame && workspaceVolume && releasePoint && workspaceVolume.transform == machineFrame && machineFrame.lossyScale == Vector3.one && Vector3.Dot(machineFrame.up, Vector3.up) > 0.999f;
        public Vector3 Up => machineFrame.up;
        public Bounds LocalBounds => new Bounds(workspaceVolume.center, workspaceVolume.size);
        public Quaternion Orientation => machineFrame.rotation;
        public Pose HomePose => PoseAtHeight(machineFrame.TransformPoint(LocalBounds.center), LocalBounds.max.y);
        public Pose ReleasePose => PoseAtHeight(releasePoint.position, LocalBounds.max.y);
        public Vector3 ToLocal(Vector3 world) => machineFrame.InverseTransformPoint(world);
        public Vector3 ToWorld(Vector3 local) => machineFrame.TransformPoint(local);
        public Pose PoseAtHeight(Vector3 world, float localHeight) { var local = ToLocal(world); local.y = localHeight; return new Pose(ToWorld(ClawMotion.Clamp(local, LocalBounds)), Orientation); }
        public Pose Clamp(Pose pose) => new Pose(ToWorld(ClawMotion.Clamp(ToLocal(pose.position), LocalBounds)), Orientation);
        public bool Contains(Vector3 world) => IsConfigured && Finite(world) && LocalBounds.Contains(ToLocal(world));
        public Vector3 AimVelocity(Vector2 input, float speed) => machineFrame.TransformDirection(ClawMotion.MachineDirection(input)) * speed;
        public static bool ValidRotation(Quaternion q) => Finite(new Vector3(q.x, q.y, q.z)) && !float.IsNaN(q.w) && !float.IsInfinity(q.w) && Quaternion.Dot(q, q) > 0.000001f;
        public static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
    }

    // Defense in depth for simulator requests. This is never a hardware safety controller.
    public sealed class ManipulatorSafetyGuard
    {
        private readonly IManipulatorDriver driver;
        private readonly ClawTaskSpace space;
        public bool Stopped { get; private set; }
        public ManipulatorSafetyGuard(IManipulatorDriver driver, ClawTaskSpace space) { this.driver = driver; this.space = space; }
        public bool Ready => driver != null && driver.IsConfigured && driver.IsSimulation && space && space.IsConfigured && !Stopped;
        public bool Move(Pose pose, float speed) => Ready && space.Contains(pose.position) && ClawTaskSpace.ValidRotation(pose.rotation) && speed > 0 && speed <= 0.65f && !float.IsNaN(speed) && driver.TryMoveTo(pose, speed);
        public bool Jog(Vector3 velocity) => Ready && ClawTaskSpace.Finite(velocity) && velocity.magnitude <= 0.65f && driver.TryJog(velocity);
        public void EmergencyStop() { Stopped = true; driver?.Stop(); }
        public void ResetStop() { driver?.Stop(); Stopped = false; }
    }
}
