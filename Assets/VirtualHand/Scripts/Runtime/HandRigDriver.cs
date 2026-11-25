using System;
using System.Linq;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Drives a skinned hand rig from 20 normalized [0,1] controls (finger-major:
    /// thumb,index,middle,ring,little; thumb block = CMC flexion, MCP flexion, IP flexion,
    /// opposition; other blocks = MCP, PIP, DIP, spread).
    /// Bones reset to imported local rest rotations before applying profile-local
    /// deltas, with explicit FBX basis and anatomical side conversion.
    /// Values are visual rig controls, not measured joint angles.
    /// </summary>
    public partial class HandRigDriver : MonoBehaviour
    {
        static readonly string[] NativeFingerNames = { "index", "middle", "ring", "pinky" };
        static readonly string[] ThumbChain = { "Thumb1", "Thumb2", "Thumb3" };
        static readonly string[][] Chains =
        {
            new[] { "Index1", "Index2", "Index3" },
            new[] { "Middle1", "Middle2", "Middle3" },
            new[] { "Ring1", "Ring2", "Ring3" },
            new[] { "Little1", "Little2", "Little3" },
        };

        [Tooltip("Optional calibration profile (Resources/Rig/hand_rig_profile.json). " +
                 "When present, per-channel bone/axis/scale/sign come from the same " +
                 "file the Blender build exports; the legacy max fields are ignored.")]
        public TextAsset profileJson;

        [Tooltip("Optional native-rig profile sampled from the imported Elena FF Open/Close clip. " +
                 "When assigned, the 20 controls drive sampled bone-local quaternion deltas.")]
        public ElenaNativeRigProfile nativeProfile;

        [Tooltip("Scales thumb opposition from the centered thumb control. The target is computed " +
                 "from the current thumb and index bone endpoints.")]
        [Range(0f, 1f)] public float nativeOppositionStrength = 1f;

        [Tooltip("Legacy fallback scales, used only when no profile is loaded.")]
        public float maxFlexionDeg = 90f;
        [Tooltip("Max spread degrees for the 4th control of each finger block.")]
        public float maxSpreadDeg = 10f;

        // ------------------------------------------------------------------
        // Inter-finger self-penetration detection (spec 3.2)
        //
        // The rig carries no per-bone skin radii, so a true penetration depth is not
        // computable here. What is computable — and what actually answers "are the
        // fingers overlapping" — is the shortest distance between adjacent fingers'
        // bone centrelines. That is reported as a conservative diagnostic, not as a
        // skin-accurate measurement, and it never silently rewrites the pose.
        // ------------------------------------------------------------------

        [Header("Self-intersection diagnostics")]
        [Tooltip("Measure adjacent-finger clearance after each pose update.")]
        public bool detectSelfIntersection = true;

        [Tooltip("Adjacent finger centrelines closer than this are reported as interpenetrating (metres).")]
        [Range(0.001f, 0.03f)] public float selfIntersectionThresholdMeters = 0.006f;

        /// <summary>Shortest centreline distance found between adjacent fingers, in metres.</summary>
        public float ClosestAdjacentFingerDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>True when that distance fell below <see cref="selfIntersectionThresholdMeters"/>.</summary>
        public bool SelfIntersectionDetected { get; private set; }

        /// <summary>
        /// Measures the clearance between adjacent fingers and updates
        /// <see cref="ClosestAdjacentFingerDistance"/> / <see cref="SelfIntersectionDetected"/>.
        ///
        /// The proximal phalanx of each finger is excluded from the comparison: adjacent
        /// knuckles meet at the palm, so their centrelines are near zero apart in every
        /// pose and including them would make the diagnostic fire constantly. What is
        /// compared is the middle and distal phalanges, which are the parts that can
        /// genuinely cross when a pose over-curls.
        /// </summary>
        public void EvaluateSelfIntersection()
        {
            if (!detectSelfIntersection || _thumb[0] == null || _fingers[0] == null)
            {
                SelfIntersectionDetected = false;
                ClosestAdjacentFingerDistance = float.PositiveInfinity;
                return;
            }

            float closest = float.PositiveInfinity;

            // Thumb against the index finger: the pair that contacts first in a fist.
            closest = Mathf.Min(closest, ChainClearance(_thumb, _fingers[0]));

            for (int f = 0; f < 3; f++)
                closest = Mathf.Min(closest, ChainClearance(_fingers[f], _fingers[f + 1]));

            ClosestAdjacentFingerDistance = closest;
            SelfIntersectionDetected = closest < selfIntersectionThresholdMeters;
        }

        /// <summary>Shortest segment-to-segment distance between two phalanx chains.</summary>
        static float ChainClearance(Transform[] a, Transform[] b)
        {
            if (a == null || b == null)
                return float.PositiveInfinity;

            float best = float.PositiveInfinity;

            // Start at 1: skip the proximal segment, which shares the palm.
            for (int i = 1; i < a.Length - 1; i++)
            {
                if (!a[i] || !a[i + 1]) continue;
                for (int j = 1; j < b.Length - 1; j++)
                {
                    if (!b[j] || !b[j + 1]) continue;
                    float d = SegmentDistance(a[i].position, a[i + 1].position,
                                              b[j].position, b[j + 1].position);
                    if (d < best) best = d;
                }
            }

            return best;
        }

        /// <summary>Shortest distance between two line segments.</summary>
        static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            const float eps = 1e-10f;
            float s, t;

            if (a <= eps && e <= eps)
                return Vector3.Distance(p1, p2);

            if (a <= eps)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }

            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return Vector3.Distance(c1, c2);
        }

        HandRigProfile.Channel[] _profile;   // 20 calibrated channels (null = legacy path)

        // Native-clip path (Elena FF). Per-bone control maps are cached at bind
        // time; ApplyNative reuses them without allocating per frame.
        Transform[] _nativeTransforms;
        Quaternion[] _nativeOpen;
        Quaternion[] _nativeDelta;
        int[] _nativeFlexControl;
        int[] _nativeSpreadControl;
        Vector3[] _nativeSpreadAxisLocal;
        int _nativeThumbBaseIndex = -1;
        int _nativeOppositionControl = -1;
        Transform _nativeThumbEndpoint;
        Transform _nativeIndexEndpoint;

        Transform[] _thumb = new Transform[3];
        Transform[][] _fingers = new Transform[4][];
        Transform[] _allBones;
        float[] _current = new float[20];
        float[] _target = new float[20];

        [SerializeField] bool _isMirrored;
        public bool IsMirrored { get => _isMirrored; private set => _isMirrored = value; }

        // Demo hands use their authored geometry. Keep this opt-out serialized per
        // instance so global proportion tuning remains the default for existing rigs.
        [SerializeField] bool ignoreGlobalHandProportions;
        public bool IgnoreGlobalHandProportions
        {
            get => ignoreGlobalHandProportions;
            set => ignoreGlobalHandProportions = value;
        }

        void Awake()
        {
            // Length tuning first: the native path derives spread axes, thumb
            // clearance and contact spheres from the current bone positions.
            ApplyProportionsOnce();
            CacheBones();
        }

        /// <summary>
        /// Applies the finger-length multipliers from
        /// Resources/Rig/ElenaFF/HandProportions once per hand instance.
        ///
        /// Runtime instead of build-time on purpose: bone localPosition is safe to
        /// edit because this driver only ever writes localRotation, and applying it
        /// on Awake means a tuned value shows up on the next Play instead of
        /// requiring a prefab rebuild.  Identity (all 1) leaves the hand untouched.
        /// </summary>
        static HandProportions _cachedProportions;
        static bool _proportionsLookupDone;
        bool _proportionsApplied;

        void ApplyProportionsOnce()
        {
            if (_proportionsApplied)
                return;
            _proportionsApplied = true;

            if (ignoreGlobalHandProportions)
                return;

            if (!_proportionsLookupDone)
            {
                _proportionsLookupDone = true;
                _cachedProportions = Resources.Load<HandProportions>("Rig/ElenaFF/HandProportions");
                if (_cachedProportions == null)
                    Debug.Log("[HandRigDriver] no HandProportions asset found under Resources/Rig/ElenaFF; " +
                              "using the model's authored finger lengths.");
            }

            if (_cachedProportions == null || _cachedProportions.IsIdentity)
                return;

            foreach (var bone in GetComponentsInChildren<Transform>(true))
            {
                float scale = _cachedProportions.ScaleFor(bone.name);
                if (Mathf.Approximately(scale, 1f))
                    continue;

                bone.localPosition = bone.localPosition * scale;
            }


        }

        /// <summary>Find bones under this hand root (direct name or name.name.side suffix).</summary>
        public bool CacheBones()
        {
            if (nativeProfile != null && CacheNativeBones())
            {
                // The native path returns early, but the per-finger chains are still
                // needed: the self-intersection diagnostic and thumb clearance both walk
                // them, and on this rig they must be resolved from the native landmarks
                // rather than the legacy FBX names.
                CacheFingerChains();
                return true;
            }

            CacheFingerChains();
            bool ok = _thumb[0] != null && _fingers[0][0] != null;
            if (!ok)
            {
                Debug.LogWarning($"[HandRigDriver] Missing finger bones under {name}. " +
                                 "Expected names like Index1/Index2/Index3 (optionally .R/.L suffix).");
                return false;
            }
            LoadProfile();
            CacheRest();
            return true;
        }

        /// <summary>Native landmark prefixes, one row per finger, proximal to distal.</summary>
        static readonly string[][] NativeChainLandmarks =
        {
            new[] { "index_01", "index_02", "index_03" },
            new[] { "middle_01", "middle_02", "middle_03" },
            new[] { "ring_01", "ring_02", "ring_03" },
            new[] { "pinky_01", "pinky_02", "pinky_03" }
        };

        static readonly string[] NativeThumbLandmarks = { "thumb_01", "thumb_02", "thumb_03" };

        /// <summary>
        /// Fills <see cref="_thumb"/> and <see cref="_fingers"/> on whichever rig layout
        /// is present: the legacy FBX names first, then the Elena native landmarks. Runs
        /// on both paths so downstream diagnostics never see a half-filled chain.
        /// </summary>
        void CacheFingerChains()
        {
            for (int i = 0; i < 3; i++)
                _thumb[i] = FindBone(ThumbChain[i]) ?? FindNativeBone(NativeThumbLandmarks[i]);

            for (int f = 0; f < 4; f++)
            {
                if (_fingers[f] == null)
                    _fingers[f] = new Transform[3];
                for (int j = 0; j < 3; j++)
                    _fingers[f][j] = FindBone(Chains[f][j]) ?? FindNativeBone(NativeChainLandmarks[f][j]);
            }
        }

        /// <summary>
        /// Resolves a native landmark by name. Searches the cached arrays first, then the
        /// hierarchy: on the native path <see cref="_allBones"/> is never filled (only the
        /// legacy fallback builds it), so relying on it alone left every chain empty.
        /// </summary>
        Transform FindNativeBone(string landmark)
        {
            if (_nativeTransforms != null)
                foreach (var t in _nativeTransforms)
                    if (t && ElenaNativeRigProfile.MatchesBone(t.name, landmark))
                        return t;

            if (_allBones != null)
                foreach (var t in _allBones)
                    if (t && ElenaNativeRigProfile.MatchesBone(t.name, landmark))
                        return t;

            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t && ElenaNativeRigProfile.MatchesBone(t.name, landmark))
                    return t;

            return null;
        }

        /// <summary>Bind the sampled native Elena bones without changing the 20-control contract.</summary>
        bool CacheNativeBones()
        {
            if (!nativeProfile.IsValid)
            {
                Debug.LogWarning("[HandRigDriver] native profile is incomplete; ignoring it.");
                return false;
            }
            bool hadNativePose = _nativeTransforms != null;
            var transforms = new System.Collections.Generic.List<Transform>();
            var open = new System.Collections.Generic.List<Quaternion>();
            var delta = new System.Collections.Generic.List<Quaternion>();
            var all = GetComponentsInChildren<Transform>(true);
            foreach (var bone in nativeProfile.bones)
            {
                Transform t = null;
                if (!string.IsNullOrEmpty(bone.path)) t = transform.Find(bone.path);
                if (t == null)
                    foreach (var candidate in all)
                        if (candidate.name == bone.name) { t = candidate; break; }
                if (t == null)
                {
                    Debug.LogWarning($"[HandRigDriver] native bone missing: {bone.path ?? bone.name}");
                    return false;
                }
                Quaternion d = Quaternion.Inverse(bone.openLocal) * bone.closedLocal;
                if (Quaternion.Dot(d, Quaternion.identity) < 0f)
                    d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
                transforms.Add(t);
                open.Add(bone.openLocal);
                delta.Add(d.normalized);
            }
            _nativeTransforms = transforms.ToArray();
            _nativeOpen = open.ToArray();
            _nativeDelta = delta.ToArray();
            _nativeFlexControl = new int[_nativeTransforms.Length];
            _nativeSpreadControl = new int[_nativeTransforms.Length];
            _nativeSpreadAxisLocal = new Vector3[_nativeTransforms.Length];
            _nativeThumbBaseIndex = -1;
            _nativeOppositionControl = -1;
            for (int b = 0; b < _nativeTransforms.Length; b++)
            {
                string boneName = nativeProfile.bones[b].name;
                int control = NativeFlexControlForName(boneName);
                // Preserve any animated native bone not covered by the named
                // anatomy map. It follows overall flexion and reaches the exact
                // sampled clip pose when all curl controls are fully closed.
                if (control == -1 && Quaternion.Angle(Quaternion.identity, _nativeDelta[b]) > 0.01f)
                    control = -3;
                _nativeFlexControl[b] = control;
                _nativeSpreadControl[b] = -1;
                if (ElenaNativeRigProfile.MatchesBone(boneName, "thumb_base")) _nativeThumbBaseIndex = b;
            }

            _nativeOppositionControl = -1;
            for (int i = 0; i < nativeProfile.bindings.Length; i++)
            {
                var binding = nativeProfile.bindings[i];
                if (binding == null) return false;
                int found = -1;
                for (int b = 0; b < nativeProfile.bones.Length; b++)
                    if (nativeProfile.bones[b].path == binding.bonePath) { found = b; break; }
                if (found < 0)
                {
                    Debug.LogWarning("[HandRigDriver] native binding has unknown bone: " + binding.bonePath);
                    return false;
                }
                if (!binding.centered) continue;
                string boneName = nativeProfile.bones[found].name;
                if (ElenaNativeRigProfile.MatchesBone(boneName, "thumb_base"))
                {
                    _nativeOppositionControl = binding.control;
                    continue;
                }
                // Older generated profiles reference the metacarpal base.
                // Both profile versions apply lateral rotation at the MCP.
                int finger = binding.control / 4 - 1;
                if (finger < 0 || finger >= NativeFingerNames.Length || binding.control % 4 != 3) continue;
                string stem = NativeFingerNames[finger];
                if (!ElenaNativeRigProfile.MatchesBone(boneName, stem + "_base") &&
                    !ElenaNativeRigProfile.MatchesBone(boneName, stem + "_01")) continue;
                int mcp = FindNativeIndex(stem + "_01");
                if (mcp >= 0) _nativeSpreadControl[mcp] = binding.control;
            }

            _nativeThumbEndpoint = FindNativeSegmentTransform("thumb_03.R_end");
            _nativeIndexEndpoint = FindNativeSegmentTransform("index_03.R_end");
            // Spread axes must be measured from the authored open pose, including on rebind.
            for (int b = 0; b < _nativeTransforms.Length; b++)
                if (_nativeTransforms[b] != null) _nativeTransforms[b].localRotation = _nativeOpen[b];
            CacheNativeSpreadAxes();
            CacheThumbClearance();
            if (_nativeThumbBaseIndex < 0 || _nativeThumbEndpoint == null || _nativeIndexEndpoint == null)
                Debug.LogWarning("[HandRigDriver] native thumb opposition endpoints are unavailable; " +
                                 "the centered thumb opposition control cannot align the pads.");

            _profileOpen = nativeProfile.openControls;
            _profileFist = nativeProfile.fistControls;
            _profilePinch = nativeProfile.pinchControls;
            if (!hadNativePose)
            {
                Array.Copy(_profileOpen, _current, 20);
                Array.Copy(_profileOpen, _target, 20);
            }
            ApplyNative(_current);

            return true;
        }

        /// <summary>Load the shared calibration profile (Blender-exported JSON).</summary>
        public void LoadProfile()
        {
            if (profileJson != null &&
                HandRigProfile.TryParseText(profileJson.text, out var p))
            {
                _profile = p.Channels;
                _profileOpen = p.Open;
                _profileFist = p.Fist;
                _profilePinch = p.Pinch;
                Debug.Log($"[HandRigDriver] profile loaded ({_profile.Length} channels, " +
                          "Resources/Rig/hand_rig_profile.json).");
            }
            else
            {
                _profile = null; // legacy maxFlexionDeg path
            }
        }

        public bool HasProfile => _profile != null || _nativeTransforms != null;

        /// <summary>
        /// The 20 controls currently driving this hand, read-only.
        /// Gesture recognition and the guidance hand use this so they classify and
        /// blend what the hand is actually doing rather than a parallel copy that
        /// could drift out of step. Callers must not mutate the array.
        /// </summary>
        public float[] LastControls => _current;
        public float[] ProfileOpen => _profileOpen;
        public float[] ProfileFist => _profileFist;
        public float[] ProfilePinch => _profilePinch;
        float[] _profileOpen, _profileFist, _profilePinch;

        Transform FindBone(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.StartsWith(name + ".") || t.name.StartsWith(name + "_"))
                    return t;
            return null;
        }

        Quaternion[] _restLocalRot;  // bone-local rest rotation as imported (FBX axes preserved)

        void CacheRest()
        {
            var list = new System.Collections.Generic.List<Transform>();
            list.AddRange(_thumb);
            foreach (var c in _fingers) list.AddRange(c);
            _allBones = list.ToArray();
            _restLocalRot = new Quaternion[_allBones.Length];
            for (int i = 0; i < _allBones.Length; i++)
            {
                var t = _allBones[i];
                // bone-LOCAL rest rotation exactly as the FBX import provides it.
                // Blender authored pose angles in this same local space, so
                // writing localRotation directly reproduces the preview poses
                // regardless of any root/armature node rotation or task anchors.
                _restLocalRot[i] = t != null ? t.localRotation : Quaternion.identity;
            }
        }

        /// <summary>
        /// Anatomical side for the frozen mirrored rig: X curl keeps its sign;
        /// Y/Z controls need a separate left-side conversion before FBX conversion.
        /// </summary>
        public void SetMirrored(bool mirrored) => IsMirrored = mirrored;

        /// <summary>Returns true when all 20 values are finite and in [0,1].</summary>
        public static bool IsValidControlArray(float[] c)
        {
            if (c == null || c.Length != 20)
                return false;
            for (int i = 0; i < 20; i++)
            {
                float v = c[i];
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > 1f)
                    return false;
            }
            return true;
        }

        /// <summary>Apply 20 controls instantly. Invalid length is rejected visibly.</summary>
        public void SetControls(float[] c)
        {
            if (c == null || c.Length != 20)
            {
                Debug.LogWarning("[HandRigDriver] SetControls expects 20 values; ignoring frame.");
                return;
            }
            if (!IsValidControlArray(c))
            {
                // Spec 3.2: an invalid frame is REJECTED, not sanitised and applied.
                // Sanitising would let a single NaN/Infinity burst overwrite the last
                // trusted pose with a zeroed hand. The caller's array is left untouched
                // so the provider can still inspect what it sent.
                Debug.LogWarning("[HandRigDriver] SetControls rejected a frame containing NaN/Infinity " +
                                 "or out-of-range values; the last trusted pose is preserved.");
                return;
            }
            Array.Copy(c, _target, 20);
            Array.Copy(c, _current, 20);
            Apply(_current);
        }

        /// <summary>Smooth-approach toward the 20-control target. Call once per frame.</summary>
        public void UpdateControls(float[] target, float smooth, float dt)
        {
            if (target == null || target.Length != 20)
                return;
            if (!IsValidControlArray(target))
            {
                // Same rejection rule as SetControls: never let a bad frame move the hand.
                Debug.LogWarning("[HandRigDriver] UpdateControls rejected a frame containing NaN/Infinity " +
                                 "or out-of-range values; the last trusted pose is preserved.");
                return;
            }
            Array.Copy(target, _target, 20);
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Mathf.Max(0.016f, smooth));
            if (float.IsNaN(k) || float.IsInfinity(k)) k = 1f;
            for (int i = 0; i < 20; i++)
                _current[i] = Mathf.Lerp(_current[i], _target[i], k);
            Apply(_current);
        }

        void ResetToRest()
        {
            for (int i = 0; i < _allBones.Length; i++)
                if (_allBones[i] != null) _allBones[i].localRotation = _restLocalRot[i];
        }


        void Apply(float[] v)
        {
            ApplyPose(v);
            // Diagnostics run after every pose path, so the reported clearance always
            // describes the pose that is actually on the bones.
            EvaluateSelfIntersection();
        }

        void ApplyPose(float[] v)
        {
            if (_nativeTransforms != null)
            {
                ApplyNative(v);
                return;
            }
            if (_allBones == null || _thumb[0] == null) return;
            ResetToRest();

            if (_profile != null)
            {
                // Profile path — bone-local, matching the Blender preview exactly:
                // each pose bone gets rotation_euler[axis] += radians(deg), with
                // Blender XYZ euler composition R_local = Rz * Ry * Rx.
                // Flexion compounds down each chain automatically through the
                // transform hierarchy (children are local to their posed parent).
                for (int i = 0; i < _allBones.Length; i++)
                {
                    Transform t = _allBones[i];
                    if (t == null) continue;
                    t.localRotation = _restLocalRot[i] * ProfileDelta(i, v);
                }
                return;
            }

            // legacy fallback: local-space bone-local-X approximation with hard-coded scales
            for (int i = 0; i < _allBones.Length; i++)
            {
                Transform t = _allBones[i];
                if (t == null) continue;
                t.localRotation = _restLocalRot[i] * BoneDelta(i, v, IsMirrored ? -1f : 1f);
            }
        }

        /// <summary>Apply native clip deltas while keeping curl, spread, and opposition independent.</summary>
        void ApplyNative(float[] v)        {
            for (int i = 0; i < _nativeTransforms.Length; i++)
            {
                var t = _nativeTransforms[i];
                if (t == null) continue;

                float weight = NativeFlexWeight(_nativeFlexControl[i], v);
                Quaternion rotation = _nativeOpen[i];

                int spreadControl = _nativeSpreadControl[i];
                Vector3 spreadAxis = _nativeSpreadAxisLocal[i];
                if (spreadControl >= 0 && spreadAxis.sqrMagnitude > 1e-8f)
                {
                    float centeredSpread = (Mathf.Clamp01(v[spreadControl]) - 0.5f) * 2f;
                    rotation *= Quaternion.AngleAxis(centeredSpread * maxSpreadDeg, spreadAxis);
                }
                // Spread turns the plane of flexion around the open-palm normal.
                if (Mathf.Abs(weight) >= 1e-5f)
                    rotation *= Quaternion.SlerpUnclamped(Quaternion.identity, _nativeDelta[i], weight);
                t.localRotation = rotation;
            }
            ApplyNativeThumbOpposition(v);
        }

        float NativeFlexWeight(int control, float[] v)
        {
            if (control >= 0) return Mathf.Clamp01(v[control]);
            if (control == -2)
                return (Mathf.Clamp01(v[4]) + Mathf.Clamp01(v[8]) +
                        Mathf.Clamp01(v[12]) + Mathf.Clamp01(v[16])) * 0.25f;
            if (control == -3)
            {
                float sum = Mathf.Clamp01(v[0]) + Mathf.Clamp01(v[1]) + Mathf.Clamp01(v[2]);
                for (int o = 4; o <= 16; o += 4)
                    sum += Mathf.Clamp01(v[o]) + Mathf.Clamp01(v[o + 1]) + Mathf.Clamp01(v[o + 2]);
                return sum / 15f;
            }
            return 0f;
        }

        void ApplyNativeThumbOpposition(float[] v)
        {
            if (_nativeOppositionControl < 0 || _nativeThumbBaseIndex < 0 ||
                _nativeThumbEndpoint == null || _nativeIndexEndpoint == null) return;

            float centered = (Mathf.Clamp01(v[_nativeOppositionControl]) - 0.5f) * 2f;
            float amount = Mathf.Clamp01(Mathf.Abs(centered) * nativeOppositionStrength);
            if (amount < 1e-5f) return;

            var thumbBase = _nativeTransforms[_nativeThumbBaseIndex];
            var basis = thumbBase.parent;
            Vector3 pivot = thumbBase.localPosition;
            Vector3 thumbVector = basis.InverseTransformPoint(_nativeThumbEndpoint.position) - pivot;
            Vector3 indexVector = basis.InverseTransformPoint(_nativeIndexEndpoint.position) - pivot;
            if (thumbVector.sqrMagnitude < 1e-8f || indexVector.sqrMagnitude < 1e-8f) return;

            // Rotate the native thumb chain toward the currently posed index endpoint.
            // The inverse correction gives a continuous outward motion below neutral.
            Quaternion align = Quaternion.FromToRotation(thumbVector, indexVector);
            if (centered < 0f) align = Quaternion.Inverse(align);
            thumbBase.localRotation = Quaternion.Slerp(Quaternion.identity, align, amount) * thumbBase.localRotation;
        }

        void CacheNativeSpreadAxes()
        {
            string[] mcpParts = { "index_01", "middle_01", "ring_01", "pinky_01" };
            string[] endParts = { "index_03.R_end", "middle_03.R_end", "ring_03.R_end", "pinky_03.R_end" };
            var mcps = new Transform[4];
            var ends = new Transform[4];
            for (int f = 0; f < 4; f++)
            {
                mcps[f] = FindNativeSegmentTransform(mcpParts[f]);
                ends[f] = FindNativeSegmentTransform(endParts[f]);
            }
            var wrist = FindNativeSegmentTransform("hand.R_02");
            if (wrist == null || mcps.Any(b => b == null) || ends.Any(e => e == null))
            {
                Debug.LogWarning("[HandRigDriver] native spread MCP/end transforms are unavailable.");
                return;
            }

            Vector3 Local(Transform t) => transform.InverseTransformPoint(t.position);
            Vector3 palmLongitudinal = Local(mcps[1]) - Local(wrist);
            Vector3 palmLateral = Local(mcps[3]) - Local(mcps[0]);
            Vector3 palmNormal = Vector3.Cross(palmLongitudinal, palmLateral);
            if (palmNormal.sqrMagnitude < 1e-8f) return;
            palmNormal.Normalize();

            for (int f = 0; f < 4; f++)
            {
                Vector3 reference;
                if (f == 0) reference = Local(ends[1]);
                else if (f == 1) reference = (Local(ends[0]) + Local(ends[2])) * 0.5f;
                else if (f == 2) reference = (Local(ends[1]) + Local(ends[3])) * 0.5f;
                else reference = Local(ends[2]);

                Vector3 awayFromNeighbor = Local(ends[f]) - reference;
                Vector3 positiveMotion = Vector3.Cross(palmNormal, Local(ends[f]) - Local(mcps[f]));
                float sign = Vector3.Dot(positiveMotion, awayFromNeighbor) < 0f ? -1f : 1f;
                int index = FindNativeIndex(mcpParts[f]);
                if (index >= 0 && mcps[f].parent != null)
                    _nativeSpreadAxisLocal[index] = mcps[f].worldToLocalMatrix.MultiplyVector(
                        transform.localToWorldMatrix.MultiplyVector(palmNormal * sign)).normalized;
            }
        }

        int FindNativeIndex(string semanticSegment)
        {
            for (int i = 0; i < _nativeTransforms.Length; i++)
                if (_nativeTransforms[i] != null && ElenaNativeRigProfile.MatchesBone(_nativeTransforms[i].name, semanticSegment))
                    return i;
            return -1;
        }

        Transform FindNativeSegmentTransform(string segment)
        {
            int index = FindNativeIndex(segment);
            return index < 0 ? null : _nativeTransforms[index];
        }

        static int NativeFlexControlForName(string name)
        {
            int id = name.IndexOf(".R_", StringComparison.Ordinal);
            if (id >= 0) name = name.Substring(0, id);
            if (NativeNameHas(name, "hand.R.001")) return -2; // artist-authored palm closure
            if (NativeNameHas(name, "thumb"))
            {
                if (NativeNameHas(name, "thumb_base") || name.EndsWith("_Ctrl", StringComparison.OrdinalIgnoreCase)) return 0;
                if (NativeNameHas(name, "thumb_tip") || NativeNameHas(name, "_03")) return 2;
                if (NativeNameHas(name, "_01") || NativeNameHas(name, "_02")) return 1;
                return -1;
            }

            int finger = NativeNameHas(name, "index") ? 0 :
                         NativeNameHas(name, "middle") ? 1 :
                         NativeNameHas(name, "ring") ? 2 :
                         NativeNameHas(name, "pinky") ? 3 : -1;
            if (finger < 0) return -1;
            int firstControl = (finger + 1) * 4;
            if (NativeNameHas(name, "_base") || name.EndsWith("_Ctrl", StringComparison.OrdinalIgnoreCase) ||
                NativeNameHas(name, "_01")) return firstControl;
            if (NativeNameHas(name, "_02")) return firstControl + 1;
            if (NativeNameHas(name, "_03") || NativeNameHas(name, "_tip")) return firstControl + 2;
            return -1;
        }

        static bool NativeNameHas(string name, string part) =>
            !string.IsNullOrEmpty(name) && name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Legacy fallback: approximate flexion about the bone-local X axis.</summary>
        Quaternion BoneDelta(int i, float[] v, float s)
        {
            Quaternion q = Quaternion.identity;
            if (i < 3)
            {
                q = AngleAround(Vector3.right, Mathf.Clamp01(v[i]) * MaxFlex(i) * s) * q;
                if (i == 0) q = AngleAround(Vector3.forward, Mathf.Clamp01(v[3]) * 60f * s) * q;
                return q;
            }
            int f = (i - 3) / 3, j = (i - 3) % 3;
            if (f < 0 || f > 3) return q;
            int o = (f + 1) * 4;
            float maxF = j == 0 ? maxFlexionDeg : j == 1 ? maxFlexionDeg * 1.15f : maxFlexionDeg * 0.75f;
            q = AngleAround(Vector3.right, Mathf.Clamp01(v[o + j]) * maxF * s) * q;
            if (j == 1)
                q = AngleAround(Vector3.up, (Mathf.Clamp01(v[o + 3]) - 0.5f) * 2f * maxSpreadDeg * s) * q;
            return q;
        }

        static Quaternion AngleAround(Vector3 axis, float deg) =>
            Quaternion.AngleAxis(deg, axis.normalized);

        float MaxFlex(int i) => i == 2 ? maxFlexionDeg * 0.8f : maxFlexionDeg;

        Transform FindBoneForChannel(string bone)
        {
            foreach (var t in _allBones)
                if (t != null && (t.name == bone || t.name.StartsWith(bone + ".")))
                    return t;
            return null;
        }

        /// <summary>
        /// Profile-driven BONE-LOCAL euler delta with the FBX-import basis
        /// conversion. The imported Unity bone basis differs from the Blender
        /// one by the axis-angle transform (+,−,−) per bone (Codex run14
        /// correspondence: inverting the Blender-interpreted Y/Z angles makes
        /// multi-axis poses match open/inder30 exactly). The profile keeps its
        /// Blender anatomical semantics; here the Y/Z euler components are
        /// negated before composing in Blender XYZ order Rz*Ry*Rx.
        /// </summary>
        Quaternion ProfileDelta(int boneIdx, float[] v)
        {
            Vector3 e = Vector3.zero;
            bool any = false;
            for (int c = 0; c < _profile.Length; c++)
            {
                var ch = _profile[c];
                Transform t = FindBoneForChannel(ch.bone);
                if (t == null || t != _allBones[boneIdx]) continue;
                float deg = (v[c] - ch.rest) * ch.scale * ch.sign;
                Vector3 add = AxisEuler(ch.axis, deg);
                e.x += add.x; e.y += add.y; e.z += add.z;
                any = true;
            }
            if (!any) return Quaternion.identity;
            // FBX basis conversion: X angle keeps its sign, Y and Z negate.
            float sx = e.x;
            float anatomicalSide = IsMirrored ? -1f : 1f;
            float sy = -e.y * anatomicalSide;
            float sz = -e.z * anatomicalSide;
            // Composed in Blender XYZ order Rz(sz) * Ry(sy) * Rx(sx).
            Quaternion rx = Quaternion.Euler(sx, 0, 0);
            Quaternion ry = Quaternion.Euler(0, sy, 0);
            Quaternion rz = Quaternion.Euler(0, 0, sz);
            return rz * ry * rx;
        }

        static Vector3 AxisEuler(string axis, float deg)
        {
            switch (axis)
            {
                case "Y": return new Vector3(0, deg, 0);
                case "Z": return new Vector3(0, 0, deg);
                default: return new Vector3(deg, 0, 0);
            }
        }

        /// <summary>Neutral pose: smoothing state returns to each channel's rest
        /// (flex=0, twist/spread=0.5) so spread/opposition don't sweep from 0.</summary>
        public void ResetControls()
        {
            if (_nativeTransforms != null)
            {
                for (int c = 0; c < 20; c++)
                {
                    float rest = nativeProfile.openControls != null &&
                                 nativeProfile.openControls.Length == 20
                        ? nativeProfile.openControls[c]
                        : (c % 4 == 3 ? 0.5f : 0f);
                    _current[c] = rest;
                    _target[c] = rest;
                }
                ApplyNative(_target);
                return;
            }
            for (int c = 0; c < 20; c++)
            {
                float rest = 0f;
                if (_profile != null) rest = _profile[c].rest;
                else if (c % 4 == 3) rest = 0.5f; // legacy: spread centered
                _current[c] = rest;
                _target[c] = rest;
            }
            if (_allBones != null) ResetToRest();
        }
    }
}
