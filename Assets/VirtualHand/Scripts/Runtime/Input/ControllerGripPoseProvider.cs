using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Maps a single grip float (0‑open, 1‑closed) to a 20‑control fist pose
    /// while the wrist position/rotation come from a tracked transform
    /// (typically a VR controller anchor).  This is the simplest non‑optical
    /// VR input path; label it as "controller" so users know the fingers are
    /// simulated, not optically measured.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Controller Grip Pose Provider")]
    [DisallowMultipleComponent]
    public sealed class ControllerGripPoseProvider : MonoBehaviour, IHandPoseProvider
    {
        public string ProviderName => "Controller";
        public string SourceCategory => "device";

        [Header("Input")]
        public SeatedHandSide side = SeatedHandSide.Right;

        [Tooltip("Transform that tracks the controller in world space.")]
        public Transform trackedTransform;

        [Tooltip("Current grip amount [0‑1]; updated externally (e.g. from PICO XR input).")]
        [Range(0f, 1f)]
        public float gripAmount;

        [Header("Blending")]
        [Range(0.016f, 0.5f)]
        public float smoothing = 0.08f;

        float[] _current = new float[20];
        bool _hasValidWrist;

        void Awake()
        {
            float[] rest = PoseLibrary.OpenPose(null);
            System.Array.Copy(rest, _current, 20);
        }

        void Update()
        {
            float[] target = PoseLibrary.Lerp(
                PoseLibrary.OpenPose(null),
                PoseLibrary.FistPose(null),
                Mathf.Clamp01(gripAmount));

            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.016f, smoothing));
            for (int i = 0; i < 20; i++)
                _current[i] = Mathf.Lerp(_current[i], target[i], k);
        }

        public bool IsActive => trackedTransform != null;

        public float[] GetPose(SeatedHandSide requestedSide)
        {
            if (requestedSide != side)
                return null;
            return _current;
        }

        public bool TryGetWrist(SeatedHandSide requestedSide,
                                out Vector3 worldPosition,
                                out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (requestedSide != side || !trackedTransform)
                return false;

            worldPosition = trackedTransform.position;
            worldRotation = trackedTransform.rotation;
            return true;
        }
    }
}