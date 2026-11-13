using UnityEngine;

namespace ClawMachine
{
    public struct SurfaceContact
    {
        public bool Valid;
        public bool Penetrating;
        public Vector3 Point, Normal;
        public float Gap, Depth;
    }
    public static class GraspContactEvaluator
    {
        public static float WorldRadius(SphereCollider pad)
        {
            Vector3 scale = pad.transform.lossyScale;
            return pad.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }
        public static SurfaceContact Sample(SphereCollider pad, Collider surface, float tolerance, float maxPenetration)
        {
            var result = new SurfaceContact();
            if (!pad || !surface || !surface.enabled) return result;
            Vector3 padDelta = pad.attachedRigidbody ? pad.attachedRigidbody.position - pad.attachedRigidbody.transform.position : Vector3.zero;
            Vector3 surfaceDelta = surface.attachedRigidbody ? surface.attachedRigidbody.position - surface.attachedRigidbody.transform.position : Vector3.zero;
            Vector3 center = pad.transform.TransformPoint(pad.center) + padDelta;
            Vector3 closest = surface.ClosestPoint(center);
            float outsideDistance = Vector3.Distance(center, closest);
            bool overlaps = Physics.ComputePenetration(pad, pad.transform.position + padDelta, pad.transform.rotation,
                surface, surface.transform.position + surfaceDelta, surface.transform.rotation, out Vector3 separation, out float depth);
            result.Depth = overlaps ? depth : 0;
            // ClosestPoint(center)==center is inside/on-surface, not a valid outside finger center.
            result.Penetrating = outsideDistance <= 0.00001f || result.Depth > maxPenetration;
            result.Gap = outsideDistance - WorldRadius(pad);
            result.Point = closest; result.Normal = outsideDistance > 0.00001f ? (center - closest) / outsideDistance : separation;
            result.Valid = !result.Penetrating && result.Gap <= tolerance && result.Gap >= -maxPenetration;
            return result;
        }
        public static GraspFailure Evaluate(Vector3 tool, PrizePhysics prize, SphereCollider[] pads, out int contacts)
        {
            contacts = 0;
            if (!prize || !prize.IsConfigured || !prize.Profile || pads == null || pads.Length < 3) return GraspFailure.NoContact;
            Vector3 offset = tool - prize.Body.worldCenterOfMass;
            if (Vector3.ProjectOnPlane(offset, Vector3.up).magnitude > prize.Profile.maxCenterOffset) return GraspFailure.Misaligned;
            float captureHeight = prize.CaptureBounds.max.y - prize.Profile.surfaceCaptureDepth;
            if (Mathf.Abs(tool.y - captureHeight) > prize.Profile.verticalTolerance) return GraspFailure.NoContact;
            var samples = new SurfaceContact[pads.Length];
            for (int i = 0; i < pads.Length; i++)
            {
                foreach (Collider collider in prize.ContactColliders)
                {
                    SurfaceContact sample = Sample(pads[i], collider, prize.Profile.contactTolerance, prize.Profile.maxPenetration);
                    if (sample.Penetrating) return GraspFailure.Penetrating;
                    if (sample.Valid && (!samples[i].Valid || sample.Gap < samples[i].Gap)) samples[i] = sample;
                }
                if (samples[i].Valid) contacts++;
            }
            if (contacts < 2) return GraspFailure.NoContact;
            bool surrounds = false;
            for (int i = 0; i < samples.Length; i++) for (int j = i + 1; j < samples.Length; j++)
                if (samples[i].Valid && samples[j].Valid && Vector3.Dot(samples[i].Normal, samples[j].Normal) < -0.15f &&
                    Vector3.Distance(samples[i].Point, samples[j].Point) >= prize.Profile.minimumContactSpan) surrounds = true;
            if (!surrounds || prize.Body.mass * Physics.gravity.magnitude * 1.5f > prize.Profile.breakForce) return GraspFailure.UnstableGrip;
            return GraspFailure.None;
        }
    }
}
