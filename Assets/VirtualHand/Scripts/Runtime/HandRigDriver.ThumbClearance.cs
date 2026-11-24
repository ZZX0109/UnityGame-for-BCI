using UnityEngine;

namespace Neuralife.VirtualHand
{
    public partial class HandRigDriver
    {
        Transform clearanceIndex, clearanceLittle;
        Vector3[] spherePositions;
        float[] sphereRadii;
        int thumbSphereCount;
        float displayedThumbAngle;

        public void ResetGloveThumbContact() => displayedThumbAngle = 0f;

        void CacheThumbClearance()
        {
            clearanceIndex = FindNativeSegmentTransform("index_01");
            clearanceLittle = FindNativeSegmentTransform("pinky_01");
            var spheres = nativeProfile.fingerSpheres;
            int count = spheres == null ? 0 : spheres.Length;
            spherePositions = new Vector3[count];
            sphereRadii = new float[count];
            thumbSphereCount = 0;
            while (thumbSphereCount < count && spheres[thumbSphereCount].thumb) thumbSphereCount++;
        }

        // Apply only to the displayed data-glove pose, after restoring its unmodified bone rotations.
        public void ResolveGloveThumbContact(float deltaTime)
        {
            if (thumbSphereCount == 0 || !clearanceIndex || !clearanceLittle || _nativeThumbBaseIndex < 0) return;
            var spheres = nativeProfile.fingerSpheres;
            for (int i = 0; i < spheres.Length; i++)
            {
                var s = spheres[i];
                var bone = _nativeTransforms[s.boneIndex];
                spherePositions[i] = bone.TransformPoint(s.center);
                sphereRadii[i] = s.radius * Mathf.Abs(bone.lossyScale.x);
            }
            var thumb = _nativeTransforms[_nativeThumbBaseIndex];
            var pivot = thumb.position;
            bool clear = ClearAt(pivot, Quaternion.identity);
            if (clear && displayedThumbAngle == 0f) return;
            var ray = _nativeThumbEndpoint.position - pivot;
            var radial = clearanceIndex.position - clearanceLittle.position;
            var axis = Vector3.Cross(ray, radial).normalized;
            if (axis.sqrMagnitude < 0.5f) return;
            const float step = 4f, limit = 40f;
            float high = 0f;
            if (!clear)
            {
                float low = 0;
                high = step;
                while (high < limit && !ClearAt(pivot, Quaternion.AngleAxis(high, axis))) { low = high; high += step; }
                for (int i = 0; i < 3; i++)
                {
                    float mid = (low + high) * 0.5f;
                    if (ClearAt(pivot, Quaternion.AngleAxis(mid, axis))) high = mid; else low = mid;
                }
            }
            displayedThumbAngle = deltaTime < 0f ? high : Mathf.MoveTowards(displayedThumbAngle, high, 160f * deltaTime);
            var parent = thumb.parent;
            var from = parent.InverseTransformPoint(pivot + ray) - thumb.localPosition;
            var to = parent.InverseTransformPoint(pivot + Quaternion.AngleAxis(displayedThumbAngle, axis) * ray) - thumb.localPosition;
            thumb.localRotation = Quaternion.FromToRotation(from, to) * thumb.localRotation;
        }

        bool ClearAt(Vector3 pivot, Quaternion rotation)
        {
            for (int i = 0; i < thumbSphereCount; i++)
            {
                Vector3 center = pivot + rotation * (spherePositions[i] - pivot);
                for (int j = thumbSphereCount; j < spherePositions.Length; j++)
                {
                    float clearance = sphereRadii[i] + sphereRadii[j];
                    if ((center - spherePositions[j]).sqrMagnitude < clearance * clearance) return false;
                }
            }
            return true;
        }
    }
}
