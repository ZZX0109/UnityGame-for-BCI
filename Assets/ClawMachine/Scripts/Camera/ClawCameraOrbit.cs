using UnityEngine;

namespace ClawMachine
{
    // Pure observation geometry; it never reads or remaps manipulator input.
    public static class ClawCameraOrbit
    {
        public const float MinimumPitch = -8;
        public const float MaximumPitch = 45;
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        public static Vector2 ClampAngles(Vector2 value)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y)) return Vector2.zero;
            // Wrap rather than stop at a preset's edges: the mouse can circle the
            // cabinet repeatedly without hitting a left/right camera boundary.
            return new Vector2(Mathf.Repeat(value.x + 180, 360) - 180,
                Mathf.Clamp(value.y, MinimumPitch, MaximumPitch));
        }
        public static Vector3 Offset(Vector3 basis, Vector2 angles, float scale, float minimumRadius, Vector3 up)
        {
            minimumRadius = IsFinite(minimumRadius) ? Mathf.Clamp(minimumRadius, 0.5f, 10) : 1.75f;
            if (!IsFinite(basis) || basis.sqrMagnitude < 0.0001f) basis = Vector3.back * minimumRadius;
            if (!IsFinite(up) || up.sqrMagnitude < 0.0001f) up = Vector3.up;
            // Finite but corrupt serialized vectors must not overflow magnitude/normalization.
            basis = BoundedVector(basis, 100);
            up = BoundedVector(up, 1);
            if (!IsFinite(scale)) scale = 1;
            angles = ClampAngles(angles);
            Vector3 yawed = Quaternion.AngleAxis(angles.x, up.normalized) * basis;
            Vector3 right = Vector3.Cross(up.normalized, -yawed.normalized);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            Vector3 rotated = Quaternion.AngleAxis(angles.y, right.normalized) * yawed;
            return rotated.normalized * Mathf.Clamp(basis.magnitude * Mathf.Clamp(scale, 0.1f, 3), minimumRadius, 20);
        }
        private static Vector3 BoundedVector(Vector3 value, float maximumComponent)
        {
            float largest = Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
            return largest > maximumComponent ? value / largest * maximumComponent : value;
        }
    }
}
