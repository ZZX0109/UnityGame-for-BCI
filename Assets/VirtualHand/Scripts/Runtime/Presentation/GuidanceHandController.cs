using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Plays a translucent teaching hand that demonstrates an action, without ever
    /// touching the player's tracked hand.
    ///
    /// Separation is the whole point: the guidance hand is a second hand instance
    /// with its own <see cref="HandRigDriver"/>, so a host can show "watch this"
    /// while the player's own hands keep following the real tracking source. Its
    /// appearance is forced to the blue style so it reads as an overlay rather than
    /// a second real hand.
    ///
    /// It never writes to the player's drivers, never changes object ownership and
    /// never creates a camera or tracking origin of its own.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Guidance Hand Controller")]
    [DisallowMultipleComponent]
    public sealed class GuidanceHandController : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The teaching hand's own driver. Must not be the player's driver.")]
        public HandRigDriver guidanceDriver;

        [Tooltip("Optional second guidance hand for two-handed demonstrations.")]
        public HandRigDriver secondGuidanceDriver;

        [Tooltip("Appearance component on the guidance hand(s). Forced to blue while showing.")]
        public AvatarVisualStyle guidanceVisualStyle;

        [Header("Playback")]
        [Tooltip("Loop the demonstration while it is visible.")]
        public bool loop = true;

        [Range(0.05f, 4f)] public float speed = 1f;

        [Tooltip("Seconds to blend from the rest pose into the demonstration.")]
        [Range(0f, 2f)] public float blendInSeconds = 0.25f;

        public bool IsShowing { get; private set; }

        float[] _fromLeft, _fromRight;
        float[] _targetLeft, _targetRight;
        float _phase;
        float _blend;

        float[] _from, _target;

        void Awake()
        {
            if (guidanceDriver)
                guidanceDriver.gameObject.SetActive(false);
            if (secondGuidanceDriver)
                secondGuidanceDriver.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows a one-off pose on the guidance hand.
        /// <paramref name="leftPose"/> / <paramref name="rightPose"/> use the frozen
        /// 20-control layout.
        /// </summary>
        public void ShowPose(float[] leftPose, float[] rightPose)
        {
            IsShowing = true;
            _phase = 0f;
            _blend = blendInSeconds > 0f ? 0f : 1f;

            _targetLeft = Copy(leftPose);
            _targetRight = Copy(rightPose);

            // Start from whatever the guidance hand is doing now, so repeated calls
            // blend instead of snapping.
            _fromLeft = CurrentControls(guidanceDriver);
            _fromRight = CurrentControls(secondGuidanceDriver);

            SetActive(guidanceDriver, _targetLeft != null);
            SetActive(secondGuidanceDriver, _targetRight != null);

            if (guidanceVisualStyle)
                guidanceVisualStyle.SetBlueMode(true);
        }

        /// <summary>Hides the guidance hand and returns it to rest.</summary>
        public void Hide()
        {
            IsShowing = false;
            SetActive(guidanceDriver, false);
            SetActive(secondGuidanceDriver, false);
            if (guidanceVisualStyle)
                guidanceVisualStyle.SetBlueMode(false);
        }

        void Update()
        {
            if (!IsShowing)
                return;

            float dt = Time.unscaledDeltaTime * speed;

            _blend = blendInSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(_blend + Time.unscaledDeltaTime / blendInSeconds);

            _phase += dt;
            if (loop)
                _phase -= Mathf.Floor(_phase);

            ApplySide(guidanceDriver, _fromLeft, _targetLeft, dt);
            ApplySide(secondGuidanceDriver, _fromRight, _targetRight, dt);
        }

        void ApplySide(HandRigDriver driver, float[] from, float[] target, float dt)
        {
            if (!driver || target == null)
                return;

            float[] blended = new float[20];
            for (int i = 0; i < 20; i++)
                blended[i] = Mathf.Lerp(from != null && from.Length == 20 ? from[i] : target[i],
                                        target[i],
                                        Mathf.SmoothStep(0f, 1f, _blend));

            driver.UpdateControls(blended, 0.08f, dt);
        }

        static float[] Copy(float[] source)
        {
            if (source == null || source.Length != 20)
                return null;
            var copy = new float[20];
            System.Array.Copy(source, copy, 20);
            return copy;
        }

        static float[] CurrentControls(HandRigDriver driver)
        {
            if (!driver)
                return null;
            return driver.LastControls;
        }

        static void SetActive(HandRigDriver driver, bool active)
        {
            if (driver && driver.gameObject.activeSelf != active)
                driver.gameObject.SetActive(active);
        }
    }
}