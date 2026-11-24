using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Turns the recognizer's per-hand state into edge events (GraspStart /
    /// GraspEnd) and guarantees the host consumes each edge exactly once per frame.
    ///
    /// This component deliberately does NOT call the game adapter itself.
    /// <see cref="VirtualHandHost"/> is the single consumer, so a host that ticks
    /// the hand from both Update and before-render cannot double-trigger a grasp,
    /// and the adapter always receives tracking frames and gestures together.
    ///
    /// While input is untrusted (tracking lost, hand suspended, replay paused) no
    /// new edges are produced, and a gesture that was held is forgotten rather than
    /// re-announced as a fresh grasp when input returns.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Hand Interaction Router")]
    [DisallowMultipleComponent]
    public sealed class HandInteractionRouter : MonoBehaviour
    {
        [Header("Wiring")]
        public HandGestureRecognizer recognizer;

        [Header("Safety")]
        [Tooltip("Seconds after input becomes trustworthy again before new edges are emitted.")]
        [Range(0f, 2f)]
        public float resyncSeconds = 0.35f;

        /// <summary>Host sets this false when tracking is lost or the hand is suspended.</summary>
        public bool InputTrusted { get; set; } = true;

        public bool LeftHeld { get; private set; }
        public bool RightHeld { get; private set; }

        /// <summary>Edges produced this frame. Both are None when nothing changed.</summary>
        public HandGestureEvent LeftEvent { get; private set; }
        public HandGestureEvent RightEvent { get; private set; }

        int _tickedFrame = -1;
        float _resyncTimer;

        void Awake()
        {
            if (!recognizer)
                recognizer = GetComponent<HandGestureRecognizer>();
        }

        // Ticked here as well as from the host. The frame latch makes the extra call a
        // no-op, so the component works whether or not a VirtualHandHost drives it.
        void Update() => Tick();

        /// <summary>
        /// Evaluates one frame of edges. Safe to call from both Update and
        /// before-render: the second call in the same frame is a no-op.
        /// </summary>
        public void Tick()
        {
            if (Time.frameCount == _tickedFrame)
                return;
            _tickedFrame = Time.frameCount;

            LeftEvent = default;
            RightEvent = default;

            if (!recognizer)
                return;

            if (!InputTrusted)
            {
                // Forget the held gesture without emitting a release: the game must
                // not be told something was let go just because tracking dropped.
                LeftHeld = false;
                RightHeld = false;
                _resyncTimer = resyncSeconds;
                return;
            }

            if (_resyncTimer > 0f)
            {
                _resyncTimer -= Time.unscaledDeltaTime;
                if (_resyncTimer > 0f)
                    return;
            }

            LeftEvent = MakeEdge(recognizer.LeftGesture, ref _leftHeldField);
            RightEvent = MakeEdge(recognizer.RightGesture, ref _rightHeldField);
        }

        bool _leftHeldField, _rightHeldField;

        HandGestureEvent MakeEdge(HandGestureEvent gesture, ref bool wasHeld)
        {
            bool nowHeld = gesture.type == HandGestureType.Fist || gesture.type == HandGestureType.Pinch;

            if (nowHeld && !wasHeld)
            {
                wasHeld = true;
                ApplyHeld();
                gesture.type = HandGestureType.GraspStart;
                return gesture;
            }

            if (!nowHeld && wasHeld)
            {
                wasHeld = false;
                ApplyHeld();
                gesture.type = HandGestureType.GraspEnd;
                return gesture;
            }

            return default;   // no edge this frame
        }

        void ApplyHeld()
        {
            LeftHeld = _leftHeldField;
            RightHeld = _rightHeldField;
        }

        /// <summary>
        /// Clears the held state without emitting a release. A host calls this when
        /// it takes an object away from the hand, so a later frame cannot report a
        /// release for a grasp the game never saw.
        /// </summary>
        public void ResetHeldState()
        {
            _leftHeldField = false;
            _rightHeldField = false;
            LeftHeld = false;
            RightHeld = false;
            LeftEvent = default;
            RightEvent = default;
        }
    }
}