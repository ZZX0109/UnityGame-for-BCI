using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Routes exactly one active pose provider per hand to the driver,
    /// respecting the provider priority order. Two providers must not write
    /// the same hand in the same frame.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Hand Input Router")]
    [DisallowMultipleComponent]
    public sealed class HandInputRouter : MonoBehaviour
    {
        [Header("Providers (priority top-to-bottom)")]
        [Tooltip("Higher-index providers override lower ones for the same hand.")]
        public IHandPoseProvider[] providers;

        HandRigDriver _left, _right;
        IHandPoseProvider _activeLeft, _activeRight;

        public IHandPoseProvider ActiveProviderLeft => _activeLeft;
        public IHandPoseProvider ActiveProviderRight => _activeRight;

        void Awake()
        {
            var drivers = GetComponentsInChildren<HandRigDriver>(true);
            foreach (var d in drivers)
            {
                if (d.IsMirrored) _left = d;
                else _right = d;
            }
        }

        void Update()
        {
            if (!_left && !_right)
                return;

            float dt = Time.deltaTime;
            ApplySide(ref _left, ref _activeLeft, SeatedHandSide.Left, dt);
            ApplySide(ref _right, ref _activeRight, SeatedHandSide.Right, dt);
        }

        void ApplySide(ref HandRigDriver driver, ref IHandPoseProvider active,
                        SeatedHandSide side, float dt)
        {
            if (!driver)
                return;

            for (int i = (providers != null ? providers.Length : 0) - 1; i >= 0; i--)
            {
                var p = providers[i];
                if (p == null || !p.IsActive)
                    continue;

                float[] pose = p.GetPose(side);
                if (pose == null || !HandRigDriver.IsValidControlArray(pose))
                    continue;

                active = p;
                driver.UpdateControls(pose, 0.08f, dt);
                return;  // highest-priority active provider wins
            }

            // No valid provider: hold last pose
        }
    }
}