using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Adapts the existing <see cref="ReplayController"/> to the
    /// <see cref="IHandPoseProvider"/> contract so the unified router can
    /// select between replay and live input without changing the driver.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Replay Hand Pose Provider")]
    [DisallowMultipleComponent]
    public sealed class ReplayHandPoseProvider : MonoBehaviour, IHandPoseProvider
    {
        public string ProviderName => "Replay";
        public string SourceCategory => "replay";

        [Tooltip("Auto-detected from this hierarchy when empty.")]
        public ReplayController controller;

        public bool IsActive
        {
            get
            {
                if (!controller || !controller.isActiveAndEnabled)
                    return false;
                if (!controller.source || !controller.source.IsPlaying)
                    return false;
                return controller.HasValidInput;
            }
        }

        void Awake()
        {
            if (!controller)
                controller = GetComponent<ReplayController>();
        }

        public float[] GetPose(SeatedHandSide side)
        {
            if (!IsActive)
                return null;

            // The ReplayController updates its internal _leftTarget / _rightTarget
            // during Update(), then calls UpdateControls on the drivers.  Here we
            // return the raw targets.  (If we need them earlier in the frame the
            // router must run after ReplayController in Script Execution Order.)
            return side == SeatedHandSide.Left ? _leftSnapshot : _rightSnapshot;
        }

        public bool TryGetWrist(SeatedHandSide side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            return false;
        }

        // Snapshot is refreshed by ReplayController in Update() before the router runs.
        // This coupling is lightweight: we assume ReplayController executes first.
        float[] _leftSnapshot, _rightSnapshot;

        public void UpdateSnapshots(float[] left, float[] right)
        {
            _leftSnapshot = left;
            _rightSnapshot = right;
        }

        void Update()
        {
            // Keep snapshots alive while not in active replay.
            // The controller must call UpdateSnapshots explicitly in its own Update path.
            if (!IsActive)
            {
                if (_leftSnapshot == null)
                    _leftSnapshot = PoseLibrary.OpenPose(null);
                if (_rightSnapshot == null)
                    _rightSnapshot = PoseLibrary.OpenPose(null);
            }
        }
    }
}