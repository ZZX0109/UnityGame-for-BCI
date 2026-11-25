using System;
using UnityEngine;
using Neuralife.VirtualHand;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// Drives whole-avatar arm poses in body space.  Body space is metres with the origin
    /// at the seated pelvis centre, +Y up, +X the wearer's right and +Z forward.
    /// Skeletal rigs use measured segment lengths and joint rotations. The pelvis
    /// supplies the origin; the avatar root supplies the anatomical axes. Independent
    /// preview markers continue to use their explicit body-space transform.
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

        public const string PoseBothArmsRaised = "BothArmsRaised";
        public const string PoseArmsCrossed = "ArmsCrossed";
        public const string PoseDesktopHandsVisible = "DesktopHandsVisible";

        static readonly string[] KnownPoseNames =
        {
            PoseLeftArmForward, PoseRightArmForward, PoseHandsOnLap, PoseNaturalSeated, PoseStanding,
            PoseBothArmsForward, PoseBothArmsRaised, PoseArmsCrossed, PoseDesktopHandsVisible
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
        [Tooltip("Optional upper arm transforms rotated so the bone aims along the solved shoulder-to-elbow direction.")]
        public Transform leftUpperArm;
        public Transform rightUpperArm;

        [Header("Solver")]
        [Tooltip("Body-space direction that keeps the elbow bend hemisphere stable.")]
        public Vector3 stableElbowPole = Vector3.down;

        [Header("Transitions")]
        [Tooltip("When enabled, ApplyPose solves the new pose without snapping the bound transforms; call BlendTowardCurrent every frame to glide them toward the latest solution.")]
        public bool blendPoseTransitions = false;

        sealed class ArmRest
        {
            public Transform upper, elbow;
            public Quaternion upperLocal, elbowLocal;
        }

        readonly ArmRest _leftRest = new ArmRest();
        readonly ArmRest _rightRest = new ArmRest();
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
        /// HandsOnLap, NaturalSeated, Standing, BothArmsForward, BothArmsRaised, ArmsCrossed
        /// and DesktopHandsVisible.
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

            if (_currentPoseName != resolved)
                _hasLeftPreviousElbow = _hasRightPreviousElbow = false;
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
                case PoseDesktopHandsVisible:
                    // With the camera at the avatar's eye centre these wrists frame
                    // the lower corners while leaving the crossing ahead unobstructed.
                    return new Vector3(sideSign * 0.185f, 0.585f, 0.45f);
                case PoseBothArmsRaised:
                    return new Vector3(sideSign * 0.22f, 0.99f, 0.12f);
                case PoseArmsCrossed:
                    return new Vector3(-sideSign * 0.12f, 0.39f + sideSign * 0.035f, 0.22f);
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
            // Prefabs serialized before the upper-arm fields were added still have both
            // lower-arm bindings. Resolve their known FBX parents before applying a pose.
            if (!leftUpperArm && leftElbow && leftElbow.parent && leftElbow.parent.name == "upperarm_l")
                leftUpperArm = leftElbow.parent;
            if (!rightUpperArm && rightElbow && rightElbow.parent && rightElbow.parent.name == "upperarm_r")
                rightUpperArm = rightElbow.parent;
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

        public bool ApplyTrackedWrist(SeatedHandSide side, Vector3 worldPosition)
        {
            bool left = side == SeatedHandSide.Left;
            Transform upper = left ? leftUpperArm : rightUpperArm;
            Transform elbow = left ? leftElbow : rightElbow;
            Transform wrist = left ? leftWrist : rightWrist;
            if (!upper || !IsBoneChain(elbow, wrist) || !elbow.IsChildOf(upper)) return false;
            Quaternion toBody = Quaternion.Inverse(transform.rotation);
            if (!SeatedArmSolver.TrySolve(toBody * (upper.position - PelvisOrigin),
                toBody * (worldPosition - PelvisOrigin), Vector3.Distance(upper.position, elbow.position),
                Vector3.Distance(elbow.position, wrist.position), side, stableElbowPole,
                out var solution)) return false;
            if (left) _leftSolution = solution;
            else _rightSolution = solution;
            BlendArm(side, solution, elbow, wrist, 1f, bodySpace != null ? bodySpace : transform);
            return true;
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
            Transform upper = side == SeatedHandSide.Left ? leftUpperArm : rightUpperArm;
            Transform elbow = side == SeatedHandSide.Left ? leftElbow : rightElbow;
            Transform wrist = side == SeatedHandSide.Left ? leftWrist : rightWrist;
            if (IsBoneChain(elbow, wrist) && upper != null && elbow.IsChildOf(upper))
            {
                shoulderPosition = Quaternion.Inverse(transform.rotation) * (upper.position - PelvisOrigin);
                upperArmLength = Vector3.Distance(upper.position, elbow.position);
                lowerArmLength = Vector3.Distance(elbow.position, wrist.position);
                if (_currentPoseName == PoseStanding)
                {
                    float outward = side == SeatedHandSide.Left ? -0.065f : 0.065f;
                    wristTarget = shoulderPosition + new Vector3(outward, -0.96f * (upperArmLength + lowerArmLength), 0.045f);
                }
            }
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
            BlendArm(side, solution, side == SeatedHandSide.Left ? leftElbow : rightElbow,
                side == SeatedHandSide.Left ? leftWrist : rightWrist, 1f, space);
        }

        /// <summary>
        /// Glides the bound elbow/wrist transforms toward the latest solved pose and blends the
        /// bound upper arm bones toward the solved shoulder-to-elbow alignment.  Returns false
        /// when <paramref name="deltaTime"/> is not positive or <see cref="blendPoseTransitions"/>
        /// is off; otherwise applies one exponential smoothing step (τ = 0.15 s) and returns true.
        /// </summary>
        public bool BlendTowardCurrent(float deltaTime)
        {
            if (deltaTime <= 0f || !blendPoseTransitions)
                return false;
            float k = 1f - Mathf.Exp(-deltaTime / 0.15f);
            Transform space = bodySpace != null ? bodySpace : transform;
            BlendArm(SeatedHandSide.Left, _leftSolution, leftElbow, leftWrist, k, space);
            BlendArm(SeatedHandSide.Right, _rightSolution, rightElbow, rightWrist, k, space);
            return true;
        }

        void BlendArm(
            SeatedHandSide side,
            SeatedArmSolver.Solution solution,
            Transform elbow,
            Transform wrist,
            float k,
            Transform space)
        {
            if (!solution.IsValid)
                return;
            Transform upper = side == SeatedHandSide.Left ? leftUpperArm : rightUpperArm;
            if (IsBoneChain(elbow, wrist))
            {
                if (upper == null || !elbow.IsChildOf(upper)) return;
                Vector3 targetElbow = PelvisOrigin + transform.rotation * solution.ElbowPositionMeters;
                Vector3 targetWrist = PelvisOrigin + transform.rotation * solution.SolvedWristPositionMeters;
                // Solve from the authored axes every time. Chaining shortest-arc swings
                // across unrelated poses otherwise accumulates roll in the sleeve and palm.
                var rest = side == SeatedHandSide.Left ? _leftRest : _rightRest;
                Quaternion upperBefore = upper.localRotation;
                Quaternion elbowBefore = elbow.localRotation;
                if (rest.upper != upper || rest.elbow != elbow)
                {
                    rest.upper = upper;
                    rest.elbow = elbow;
                    rest.upperLocal = upperBefore;
                    rest.elbowLocal = elbowBefore;
                }
                upper.localRotation = rest.upperLocal;
                elbow.localRotation = rest.elbowLocal;
                AimBone(upper, elbow, targetElbow - upper.position, 1f);
                AimBone(elbow, wrist, targetWrist - targetElbow, 1f);
                upper.localRotation = Quaternion.Slerp(upperBefore, upper.localRotation, k);
                elbow.localRotation = Quaternion.Slerp(elbowBefore, elbow.localRotation, k);
                return;
            }
            // Independent target markers used by unskinned preview rigs.
            if (upper != null && elbow != null)
                AimBone(upper, elbow, space.TransformDirection(solution.ElbowPositionMeters - solution.ShoulderPositionMeters), k);
            if (elbow != null && (upper == null || !elbow.IsChildOf(upper)))
                elbow.position = Vector3.Lerp(elbow.position, space.TransformPoint(solution.ElbowPositionMeters), k);
            if (wrist != null)
                wrist.position = Vector3.Lerp(wrist.position, space.TransformPoint(solution.SolvedWristPositionMeters), k);
        }

        Vector3 PelvisOrigin => bodySpace != null ? bodySpace.position : transform.position;

        static bool IsBoneChain(Transform elbow, Transform wrist)
        {
            return elbow != null && wrist != null && wrist.IsChildOf(elbow);
        }

        static void AimBone(Transform joint, Transform child, Vector3 direction, float weight)
        {
            Vector3 axis = child.position - joint.position;
            if (axis.sqrMagnitude < 1e-12f || direction.sqrMagnitude < 1e-12f) return;
            Quaternion rotation = Quaternion.FromToRotation(axis, direction) * joint.rotation;
            joint.rotation = Quaternion.Slerp(joint.rotation, rotation, weight);
        }
    }
}
