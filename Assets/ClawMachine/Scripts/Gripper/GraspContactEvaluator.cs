using UnityEngine;

namespace ClawMachine
{
    public static class GraspContactEvaluator
    {
        // Geometric contact proxy against the actual prize collider; no random outcome.
        public static GraspFailure Evaluate(Vector3 tool, PrizePhysics prize, Transform[] pads, out int contacts)
        {
            contacts = 0;
            if (!prize || !prize.IsConfigured || !prize.Profile || pads == null) return GraspFailure.NoContact;
            Vector3 center = prize.Body.worldCenterOfMass;
            Vector3 offset = tool - center;
            if (new Vector2(offset.x, offset.z).magnitude > prize.Profile.maxCenterOffset) return GraspFailure.Misaligned;
            if (Mathf.Abs(offset.y) > prize.Profile.verticalTolerance) return GraspFailure.NoContact;
            foreach (Transform pad in pads)
                if (pad && Vector3.Distance(prize.Collider.ClosestPoint(pad.position), pad.position) <= prize.Profile.contactTolerance) contacts++;
            if (contacts < 2) return GraspFailure.NoContact;
            if (prize.Body.mass * Physics.gravity.magnitude * 1.5f > prize.Profile.breakForce) return GraspFailure.UnstableGrip;
            return GraspFailure.None;
        }
    }
}
