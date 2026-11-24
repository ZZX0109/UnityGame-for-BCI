using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// The single entry point that a host game uses to embed the Neuralife
    /// Virtual Hand System.  It owns the FirstPersonHands prefab instance,
    /// routes input, manages appearance, and multiplexes gesture events to
    /// the game's <see cref="IGameHandActionAdapter"/>.
    /// 
    /// Usage from a host:
    ///   1. Instantiate FirstPersonHands.prefab under the tracking origin.
    ///   2. Add a VirtualHandHost component to that root.
    ///   3. Assign the adapter in the Inspector.
    ///   4. The hands appear and follow the configured input providers.
    ///   5. Call SetEnabled(false) to hide hands and restore game input.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Virtual Hand Host")]
    [DisallowMultipleComponent]
    public sealed class VirtualHandHost : MonoBehaviour
    {
        [Header("Input routing")]
        public HandInputRouter inputRouter;

        [Tooltip("Produces the grasp edges handed to the adapter. Auto-found on this object.")]
        public HandInteractionRouter interactionRouter;

        [Tooltip("Independent setting for the teaching hand overlay. Controls the guidance " +
                 "hand only — never the player's own hands.")]
        public bool guidanceOverlayEnabled = true;

        [Tooltip("Optional guidance hand shown by GuidanceStart / hidden by GuidanceEnd.")]
        public GuidanceHandController guidance;

        /// <summary>
        /// The separate Guidance Overlay setting required by spec 6.3. Toggling it starts
        /// or stops the teaching hand and nothing else: it deliberately does not touch the
        /// player's drivers, the input source, or the appearance of the tracked hands.
        /// </summary>
        public bool GuidanceOverlay
        {
            get => guidanceOverlayEnabled;
            set
            {
                if (guidanceOverlayEnabled == value)
                    return;
                guidanceOverlayEnabled = value;
                if (!value && guidance)
                    guidance.Hide();
            }
        }

        [Header("Appearance")]
        public HandAppearanceController appearance;

        [Header("Game integration")]
        [Tooltip("The owning game's adapter; null when standalone/demo mode.")]
        public IGameHandActionAdapter adapter;

        /// <summary>Disable to hide hands and suspend all hand input processing.</summary>
        public bool IsEnabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                ApplyEnabled();
            }
        }
        bool _enabled = true;

        public HandAppearanceMode AppearanceMode
        {
            get => appearance ? appearance.Mode : HandAppearanceMode.Realistic;
            set { if (appearance) appearance.Mode = value; }
        }

        HandTrackingFrame _leftFrame, _rightFrame;
        HandGestureEvent _leftGesture, _rightGesture;
        HandRigDriver _leftHand, _rightHand;

        void Awake()
        {
            if (!inputRouter)
                inputRouter = GetComponent<HandInputRouter>();
            if (!appearance)
                appearance = GetComponent<HandAppearanceController>();
            if (!interactionRouter)
                interactionRouter = GetComponent<HandInteractionRouter>();
            if (!guidance)
                guidance = GetComponent<GuidanceHandController>();

            var drivers = GetComponentsInChildren<HandRigDriver>(true);
            foreach (var d in drivers)
            {
                if (d.IsMirrored) _leftHand = d;
                else _rightHand = d;
            }

            ApplyEnabled();
        }

        void Update()
        {
            if (!_enabled)
                return;

            // The host is the single consumer of interaction events. Reading them from
            // the router here — rather than letting the router call the adapter itself —
            // is what guarantees the adapter sees each grasp edge exactly once even when
            // the hand is ticked from both Update and before-render.
            if (interactionRouter)
            {
                interactionRouter.Tick();
                _leftGesture = interactionRouter.LeftEvent;
                _rightGesture = interactionRouter.RightEvent;
            }

            if (adapter != null)
            {
                adapter.OnHandUpdate(_leftFrame, _rightFrame, _leftGesture, _rightGesture);
            }
        }

        void ApplyEnabled()
        {
            if (inputRouter)
                inputRouter.enabled = _enabled;
            if (_leftHand)
                _leftHand.gameObject.SetActive(_enabled);
            if (_rightHand)
                _rightHand.gameObject.SetActive(_enabled);
        }

        public HandRigDriver GetHand(SeatedHandSide side) =>
            side == SeatedHandSide.Left ? _leftHand : _rightHand;
    }
}