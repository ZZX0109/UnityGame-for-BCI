using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Derives discrete gestures from the 20-control array that is actually driving
    /// the hand. It reads the same values the driver receives, so a gesture can
    /// never report something the visible hand is not doing.
    ///
    /// Thresholds have hysteresis (separate enter/exit values) so a hand resting on
    /// a boundary does not emit a stream of alternating events.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Hand Gesture Recognizer")]
    [DisallowMultipleComponent]
    public sealed class HandGestureRecognizer : MonoBehaviour
    {
        [Header("Wiring")]
        public HandRigDriver leftHand;
        public HandRigDriver rightHand;

        [Header("Thresholds — curl")]
        [Range(0.4f, 1f)] public float fistEnterCurl = 0.75f;
        [Range(0.2f, 0.9f)] public float fistExitCurl = 0.55f;

        [Header("Thresholds — finger extended")]
        [Range(0f, 0.5f)] public float extendedEnterCurl = 0.25f;
        [Range(0.2f, 0.8f)] public float extendedExitCurl = 0.45f;

        [Header("Thresholds — pinch (thumb tip to index tip)")]
        [Tooltip("Normalised distance below which a pinch is recognised.")]
        [Range(0.01f, 0.5f)] public float pinchEnterDistance = 0.12f;
        [Tooltip("Normalised distance above which the pinch ends.")]
        [Range(0.02f, 0.6f)] public float pinchExitDistance = 0.20f;

        [Header("Timing")]
        [Tooltip("Seconds a gesture must hold before it is emitted.")]
        [Range(0f, 0.5f)] public float confirmSeconds = 0.05f;

        public HandGestureEvent LeftGesture { get; private set; }
        public HandGestureEvent RightGesture { get; private set; }

        HandGestureType _leftState, _rightState;
        float _leftHeld, _rightHeld;

        void Awake()
        {
            if (!leftHand || !rightHand)
            {
                foreach (var d in GetComponentsInChildren<HandRigDriver>(true))
                {
                    if (!d) continue;
                    if (d.IsMirrored) { if (!leftHand) leftHand = d; }
                    else if (!rightHand) rightHand = d;
                }
            }
        }

        /// <summary>
        /// Optionally overrides the arrays the recognizer classifies. When left
        /// null it reads each driver's own current controls, which is the safer
        /// default: the gesture can then never disagree with the visible hand.
        /// </summary>
        public void SetSourcePoses(float[] left, float[] right)
        {
            _leftPose = left;
            _rightPose = right;
        }

        float[] _leftPose, _rightPose;
        float? _leftTipGap, _rightTipGap;

        // In optical mode the gap is measured in palm lengths. NaN marks a lost
        // observation and clears confirmation immediately, even between Update calls.
        public void SetOpticalTipGap(SeatedHandSide side, float gap)
        {
            if (side == SeatedHandSide.Left)
            {
                _leftTipGap = gap;
                if (float.IsNaN(gap)) { _leftState = HandGestureType.None; _leftHeld = 0f; LeftGesture = default; }
            }
            else
            {
                _rightTipGap = gap;
                if (float.IsNaN(gap)) { _rightState = HandGestureType.None; _rightHeld = 0f; RightGesture = default; }
            }
        }

        void Update() => TickClassify(Time.unscaledDeltaTime);

        /// <summary>
        /// Advances classification by an explicit time step and updates
        /// <see cref="LeftGesture"/> / <see cref="RightGesture"/>.
        ///
        /// Separated from Update so the confirm window can be driven deterministically.
        /// Frame deltas are not wall-clock in a batch-mode player, so a test that relied
        /// on Update alone would either never confirm or confirm unpredictably.
        /// </summary>
        public void TickClassify(float dt)
        {
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                dt = 0f;

            float[] left = _leftPose ?? (leftHand ? leftHand.LastControls : null);
            float[] right = _rightPose ?? (rightHand ? rightHand.LastControls : null);

            LeftGesture = Classify(SeatedHandSide.Left, left, ref _leftState, ref _leftHeld, dt);
            RightGesture = Classify(SeatedHandSide.Right, right, ref _rightState, ref _rightHeld, dt);
        }

        HandGestureEvent Classify(SeatedHandSide side, float[] pose, ref HandGestureType state,
                                 ref float held, float dt)
        {
            var result = new HandGestureEvent { side = side, type = HandGestureType.None };
            float? gap = side == SeatedHandSide.Left ? _leftTipGap : _rightTipGap;
            if (pose == null || pose.Length != 20 || (gap.HasValue && float.IsNaN(gap.Value)))
            {
                state = HandGestureType.None;
                held = 0f;
                return result;
            }

            HandGestureType observed = Observe(pose, gap, state);

            if (observed == state)
            {
                held += dt;
                if (held >= confirmSeconds)
                {
                    result.type = state;
                    result.confidence = 1f;
                    result.timestamp = Time.timeAsDouble;
                    result.isSimulated = false;
                }
            }
            else
            {
                state = observed;
                held = 0f;
            }

            return result;
        }

        /// <summary>Classifies one pose using hysteresis thresholds.</summary>
        HandGestureType Observe(float[] c, float? gap, HandGestureType state)
        {
            float index = Curl(c, 1);
            float middle = Curl(c, 2);
            float ring = Curl(c, 3);
            float little = Curl(c, 4);
            float othersMax = Mathf.Max(middle, Mathf.Max(ring, little));

            if (index >= fistEnterCurl && middle >= fistEnterCurl &&
                ring >= fistEnterCurl && little >= fistEnterCurl)
                return HandGestureType.Fist;

            if (gap.HasValue && gap.Value <= (state == HandGestureType.Pinch ? pinchExitDistance : pinchEnterDistance))
                return HandGestureType.Pinch;

            if (index <= extendedEnterCurl && othersMax >= fistEnterCurl)
                return HandGestureType.Point;

            if (!gap.HasValue && PinchDistance(c) <= pinchEnterDistance)
                return HandGestureType.Pinch;

            if (index <= extendedEnterCurl && othersMax <= extendedEnterCurl)
                return HandGestureType.Open;

            return HandGestureType.None;
        }

        static float Curl(float[] c, int finger)
        {
            int o = finger * 4;
            return (c[o] + c[o + 1] + c[o + 2]) / 3f;
        }

        /// <summary>
        /// Normalised thumb-to-index proximity. Uses the control values rather than
        /// bone positions so it needs no skin queries: full index curl plus full
        /// thumb opposition reads as the closest pinch.
        /// </summary>
        float PinchDistance(float[] c)
        {
            float indexCurl = Curl(c, 1);
            float thumbFlex = (c[0] + c[1] + c[2]) / 3f;
            float opposition = c[3];

            // Ideal pinch: index about half curled, thumb opposed, both moderate.
            float indexError = Mathf.Abs(indexCurl - 0.45f);
            float thumbError = Mathf.Abs(thumbFlex - 0.45f);
            float oppError = Mathf.Abs(opposition - 1f) * 0.5f;
            return (indexError + thumbError + oppError) / 3f;
        }
    }
}
