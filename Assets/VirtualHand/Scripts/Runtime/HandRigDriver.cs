using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Drives a skinned hand rig from 20 normalized [0,1] controls (finger-major:
    /// thumb,index,middle,ring,little; thumb block = CMC flexion, MCP flexion, IP flexion,
    /// opposition; other blocks = MCP, PIP, DIP, spread).
    /// Values are visual rig controls, not measured joint degrees.
    /// </summary>
    public class HandRigDriver : MonoBehaviour
    {
        // Bone name suffixes expected in the imported rig (Blender export keeps them).
        static readonly string[] ThumbChain = { "Thumb1", "Thumb2", "Thumb3" };
        static readonly string[][] Chains =
        {
            new[] { "Index1", "Index2", "Index3" },
            new[] { "Middle1", "Middle2", "Middle3" },
            new[] { "Ring1", "Ring2", "Ring3" },
            new[] { "Little1", "Little2", "Little3" },
        };

        [Tooltip("Bend axis sign. Flip in Inspector if fingers curl backwards on your rig.")]
        public float flexionSign = 1f;
        [Tooltip("Max flexion degrees at MCP/CMC block (PIP/DIP scale down).")]
        public float maxFlexionDeg = 95f;
        [Tooltip("Max lateral spread degrees for the spread control.")]
        public float maxSpreadDeg = 12f;

        Transform[] _thumb;   // T1,T2,T3
        Transform[][] _fingers; // 4 chains x 3
        float[] _current = new float[20];
        float[] _target = new float[20];

        public bool IsMirrored { get; private set; }

        void Awake() => CacheBones();

        /// <summary>Find bones under this hand root, trying direct name and suffix match.</summary>
        public bool CacheBones()
        {
            _thumb = new Transform[3];
            _fingers = new Transform[4][];
            var all = GetComponentsInChildren<Transform>(true);
            Func<string, Transform> find = n => FindBone(all, n);
            for (int i = 0; i < 3; i++) _thumb[i] = find(ThumbChain[i]);
            for (int f = 0; f < 4; f++)
            {
                _fingers[f] = new Transform[3];
                for (int j = 0; j < 3; j++) _fingers[f][j] = find(Chains[f][j]);
            }
            bool ok = _thumb[0] != null && _fingers[0][0] != null;
            if (!ok)
                Debug.LogWarning($"[HandRigDriver] Missing finger bones under {name}. " +
                                 "Expected names like Index1/Index2/Index3.");
            return ok;
        }

        static Transform FindBone(Transform[] all, string name)
        {
            foreach (var t in all)
                if (t.name == name || t.name.EndsWith("." + name) || t.name.EndsWith("_" + name))
                    return t;
            return null;
        }

        /// <summary>Apply 20 controls instantly (no smoothing). Invalid length is rejected visibly.</summary>
        public void SetControls(float[] c)
        {
            if (c == null || c.Length != 20)
            {
                Debug.LogWarning("[HandRigDriver] SetControls expects 20 values; ignoring frame.");
                return;
            }
            Array.Copy(c, _target, 20);
            Apply(_target, _target);
        }

        /// <summary>Smooth-approach toward the 20-control target. Call once per frame.</summary>
        public void UpdateControls(float[] target, float smooth, float dt)
        {
            if (target != null && target.Length == 20)
                Array.Copy(target, _target, 20);
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.016f, smooth));
            for (int i = 0; i < 20; i++)
                _current[i] = Mathf.Lerp(_current[i], _target[i], k);
            Apply(_current, _target);
        }

        void Apply(float[] v, float[] raw)
        {
            if (_thumb == null || _thumb[0] == null) return;
            float s = flexionSign;

            // thumb block: CMC flexion, MCP flexion, IP flexion, opposition
            ApplyFlex(_thumb[0], v[0], s, maxFlexionDeg);
            ApplyFlex(_thumb[1], v[1], s, maxFlexionDeg);
            ApplyFlex(_thumb[2], v[2], s, maxFlexionDeg * 0.8f);
            if (_thumb[0] != null)
            {
                // opposition: rotate thumb CMC about the local twist-ish axis so the pad
                // swings across the palm; sign mirrors on left hands.
                float opp = Mathf.Lerp(0f, 55f, Mathf.Clamp01(v[3])) * (IsMirrored ? -1f : 1f);
                _thumb[0].localRotation = _thumb[0].localRotation * Quaternion.Euler(0, 0, opp);
            }

            // four finger blocks: MCP, PIP, DIP, spread
            for (int f = 0; f < 4; f++)
            {
                var b = _fingers[f];
                if (b == null || b[0] == null) continue;
                int o = (f + 1) * 4;
                ApplyFlex(b[0], v[o], s, maxFlexionDeg);
                ApplyFlex(b[1], v[o + 1], s, maxFlexionDeg * 1.1f);
                ApplyFlex(b[2], v[o + 2], s, maxFlexionDeg * 0.7f);
                if (b[1] != null)
                {
                    float spread = Mathf.Lerp(0f, maxSpreadDeg, Mathf.Clamp01(v[o + 3]) - 0.5f) * 2f
                                   * (IsMirrored ? -1f : 1f);
                    b[1].localRotation = b[1].localRotation * Quaternion.Euler(0, 0, spread);
                }
            }
        }

        static void ApplyFlex(Transform bone, float v01, float sign, float maxDeg)
        {
            if (bone == null) return;
            float deg = Mathf.Clamp01(v01) * maxDeg * sign;
            bone.localRotation = bone.localRotation * Quaternion.Euler(deg, 0, 0);
        }

        /// <summary>Mark handedness so opposition/spread mirror correctly. Called by the rig builder.</summary>
        public void SetMirrored(bool mirrored) => IsMirrored = mirrored;

        /// <summary>Neutral pose: everything zero (rest). Does not touch wrist.</summary>
        public void ResetControls()
        {
            Array.Clear(_current, 0, 20);
            Array.Clear(_target, 0, 20);
            Apply(_current, _target);
        }
    }
}
