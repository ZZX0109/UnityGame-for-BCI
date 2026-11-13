using System;
using UnityEngine;

namespace StreetCrossing.Player
{
    public struct RoadObservationEvidence
    {
        public bool atKerb, beforeEntry, leftTargetVisible, rightTargetVisible;
        public float yaw, pitch;
    }
    public sealed class ObservationTracker
    {
        private float leftTime, rightTime;
        private readonly float dwellSeconds;
        public bool Left { get; private set; }
        public bool Right { get; private set; }
        public event Action<bool> Observed;
        public ObservationTracker(float dwell = 0.35f) { dwellSeconds = Mathf.Max(0.2f, dwell); }
        public void Tick(RoadObservationEvidence evidence, float dt)
        {
            if (dt <= 0 || !evidence.atKerb || !evidence.beforeEntry) { leftTime = rightTime = 0; return; }
            float yaw = Mathf.DeltaAngle(0, evidence.yaw);
            bool horizontalView = Mathf.Abs(evidence.pitch) <= 35;
            leftTime = horizontalView && evidence.leftTargetVisible && yaw <= -45 && yaw >= -125 ? leftTime + dt : 0;
            rightTime = horizontalView && evidence.rightTargetVisible && yaw >= 45 && yaw <= 125 ? rightTime + dt : 0;
            if (!Left && leftTime >= dwellSeconds) { Left = true; Observed?.Invoke(true); }
            if (!Right && rightTime >= dwellSeconds) { Right = true; Observed?.Invoke(false); }
        }
        /// <summary>Legacy callers must supply actual visibility evidence; yaw alone is deliberately insufficient.</summary>
        public void Tick(float yaw, bool atKerb, float dt) { Tick(new RoadObservationEvidence { yaw = yaw, atKerb = atKerb }, dt); }
    }
    public static class RoadObservationVisibility
    {
        public static bool TargetVisible(Camera camera, Vector3[] targets, Transform playerRoot, Transform visibleTargetRoot = null)
        {
            if (camera == null || targets == null) return false;
            foreach (Vector3 target in targets)
            {
                Vector3 viewport = camera.WorldToViewportPoint(target);
                if (viewport.z <= camera.nearClipPlane || viewport.x < 0.06f || viewport.x > 0.94f || viewport.y < 0.08f || viewport.y > 0.92f) continue;
                Vector3 direction = target - camera.transform.position;
                if (direction.sqrMagnitude > 1600 || direction.sqrMagnitude < 1) continue;
                RaycastHit[] hits = Physics.RaycastAll(camera.transform.position, direction.normalized, direction.magnitude - 0.15f, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                foreach (RaycastHit hit in hits) if (!hit.transform.IsChildOf(playerRoot) && (visibleTargetRoot == null || !hit.transform.IsChildOf(visibleTargetRoot))) { blocked = true; break; }
                if (!blocked) return true;
            }
            return false;
        }
    }
}
