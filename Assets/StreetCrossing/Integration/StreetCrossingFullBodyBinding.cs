using System;
using StreetCrossing.Player;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StreetCrossing.Integration
{
    /// <summary>
    /// Glues the VirtualHand full-body prefab (Game04 source of truth) onto the Game03
    /// StreetCrossing player without touching any host script.
    ///
    /// Responsibilities:
    ///  - instantiate <see cref="BodyPrefabPath"/> at runtime, parented under the
    ///    PedestrianPlayerController root so ObservationTracker's raycast filter
    ///    (hit.IsChildOf(playerRoot)) still treats the body as "self" and lets gaze through;
    ///  - add <see cref="GameIntegrationAdapter"/> at runtime (the prefab intentionally
    ///    ships without it — FirstPersonFullBodyBuilder.cs:33 "belongs to Game02/Game03");
    ///  - feed root-motion-derived locomotion (m/s in body space + yaw deg/s) into
    ///    <see cref="GameIntegrationAdapter.FeedLocomotion"/> every LateUpdate;
    ///  - expose keyboard-only toggles: B = first-person visibility, V = appearance mode.
    ///    Keys are chosen to never collide with the host's LegacyStreetInput binding
    ///    (E = interact, R = restart, Escape = pause, Shift = fast walk).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreetCrossingFullBodyBinding : MonoBehaviour
    {
        /// <summary>
        /// Project-relative path of the full-body prefab. The prefab lives under a Resources
        /// folder so player builds resolve it via <c>Resources.Load("FirstPersonFullBody")</c>
        /// (see <see cref="LoadBodyPrefab"/>); in the editor it is also loadable via AssetDatabase.
        /// </summary>
        public const string BodyPrefabPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";

        /// <summary>
        /// Resources-relative name of the prefab, used only by the player-build fallback in
        /// <see cref="LoadBodyPrefab"/> (Resources.Load paths are project-Resources-relative,
        /// without the .prefab extension).
        /// </summary>
        const string BodyPrefabResourceName = "FirstPersonFullBody";

        /// <summary>
        /// Per-frame planar displacement gate (metres). Below this the avatar is considered
        /// standing still and a zero-velocity feed is issued instead, so the WalkCycleDriver
        /// (blend threshold 0.01 m/s) settles back to idle.
        /// </summary>
        public const float MinDisplacementPerFrameMeters = 0.001f;

        /// <summary>
        /// Largest deltaTime accepted for a velocity feed (seconds). Larger deltas mean a
        /// hitch, scene load or teleport: the frame is dropped and the baseline re-primed so
        /// the differential never turns a discontinuity into a fake sprint.
        /// </summary>
        public const float MaxDeltaTimeSeconds = 0.5f;

        /// <summary>Minimum yaw change per frame (degrees) treated as an actual turn.</summary>
        public const float MinYawDeltaPerFrameDegrees = 0.01f;

        /// <summary>The instance created by <see cref="EnsureWiring"/>; null until wiring succeeds.</summary>
        public GameObject BodyInstance { get; private set; }

        /// <summary>Adapter added to <see cref="BodyInstance"/> at runtime (never stored on the prefab).</summary>
        public GameIntegrationAdapter Adapter { get; private set; }

        /// <summary>Transform the body is parented under (PedestrianPlayerController root).</summary>
        public Transform PlayerRoot { get; private set; }

        /// <summary>True once the body was instantiated and the adapter wired.</summary>
        public bool IsWired => Adapter != null && BodyInstance != null && PlayerRoot != null;

        bool _primed;              // baseline pose stored, differential feeds allowed
        bool _firstPerson = true;  // binding owns the toggle state; starts hidden-head (standing)
        bool _blueAppearance;      // V cycles Realistic <-> Blue
        Vector3 _prevPosition;
        Vector3 _prevForward;
        bool _warnedPrefabMissing;

        /// <summary>
        /// Runtime bootstrap. AfterSceneLoad guarantees scene objects (the player controller)
        /// already exist; <see cref="EnsureWiring"/> keeps retrying otherwise.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Bootstrap()
        {
            var host = new GameObject("StreetCrossingFullBodyBinding");
            DontDestroyOnLoad(host);
            host.AddComponent<StreetCrossingFullBodyBinding>();
        }

        void Update()
        {
            EnsureWiring();

            PollKeyboard();

            // Re-bind after a scene reload destroyed the player or the body instance.
            if (!IsWired)
                _primed = false;
        }

        void LateUpdate()
        {
            TickLocomotion(Time.deltaTime);
        }

        /// <summary>
        /// Idempotently instantiates + wires the full-body prefab under the player root.
        /// Public so EditMode tests (and hosts with unusual bootstrap orders) can drive it.
        /// </summary>
        public bool EnsureWiring()
        {
            if (IsWired)
                return true;

            var player = FindObjectOfType<PedestrianPlayerController>();
            if (player == null)
                return false;

            PlayerRoot = player.transform;

            if (BodyInstance == null)
            {
                var prefab = LoadBodyPrefab();
                if (prefab == null)
                {
                    if (!_warnedPrefabMissing)
                    {
                        _warnedPrefabMissing = true;
                        Debug.LogWarning($"[StreetCrossingFullBodyBinding] Full-body prefab not found at '{BodyPrefabPath}'. " +
                                         "In player builds the prefab must live under a Resources folder as 'FirstPersonFullBody'; retrying every frame.");
                    }

                    PlayerRoot = null; // retry next frame
                    return false;
                }

                BodyInstance = Instantiate(prefab);
                BodyInstance.name = "FirstPersonFullBody(StreetCrossing)";
                BodyInstance.transform.SetParent(PlayerRoot, false);
                BodyInstance.transform.localPosition = Vector3.zero;
                BodyInstance.transform.localRotation = Quaternion.identity;
            }

            if (Adapter == null)
            {
                // The prefab deliberately ships without the adapter (Game02/Game03 hosts add
                // it themselves). AddComponent fires Awake() => Wire(), which auto-populates
                // the four public fields; the null-guard fallback below only fills any field
                // Wire() could not find, using the same lookup Wire() uses.
                Adapter = BodyInstance.GetComponent<GameIntegrationAdapter>();
                if (Adapter == null)
                    Adapter = BodyInstance.AddComponent<GameIntegrationAdapter>();

                if (Adapter.poseController == null)
                    Adapter.poseController = BodyInstance.GetComponentInChildren<FullBodyPoseController>(true);
                if (Adapter.locomotion == null)
                    Adapter.locomotion = BodyInstance.GetComponentInChildren<LowerBodyPoseController>(true);
                if (Adapter.visibility == null)
                    Adapter.visibility = BodyInstance.GetComponentInChildren<FirstPersonVisibility>(true);
                if (Adapter.appearance == null)
                    Adapter.appearance = BodyInstance.GetComponentInChildren<FullBodyAppearanceController>(true);
            }

            // Standing pose + first-person (head meshes hidden via FirstPersonVisibility's
            // pre-wired HiddenRenderers list; no extra hide logic added here).
            Adapter.SetSeated(false);
            Adapter.SetFirstPerson(true);
            _firstPerson = true;
            _blueAppearance = false;

            PrimeBaseline(PlayerRoot.position, PlayerRoot.forward);
            return true;
        }

        /// <summary>
        /// One differential locomotion cycle: diffs the player root's planar pose against the
        /// previous frame, converts to body-space velocity + yaw rate and feeds the adapter.
        /// LateUpdate calls this with <see cref="Time"/>.deltaTime; tests call it explicitly.
        /// </summary>
        public void TickLocomotion(float deltaTime)
        {
            if (!IsWired)
                return;

            Vector3 position = PlayerRoot.position;
            Vector3 forward = PlayerRoot.forward;

            if (!_primed)
            {
                PrimeBaseline(position, forward);
                return;
            }

            Vector3 delta = position - _prevPosition;
            float planarDisplacement = new Vector2(delta.x, delta.z).magnitude;

            // Yaw derives from the forward vector alone, so rotation-in-place frames
            // (zero planar displacement) must still feed the turn rate (30° / 0.1 s = 300°/s).
            float yawDelta = SignedYawDeltaDegrees(_prevForward, forward);
            float yawRate = Mathf.Abs(yawDelta) < MinYawDeltaPerFrameDegrees ? 0f : yawDelta / deltaTime;

            if (deltaTime <= 0f || deltaTime > MaxDeltaTimeSeconds)
            {
                // No usable dt (hitch/teleport): report "standing still" so the walk cycle
                // decays instead of freezing at the last stride, then re-prime.
                Adapter.FeedLocomotion(Vector2.zero, 0f);
                PrimeBaseline(position, forward);
                return;
            }

            if (planarDisplacement <= MinDisplacementPerFrameMeters)
            {
                // Pivot turn in place: translation is zero but the yaw rate is real.
                Adapter.FeedLocomotion(Vector2.zero, yawRate);
                PrimeBaseline(position, forward);
                return;
            }

            // Body space: +X = wearer's right, +Y = forward (FullBodyPoseController header).
            Vector3 right = PlayerRoot.right;
            Vector2 planarVelocity = new Vector2(
                Vector3.Dot(delta, Flatten(right)) / deltaTime,
                Vector3.Dot(delta, Flatten(forward)) / deltaTime);

            Adapter.FeedLocomotion(planarVelocity, yawRate);
            PrimeBaseline(position, forward);
        }

        /// <summary>B toggles first-person visibility (host keys E/R/Escape/Shift untouched).</summary>
        public void ToggleFirstPerson()
        {
            _firstPerson = !_firstPerson;
            if (Adapter != null)
                Adapter.SetFirstPerson(_firstPerson);
        }

        /// <summary>V cycles the appearance mode: Realistic -> Blue -> Realistic ...</summary>
        public void CycleAppearance()
        {
            _blueAppearance = !_blueAppearance;
            if (Adapter != null)
                Adapter.SetAppearanceMode(_blueAppearance ? FullBodyAppearanceMode.Blue : FullBodyAppearanceMode.Realistic);
        }

        void PollKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.bKey.wasPressedThisFrame)
                ToggleFirstPerson();
            if (keyboard.vKey.wasPressedThisFrame)
                CycleAppearance();
#endif
        }

        void PrimeBaseline(Vector3 position, Vector3 forward)
        {
            _prevPosition = position;
            _prevForward = Flatten(forward);
            _primed = true;
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.normalized;
        }

        /// <summary>Signed planar yaw change in degrees (positive = turning left, CCW around +Y).</summary>
        static float SignedYawDeltaDegrees(Vector3 fromForward, Vector3 toForward)
        {
            float fromYaw = Mathf.Atan2(fromForward.x, fromForward.z) * Mathf.Rad2Deg;
            float toYaw = Mathf.Atan2(toForward.x, toForward.z) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(fromYaw, toYaw);
        }

        static GameObject LoadBodyPrefab()
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);
            if (asset != null)
                return asset;
#endif
            // Player-build fallback: resolves because the prefab lives under
            // Assets/StreetCrossing/Resources (see BodyPrefabPath). Also reached in the
            // editor when the AssetDatabase load above misses.
            return Resources.Load<GameObject>(BodyPrefabResourceName);
        }
    }
}
