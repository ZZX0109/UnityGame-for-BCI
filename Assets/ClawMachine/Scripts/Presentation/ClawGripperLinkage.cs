using UnityEngine;

namespace ClawMachine
{
    /// <summary>Presentation-only links between the wrist and moving finger knuckles.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ClawGripperLinkage : MonoBehaviour
    {
        [SerializeField, Tooltip("Tool-space origin used by the gripper; do not use the offset hub mesh.")]
        private Transform hubOrigin;
        [SerializeField, Tooltip("The same three finger roots, in the same order, as GripperController.")]
        private Transform[] fingers;
        [SerializeField, Tooltip("Visual-only cylinders of native height 2, parented directly to hubOrigin. No colliders.")]
        private Transform[] links;

        private const float HubRadius = 0.045f;
        private const float HubHeight = 0.30f;
        private const float KnuckleHeight = 0.10f;
        private const float MinimumLinkHeight = 0.20f;

        private void OnEnable() { RefreshNow(); }
        private void LateUpdate() { RefreshNow(); }

        public void RefreshNow()
        {
            if (!hubOrigin || fingers == null || links == null) return;
            int count = Mathf.Min(fingers.Length, links.Length);
            for (int index = 0; index < count; index++)
            {
                Transform finger = fingers[index], link = links[index];
                // A wrongly wired finger or collider must never be moved by a visual component.
                if (!link || link == hubOrigin || link.parent != hubOrigin || System.Array.IndexOf(fingers, link) >= 0 ||
                    link.GetComponentInChildren<Collider>(true) || (finger && finger.IsChildOf(link))) continue;

                Vector3 thickness = link.localScale;
                if (!finger)
                {
                    if (Finite(thickness)) link.localScale = new Vector3(thickness.x, 0, thickness.z);
                    continue;
                }

                Vector3 knuckle = hubOrigin.InverseTransformPoint(finger.TransformPoint(Vector3.up * KnuckleHeight));
                Vector3 radial = new Vector3(knuckle.x, 0, knuckle.z);
                if (!Finite(knuckle) || !Finite(thickness) || radial.sqrMagnitude < 0.000001f ||
                    knuckle.y < MinimumLinkHeight)
                {
                    // Invalid/low anchors cannot leave a stale strut protruding into the gripping area.
                    if (Finite(thickness)) link.localScale = new Vector3(thickness.x, 0, thickness.z);
                    continue;
                }
                Vector3 wrist = radial.normalized * HubRadius + Vector3.up * HubHeight;
                Vector3 direction = knuckle - wrist;
                if (!Finite(direction) || float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < 0.000001f) continue;
                link.localPosition = (wrist + knuckle) * 0.5f;
                link.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                link.localScale = new Vector3(thickness.x, direction.magnitude * 0.5f, thickness.z);
            }
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
