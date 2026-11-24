using UnityEngine;

namespace Neuralife.XR
{
    /// <summary>A reconnect never turns a held button into a new command.</summary>
    public sealed class ReleaseToArmButton
    {
        private bool armed, previous;
        public bool Pressed { get; private set; }
        public bool Released { get; private set; }
        public bool Held { get; private set; }

        public void Sample(bool available, bool down)
        {
            Pressed = Released = false;
            if (!available)
            {
                Released = Held; Held = previous = armed = false;
                return;
            }
            if (!armed)
            {
                if (!down) armed = true;
                Held = previous = false;
                return;
            }
            Pressed = down && !previous;
            Released = !down && previous;
            Held = previous = down;
        }
    }

    public sealed class CentreToArmAxis
    {
        private bool armed;
        public Vector2 Sample(bool available, Vector2 raw)
        {
            if (!available) { armed = false; return Vector2.zero; }
            Vector2 value = PicoControls.Deadzone(raw);
            if (!armed) { if (value == Vector2.zero) armed = true; return Vector2.zero; }
            return value;
        }
    }

    public sealed class SnapTurnLatch
    {
        private bool armed;
        public float Sample(bool available, float horizontal)
        {
            if (!available || float.IsNaN(horizontal) || float.IsInfinity(horizontal))
            { armed = false; return 0; }
            if (Mathf.Abs(horizontal) < 0.25f) { armed = true; return 0; }
            if (!armed || Mathf.Abs(horizontal) < 0.7f) return 0;
            armed = false;
            return Mathf.Sign(horizontal) * 30;
        }
    }

    public sealed class PicoControls
    {
        public readonly ReleaseToArmButton Select = new ReleaseToArmButton();
        public readonly ReleaseToArmButton Undo = new ReleaseToArmButton();
        public readonly ReleaseToArmButton Reset = new ReleaseToArmButton();
        public readonly ReleaseToArmButton Pause = new ReleaseToArmButton();
        public readonly ReleaseToArmButton Auxiliary = new ReleaseToArmButton();
        public readonly ReleaseToArmButton Recenter = new ReleaseToArmButton();
        private readonly CentreToArmAxis movement = new CentreToArmAxis();
        private readonly CentreToArmAxis rightMovement = new CentreToArmAxis();
        private readonly SnapTurnLatch turn = new SnapTurnLatch();
        public Vector2 Move { get; private set; }
        public Vector2 RightAxis { get; private set; }
        public float TurnDegrees { get; private set; }

        public void Sample(bool head, bool left, bool right, Vector2 leftAxis, Vector2 rightAxis,
            bool trigger, bool a, bool b, bool x, bool y, bool stickClick)
        {
            Move = movement.Sample(head && left, leftAxis);
            RightAxis = rightMovement.Sample(head && right, rightAxis);
            TurnDegrees = turn.Sample(head && right, rightAxis.x);
            Select.Sample(head && right, trigger);
            Undo.Sample(head && right, a);
            Reset.Sample(head && right, b);
            Pause.Sample(head && left, x);
            Auxiliary.Sample(head && left, y);
            Recenter.Sample(head && right, stickClick);
        }

        public static Vector2 Deadzone(Vector2 raw)
        {
            if (float.IsNaN(raw.x) || float.IsInfinity(raw.x) || float.IsNaN(raw.y) || float.IsInfinity(raw.y)) return Vector2.zero;
            float magnitude = raw.magnitude;
            return magnitude <= 0.2f ? Vector2.zero : raw / magnitude * Mathf.Clamp01((magnitude - 0.2f) / 0.8f);
        }

        public static Vector3 PlanarMotion(Transform head, Vector2 axis)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            forward.Normalize();
            return (forward * axis.y + Vector3.Cross(Vector3.up, forward) * axis.x);
        }
    }
}
