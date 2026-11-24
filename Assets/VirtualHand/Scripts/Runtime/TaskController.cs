using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Assisted cooperative task driven by VALID recognized intent, not EEG reach decoding.
    /// The object has one owner at a time; invalid or low-confidence input never forces a
    /// grip or a release. A paused/invalid replay freezes task progress while preserving
    /// whatever is already held.
    /// </summary>
    public partial class TaskController : MonoBehaviour
    {
        public enum TaskType { None, Carry, Handoff }
        public enum Stage { Idle, Approach, Grip, Lift, Follow, Transfer, Place, Done }

        [Header("Scene refs (left/right hand task proxies)")]
        public Transform leftAnchor;
        public Transform rightAnchor;
        public Transform dropZone;
        public Renderer boxVisual;
        public Renderer zoneVisual;
        public ReplayController replay;

        [Header("Grip offsets (world-space hand separation at a two-hand grip)")]
        public Vector3 leftGripOffset = new Vector3(-0.25f, 0f, 0f);
        public Vector3 rightGripOffset = new Vector3(0.25f, 0f, 0f);

        [Header("Task-only visual hand assistance")]
        public HandRigDriver leftHand;
        public HandRigDriver rightHand;
        [Min(0.01f)] public float taskContactRange = 0.18f;
        [Range(0.1f, 1f)] public float leftTaskHoldCurl = 0.55f;
        [Range(0.1f, 1f)] public float rightTaskHoldCurl = 0.55f;
        public Vector3 leftOpenWristOffset;
        public Vector3 rightOpenWristOffset;
        public Vector3 leftTaskWristOffset;
        public Vector3 rightTaskWristOffset;
        public Quaternion leftTaskWristRotation = Quaternion.identity;
        public Quaternion rightTaskWristRotation = Quaternion.identity;

        [Header("Calibrated single-hand wrist placement")]
        public bool leftHandoffFitValid, rightHandoffFitValid;
        public Vector3 leftHandoffWristOffset, rightHandoffWristOffset;
        public Quaternion leftHandoffWristRotation = Quaternion.identity;
        public Quaternion rightHandoffWristRotation = Quaternion.identity;

        public bool HasCalibratedHandoffFit(bool left) => left ? leftHandoffFitValid : rightHandoffFitValid;

        public struct TaskHoldGeometry
        {
            public float meanDistalFaceDistanceMm;
            public float insideVertexFraction;
            public float maxPenetrationMm;
            public float lowestWorldY;
            public float beyondSidePlaneMm;
            public float indexFaceDistanceMm;
            public float middleFaceDistanceMm;
        }

        public struct ThumbPadGeometry
        {
            public int thumbPadVertexCount;
            public int indexPadVertexCount;
            public float closestPadGapMm;
            public float meanNearestPadGapMm;
            public float opposingPadNormals;
            public int thumbContactVertices, indexContactVertices;
            public float maxPadOverlapMm;
            public float adjacentFaceGapMm;
            public float adjacentFaceFacing;
            public int adjacentFace;
            public int topContactVertices;
            public float closestTopGapMm;
        }

        [Header("Motion and acceptance")]
        public float approachSpeed = 1.2f;
        public float dropRadius = 0.6f;
        public float minConfidence = 0.6f;

        public TaskType Type { get; private set; } = TaskType.None;
        public bool LeftIsSender { get; private set; } = true;
        public Stage CurrentStage { get; private set; } = Stage.Idle;
        public string StageLabel => CurrentStage.ToString();

        Vector3[] _path;
        int _pathIdx;
        GameObject _heldObject;
        Rigidbody _heldRb;
        bool _heldRbWasKinematic;
        Vector3 _heldStartPos;
        Quaternion _heldStartRot;
        bool _senderLatched, _receiverLatched, _receiverIsHolder;
        float _restTimer, _receiverStableTimer;
        const float RestStableSeconds = 0.8f;
        const float GripStableSeconds = 0.3f;
        const float ContactTolerance = 0.12f;
        const float WaypointTolerance = 0.08f;

        bool _resetHooked;
        bool _taskAssistChecked, _taskAssistReady, _taskStartCaptured;
        float[] _leftHoldControls = new float[20];
        float[] _rightHoldControls = new float[20];
        float[] _leftAssistControls = new float[20];
        float[] _rightAssistControls = new float[20];
        Vector3 _leftAnchorStartPosition, _rightAnchorStartPosition;
        Quaternion _leftAnchorStartRotation, _rightAnchorStartRotation;
        Vector3 _leftHandStartLocalPosition, _rightHandStartLocalPosition;
        Quaternion _leftHandStartLocalRotation, _rightHandStartLocalRotation;

        public bool ControlsTaskHands => Type != TaskType.None && CurrentStage != Stage.Done && _taskAssistReady;


        public void Begin(TaskType type, Vector3[] path, GameObject target, bool leftIsSender = true)
        {
            End();
            if (target == null) throw new ArgumentNullException(nameof(target));
            EnsureTaskAssistReady();
            CaptureTaskStartState();
            Type = type;
            SetPropsVisible(true);
            LeftIsSender = leftIsSender;
            _path = path;
            _pathIdx = 0;
            _heldObject = target;
            _heldStartPos = target.transform.position;
            _heldStartRot = target.transform.rotation;
            if (target.TryGetComponent<Rigidbody>(out var rb))
            {
                _heldRb = rb;
                _heldRbWasKinematic = rb.isKinematic;
                rb.isKinematic = true;
            }
            CurrentStage = Stage.Approach;
            UpdateTaskHands();
        }

        public void End()
        {
            ReleaseOwnership();
            SetPropsVisible(false);
            if (_heldObject != null)
            {
                if (_heldObject.TryGetComponent<Rigidbody>(out var body))
                {
                    body.position = _heldStartPos;
                    body.rotation = _heldStartRot;
                }
                _heldObject.transform.position = _heldStartPos;
                _heldObject.transform.rotation = _heldStartRot;
            }
            RestoreHandsAfterTask();
            Type = TaskType.None;
            LeftIsSender = true;
            CurrentStage = Stage.Idle;
            _senderLatched = _receiverLatched = _receiverIsHolder = false;
            _path = null;
            _pathIdx = 0;
            _restTimer = _receiverStableTimer = 0f;
            _heldObject = null;
            _heldRb = null;
        }

        void RestoreHandsAfterTask()
        {
            if (!_taskStartCaptured) return;
            if (leftAnchor != null) leftAnchor.SetPositionAndRotation(_leftAnchorStartPosition, _leftAnchorStartRotation);
            if (rightAnchor != null) rightAnchor.SetPositionAndRotation(_rightAnchorStartPosition, _rightAnchorStartRotation);
            if (leftHand != null)
            {
                leftHand.transform.localPosition = _leftHandStartLocalPosition;
                leftHand.transform.localRotation = _leftHandStartLocalRotation;
            }
            if (rightHand != null)
            {
                rightHand.transform.localPosition = _rightHandStartLocalPosition;
                rightHand.transform.localRotation = _rightHandStartLocalRotation;
            }
            _taskStartCaptured = false;
            if (replay != null) replay.RestoreCurrentTargets();
        }

        void CaptureTaskStartState()
        {
            if (leftAnchor != null)
            {
                _leftAnchorStartPosition = leftAnchor.position;
                _leftAnchorStartRotation = leftAnchor.rotation;
            }
            if (rightAnchor != null)
            {
                _rightAnchorStartPosition = rightAnchor.position;
                _rightAnchorStartRotation = rightAnchor.rotation;
            }
            if (leftHand != null)
            {
                _leftHandStartLocalPosition = leftHand.transform.localPosition;
                _leftHandStartLocalRotation = leftHand.transform.localRotation;
            }
            if (rightHand != null)
            {
                _rightHandStartLocalPosition = rightHand.transform.localPosition;
                _rightHandStartLocalRotation = rightHand.transform.localRotation;
            }
            _taskStartCaptured = true;
        }

        bool EnsureTaskAssistReady()
        {
            if (_taskAssistChecked) return _taskAssistReady;
            _taskAssistChecked = true;
            if (leftHand == null && leftAnchor != null) leftHand = leftAnchor.GetComponentInChildren<HandRigDriver>(true);
            if (rightHand == null && rightAnchor != null) rightHand = rightAnchor.GetComponentInChildren<HandRigDriver>(true);
            if (leftHand != null && !leftHand.HasProfile) leftHand.CacheBones();
            if (rightHand != null && !rightHand.HasProfile) rightHand.CacheBones();
            if (leftHand == null || rightHand == null ||
                leftHand.ProfileOpen == null || leftHand.ProfileFist == null ||
                rightHand.ProfileOpen == null || rightHand.ProfileFist == null ||
                leftHand.ProfileOpen.Length != 20 || leftHand.ProfileFist.Length != 20 ||
                rightHand.ProfileOpen.Length != 20 || rightHand.ProfileFist.Length != 20)
                return false;
            BuildHoldControls(leftHand, leftTaskHoldCurl, _leftHoldControls);
            BuildHoldControls(rightHand, rightTaskHoldCurl, _rightHoldControls);
            _taskAssistReady = true;
            return true;
        }

        static void BuildHoldControls(HandRigDriver hand, float curl, float[] output)
        {
            float[] open = hand.ProfileOpen;
            float[] fist = hand.ProfileFist;
            for (int i = 0; i < 20; i++) output[i] = Mathf.Lerp(open[i], fist[i], Mathf.Clamp01(curl));
        }

        /// <summary>Intent is only actionable while the input source is valid and confident.</summary>
        public void OnIntent(string intent, double dt, bool inputValid = true, float confidence = 1f)
        {
            if (Type == TaskType.None || CurrentStage == Stage.Done || _heldObject == null) return;
            if (leftAnchor == null || rightAnchor == null) return;
            bool actionable = inputValid && confidence >= minConfidence;
            if (!actionable)
            {
                _restTimer = _receiverStableTimer = 0f;
                return;
            }
            bool validRest = actionable && intent == "rest";
            _restTimer = validRest && CurrentStage == Stage.Place && InDropZone()
                ? _restTimer + (float)dt
                : 0f;

            switch (CurrentStage)
            {
                case Stage.Approach:
                    if (Type == TaskType.Carry)
                    {
                        MoveAnchorPairTowards(_heldObject.transform.position, dt);
                        if (GripAt(SenderAnchor, SenderGrip()) && GripAt(ReceiverAnchor, ReceiverGrip()) &&
                            ActionableGrip(intent, true, actionable) &&
                            ActionableGrip(intent, false, actionable))
                        {
                            _senderLatched = _receiverLatched = true;
                            _receiverIsHolder = false;
                            AdvanceToNextStageAfterApproach();
                        }
                    }
                    else
                    {
                        MoveAnchorTowards(SenderAnchor, _heldObject.transform.position + SenderGrip(), dt);
                        if (GripAt(SenderAnchor, SenderGrip()) &&
                            ActionableGrip(intent, LeftIsSender, actionable))
                        {
                            _senderLatched = true;
                            _receiverIsHolder = false;
                            CurrentStage = Stage.Lift;
                        }
                    }
                    break;

                case Stage.Lift:
                case Stage.Follow:
                    AdvancePath(dt);
                    break;

                case Stage.Transfer:
                    if (!_receiverLatched)
                    {
                        Vector3 receiverGrip = _heldObject.transform.position + ReceiverGrip();
                        MoveAnchorTowards(ReceiverAnchor, receiverGrip, dt);
                        // GripAt expects the hand-relative grip OFFSET. The
                        // receiverGrip local above is the world-space target;
                        // passing it here would add the object position twice.
                        bool canLatch = GripAt(ReceiverAnchor, ReceiverGrip()) &&
                                        ActionableGrip(intent, !LeftIsSender, actionable);
                        _receiverStableTimer = canLatch ? _receiverStableTimer + (float)dt : 0f;
                        if (_receiverStableTimer >= GripStableSeconds)
                        {
                            _receiverLatched = true;
                            _senderLatched = false;
                            _receiverIsHolder = true;   // receiver alone drives the object now
                            _receiverStableTimer = 0f;
                            CurrentStage = Stage.Follow;
                        }
                    }
                    break;

                case Stage.Place:
                    if (_restTimer >= RestStableSeconds && InDropZone())
                    {
                        ReleaseOwnership();
                        CurrentStage = Stage.Done;
                        RestoreHandsAfterTask();
                    }
                    break;
            }
            UpdateTaskHands();
        }

        void AdvanceToNextStageAfterApproach() => CurrentStage = Stage.Lift;

        bool ActionableGrip(string intent, bool isLeft, bool actionable)
        {
            return actionable && IntentMapper.HandShouldGrip(intent, isLeft);
        }

        void AdvancePath(double dt)
        {
            if (_path == null || _path.Length == 0 || _pathIdx >= _path.Length)
            {
                CurrentStage = Stage.Place;
                return;
            }
            Vector3 target = _path[_pathIdx];
            if (Type == TaskType.Carry)
            {
                MoveAnchorPairTowards(target, dt);
                _heldObject.transform.position = BothAnchorsMid();
            }
            else if (_receiverIsHolder)
            {
                MoveAnchorTowards(ReceiverAnchor, target + ReceiverGrip(), dt);
                _heldObject.transform.position = ReceiverAnchor.position - ReceiverGrip();
            }
            else
            {
                Transform holder = SenderAnchor;
                MoveAnchorTowards(holder, target + SenderGrip(), dt);
                _heldObject.transform.position = holder.position - SenderGrip();
            }
            Vector3 objectMid = _heldObject.transform.position;
            if (Vector3.Distance(objectMid, target) < WaypointTolerance)
            {
                _pathIdx++;
                if (Type == TaskType.Handoff && !_receiverIsHolder &&
                    CurrentStage == Stage.Lift && _pathIdx >= Math.Max(1, _path.Length / 2))
                {
                    CurrentStage = Stage.Transfer;
                    return;
                }
                CurrentStage = _pathIdx >= _path.Length ? Stage.Place : Stage.Follow;
            }
        }

        void MoveAnchorPairTowards(Vector3 worldPos, double dt)
        {
            MoveAnchorTowards(leftAnchor, worldPos + leftGripOffset, dt);
            MoveAnchorTowards(rightAnchor, worldPos + rightGripOffset, dt);
        }

        void MoveAnchorTowards(Transform anchor, Vector3 target, double dt)
        {
            anchor.position = Vector3.MoveTowards(anchor.position, target,
                approachSpeed * Mathf.Max(0f, (float)dt));
        }

        Vector3 BothAnchorsMid() => (leftAnchor.position + rightAnchor.position) * 0.5f;
        Transform SenderAnchor => LeftIsSender ? leftAnchor : rightAnchor;
        Transform ReceiverAnchor => LeftIsSender ? rightAnchor : leftAnchor;
        Vector3 SenderGrip() => LeftIsSender ? leftGripOffset : rightGripOffset;
        Vector3 ReceiverGrip() => LeftIsSender ? rightGripOffset : leftGripOffset;

        bool GripAt(Transform anchor, Vector3 grip) =>
            Vector3.Distance(anchor.position, _heldObject.transform.position + grip) < ContactTolerance;

        bool InDropZone() => dropZone != null &&
            Vector3.Distance(_heldObject.transform.position, dropZone.position) <= dropRadius;

        void SetPropsVisible(bool visible)
        {
            if (boxVisual != null) boxVisual.enabled = visible;
            if (zoneVisual != null) zoneVisual.enabled = visible;
        }

        void ReleaseOwnership()
        {
            if (_heldRb != null) _heldRb.isKinematic = _heldRbWasKinematic;
            _heldRb = null;
        }

        public void ReceiverGripLatch() => _receiverLatched = true;

        public void SenderReleaseAfterReceiverSecure()
        {
            if (!_receiverLatched) return;
            _senderLatched = false;
            _receiverIsHolder = true;
            if (CurrentStage == Stage.Transfer) CurrentStage = Stage.Follow;
        }

        void Update()
        {
            if (replay == null) return;
            if (!_resetHooked)
            {
                replay.OnReset += End;
                _resetHooked = true;
            }
            if (Type == TaskType.None || replay.source == null || !replay.source.IsPlaying ||
                replay.CurrentSource == "manual") return;
            OnIntent(replay.CurrentIntent, Time.deltaTime,
                     replay.HasValidInput, replay.CurrentConfidence);
        }

        float TaskAssistFactor(bool isLeftHand)
        {
            if (CurrentStage == Stage.Done) return 0f;
            bool latched = Type == TaskType.Carry
                ? (isLeftHand ? _senderLatched : _receiverLatched)
                : (isLeftHand == LeftIsSender ? _senderLatched : _receiverLatched);
            if (latched) return 1f;
            if (CurrentStage != Stage.Transfer || isLeftHand == LeftIsSender) return 0f;
            Transform anchor = isLeftHand ? leftAnchor : rightAnchor;
            Vector3 grip = isLeftHand ? leftGripOffset : rightGripOffset;
            if (anchor == null || _heldObject == null) return 0f;
            float distance = Vector3.Distance(anchor.position, _heldObject.transform.position + grip);
            float t = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, taskContactRange));
            return t * t * (3f - 2f * t);
        }

        void ApplyTaskAssist(HandRigDriver hand, bool isLeftHand, float factor)
        {
            if (hand == null) return;
            float[] open = hand.ProfileOpen;
            if (open == null || open.Length != 20) return;
            float[] hold = isLeftHand ? _leftHoldControls : _rightHoldControls;
            float[] target = isLeftHand ? _leftAssistControls : _rightAssistControls;
            Vector3 offset = isLeftHand ? leftTaskWristOffset : rightTaskWristOffset;
            Quaternion rotation = isLeftHand ? leftTaskWristRotation : rightTaskWristRotation;
            if (Type == TaskType.Handoff && HasCalibratedHandoffFit(isLeftHand))
            {
                offset = isLeftHand ? leftHandoffWristOffset : rightHandoffWristOffset;
                rotation = isLeftHand ? leftHandoffWristRotation : rightHandoffWristRotation;
            }
            Vector3 startPosition = isLeftHand ? _leftHandStartLocalPosition : _rightHandStartLocalPosition;
            Quaternion startRotation = isLeftHand ? _leftHandStartLocalRotation : _rightHandStartLocalRotation;
            for (int i = 0; i < 20; i++) target[i] = Mathf.Lerp(open[i], hold[i], factor);
            hand.SetControls(target);
            Vector3 openOffset = isLeftHand ? leftOpenWristOffset : rightOpenWristOffset;
            hand.transform.localPosition = startPosition + Vector3.Lerp(openOffset, offset, factor);
            hand.transform.localRotation = startRotation * Quaternion.Slerp(Quaternion.identity, rotation, factor);
            if (Type == TaskType.Handoff && CurrentStage == Stage.Transfer && isLeftHand != LeftIsSender &&
                HasCalibratedHandoffFit(isLeftHand))
            {
                // The wrist rotates around the box edge while the receiver closes its fingers.
                Vector3 outside = (isLeftHand ? leftGripOffset : rightGripOffset).normalized;
                float arc = 4f * factor * (1f - factor);
                hand.transform.position += (outside * 0.10f + Vector3.up * 0.06f) * arc;
            }
            if (Type == TaskType.Carry)
                hand.transform.position += Vector3.up * (factor * 0.003f);
        }

        void UpdateTaskHands()
        {
            if (!ControlsTaskHands || _heldObject == null) return;
            ApplyTaskAssist(leftHand, true, TaskAssistFactor(true));
            ApplyTaskAssist(rightHand, false, TaskAssistFactor(false));
        }

        void OnDestroy()
        {
            if (replay != null && _resetHooked) replay.OnReset -= End;
        }
    }
}
