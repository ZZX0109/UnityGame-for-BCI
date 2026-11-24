using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Optional visual adapter for ControllerHandPair. Shoulder offsets and pole directions are in
    /// metres/body space relative to the explicit bodyOrigin; bodyOrigin scale is ignored. Assign each
    /// output root to the corresponding sleeve group and point transforms to anchors used by the mesh.
    /// Output anchors must be separate from the tracked wrist hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatedArmPreview : MonoBehaviour
    {
        [System.Serializable]
        public struct ArmState
        {
            public bool visible;
            public ControllerHandPair.PoseSource source;
            public bool isMirrored;
            public SeatedArmReachStatus reachStatus;
            public Vector3 shoulderPositionBodyMeters;
            public Vector3 elbowPositionBodyMeters;
            public Vector3 wristPositionBodyMeters;
            public float displayWristSeparationMeters;
        }

        [Header("Source and body-space frame")]
        public ControllerHandPair pair;
        [Tooltip("Explicit head, neck, or body origin. Its rotation defines body space; its scale is ignored.")]
        public Transform bodyOrigin;

        [Header("Arm measurements (metres)")]
        public float upperArmLengthMeters = 0.30f;
        public float lowerArmLengthMeters = 0.27f;
        public Vector3 leftShoulderBodyPositionMeters = new Vector3(-0.18f, -0.28f, 0f);
        public Vector3 rightShoulderBodyPositionMeters = new Vector3(0.18f, -0.28f, 0f);
        [Tooltip("Stable bend direction in body space. Use the same forward/back direction on both sides for mirrored poses.")]
        public Vector3 leftElbowPoleBodyDirection = Vector3.down;
        public Vector3 rightElbowPoleBodyDirection = Vector3.down;

        [Header("Optional sleeve roots and bind points")]
        public GameObject leftOutputRoot;
        public GameObject rightOutputRoot;
        public Transform leftShoulderPoint;
        public Transform leftElbowPoint;
        public Transform leftWristPoint;
        public Transform rightShoulderPoint;
        public Transform rightElbowPoint;
        public Transform rightWristPoint;

        public ArmState LeftArm { get; private set; }
        public ArmState RightArm { get; private set; }

        private bool hasLeftContinuityHint;
        private bool hasRightContinuityHint;
        private Vector3 previousLeftElbowPositionMeters;
        private Vector3 previousRightElbowPositionMeters;

        private void LateUpdate()
        {
            Refresh();
        }

        private void OnDisable()
        {
            SetOutputRootVisible(leftOutputRoot, false);
            SetOutputRootVisible(rightOutputRoot, false);
            hasLeftContinuityHint = false;
            hasRightContinuityHint = false;
            LeftArm = HiddenState(ControllerHandPair.PoseSource.Hidden);
            RightArm = HiddenState(ControllerHandPair.PoseSource.Hidden);
        }

        /// <summary>Refreshes the two sleeve solutions from the pair's current display sources.</summary>
        public void Refresh()
        {
            if (!pair)
            {
                LeftArm = RefreshArm(
                    SeatedHandSide.Left, ControllerHandPair.PoseSource.Hidden, null,
                    leftShoulderBodyPositionMeters, leftElbowPoleBodyDirection,
                    leftShoulderPoint, leftElbowPoint, leftWristPoint, leftOutputRoot,
                    ref hasLeftContinuityHint, ref previousLeftElbowPositionMeters);
                RightArm = RefreshArm(
                    SeatedHandSide.Right, ControllerHandPair.PoseSource.Hidden, null,
                    rightShoulderBodyPositionMeters, rightElbowPoleBodyDirection,
                    rightShoulderPoint, rightElbowPoint, rightWristPoint, rightOutputRoot,
                    ref hasRightContinuityHint, ref previousRightElbowPositionMeters);
                return;
            }

            LeftArm = RefreshArm(
                SeatedHandSide.Left, pair.LeftSource, pair.leftWrist,
                leftShoulderBodyPositionMeters, leftElbowPoleBodyDirection,
                leftShoulderPoint, leftElbowPoint, leftWristPoint, leftOutputRoot,
                ref hasLeftContinuityHint, ref previousLeftElbowPositionMeters);
            RightArm = RefreshArm(
                SeatedHandSide.Right, pair.RightSource, pair.rightWrist,
                rightShoulderBodyPositionMeters, rightElbowPoleBodyDirection,
                rightShoulderPoint, rightElbowPoint, rightWristPoint, rightOutputRoot,
                ref hasRightContinuityHint, ref previousRightElbowPositionMeters);
        }

        private ArmState RefreshArm(
            SeatedHandSide side,
            ControllerHandPair.PoseSource source,
            FirstPersonWrist wrist,
            Vector3 shoulderBodyPositionMeters,
            Vector3 elbowPoleBodyDirection,
            Transform shoulderPoint,
            Transform elbowPoint,
            Transform wristPoint,
            GameObject outputRoot,
            ref bool hasContinuityHint,
            ref Vector3 previousElbowPositionMeters)
        {
            ArmState state = HiddenState(source);
            if (!bodyOrigin || !wrist || source == ControllerHandPair.PoseSource.Hidden ||
                (source != ControllerHandPair.PoseSource.Controller && source != ControllerHandPair.PoseSource.Mirror))
            {
                SetOutputRootVisible(outputRoot, false);
                hasContinuityHint = false;
                return state;
            }

            Vector3 wristBodyPositionMeters = Quaternion.Inverse(bodyOrigin.rotation) *
                                             (wrist.transform.position - bodyOrigin.position);
            SeatedArmSolver.Solution solution;
            bool solved;
            if (hasContinuityHint)
            {
                solved = SeatedArmSolver.TrySolve(
                    shoulderBodyPositionMeters, wristBodyPositionMeters,
                    upperArmLengthMeters, lowerArmLengthMeters, side,
                    elbowPoleBodyDirection, previousElbowPositionMeters, true, out solution);
            }
            else
            {
                solved = SeatedArmSolver.TrySolve(
                    shoulderBodyPositionMeters, wristBodyPositionMeters,
                    upperArmLengthMeters, lowerArmLengthMeters, side,
                    elbowPoleBodyDirection, out solution);
            }

            if (!solved)
            {
                SetOutputRootVisible(outputRoot, false);
                hasContinuityHint = false;
                return state;
            }

            WriteOutputPoint(shoulderPoint, solution.ShoulderPositionMeters, wrist);
            WriteOutputPoint(elbowPoint, solution.ElbowPositionMeters, wrist);
            WriteOutputPoint(wristPoint, solution.WristPositionMeters, wrist);
            SetOutputRootVisible(outputRoot, true);

            state.visible = true;
            state.source = source;
            state.isMirrored = source == ControllerHandPair.PoseSource.Mirror;
            state.reachStatus = solution.ReachStatus;
            state.shoulderPositionBodyMeters = solution.ShoulderPositionMeters;
            state.elbowPositionBodyMeters = solution.ElbowPositionMeters;
            state.wristPositionBodyMeters = solution.WristPositionMeters;
            state.displayWristSeparationMeters = solution.DisplayWristSeparationMeters;

            previousElbowPositionMeters = solution.ElbowPositionMeters;
            hasContinuityHint = true;
            return state;
        }

        private void WriteOutputPoint(Transform point, Vector3 bodyPositionMeters, FirstPersonWrist wrist)
        {
            if (!point || !bodyOrigin || point == bodyOrigin || IsRelatedToWrist(point, wrist))
                return;

            point.position = bodyOrigin.position + bodyOrigin.rotation * bodyPositionMeters;
        }

        private void SetOutputRootVisible(GameObject outputRoot, bool visible)
        {
            if (!outputRoot || outputRoot == gameObject || transform.IsChildOf(outputRoot.transform))
                return;
            if (pair && (RootContainsWrist(outputRoot, pair.leftWrist) || RootContainsWrist(outputRoot, pair.rightWrist)))
                return;
            if (outputRoot.activeSelf != visible)
                outputRoot.SetActive(visible);
        }

        private static bool RootContainsWrist(GameObject root, FirstPersonWrist wrist)
        {
            return wrist && (wrist.transform == root.transform || wrist.transform.IsChildOf(root.transform));
        }

        private static bool IsRelatedToWrist(Transform point, FirstPersonWrist wrist)
        {
            return wrist && (point == wrist.transform ||
                             point.IsChildOf(wrist.transform) ||
                             wrist.transform.IsChildOf(point));
        }

        private static ArmState HiddenState(ControllerHandPair.PoseSource source)
        {
            return new ArmState
            {
                visible = false,
                source = source,
                isMirrored = source == ControllerHandPair.PoseSource.Mirror,
                reachStatus = SeatedArmReachStatus.InvalidMeasurements
            };
        }
    }
}
