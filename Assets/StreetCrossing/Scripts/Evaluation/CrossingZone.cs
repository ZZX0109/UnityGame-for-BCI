using UnityEngine;

namespace StreetCrossing.Evaluation
{
    /// <summary>Explicit world-space semantic volumes, sampled centrally; no trigger callback ordering dependency.</summary>
    public sealed class CrossingZone : MonoBehaviour
    {
        public CrossingRegion region;
        public Vector3 size;
        public bool Contains(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f;
        }
        private void OnDrawGizmos()
        {
            Gizmos.color = region == CrossingRegion.RoadwayHazard ? new Color(1, 0.5f, 0, 0.25f) : new Color(0.2f, 0.9f, 0.5f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
