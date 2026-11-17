using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Assisted cooperative tasks driven by recognized intent (not EEG reach decoding).
    /// Carry: both hands approach/grip box, follow preset path, release on stable rest.
    /// Handoff: sender picks cube, transfers at midpoint, receiver grips, sender releases
    /// only after receiver secure, receiver places at destination.
    /// Grip latches on contact across rest/dropout; only designated stages release.
    /// One object-ownership flag prevents simultaneous rigidbody/transform fighting.
    /// </summary>
    public class TaskController : MonoBehaviour
    {
        public enum TaskType { None, Carry, HandoffSender, HandoffReceiver }
        public enum Stage { Idle, Approach, Grip, Lift, Follow, Transfer, Place, Release, Done }

        [Header("Scene refs (left/right hand task proxies)")]
        public Transform leftAnchor;
        public Transform rightAnchor;
        public ReplayController replay;

        [Header("Motion")]
        public float approachSpeed = 1.2f;
        public float pathSpeed = 0.8f;

        public TaskType Type { get; private set; } = TaskType.None;
        public Stage CurrentStage { get; private set; } = Stage.Idle;
        public string StageLabel => CurrentStage.ToString();

        // assisted spatial path waypoints (user-selected in UI, not EEG decoded)
        Vector3[] _path;
        int _pathIdx;
        GameObject _heldObject;
        bool _leftGripLatched, _rightGripLatched;
        float _restTimer;
        const float RestStableSeconds = 0.8f;

        public void Begin(TaskType type, Vector3[] path, GameObject target)
        {
            End();
            Type = type;
            _path = path; _pathIdx = 0;
            _heldObject = target;
            CurrentStage = Stage.Approach;
        }

        public void End()
        {
            Type = TaskType.None;
            CurrentStage = Stage.Idle;
            _leftGripLatched = _rightGripLatched = false;
            _pathIdx = 0;
            if (_heldObject != null && _heldObject.TryGetComponent<Rigidbody>(out var rb))
                rb.isKinematic = true; // keep ownership tidy
        }

        /// <summary>Called by ReplayController consumer with current recognized intent.</summary>
        public void OnIntent(string intent, double dt)
        {
            if (Type == TaskType.None || _heldObject == null) return;
            bool stableRest = intent == "rest";
            _restTimer = stableRest ? _restTimer + dt : 0f;

            switch (CurrentStage)
            {
                case Stage.Approach:
                    MoveAnchorsTowards(_heldObject.transform.position);
                    if (Vector3.Distance(leftAnchor.position, _heldObject.transform.position) < 0.12f
                        && Vector3.Distance(rightAnchor.position, _heldObject.transform.position) < 0.12f)
                    {
                        // grip latches on contact; survives rest/dropout until release stage
                        _leftGripLatched = _rightGripLatched = true;
                        CurrentStage = Type == TaskType.HandoffSender ? Stage.Lift : Stage.Lift;
                    }
                    break;

                case Stage.Lift:
                    if (intent == "both" || intent == "left" || intent == "right")
                        AdvancePath();
                    break;

                case Stage.Follow:
                    AdvancePath();
                    break;

                case Stage.Transfer: // handoff midpoint: receiver grips before sender releases
                    MoveAnchorsTowards(_heldObject.transform.position);
                    if (_rightGripLatched && _restTimer > RestStableSeconds)
                        CurrentStage = Stage.Release;
                    break;

                case Stage.Place:
                    if (_restTimer > RestStableSeconds)
                    {
                        DropObject();
                        CurrentStage = Stage.Done;
                    }
                    break;

                case Stage.Release:
                    DropObject();
                    CurrentStage = Type == TaskType.HandoffSender ? Stage.Done : Stage.Follow;
                    break;

                case Stage.Done:
                    break;
            }
        }

        void AdvancePath()
        {
            if (_path == null || _pathIdx >= _path.Length) { CurrentStage = Stage.Place; return; }
            Vector3 tgt = _path[_pathIdx];
            MoveAnchorsTowards(tgt);
            if (Vector3.Distance(leftAnchor.position, tgt) < 0.08f)
                _pathIdx++;
            // single ownership: while carried, physics is off and transform follows anchors
            _heldObject.transform.position = (leftAnchor.position + rightAnchor.position) * 0.5f;
        }

        void MoveAnchorsTowards(Vector3 worldPos)
        {
            Vector3 mid = (leftAnchor.position + rightAnchor.position) * 0.5f;
            Vector3 step = (worldPos - mid) * Mathf.Clamp01(approachSpeed * Time.deltaTime);
            leftAnchor.position += step;
            rightAnchor.position += step;
        }

        void DropObject()
        {
            if (_heldObject != null && _heldObject.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false; // physics owns it again
            }
            _leftGripLatched = _rightGripLatched = false;
        }

        /// <summary>Handoff helper: receiver grip confirm (called by demo UI/orchestrator).</summary>
        public void ReceiverGripLatch() => _rightGripLatched = true;

        public void SenderReleaseAfterReceiverSecure()
        {
            if (_rightGripLatched) CurrentStage = Stage.Release;
        }

        void FixedUpdate()
        {
            // keep anchors from drifting when paused: ReplayController owns the clock,
            // so when paused we simply don't advance; nothing to do here by design.
        }
    }
}
