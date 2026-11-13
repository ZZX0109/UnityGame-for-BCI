using UnityEngine;

namespace ClawMachine
{
    public struct GraspPlan
    {
        public Pose SafeApproach;
        public Pose ContactPose;
        public PrizePhysics Candidate;
        public bool HasSurface;
        public bool IsSafe;
    }
    public static class GraspPlanner
    {
        public static GraspPlan Plan(ClawTaskSpace space, Pose current, System.Collections.Generic.IReadOnlyList<PrizePhysics> prizes)
        {
            var result = new GraspPlan();
            float top = float.NegativeInfinity;
            float safeAboveAll = space.LocalBounds.min.y + 0.22f;
            Vector3 rayOrigin = space.HomePose.position;
            var local = space.ToLocal(current.position); local.y = space.LocalBounds.max.y;
            rayOrigin = space.ToWorld(local);
            RaycastHit chosenHit = default(RaycastHit);
            foreach (PrizePhysics prize in prizes)
            {
                if (!prize || !prize.gameObject.activeInHierarchy || !prize.Profile) continue;
                safeAboveAll = Mathf.Max(safeAboveAll, space.ToLocal(prize.AllBounds.max + space.Up * prize.Profile.approachClearance).y);
                if (prize.TrySurfaceBelow(rayOrigin, -space.Up, out RaycastHit hit) && Vector3.Dot(hit.normal, space.Up) > 0.35f && hit.point.y > top)
                { top = hit.point.y; chosenHit = hit; result.Candidate = prize; result.HasSurface = true; }
            }
            result.IsSafe = safeAboveAll <= space.LocalBounds.max.y + 0.0001f;
            if (!result.IsSafe) { result.SafeApproach = result.ContactPose = space.HomePose; return result; }
            if (result.HasSurface)
            {
                PrizeProfile profile = result.Candidate.Profile;
                float safe = safeAboveAll;
                float contact = space.ToLocal(chosenHit.point - space.Up * profile.surfaceCaptureDepth).y;
                result.SafeApproach = space.PoseAtHeight(current.position, safe);
                result.ContactPose = space.PoseAtHeight(current.position, contact);
            }
            else
            {
                result.SafeApproach = space.PoseAtHeight(current.position, safeAboveAll);
                result.ContactPose = space.PoseAtHeight(current.position, space.LocalBounds.min.y + 0.08f);
            }
            return result;
        }
    }
}
