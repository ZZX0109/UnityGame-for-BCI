using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// Procedural walk cycle for the full-body avatar legs.  The stride phase is tied to
    /// the monotonic path distance integrated by <see cref="LowerBodyPoseController"/>
    /// (<c>p = 2π · path / strideLength</c>), so faster input steps faster and reversing
    /// keeps stepping forward.  Per leg (left phase q = p, right phase q = p + π):
    /// hip = −A_h·cos(q) with A_h in degrees (leg most forward at q = π, heel strike;
    /// most backward at q = 0, toe-off), knee = A_k·max(0, sin q)² — a swing-windowed
    /// bump that peaks at mid-swing and is exactly straight through the whole stance
    /// half (no spurious mid-stance buckling), ankle = knee − hip in anatomical signs
    /// (hip + = forward, knee + = flexion, ankle + = toes up), which keeps the foot at
    /// a constant world orientation while the joint pitch axes are parallel.  The
    /// stride length auto-derives from the exact zero-net-drift condition
    /// S = 4·L_eff·sin(A_h) with L_eff the runtime-measured hip→ankle length.
    /// Bones are driven relative to their cached rest local rotations about the
    /// rest-frame local X axis; per-joint sign probes at bind time resolve flipped
    /// Blender export axes (no user configuration).  When the avatar stops, everything
    /// blends back to rest with a frame-rate-independent exponential
    /// (k = 1 − exp(−dt/τ)).  This component only animates leg bones — it never moves
    /// the root transform; hosts own locomotion.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Body/Walk Cycle Driver")]
    [DisallowMultipleComponent]
    public sealed class WalkCycleDriver : MonoBehaviour
    {
        const float MoveBlendSpeedThreshold = 0.01f; // m/s
        const float ProbeAngleDegrees = 10f;
        const float ProbeBlindThreshold = 0.001f;    // m; below this the probe axis is considered blind
        const float DeltaTimeClampSeconds = 0.1f;    // guards stall frames from a near-instant blend snap

        [Header("Phase source")]
        [Tooltip("Locomotion integrator supplying PathDistanceMeters. Auto-found on the same root when empty.")]
        [SerializeField] LowerBodyPoseController locomotion;

        [Header("Leg bones (auto-found by manifest names when empty)")]
        [SerializeField] Transform leftThigh;
        [SerializeField] Transform leftCalf;
        [SerializeField] Transform leftFoot;
        [SerializeField] Transform rightThigh;
        [SerializeField] Transform rightCalf;
        [SerializeField] Transform rightFoot;

        [Header("Gait tuning")]
        [Tooltip("Metres of travelled path per full gait cycle (two steps). 0 auto-computes from leg length.")]
        [SerializeField] float strideLengthMeters = 0f;
        [Tooltip("Thigh swing amplitude in degrees (peak forward/backward). V5-075 fix②: 25→15 removes crotch clipping on the shipped rig.")]
        [SerializeField] float thighAmplitudeDegrees = 15f;
        [Tooltip("Knee flexion amplitude in degrees (peak at mid-swing only). V5-075 fix②: 45→30 removes knee clipping on the shipped rig.")]
        [SerializeField] float kneeAmplitudeDegrees = 30f;
        [Tooltip("Exponential blend time constant to/from rest, seconds.")]
        [SerializeField] float blendTauSeconds = 0.05f;

        Quaternion _leftThighRest = Quaternion.identity;
        Quaternion _leftCalfRest = Quaternion.identity;
        Quaternion _leftFootRest = Quaternion.identity;
        Quaternion _rightThighRest = Quaternion.identity;
        Quaternion _rightCalfRest = Quaternion.identity;
        Quaternion _rightFootRest = Quaternion.identity;
        // +1 when a positive angle about the bone's rest-frame local X equals the
        // anatomical direction (hip: thigh forward; knee: flexion; ankle: toes up).
        // Detected on the left leg at bind time and assumed symmetric across the rig.
        float _hipSign = 1f;
        float _kneeSign = 1f;
        float _ankleSign = 1f;
        float _moveBlend;
        bool _bound;

        /// <summary>Current stride phase in radians (0..2π) derived from path distance.</summary>
        public float PhaseRadians
        {
            get
            {
                float stride = ResolveStrideLength();
                return stride > 1e-4f ? 2f * Mathf.PI * LocomotionPathDistance() / stride : 0f;
            }
        }

        /// <summary>Current 0..1 blend weight between rest pose and full gait.</summary>
        public float MoveBlend { get { return _moveBlend; } }

        /// <summary>Resolved stride length actually used for the phase (metres).</summary>
        public float ResolvedStrideLengthMeters { get { return ResolveStrideLength(); } }

        void Awake()
        {
            Bind();
        }

        void LateUpdate()
        {
            Evaluate(Time.deltaTime);
        }

        /// <summary>
        /// Advances the move blend, computes the gait angles from the current phase and
        /// applies them to the six leg bones.  Public so host components (and tests) can
        /// drive evaluation explicitly instead of relying on LateUpdate.
        /// </summary>
        public void Evaluate(float deltaTime)
        {
            if (!_bound)
                Bind();
            if (!_bound || locomotion == null)
                return;

            float dt = Mathf.Clamp(deltaTime, 1e-5f, DeltaTimeClampSeconds);
            float tau = Mathf.Max(blendTauSeconds, 1e-3f);
            float speed = locomotion.PlanarVelocityMetersPerSecond.magnitude;
            float target = speed > MoveBlendSpeedThreshold ? 1f : 0f;
            float k = 1f - Mathf.Exp(-dt / tau);
            _moveBlend = Mathf.Lerp(_moveBlend, target, k);
            if (_moveBlend < 0.001f && target == 0f)
            {
                ApplyRest();
                return;
            }

            // Anatomical gait angles; q is each leg's own phase (right leads by π).
            float phase = PhaseRadians;
            ApplyLegSide(phase, leftThigh, leftCalf, leftFoot);
            ApplyLegSide(phase + Mathf.PI, rightThigh, rightCalf, rightFoot);
        }

        /// <summary>Instantly restores the cached rest pose (used when disabling or at zero blend).</summary>
        public void ApplyRest()
        {
            if (!_bound)
                return;
            ApplyLeg(leftThigh, _leftThighRest, 0f);
            ApplyLeg(leftCalf, _leftCalfRest, 0f);
            ApplyLeg(leftFoot, _leftFootRest, 0f);
            ApplyLeg(rightThigh, _rightThighRest, 0f);
            ApplyLeg(rightCalf, _rightCalfRest, 0f);
            ApplyLeg(rightFoot, _rightFootRest, 0f);
        }

        void ApplyLegSide(float legPhase, Transform thigh, Transform calf, Transform foot)
        {
            float hip = -thighAmplitudeDegrees * Mathf.Cos(legPhase);
            float swing = Mathf.Max(0f, Mathf.Sin(legPhase));
            float knee = kneeAmplitudeDegrees * swing * swing;
            float ankle = knee - hip; // anatomical: keeps the foot's world orientation constant
            float blend = _moveBlend;
            ApplyLeg(thigh, RestOf(thigh), _hipSign * hip * blend);
            ApplyLeg(calf, RestOf(calf), _kneeSign * knee * blend);
            ApplyLeg(foot, RestOf(foot), _ankleSign * ankle * blend);
        }

        Quaternion RestOf(Transform bone)
        {
            if (bone == leftThigh) return _leftThighRest;
            if (bone == leftCalf) return _leftCalfRest;
            if (bone == leftFoot) return _leftFootRest;
            if (bone == rightThigh) return _rightThighRest;
            if (bone == rightCalf) return _rightCalfRest;
            if (bone == rightFoot) return _rightFootRest;
            return bone.localRotation;
        }

        void OnDisable()
        {
            ApplyRest();
        }

        float LocomotionPathDistance()
        {
            return locomotion != null ? locomotion.PathDistanceMeters : 0f;
        }

        float ResolveStrideLength()
        {
            if (strideLengthMeters > 0.05f)
                return strideLengthMeters;
            // Exact zero-net-drift stride: the hip advances S/2 during a stance half-cycle
            // (distance-driven phase) while the relative foot travel is 2·L_eff·sin(A_h),
            // so S = 4·L_eff·sin(A_h) with A_h in radians.  Independent of speed profile.
            float legLength = LegLengthMeters();
            float amplitudeRad = Mathf.Max(thighAmplitudeDegrees, 1f) * Mathf.Deg2Rad;
            return Mathf.Max(4f * legLength * Mathf.Sin(amplitudeRad), 0.2f);
        }

        float LegLengthMeters()
        {
            float length = 0f;
            if (leftThigh != null && leftCalf != null)
                length += Vector3.Distance(leftThigh.position, leftCalf.position);
            if (leftCalf != null && leftFoot != null)
                length += Vector3.Distance(leftCalf.position, leftFoot.position);
            return length > 0.05f ? length : 0.87f; // manifest baseline: ~0.44 + 0.43 m hip→ankle
        }

        void Bind()
        {
            if (locomotion == null)
                locomotion = GetComponentInParent<LowerBodyPoseController>();
            bool missing = leftThigh == null || leftCalf == null || leftFoot == null
                || rightThigh == null || rightCalf == null || rightFoot == null;
            if (missing)
            {
                Transform root = ResolveBoneRoot();
                if (root == null)
                    return;
                leftThigh = FindBone(root, "thigh_l", leftThigh);
                leftCalf = FindBone(root, "calf_l", leftCalf);
                leftFoot = FindBone(root, "foot_l", leftFoot);
                rightThigh = FindBone(root, "thigh_r", rightThigh);
                rightCalf = FindBone(root, "calf_r", rightCalf);
                rightFoot = FindBone(root, "foot_r", rightFoot);
            }

            if (leftThigh == null || leftCalf == null || leftFoot == null
                || rightThigh == null || rightCalf == null || rightFoot == null)
            {
                Debug.LogWarning("[WalkCycleDriver] Leg bones not fully bound; walk cycle inactive until bones are assigned.", this);
                return;
            }

            _leftThighRest = leftThigh.localRotation;
            _leftCalfRest = leftCalf.localRotation;
            _leftFootRest = leftFoot.localRotation;
            _rightThighRest = rightThigh.localRotation;
            _rightCalfRest = rightCalf.localRotation;
            _rightFootRest = rightFoot.localRotation;
            _hipSign = DetectJointSign(leftThigh, leftCalf, ProbeDirection.Forward, "hip");
            _kneeSign = DetectJointSign(leftCalf, leftFoot, ProbeDirection.Backward, "knee");
            _ankleSign = DetectAnkleSign();
            _bound = true;
        }

        Transform ResolveBoneRoot()
        {
            if (leftThigh != null)
                return leftThigh.root;
            // Same root as the pose controller's body space holds the 53-bone rig.
            FullBodyPoseController pose = GetComponentInParent<FullBodyPoseController>();
            if (pose != null && pose.bodySpace != null)
                return pose.bodySpace.root;
            return transform.root;
        }

        static Transform FindBone(Transform root, string manifestName, Transform existing)
        {
            if (existing != null)
                return existing;
            string dotted = manifestName.Replace('_', '.');
            Transform found = root.Find(manifestName);
            if (found != null)
                return found;
            found = root.Find(dotted);
            if (found != null)
                return found;
            // Deep search — manifest names can sit under spine_0X parents.
            foreach (Transform child in root.GetComponentsInChildren<Transform>())
            {
                if (child.name == manifestName || child.name == dotted)
                    return child;
            }
            return null;
        }

        enum ProbeDirection
        {
            Forward,  // anatomical positive moves the child tip along the body forward axis
            Backward  // anatomical positive moves the child tip against the body forward axis
        }

        /// <summary>
        /// Wiggle probe (no user configuration, survives flipped Blender exports): applies
        /// a small rotation about the joint's rest-frame local X, measures the child tip's
        /// horizontal body-frame displacement and reverts.  Returns the sign that maps an
        /// anatomically positive angle onto the observed direction, or +1 when the probe
        /// axis is blind (near-orthogonal to the pitch axis).
        /// </summary>
        float DetectJointSign(Transform joint, Transform tip, ProbeDirection direction, string label)
        {
            if (joint == null || tip == null)
                return 1f;
            Vector3 bodyForward = GetBodyForward(joint);
            if (bodyForward.sqrMagnitude < 0.5f)
                return 1f;

            Quaternion rest = joint.localRotation;
            Vector3 tipBefore = tip.position;
            joint.localRotation = rest * Quaternion.AngleAxis(ProbeAngleDegrees, Vector3.right);
            Vector3 displacement = tip.position - tipBefore;
            joint.localRotation = rest;

            if (displacement.magnitude < ProbeBlindThreshold)
            {
                Debug.LogWarning($"[WalkCycleDriver] {label} sign probe moved the tip by only {displacement.magnitude * 1000f:F2} mm; assuming +1.", this);
                return 1f;
            }
            Vector3 flat = Vector3.ProjectOnPlane(displacement, Vector3.up);
            float dot = Vector3.Dot(flat, bodyForward);
            bool positive = direction == ProbeDirection.Forward ? dot > 0f : dot < 0f;
            return positive ? 1f : -1f;
        }

        /// <summary>
        /// Ankle probe: dorsiflexion (toes up) is measured as upward displacement of the
        /// ball joint when present; without a ball bone the knee's convention is reused
        /// (logged), which matches the common export case where foot and calf axes agree.
        /// </summary>
        float DetectAnkleSign()
        {
            Transform ball = FindBone(leftFoot, "ball_l", null);
            if (ball == null)
            {
                Debug.LogWarning("[WalkCycleDriver] ball_l not found; ankle sign reuses the knee probe result.", this);
                return _kneeSign;
            }
            Vector3 bodyUp = GetBodyUp(leftFoot);
            Quaternion rest = leftFoot.localRotation;
            Vector3 ballBefore = ball.position;
            leftFoot.localRotation = rest * Quaternion.AngleAxis(ProbeAngleDegrees, Vector3.right);
            Vector3 displacement = ball.position - ballBefore;
            leftFoot.localRotation = rest;

            if (displacement.magnitude < ProbeBlindThreshold)
            {
                Debug.LogWarning("[WalkCycleDriver] Ankle sign probe moved ball_l by less than 1 mm; reusing the knee probe result.", this);
                return _kneeSign;
            }
            // Anatomical dorsiflexion (toes up) lifts the ball joint along body up.
            return Vector3.Dot(displacement, bodyUp) > 0f ? 1f : -1f;
        }

        Vector3 GetBodyForward(Transform fallback)
        {
            FullBodyPoseController pose = GetComponentInParent<FullBodyPoseController>();
            if (pose != null && pose.bodySpace != null)
                return pose.bodySpace.forward;
            return fallback != null ? fallback.forward : Vector3.forward;
        }

        Vector3 GetBodyUp(Transform fallback)
        {
            FullBodyPoseController pose = GetComponentInParent<FullBodyPoseController>();
            if (pose != null && pose.bodySpace != null)
                return pose.bodySpace.up;
            return fallback != null ? fallback.up : Vector3.up;
        }

        void ApplyLeg(Transform bone, Quaternion rest, float signedAngleDegrees)
        {
            if (bone == null)
                return;
            if (signedAngleDegrees == 0f)
            {
                bone.localRotation = rest;
                return;
            }
            // Pitch about the rest-frame local X axis; the caller folds in the probe sign.
            bone.localRotation = rest * Quaternion.AngleAxis(signedAngleDegrees, Vector3.right);
        }
    }
}
