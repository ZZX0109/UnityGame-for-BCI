using UnityEngine;

namespace ClawMachine
{
    public sealed class GantryDriver : MonoBehaviour, IManipulatorDriver
    {
        [SerializeField] private Transform machineRoot;
        [SerializeField] private BoxCollider workspaceVolume;
        [SerializeField] private Rigidbody toolBody;
        [SerializeField] private GripperController gripper;
        [SerializeField] private Transform bridge, trolley, cable;
        [SerializeField] private float railHeight = 1.92f;
        [SerializeField] private float cableAnchorHeight = 0.325f;
        [SerializeField] private float acceleration = 1.5f;
        [SerializeField] private float tolerance = 0.005f;
        private Vector3 target, velocity;
        private float speed;
        private bool aiming;
        private Vector3 aimVelocity;
        public ManipulatorStatus Status { get; private set; }
        public string Error { get; private set; } = "";
        public bool IsBusy => Status == ManipulatorStatus.Moving;
        public bool IsSimulation => true;
        public Vector3 CurrentVelocity => machineRoot ? machineRoot.TransformDirection(velocity) : Vector3.zero;
        public Pose CurrentToolPose => new Pose(toolBody.position, toolBody.rotation);
        public Vector3 LocalPosition => machineRoot.InverseTransformPoint(toolBody.position);
        public Bounds Workspace => new Bounds(workspaceVolume.center, workspaceVolume.size);
        public bool IsConfigured => machineRoot && workspaceVolume && toolBody && gripper &&
            workspaceVolume.transform == machineRoot && toolBody.isKinematic && machineRoot.lossyScale == Vector3.one;

        public bool TryMoveTo(Pose pose, float maxSpeed)
        {
            if (!IsConfigured || maxSpeed <= 0 || !Finite(maxSpeed) || !Finite(pose.position) || !Finite(pose.rotation)) return false;
            if (Quaternion.Angle(pose.rotation, machineRoot.rotation) > 0.5f) { Error = "Gantry tool orientation is fixed"; return false; }
            Vector3 local = machineRoot.InverseTransformPoint(pose.position);
            Vector3 clamped = ClawMotion.Clamp(local, Workspace);
            if ((local - clamped).sqrMagnitude > 0.000001f) { Error = "Workspace limit"; return false; }
            aiming = false; target = local; speed = Mathf.Min(maxSpeed, 0.65f); Status = ManipulatorStatus.Moving; Error = ""; return true;
        }
        public bool TrySetGripper(float openness) { if (!gripper || !Finite(openness)) return false; return gripper.TrySetOpenness(openness); }
        public bool TryJog(Vector3 worldVelocity)
        {
            if (!IsConfigured || !Finite(worldVelocity) || worldVelocity.magnitude > 0.65f) return false;
            Vector3 local = machineRoot.InverseTransformDirection(worldVelocity);
            if (Mathf.Abs(local.y) > 0.001f) return false;
            Aim(new Vector2(local.x, local.z).normalized, local.magnitude, 0); return true;
        }
        public bool TryReset(Pose pose)
        {
            if (!IsConfigured || !Finite(pose.position) || !Finite(pose.rotation) || Quaternion.Angle(pose.rotation, machineRoot.rotation) > 0.5f || !Workspace.Contains(ToLocal(pose.position))) return false;
            ResetPose(ToLocal(pose.position)); return true;
        }
        public void Tick(float dt) { Step(dt); }
        public void Aim(Vector2 input, float maxSpeed, float dt)
        {
            if (!IsConfigured) return;
            if (!Finite(new Vector3(input.x, 0, input.y)) || !Finite(maxSpeed)) { Stop(); return; }
            aiming = true;
            aimVelocity = ClawMotion.MachineDirection(input) * Mathf.Clamp(maxSpeed, 0, 0.65f);
        }
        public Pose WorldPose(Vector3 local) => new Pose(machineRoot.TransformPoint(local), machineRoot.rotation);
        public Vector3 ToLocal(Vector3 world) => machineRoot.InverseTransformPoint(world);
        public void Step(float dt)
        {
            if (!IsConfigured || dt <= 0 || !Finite(dt)) return;
            Vector3 current = LocalPosition;
            if (aiming)
            {
                velocity = ClawMotion.Accelerate(velocity, aimVelocity, acceleration, dt);
                Vector3 next = ClawMotion.Clamp(current + velocity * dt, Workspace);
                toolBody.MovePosition(machineRoot.TransformPoint(next));
                Status = velocity.sqrMagnitude > 0.000001f ? ManipulatorStatus.Moving : ManipulatorStatus.Idle;
                return;
            }
            Vector3 delta = target - current;
            if (Status != ManipulatorStatus.Moving || delta.magnitude <= tolerance)
            {
                velocity = Vector3.zero;
                if (Status == ManipulatorStatus.Moving) toolBody.MovePosition(machineRoot.TransformPoint(target));
                Status = ManipulatorStatus.Idle; return;
            }
            // Decelerate over the remaining distance, then clamp every step.
            Vector3 wanted = delta.normalized * Mathf.Min(speed, Mathf.Sqrt(2 * acceleration * delta.magnitude));
            velocity = ClawMotion.Accelerate(velocity, wanted, acceleration, dt);
            Vector3 step = velocity * dt;
            if (Vector3.Dot(step, delta) >= delta.sqrMagnitude) { step = delta; velocity = Vector3.zero; }
            toolBody.MovePosition(machineRoot.TransformPoint(ClawMotion.Clamp(current + step, Workspace)));
            toolBody.MoveRotation(machineRoot.rotation);
        }
        public void Stop() { aiming = false; aimVelocity = velocity = Vector3.zero; Status = ManipulatorStatus.Idle; if (IsConfigured) target = LocalPosition; }
        public void ResetPose(Vector3 local)
        {
            if (!IsConfigured) return;
            Stop(); target = ClawMotion.Clamp(local, Workspace);
            toolBody.position = machineRoot.TransformPoint(target); toolBody.rotation = machineRoot.rotation;
        }
        private void LateUpdate()
        {
            if (!IsConfigured) return;
            Vector3 p = LocalPosition;
            if (bridge) bridge.localPosition = new Vector3(0, railHeight, p.z);
            if (trolley) trolley.localPosition = new Vector3(p.x, railHeight, p.z);
            if (cable) { cable.localPosition = new Vector3(p.x, (railHeight + p.y + cableAnchorHeight) * 0.5f, p.z); cable.localScale = new Vector3(0.012f, Mathf.Max(0.005f, (railHeight - p.y - cableAnchorHeight) * 0.5f), 0.012f); }
        }
        private static bool Finite(float p) => !float.IsNaN(p) && !float.IsInfinity(p);
        private static bool Finite(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) && Quaternion.Dot(q, q) > 0.000001f;
        private void OnDrawGizmosSelected()
        {
            if (!machineRoot || !workspaceVolume) return;
            Gizmos.matrix = machineRoot.localToWorldMatrix; Gizmos.color = new Color(0.3f, 0.85f, 0.65f, 0.7f);
            Gizmos.DrawWireCube(workspaceVolume.center, workspaceVolume.size);
            Gizmos.color = Color.red; Gizmos.DrawLine(Vector3.up * 0.75f, new Vector3(0.5f, 0.75f, 0));
            Gizmos.color = Color.blue; Gizmos.DrawLine(Vector3.up * 0.75f, new Vector3(0, 0.75f, 0.5f));
        }
    }
}
