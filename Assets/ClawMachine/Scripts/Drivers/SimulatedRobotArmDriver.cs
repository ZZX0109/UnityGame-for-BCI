using UnityEngine;

namespace ClawMachine
{
    // Alternate world-space simulator executor. No IK, sockets, ROS, serial port or hardware code.
    public sealed class SimulatedRobotArmDriver : MonoBehaviour, IManipulatorDriver
    {
        [SerializeField] private Rigidbody simulatedTool;
        [SerializeField] private ClawTaskSpace taskSpace;
        [SerializeField] private GripperController gripper;
        [SerializeField] private float acceleration = 1.2f;
        private Pose goal;
        private Vector3 velocity, jog;
        private bool jogging;
        private float speed;
        public bool IsConfigured => simulatedTool && simulatedTool.isKinematic && taskSpace && taskSpace.IsConfigured && gripper;
        public bool IsSimulation => true;
        public Pose CurrentToolPose => new Pose(simulatedTool.position, simulatedTool.rotation);
        public Vector3 CurrentVelocity => velocity;
        public ManipulatorStatus Status { get; private set; }
        public string Error { get; private set; } = "";
        public bool IsBusy => Status == ManipulatorStatus.Moving;
        public bool TryMoveTo(Pose pose, float maxSpeed)
        {
            if (!IsConfigured || !taskSpace.Contains(pose.position) || !ClawTaskSpace.ValidRotation(pose.rotation) || maxSpeed <= 0 || float.IsNaN(maxSpeed) || float.IsInfinity(maxSpeed)) return false;
            goal = pose; speed = Mathf.Min(0.65f, maxSpeed); jogging = false; Status = ManipulatorStatus.Moving; return true;
        }
        public bool TryJog(Vector3 worldVelocity)
        {
            if (!IsConfigured || !ClawTaskSpace.Finite(worldVelocity) || worldVelocity.magnitude > 0.65f) return false;
            jog = worldVelocity; jogging = true; return true;
        }
        public bool TryReset(Pose pose)
        {
            if (!IsConfigured || !taskSpace.Contains(pose.position) || !ClawTaskSpace.ValidRotation(pose.rotation)) return false;
            Stop(); goal = pose; simulatedTool.position = goal.position; simulatedTool.rotation = goal.rotation; return true;
        }
        public bool TrySetGripper(float openness) { if (!gripper || float.IsNaN(openness) || float.IsInfinity(openness)) return false; return gripper.TrySetOpenness(openness); }
        public void Tick(float dt)
        {
            if (!IsConfigured || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt) || (!jogging && Status != ManipulatorStatus.Moving)) return;
            Vector3 delta = goal.position - simulatedTool.position;
            Vector3 wanted = jogging ? jog : IsBusy ? delta.normalized * Mathf.Min(speed, Mathf.Sqrt(2 * acceleration * delta.magnitude)) : Vector3.zero;
            velocity = ClawMotion.Accelerate(velocity, wanted, acceleration, dt);
            Vector3 step = velocity * dt;
            if (!jogging && step.magnitude >= delta.magnitude) { step = delta; velocity = Vector3.zero; Status = Quaternion.Angle(simulatedTool.rotation, goal.rotation) < 0.5f ? ManipulatorStatus.Idle : ManipulatorStatus.Moving; }
            if (jogging) Status = velocity.sqrMagnitude > 0.000001f ? ManipulatorStatus.Moving : ManipulatorStatus.Idle;
            Pose next = taskSpace.Clamp(new Pose(simulatedTool.position + step, taskSpace.Orientation));
            simulatedTool.MovePosition(next.position); simulatedTool.MoveRotation(jogging ? taskSpace.Orientation : Quaternion.RotateTowards(simulatedTool.rotation, goal.rotation, 90 * dt));
        }
        public void Stop() { jogging = false; velocity = jog = Vector3.zero; Status = ManipulatorStatus.Idle; }
        private void OnDisable() { Stop(); }
    }
}
