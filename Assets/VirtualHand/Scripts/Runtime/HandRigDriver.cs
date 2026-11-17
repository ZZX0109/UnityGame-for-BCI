using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Drives a skinned hand rig from 20 normalized [0,1] controls (finger-major:
    /// thumb,index,middle,ring,little; thumb block = CMC flexion, MCP flexion, IP flexion,
    /// opposition; other blocks = MCP, PIP, DIP, spread).
    /// Convention-free: bones are reset to their rest world rotation each frame, then
    /// rotated incrementally around world-space bend axes, so the visual result does not
    /// depend on FBX bone roll conventions. Values are visual rig controls, not degrees.
    /// </summary>
    public class HandRigDriver : MonoBehaviour
    {
        static readonly string[] ThumbChain = { "Thumb1", "Thumb2", "Thumb3" };
        static readonly string[][] Chains =
        {
            new[] { "Index1", "Index2", "Index3" },
            new[] { "Middle1", "Middle2", "Middle3" },
            new[] { "Ring1", "Ring2", "Ring3" },
            new[] { "Little1", "Little2", "Little3" },
        };

        [Tooltip("Max flexion degrees for MCP/CMC-class controls.")]
        public float maxFlexionDeg = 90f;
        [Tooltip("Max spread degrees for the 4th control of each finger block.")]
        public float maxSpreadDeg = 10f;

        Transform[] _thumb = new Transform[3];
        Transform[][] _fingers = new Transform[4][];
        Quaternion[] _restRot;          // world rest rotations, reset each Apply
        Transform[] _allBones;
        float[] _current = new float[20];
        float[] _target = new float[20];
        bool _hasPose;

        public bool IsMirrored { get; private set; }

        void Awake() => CacheBones();

        /// <summary>Find bones under this hand root (direct name or name.side suffix).</summary>
        public bool CacheBones()
        {
            for (int i = 0; i < 3; i++) _thumb[i] = FindBone(ThumbChain[i]);
            for (int f = 0; f < 4; f++)
            {
                _fingers[f] = new Transform[3];
                for (int j = 0; j < 3; j++) _fingers[f][j] = FindBone(Chains[f][j]);
            }
            bool ok = _thumb[0] != null && _fingers[0][0] != null;
            if (!ok)
                Debug.LogWarning($"[HandRigDriver] Missing finger bones under {name}. " +
                                 "Expected names like Index1/Index2/Index3 (optionally .R/.L suffix).");
            else CacheRest();
            return ok;
        }

        Transform FindBone(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.StartsWith(name + ".") || t.name.StartsWith(name + "_"))
                    return t;
            return null;
        }

        Vector3[] _bendAxis;   // per-bone curl axis = boneDir x palmNormal (cached at rest)
        Vector3[] _restHead;

        void CacheRest()
        {
            var list = new System.Collections.Generic.List<Transform>();
            list.AddRange(_thumb);
            foreach (var c in _fingers) list.AddRange(c);
            _allBones = list.ToArray();
            _restRot = new Quaternion[_allBones.Length];
            _bendAxis = new Vector3[_allBones.Length];
            _restHead = new Vector3[_allBones.Length];
            Vector3 palmNormal = -Vector3.up; // rest pose: palm faces down (A-pose forearms)
            for (int i = 0; i < _allBones.Length; i++)
            {
                var t = _allBones[i];
                _restRot[i] = t != null ? t.rotation : Quaternion.identity;
                _restHead[i] = t != null ? t.position : Vector3.zero;
                if (t == null) continue;
                Vector3 dir = (t.position - _restHead[i]).normalized;
                if (t.childCount > 0)
                {
                    // use child joint for direction (bone points to its child)
                    Vector3 cp = t.GetChild(0).position;
                    dir = (cp - t.position).normalized;
                }
                Vector3 axis = Vector3.Cross(dir, palmNormal);
                if (axis.sqrMagnitude < 0.05f) axis = Vector3.Cross(dir, Vector3.forward);
                _bendAxis[i] = axis.normalized;
            }
        }

        /// <summary>Handedness flips the world bend axes for mirrored hands.</summary>
        public void SetMirrored(bool mirrored) => IsMirrored = mirrored;

        /// <summary>Apply 20 controls instantly. Invalid length is rejected visibly.</summary>
        public void SetControls(float[] c)
        {
            if (c == null || c.Length != 20)
            {
                Debug.LogWarning("[HandRigDriver] SetControls expects 20 values; ignoring frame.");
                return;
            }
            Array.Copy(c, _target, 20);
            _hasPose = true;
            Apply(_target);
        }

        /// <summary>Smooth-approach toward the 20-control target. Call once per frame.</summary>
        public void UpdateControls(float[] target, float smooth, float dt)
        {
            if (target != null && target.Length == 20)
                Array.Copy(target, _target, 20);
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.016f, smooth));
            for (int i = 0; i < 20; i++)
                _current[i] = Mathf.Lerp(_current[i], _target[i], k);
            _hasPose = true;
            Apply(_current);
        }

        void ResetToRest()
        {
            for (int i = 0; i < _allBones.Length; i++)
                if (_allBones[i] != null) _allBones[i].rotation = _restRot[i];
        }


        void Apply(float[] v)
        {
            if (_allBones == null || _thumb[0] == null) return;
            ResetToRest();
            float s = IsMirrored ? -1f : 1f;

            // Bend each bone about the world-space bend axis. Child bones inherit the
            // parent's posed delta so flexion compounds down the chain like a real fist.
            Quaternion[] cur = new Quaternion[_allBones.Length];
            Quaternion[] delta = new Quaternion[_allBones.Length];
            for (int i = 0; i < _allBones.Length; i++)
            {
                cur[i] = _restRot[i];
                delta[i] = Quaternion.identity;
            }
            for (int i = 0; i < _allBones.Length; i++)
            {
                Transform t = _allBones[i];
                if (t == null) continue;
                int pi = ParentIndex(i);
                if (pi >= 0) cur[i] = delta[pi] * cur[i];
                cur[i] = BoneDelta(i, v, s) * cur[i];
                delta[i] = cur[i] * Quaternion.Inverse(_restRot[i]);
                t.rotation = cur[i];
            }
        }

        /// <summary>Rotation delta for bone i from its controls, about its own curl axis.</summary>
        Quaternion BoneDelta(int i, float[] v, float s)
        {
            Quaternion q = Quaternion.identity;
            if (i < 3) // thumb chain: flexion about own axis, opposition (i==0) about world-forward
            {
                q = AngleAround(_bendAxis[i], Mathf.Clamp01(v[i]) * MaxFlex(i) * s) * q;
                if (i == 0) q = AngleAround(Vector3.forward, Mathf.Clamp01(v[3]) * 60f * s) * q;
                return q;
            }
            int f = (i - 3) / 3, j = (i - 3) % 3;
            if (f < 0 || f > 3) return q;
            int o = (f + 1) * 4;
            float maxF = j == 0 ? maxFlexionDeg : j == 1 ? maxFlexionDeg * 1.15f : maxFlexionDeg * 0.75f;
            q = AngleAround(_bendAxis[i], Mathf.Clamp01(v[o + j]) * maxF * s) * q;
            if (j == 1)
                q = AngleAround(Vector3.up, (Mathf.Clamp01(v[o + 3]) - 0.5f) * 2f * maxSpreadDeg * s) * q;
            return q;
        }

        static Quaternion AngleAround(Vector3 axis, float deg) =>
            Quaternion.AngleAxis(deg, axis.normalized);

        int[] _parentIdx;

        int ParentIndex(int i)
        {
            if (_parentIdx == null)
            {
                _parentIdx = new int[_allBones.Length];
                for (int a = 0; a < _allBones.Length; a++)
                {
                    _parentIdx[a] = -1;
                    if (_allBones[a] == null) continue;
                    var p = _allBones[a].parent;
                    for (int b = 0; b < _allBones.Length; b++)
                        if (b != a && _allBones[b] == p) { _parentIdx[a] = b; break; }
                }
            }
            return _parentIdx[i];
        }


        float MaxFlex(int i) => i == 2 ? maxFlexionDeg * 0.8f : maxFlexionDeg;

        /// <summary>Neutral pose (everything rest). Clears smoothing state.</summary>
        public void ResetControls()
        {
            Array.Clear(_current, 0, 20);
            Array.Clear(_target, 0, 20);
            if (_allBones != null) ResetToRest();
            _hasPose = false;
        }
    }
}
