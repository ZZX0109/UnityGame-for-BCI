using System;
using UnityEngine;
using Neuralife.VirtualHand;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// Drives whole-avatar arm poses in body space.  Body space is metres with the origin
    /// at the seated pelvis centre, +Y up, +X the wearer's right and +Z forward.
    /// Per-pose wrist targets are turned into shoulder/elbow/wrist chains by the existing
    /// pure <see cref="SeatedArmSolver"/>; this component only feeds inputs and keeps the
    /// previous elbow per arm as the explicit continuity hint, so identical inputs always
    /// reproduce identical solves.  The solver itself stays stateless.  Arms only: the legs
    /// and pelvis are not animated here — <see cref="LowerBodyPoseController"/> integrates
    /// planar locomotion and yaw, and no skeletal leg animation is driven by this package.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Body/Full Body Pose Controller")]
    [DisallowMultipleComponent]
    public sealed class FullBodyPoseController : MonoBehaviour
    {
        public const string PoseLeftArmForward = "LeftArmForward";
        public const string PoseRightArmForward = "RightArmForward";
        public const string PoseHandsOnLap = "HandsOnLap";
        public const string PoseNaturalSeated = "NaturalSeated";
        public const string PoseStanding = "Standing";
        public const string PoseBothArmsForward = "BothArmsForward";

        static readonly string[] KnownPoseNames =
        {
            PoseLeftArmForward, PoseRightArmForward, PoseHandsOnLap, PoseNaturalSeated, PoseStanding,
            PoseBothArmsForward
        };

        [Header("Rig mapping (optional)")]
        [Tooltip("Transform defining body space (origin at the seated pelvis centre, +Y up, +X the wearer's right, +Z forward). Defaults to this component's transform.")]
        public Transform bodySpace;
        [Tooltip("Optional elbow transforms driven with solved body-space positions.")]
        public Transform leftElbow;
        public Transform rightElbow;
        [Tooltip("Optional wrist transforms driven with solved body-space positions.")]
        public Transform leftWrist;
        public Transform rightWrist;

        [Header("Solver")]
        [Tooltip("Body-space direction that keeps the elbow bend hemisphere stable.")]
        public Vector3 stableElbowPole = Vector3.down;

        [Header("Transitions")]
        [Tooltip("When enabled, ApplyPose solves the new pose without snapping the bound transforms; call BlendTowardCurrent every frame to glide them toward the latest solution.")]
        public bool blendPoseTransitions = false;

        bool _seated = true;
        string _currentPoseName = PoseNaturalSeated;
        Vector3 _leftPreviousElbow;
        Vector3 _rightPreviousElbow;
        bool _hasLeftPreviousElbow;
        bool _hasRightPreviousElbow;
        SeatedArmSolver.Solution _leftSolution;
        SeatedArmSolver.Solution _rightSolution;

        /// <summary>Seat or stand the avatar.  Crossing modes auto-applies the mode's idle pose.</summary>
        public bool Seated
        {
            get { return _seated; }
            set
            {
                if (_seated == value)
                    return;
                _seated = value;
                if (_currentPoseName == PoseStanding && _seated)
                    ApplyPose(PoseNaturalSeated);
                else if (_currentPoseName == PoseNaturalSeated && !_seated)
                    ApplyPose(PoseStanding);
            }
        }

        /// <summary>Canonical name of the currently applied pose.</summary>
        public string CurrentPoseName { get { return _currentPoseName; } }

        /// <summary>
        /// Applies a known pose and re-solves both arm chains.  Unknown names are rejected
        /// without changing the current pose.  Known names: LeftArmForward, RightArmForward,
        /// HandsOnLap, NaturalSeated, Standing and BothArmsForward (case-insensitive).
        /// </summary>
        public bool ApplyPose(string poseName)
        {
            string resolved = ResolvePoseName(poseName);
            if (resolved == null)
            {
                Debug.LogWarning("[Body] FullBodyPoseController received unknown pose '" + poseName +
                                 "'; keeping '" + _currentPoseName + "'.");
                return false;
            }

            _currentPoseName = resolved;
            SolveCurrentPose();
            return true;
        }

        /// <summary>Restores the idle pose of the current seat mode and drops the continuity history.</summary>
        public void ResetToNeutral()
        {
            _hasLeftPreviousElbow = false;
            _hasRightPreviousElbow = false;
            ApplyPose(_seated ? PoseNaturalSeated : PoseStanding);
        }

        /// <summary>Latest solve for one arm; returns false until the first successful ApplyPose.</summary>
        public bool TryGetSolvedArm(SeatedHandSide side, out SeatedArmSolver.Solution solution)
        {
            solution = side == SeatedHandSide.Left ? _leftSolution : _rightSolution;
            return solution.IsValid;
        }

        /// <summary>Body-space shoulder position for one arm, from the presentation-default calibration.</summary>
        public static Vector3 GetBodySpaceShoulder(SeatedHandSide side)
        {
            AvatarBodyCalibration calibration = AvatarBodyCalibration.CreatePresentationDefaults();
            float sideSign = side == SeatedHandSide.Left ? -1f : 1f;
            return new Vector3(sideSign * calibration.shoulderWidth.meters * 0.5f, calibration.torsoHeight.meters, 0f);
        }

        /// <summary>Body-space wrist target of a pose for one arm.  One-sided forward poses keep the other arm on NaturalSeated.</summary>
        public static Vector3 GetBodySpaceWristTarget(string poseName, SeatedHandSide side)
        {
            float sideSign = side == SeatedHandSide.Left ? -1f : 1f;
            switch (ResolvePoseName(poseName))
            {
                case PoseStanding:
                    return new Vector3(sideSign * 0.17f, 0.16f, 0.20f);
                case PoseHandsOnLap:
                    return new Vector3(sideSign * 0.12f, 0.18f, 0.32f);
                case PoseBothArmsForward:
                    return new Vector3(sideSign * 0.18f, 0.50f, 0.52f);
                case PoseLeftArmForward:
                    return side == SeatedHandSide.Left
                        ? new Vector3(-0.18f, 0.50f, 0.52f)
                        : GetBodySpaceWristTarget(PoseNaturalSeated, side);
                case PoseRightArmForward:
                    return side == SeatedHandSide.Right
                        ? new Vector3(0.18f, 0.50f, 0.52f)
                        : GetBodySpaceWristTarget(PoseNaturalSeated, side);
                case PoseNaturalSeated:
                default:
                    return new Vector3(sideSign * 0.16f, 0.28f, 0.18f);
            }
        }

        static string ResolvePoseName(string poseName)
        {
            if (string.IsNullOrEmpty(poseName))
                return null;
            for (int i = 0; i < KnownPoseNames.Length; i++)
            {
                if (string.Equals(KnownPoseNames[i], poseName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return KnownPoseNames[i];
            }
            return null;
        }

        void SolveCurrentPose()
        {
            AvatarBodyCalibration calibration = AvatarBodyCalibration.CreatePresentationDefaults();
            float upperArmLength = calibration.upperArmLength.meters;
            float lowerArmLength = calibration.forearmLength.meters;
            Vector3 leftShoulder = GetBodySpaceShoulder(SeatedHandSide.Left);
            Vector3 rightShoulder = GetBodySpaceShoulder(SeatedHandSide.Right);

            _leftSolution = SolveArm(SeatedHandSide.Left, leftShoulder, upperArmLength, lowerArmLength,
                ref _leftPreviousElbow, ref _hasLeftPreviousElbow);
            _rightSolution = SolveArm(SeatedHandSide.Right, rightShoulder, upperArmLength, lowerArmLength,
                ref _rightPreviousElbow, ref _hasRightPreviousElbow);
        }

        SeatedArmSolver.Solution SolveArm(
            SeatedHandSide side,
            Vector3 shoulderPosition,
            float upperArmLength,
            float lowerArmLength,
            ref Vector3 previousElbow,
            ref bool hasPreviousElbow)
        {
            SeatedArmSolver.Solution solution;
            Vector3 wristTarget = GetBodySpaceWristTarget(_currentPoseName, side);
            bool solved = SeatedArmSolver.TrySolve(
                shoulderPosition,
                wristTarget,
                upperArmLength,
                lowerArmLength,
                side,
                stableElbowPole,
                previousElbow,
                hasPreviousElbow,
                out solution);

            if (solved && solution.IsValid)
            {
                previousElbow = solution.ElbowPositionMeters;
                hasPreviousElbow = true;
            }

            ApplyToRig(side, solution);
            return solution;
        }

        void ApplyToRig(SeatedHandSide side, SeatedArmSolver.Solution solution)
        {
            if (!solution.IsValid)
                return;
            if (blendPoseTransitions)
                return;
            Transform space = bodySpace != null ? bodySpace : transform;
            Transform elbow = side == SeatedHandSide.Left ? leftElbow : rightElbow;
            Transform wrist = side == SeatedHandSide.Left ? leftWrist : rightWrist;
            if (elbow != null)
                elbow.position = space.TransformPoint(solution.ElbowPositionMeters);
            if (wrist != null)
                wrist.position = space.TransformPoint(solution.SolvedWristPositionMeters);
        }

        /// <summary>
        /// Glides the bound elbow/wrist transforms toward the latest solved pose.  Returns false
        /// when <paramref name="deltaTime"/> is not positive or <see cref="blendPoseTransitions"/>
        /// is off; otherwise applies one exponential smoothing step (τ = 0.15 s) and returns true.
        /// </summary>
        public bool BlendTowardCurrent(float deltaTime)
        {
            if (deltaTime <= 0f || !blendPoseTransitions)
                return false;
            float k = 1f - Mathf.Exp(-deltaTime / 0.15f);
            Transform space = bodySpace != null ? bodySpace : transform;
            BlendArm(_leftSolution, leftElbow, leftWrist, k, space);
            BlendArm(_rightSolution, rightElbow, rightWrist, k, space);
            return true;
        }

        void BlendArm(SeatedArmSolver.Solution solution, Transform elbow, Transform wrist, float k, Transform space)
        {
            if (!solution.IsValid)
                return;
            if (elbow != null)
            {
                Vector3 elbowTarget = space.TransformPoint(solution.ElbowPositionMeters);
                elbow.position = elbow.position + (elbowTarget - elbow.position) * k;
            }
            if (wrist != null)
            {
                Vector3 wristTarget = space.TransformPoint(solution.SolvedWristPositionMeters);
                wrist.position = wrist.position + (wristTarget - wrist.position) * k;
            }
        }
    }
}
