using UnityEngine;

namespace StreetCrossing.Traffic
{
    public static class VehicleFollowing
    {
        /// <summary>Routes are identities, not lane occupancy. A turned vehicle remains a leader after merging onto a straight route.</summary>
        public static float ProjectedRear(VehicleLanePath followerPath, VehicleKinematics follower, Vector3 otherPosition,
            Vector3 otherForward, float otherHalfLength, float otherHalfWidth, float followerHalfWidth)
        {
            float projection = followerPath.NearestDistance(otherPosition);
            if (projection <= follower.Distance + 0.05f) return float.PositiveInfinity;
            Vector3 pathForward = followerPath.RotationAt(projection) * Vector3.forward;
            float alignment = Vector3.Dot(pathForward, otherForward);
            if (alignment < 0.65f) return float.PositiveInfinity;
            Vector3 closest = followerPath.Position(projection);
            float lateral = new Vector2(closest.x - otherPosition.x, closest.z - otherPosition.z).magnitude;
            if (lateral > followerHalfWidth + otherHalfWidth + 0.15f) return float.PositiveInfinity;
            float longitudinalExtent = otherHalfLength * Mathf.Abs(alignment) + otherHalfWidth * Mathf.Sqrt(Mathf.Max(0, 1 - alignment * alignment));
            return projection - longitudinalExtent;
        }
    }
}
