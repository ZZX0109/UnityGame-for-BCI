using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_LEGACY_INPUT_MANAGER
using Input = UnityEngine.Input;
#endif

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Keyboard-driven pose provider using the legacy Input Manager.
    /// Key bindings are serialised in the inspector so non-programmers can
    /// remap them per-hand without touching code.
    ///
    /// This is the compatibility entry point for the original fixed layout. New
    /// profiles should use <see cref="InputActionHandProvider"/>, which supports
    /// rebinding and save/restore.
    ///
    /// Like the Input Actions provider it stands down while a UI element has focus,
    /// so typing in a settings field cannot also punch the player, and a key that is
    /// held when focus is gained does not keep driving the hand.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Keyboard Hand Pose Provider")]
    public sealed class KeyboardHandPoseProvider : MonoBehaviour, IHandPoseProvider
    {
        public string ProviderName => "Keyboard";
        public bool IsActive => isActiveAndEnabled;
        public string SourceCategory => "manual";

        [Header("Target side")]
        public SeatedHandSide targetSide = SeatedHandSide.Right;

        [Tooltip("Suppress hand actions while a UI element has focus.")]
        public bool suppressWhileUiFocused = true;

        [Header("Key bindings")]
        public KeyCode keyRest    = KeyCode.None;
        public KeyCode keyFist    = KeyCode.None;
        public KeyCode keyPinch   = KeyCode.None;
        public KeyCode keyPoint   = KeyCode.None;
        public KeyCode keySpread  = KeyCode.None;
        public KeyCode keyGripCycle = KeyCode.None;

        [Header("Behaviour")]
        [Tooltip("Restore the resting pose when the key is released.")]
        public bool releaseRestores = true;

        [Range(0.016f, 0.5f)]
        public float smoothing = 0.08f;

        float[] _target;
        float[] _current;
        string _activeAction;

        /// <summary>True while a UI element owns keyboard focus.</summary>
        public static bool UiHasFocus =>
            EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null;

        void Awake()
        {
            _target = PoseLibrary.OpenPose(null);  // uses authored rest
            _current = new float[20];
            System.Array.Copy(_target, _current, 20);
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            // A focused text field or menu owns the keyboard: fall back to rest
            // instead of letting a held letter key keep posing the hand.
            if (suppressWhileUiFocused && UiHasFocus)
            {
                _activeAction = null;
                float[] rest = PoseLibrary.Rest();
                float kBlocked = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.016f, smoothing));
                for (int i = 0; i < 20; i++)
                    _current[i] = Mathf.Lerp(_current[i], rest[i], kBlocked);
                return;
            }

            float[][] candidates = null;
            string action = null;

            if (Pressed(keyFist))    { candidates = BuildFist(); action = "fist"; }
            else if (Pressed(keyPinch)) { candidates = BuildPinch(); action = "pinch"; }
            else if (Pressed(keyPoint)) { candidates = BuildPoint(); action = "point"; }
            else if (Pressed(keySpread)) { candidates = BuildSpread(); action = "spread"; }
            else if (Pressed(keyGripCycle)) { candidates = BuildCycle(); action = "gripCycle"; }
            else if (Pressed(keyRest)) { candidates = BuildRest(); action = "rest"; }
            else if (releaseRestores)
            {
                action = null;
                candidates = BuildRest();
            }

            if (candidates == null)
                return;

            _activeAction = action;

            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.016f, smoothing));
            // SeatedHandSide is { Unspecified = 0, Left = 1, Right = 2 }, so the enum value
            // must NOT be used directly as an array index into a two-element table — doing
            // so threw IndexOutOfRangeException on every frame for the default side.
            float[] source = candidates[SideIndex()];
            for (int i = 0; i < 20; i++)
                _current[i] = Mathf.Lerp(_current[i], source[i], k);
#endif
        }

        /// <summary>0 for the left hand, 1 for the right; unsafe sides fall back to right.</summary>
        int SideIndex() => targetSide == SeatedHandSide.Left ? 0 : 1;

        public float[] GetPose(SeatedHandSide side)
        {
            if (side != targetSide) return null;
            return _current;
        }

        public bool TryGetWrist(SeatedHandSide side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            return false; // keyboard does not provide wrist tracking
        }

        static bool Pressed(KeyCode key) => key != KeyCode.None && Input.GetKey(key);

        static float[][] BuildRest()
        {
            float[] p = PoseLibrary.Rest();
            return new[] { p, p };
        }
        static float[][] BuildFist()
        {
            float[] p = PoseLibrary.Fist();
            return new[] { p, p };
        }
        static float[][] BuildPinch()
        {
            float[] p = PoseLibrary.Pinch();
            return new[] { p, p };
        }
        static float[][] BuildPoint()
        {
            float[] p = PoseLibrary.Point();
            return new[] { p, p };
        }
        static float[][] BuildSpread()
        {
            float[] p = PoseLibrary.OpenPose(null);
            p[7] = 1f; p[11] = 1f; p[15] = 1f; p[19] = 1f;
            return new[] { p, p };
        }
        static float[][] BuildCycle()
        {
            float phase = Mathf.PingPong(Time.time * 0.7f, 1f);
            float[] p = PoseLibrary.Lerp(PoseLibrary.OpenPose(null), PoseLibrary.FistPose(null), phase);
            return new[] { p, p };
        }
    }
}