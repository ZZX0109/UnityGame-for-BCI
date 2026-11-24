using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Drives the hand from <see cref="HandActionBindingProfile"/> through Input
    /// Actions, replacing the fixed-key DemoUI path for anyone who rebinds.
    ///
    /// Coexistence with the games' legacy input is deliberate:
    ///   • Reading goes through Input Actions only; nothing is polled from the old
    ///     Input Manager, so enabling this component cannot consume a key the game
    ///     also reads.
    ///   • While a UI element has focus the whole binding set is suppressed, so
    ///     typing in a settings field cannot also punch the player.
    ///   • If a game key is bound by mistake, <see cref="ReportConflicts"/> shows it
    ///     instead of silently double-firing.
    ///
    /// It reports the pose source as "manual" so no consumer can mistake it for
    /// optical hand tracking.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Input Action Hand Provider")]
    [DisallowMultipleComponent]
    public sealed class InputActionHandProvider : MonoBehaviour, IHandPoseProvider
    {
        [Header("Configuration")]
        public HandActionBindingProfile profile;
        public HandRigDriver leftHand;
        public HandRigDriver rightHand;

        [Tooltip("Suppress all hand actions while a UI element has focus.")]
        public bool suppressWhileUiFocused = true;

        [Tooltip("Component that forwards ToggleAppearance / Guidance commands, if any.")]
        public MonoBehaviour commandReceiver;

        public string ProviderName => "Input Actions";
        public string SourceCategory => "manual";

#if ENABLE_INPUT_SYSTEM
        public bool IsActive => isActiveAndEnabled && _asset != null;
#else
        // Input System is installed but not the active handler: the component still
        // exists so hosts can reference it, but it produces no pose.
        public bool IsActive => isActiveAndEnabled;
#endif

        /// <summary>Set by the host when its menus take input focus.</summary>
        public bool UiHasFocus { get; set; }

        readonly float[] _leftPose = new float[20];
        readonly float[] _rightPose = new float[20];
        bool _leftDirty = true, _rightDirty = true;

#if ENABLE_INPUT_SYSTEM
        InputActionAsset _asset;
        readonly List<InputAction> _actionOrder = new List<InputAction>();
        readonly List<HandActionBinding> _bindingOrder = new List<HandActionBinding>();

        void OnEnable()
        {
            if (!profile)
            {
                Debug.LogWarning("[InputActionHandProvider] no HandActionBindingProfile assigned; provider idle.");
                return;
            }

            _asset = profile.Asset;
            _asset.Enable();
            IndexActions();
        }

        void OnDisable()
        {
            if (_asset != null)
                _asset.Disable();
        }

        void IndexActions()
        {
            _actionOrder.Clear();
            _bindingOrder.Clear();

            var map = _asset.FindActionMap(HandActionBindingProfile.MapName);
            if (map == null)
                return;

            foreach (var b in profile.bindings)
            {
                if (b == null) continue;
                var action = map.FindAction(b.Key);
                if (action == null) continue;
                _actionOrder.Add(action);
                _bindingOrder.Add(b);
            }
        }

        void Update()
        {
            if (_asset == null)
                return;

            bool blocked = suppressWhileUiFocused && UiHasFocus;
            if (!blocked && suppressWhileUiFocused)
                blocked = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null;

            if (blocked)
            {
                // Held keys must not keep driving the hand while a menu owns focus.
                ResetPose(_leftPose);
                ResetPose(_rightPose);
                return;
            }

            bool leftChanged = false, rightChanged = false;

            for (int i = 0; i < _actionOrder.Count; i++)
            {
                var action = _actionOrder[i];
                var binding = _bindingOrder[i];
                bool active = IsTriggered(action, binding.trigger);

                if (HandActionDefinitions.IsCommand(binding.action))
                {
                    if (active && binding.trigger == HandActionTrigger.OnPress)
                        ApplyCommand(binding.action);
                    continue;
                }

                if (binding.side == SeatedHandSide.Left)
                    leftChanged |= Apply(binding, active, _leftPose);
                else
                    rightChanged |= Apply(binding, active, _rightPose);
            }

            if (leftChanged) _leftDirty = true;
            if (rightChanged) _rightDirty = true;
        }

        static bool IsTriggered(InputAction action, HandActionTrigger trigger)
        {
            switch (trigger)
            {
                case HandActionTrigger.WhileHeld: return action.IsPressed();
                case HandActionTrigger.OnRelease: return action.WasReleasedThisFrame();
                default: return action.WasPressedThisFrame();
            }
        }

        /// <summary>Applies one binding to a pose buffer. Returns true when it changed.</summary>
        bool Apply(HandActionBinding binding, bool active, float[] pose)
        {
            if (HandActionDefinitions.IsPose(binding.action))
            {
                if (!active)
                    return false;

                // Whole-hand poses replace the buffer wholesale.
                var resolved = ResolvePoseForAction(binding.action);
                if (resolved == null)
                    return false;

                System.Array.Copy(resolved, pose, 20);
                return true;
            }

            int finger = HandActionDefinitions.FingerOf(binding.action);
            if (finger < 0)
                return false;

            if (!binding.allowCombination)
                ResetPose(pose);

            float amount = active ? 1f : (binding.restoreRestOnRelease ? 0f : -1f);
            if (amount < 0f)
                return false;

            HandActionDefinitions.ApplyFingerFlex(pose, finger, amount);
            return true;
        }

        float[] ResolvePoseForAction(HandAction action)
        {
            switch (action)
            {
                case HandAction.Open:
                case HandAction.Release:
                    return leftHand || rightHand
                        ? PoseLibrary.OpenPose(leftHand ? leftHand : rightHand)
                        : null;
                case HandAction.Fist:
                    return leftHand || rightHand
                        ? PoseLibrary.FistPose(leftHand ? leftHand : rightHand)
                        : null;
                case HandAction.Pinch:
                    return leftHand || rightHand
                        ? PoseLibrary.PinchPose(leftHand ? leftHand : rightHand)
                        : null;
                case HandAction.Point:
                    return PoseLibrary.Point();
                default:
                    return null;
            }
        }

        void ApplyCommand(HandAction action)
        {
            if (commandReceiver == null)
                return;

            switch (action)
            {
                case HandAction.ToggleAppearance:
                    if (commandReceiver is HandAppearanceController appearance)
                        appearance.Toggle();
                    break;
                case HandAction.GuidanceStart:
                    if (commandReceiver is GuidanceHandController guidance)
                        guidance.ShowPose(PoseLibrary.Point(), PoseLibrary.Point());
                    break;
                case HandAction.GuidanceEnd:
                    if (commandReceiver is GuidanceHandController end)
                        end.Hide();
                    break;
            }
        }
#else
        void OnEnable() { }
#endif

        public float[] GetPose(SeatedHandSide side)
        {
            var pose = side == SeatedHandSide.Left ? _leftPose : _rightPose;
            bool dirty = side == SeatedHandSide.Left ? _leftDirty : _rightDirty;
            if (!dirty)
                return pose;

            if (side == SeatedHandSide.Left) _leftDirty = false;
            else _rightDirty = false;
            return pose;
        }

        public bool TryGetWrist(SeatedHandSide side, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            // Keyboard/manual input supplies no wrist: claiming one would misrepresent
            // the source as tracked.
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            return false;
        }

        static void ResetPose(float[] pose)
        {
            for (int i = 0; i < pose.Length; i++)
                pose[i] = 0f;
        }

        /// <summary>Conflict report for the settings UI. Empty means clean.</summary>
        public List<string> ReportConflicts() =>
            profile ? profile.DetectConflicts() : new List<string>();
    }
}