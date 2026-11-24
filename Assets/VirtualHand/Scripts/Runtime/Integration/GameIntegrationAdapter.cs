using UnityEngine;
using Neuralife.VirtualHand.Body;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Game-facing bridge for the full-body system.  A host game adds this
    /// component next to <see cref="VirtualHandHost"/> (or standalone) and
    /// wires the body controllers once.  The adapter:
    ///  - maps recognised hand gestures to whole-body poses (pointing, grasping, ...),
    ///  - forwards locomotion input to the lower-body walk cycle,
    ///  - exposes first-person visibility and appearance toggles used by the
    ///    realistic/blue mode switch in the games.
    /// It deliberately knows nothing about box pushing or traffic rules; the
    /// per-game <see cref="IGameHandActionAdapter"/> implementations keep that logic.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Game Integration Adapter")]
    [DisallowMultipleComponent]
    public sealed class GameIntegrationAdapter : MonoBehaviour, IGameHandActionAdapter
    {
        [Header("Body wiring (auto-found on children when empty)")]
        public FullBodyPoseController poseController;
        public LowerBodyPoseController locomotion;
        public FirstPersonVisibility visibility;
        public FullBodyAppearanceController appearance;

        [Tooltip("Gesture→pose bridge active switch. When false only passthrough of frames happens.")]
        public bool gesturePosesEnabled = true;

        public string GameName => "FullBody";

        /// <summary>Latest raw frames, for per-game adapters that want them.</summary>
        public HandTrackingFrame LastLeftFrame => _lastLeft;
        public HandTrackingFrame LastRightFrame => _lastRight;

        HandTrackingFrame _lastLeft, _lastRight;

        void Awake() => Wire();

        // Idempotent: only searches for a field that is still empty, so components
        // added after Awake (e.g. built by the prefab builder at runtime) are picked up.
        void Wire()
        {
            if (!poseController) poseController = GetComponentInChildren<FullBodyPoseController>(true);
            if (!locomotion) locomotion = GetComponentInChildren<LowerBodyPoseController>(true);
            if (!visibility) visibility = GetComponentInChildren<FirstPersonVisibility>(true);
            if (!appearance) appearance = GetComponentInChildren<FullBodyAppearanceController>(true);
        }

        /// <summary>Entry point for the host's adapter slot when no per-game adapter exists.</summary>
        public void OnHandUpdate(
            HandTrackingFrame leftFrame,
            HandTrackingFrame rightFrame,
            HandGestureEvent leftGesture,
            HandGestureEvent rightGesture)
        {
            Wire();
            _lastLeft = leftFrame;
            _lastRight = rightFrame;

            if (!gesturePosesEnabled || !poseController)
                return;

            MapGesture(leftGesture, SeatedHandSide.Left);
            MapGesture(rightGesture, SeatedHandSide.Right);
        }

        void MapGesture(HandGestureEvent gesture, SeatedHandSide side)
        {
            switch (gesture.type)
            {
                case HandGestureType.Point:
                    poseController.ApplyPose(side == SeatedHandSide.Left ? "LeftArmForward" : "RightArmForward");
                    break;
                case HandGestureType.Open:
                case HandGestureType.Release:
                    poseController.ApplyPose("HandsOnLap");
                    break;
                // Fist / Pinch / Grasp edges keep the current arm pose so the
                // in-game interaction (pushing, hailing) is not visually
                // interrupted; the games drive those poses themselves.
            }
        }

        // ---- host-facing body API -------------------------------------------------

        /// <summary>Seat or stand the avatar. Idle pose is applied on transition.</summary>
        public void SetSeated(bool seated)
        {
            Wire();
            if (poseController)
            {
                string idlePose = seated ? "NaturalSeated" : "Standing";
                poseController.Seated = seated;
                // The Seated setter already cross-applies the mode idle pose; only
                // solve again when the current pose is not already the target idle.
                if (poseController.CurrentPoseName != idlePose)
                    poseController.ApplyPose(idlePose);
            }
        }

        /// <summary>Feed per-frame planar velocity (m/s) and yaw rate (deg/s).</summary>
        public void FeedLocomotion(Vector2 planarVelocity, float yawDeltaPerSecond)
        {
            Wire();
            if (locomotion) locomotion.SetLocomotionInput(planarVelocity, yawDeltaPerSecond);
        }

        /// <summary>First-person mode hides the head renderer only; never the body logic.</summary>
        public void SetFirstPerson(bool firstPerson)
        {
            Wire();
            if (visibility) visibility.SetFirstPerson(firstPerson);
        }

        /// <summary>Realistic ↔ blue-transparent appearance, no motion/collision side effects.</summary>
        public void SetAppearanceMode(FullBodyAppearanceMode mode)
        {
            Wire();
            if (appearance) appearance.SetMode(mode);
        }

        /// <summary>Hide or show the whole avatar and restore neutral pose on hide.</summary>
        public void SetBodyEnabled(bool enabled)
        {
            Wire();
            if (!enabled && poseController) poseController.ResetToNeutral();
            if (locomotion) locomotion.enabled = enabled;
            if (poseController) poseController.enabled = enabled;
            if (visibility) visibility.SetFirstPerson(enabled && visibility.FirstPersonActive);
        }
    }
}
