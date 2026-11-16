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
        private float clock, leftLastSeen = float.NegativeInfinity, rightLastSeen = float.NegativeInfinity;
        private readonly float dwellSeconds;
        public bool Left { get; private set; }
        public bool Right { get; private set; }
        public event Action<bool> Observed;
        public bool NeedsRefresh(float maximumAge) => (Left && clock - leftLastSeen > maximumAge) || (Right && clock - rightLastSeen > maximumAge);
        public ObservationTracker(float dwell = 0.35f) { dwellSeconds = Mathf.Max(0.2f, dwell); }
        public void Tick(RoadObservationEvidence evidence, float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt)) return;
            clock += Mathf.Max(0, dt);
            if (dt <= 0 || !evidence.atKerb || !evidence.beforeEntry) { leftTime = rightTime = 0; return; }
            float yaw = Mathf.DeltaAngle(0, evidence.yaw);
            bool horizontalView = Mathf.Abs(evidence.pitch) <= 35;
            leftTime = horizontalView && evidence.leftTargetVisible && yaw <= -45 && yaw >= -125 ? leftTime + dt : 0;
            rightTime = horizontalView && evidence.rightTargetVisible && yaw >= 45 && yaw <= 125 ? rightTime + dt : 0;
            if (leftTime >= dwellSeconds) { leftLastSeen = clock; if (!Left) { Left = true; Observed?.Invoke(true); } }
            if (rightTime >= dwellSeconds) { rightLastSeen = clock; if (!Right) { Right = true; Observed?.Invoke(false); } }
        }
        /// <summary>Legacy callers must supply actual visibility evidence; yaw alone is deliberately insufficient.</summary>
        public void Tick(float yaw, bool atKerb, float dt) { Tick(new RoadObservationEvidence { yaw = yaw, atKerb = atKerb }, dt); }
    }
    public static class RoadObservationVisibility
    {
        private static RaycastHit[] hits = new RaycastHit[16];
        public static bool TargetVisible(Camera camera, Vector3[] targets, Transform playerRoot, Transform visibleTargetRoot = null)
        {
            if (camera == null || targets == null) return false;
            foreach (Vector3 target in targets)
                if (PointVisible(camera, target, playerRoot, visibleTargetRoot)) return true;
            return false;
        }
        public static bool PointVisible(Camera camera, Vector3 target, Transform playerRoot, Transform visibleTargetRoot = null)
        {
                if (camera == null || playerRoot == null) return false;
                Vector3 viewport = camera.WorldToViewportPoint(target);
                if (viewport.z <= camera.nearClipPlane || viewport.x < 0.06f || viewport.x > 0.94f || viewport.y < 0.08f || viewport.y > 0.92f) return false;
                Vector3 direction = target - camera.transform.position;
                if (direction.sqrMagnitude > 1600 || direction.sqrMagnitude < 1) return false;
                int count;
                // A full buffer is ambiguous: grow and retry rather than silently hiding an occluder.
                while ((count = Physics.RaycastNonAlloc(camera.transform.position, direction.normalized, hits, direction.magnitude - 0.15f, ~0, QueryTriggerInteraction.Ignore)) == hits.Length)
                    Array.Resize(ref hits, hits.Length * 2);
                for (int i = 0; i < count; i++)
                {
                    Transform hit = hits[i].transform;
                    if (!hit.IsChildOf(playerRoot) && (visibleTargetRoot == null || !hit.IsChildOf(visibleTargetRoot))) return false;
                }
                return true;
        }
    }
}
