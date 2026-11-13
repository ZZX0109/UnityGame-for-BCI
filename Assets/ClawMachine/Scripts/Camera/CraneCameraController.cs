using System;
using UnityEngine;

namespace ClawMachine
{
    public sealed class CraneCameraController : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera[] views;
        [SerializeField] private Transform lookTarget;
        [SerializeField, Min(0.5f)] private float minimumOrbitRadius = 1.75f;
        [SerializeField, Min(0.01f)] private float obstructionProbeRadius = 0.08f;
        [SerializeField] private LayerMask obstructionLayers = ~0;
        [SerializeField, Range(0.02f, 1)] private float dragDegreesPerPixel = 0.15f;
        [SerializeField] private bool pipEnabled;
        [SerializeField] private bool pipLowQuality = true;
        private Vector3[] presetOffsets;
        private readonly Vector2[] orbitAngles = new Vector2[3];
        private readonly RaycastHit[] obstructionHits = new RaycastHit[24];
        private UnityEngine.Camera pipCamera;
        private RenderTexture pipTexture;
        private bool dragging;
        private Vector3 lastMousePosition;
        private int selected, distance = 1;
        public event Action Changed;
        public bool Smooth { get; set; } = true;
        public UnityEngine.Camera CurrentCamera => IsConfigured ? views[selected] : null;
        public RenderTexture PipTexture => pipTexture;
        public bool PipEnabled => pipEnabled && isActiveAndEnabled && pipCamera && pipTexture;
        public bool PipLowQuality => pipLowQuality;
        public Vector2 OrbitAngles => orbitAngles[selected];
        public int SelectedView => selected + 1;
        public int ObservationDistance => distance;
        public string PipViewName => selected == 0 ? "左侧 Left" : "正面 Front";
        public string ViewName => selected == 0 ? "正面 Front" : selected == 1 ? "左侧 Left" : "右侧 Right";
        public string DistanceName => distance == 0 ? "近" : distance == 1 ? "标准" : "远";
        public bool IsConfigured => views != null && views.Length == 3 && views[0] && views[1] && views[2] && lookTarget;
        private void Awake()
        {
            EnsurePresets();
            Select(1);
        }
        private void OnEnable()
        {
            if (EnsurePresets() && pipEnabled) CreatePipResources();
        }

        // Input remains machine independent: orbit changes observation only, never WASD axes.
        private void Update()
        {
            if (!IsConfigured) return;
            if (Input.GetMouseButtonDown(1) && CurrentCamera.pixelRect.Contains(Input.mousePosition))
            {
                dragging = true;
                lastMousePosition = Input.mousePosition;
            }
            if (!Input.GetMouseButton(1)) { dragging = false; return; }
            if (!dragging) return;
            Vector3 mouse = Input.mousePosition;
            Vector3 delta = mouse - lastMousePosition;
            lastMousePosition = mouse;
            OrbitBy(new Vector2(delta.x, delta.y) * dragDegreesPerPixel);
        }

        public void Select(int number)
        {
            if (!EnsurePresets() || number < 1 || number > 3) return;
            selected = number - 1;
            for (int i = 0; i < views.Length; i++) views[i].enabled = i == selected;
            dragging = false;
            UpdatePipPose();
            Changed?.Invoke();
        }
        public void CycleDistance()
        {
            distance = (distance + 1) % 3;
            Changed?.Invoke();
        }
        public void Zoom(float amount)
        {
            if (!ClawCameraOrbit.IsFinite(amount) || amount == 0) return;
            int next = Mathf.Clamp(distance - (amount > 0 ? 1 : -1), 0, 2);
            if (next == distance) return;
            distance = next;
            Changed?.Invoke();
        }
        public void OrbitBy(Vector2 deltaDegrees)
        {
            if (!ClawCameraOrbit.IsFinite(deltaDegrees.x) || !ClawCameraOrbit.IsFinite(deltaDegrees.y)) return;
            Vector2 next = ClawCameraOrbit.ClampAngles(orbitAngles[selected] + new Vector2(
                Mathf.Clamp(deltaDegrees.x, -360, 360), Mathf.Clamp(deltaDegrees.y, -360, 360)));
            if (next == orbitAngles[selected]) return;
            orbitAngles[selected] = next;
            Changed?.Invoke();
        }
        public void ResetOrbit()
        {
            orbitAngles[selected] = Vector2.zero;
            Changed?.Invoke();
        }
        public void TogglePip() => SetPipEnabled(!pipEnabled);
        public void SetPipEnabled(bool value)
        {
            pipEnabled = value;
            if (value && isActiveAndEnabled && EnsurePresets()) CreatePipResources();
            else ReleasePipResources();
            Changed?.Invoke();
        }
        // Low performance mode uses one 320x240 render target; standard uses 480x360.
        public void SetPipQuality(bool lowPerformance)
        {
            if (pipLowQuality == lowPerformance) return;
            pipLowQuality = lowPerformance;
            ReleasePipResources();
            if (pipEnabled && isActiveAndEnabled && EnsurePresets()) CreatePipResources();
            Changed?.Invoke();
        }
        private void LateUpdate()
        {
            if (!EnsurePresets()) { ReleasePipResources(); return; }
            UnityEngine.Camera camera = views[selected];
            Vector3 wanted = DesiredPosition(selected);
            Vector3 next = Smooth ? Vector3.Lerp(camera.transform.position, wanted, 1 - Mathf.Exp(-10 * Time.unscaledDeltaTime)) : wanted;
            camera.transform.position = ResolveObstruction(KeepOutsideCabinet(next), selected);
            camera.transform.LookAt(lookTarget.position, transform.up);
            if (pipEnabled && !pipCamera) CreatePipResources();
            UpdatePipPose();
        }

        private bool EnsurePresets()
        {
            if (!IsConfigured) return false;
            if (presetOffsets != null) return true;
            presetOffsets = new Vector3[3];
            Vector3[] fallback = { Vector3.back, Vector3.left, Vector3.right };
            for (int i = 0; i < views.Length; i++)
            {
                Vector3 offset = transform.InverseTransformVector(views[i].transform.position - lookTarget.position);
                presetOffsets[i] = ClawCameraOrbit.IsFinite(offset) && offset.sqrMagnitude > 0.01f ? offset : fallback[i] * 3.45f;
            }
            return true;
        }
        private float SafeRadius => ClawCameraOrbit.IsFinite(minimumOrbitRadius) ? Mathf.Clamp(minimumOrbitRadius, 0.5f, 10) : 1.75f;
        private Vector3 DesiredPosition(int index)
        {
            float scale = distance == 0 ? 0.85f : distance == 1 ? 1 : 1.2f;
            Vector3 basis = transform.TransformVector(presetOffsets[index]);
            Vector3 offset = ClawCameraOrbit.Offset(basis, orbitAngles[index], scale, SafeRadius, transform.up);
            return ResolveObstruction(lookTarget.position + offset, index);
        }
        private Vector3 KeepOutsideCabinet(Vector3 position)
        {
            Vector3 offset = position - lookTarget.position;
            if (!ClawCameraOrbit.IsFinite(offset) || offset.sqrMagnitude < 0.01f) offset = transform.TransformVector(presetOffsets[selected]);
            return lookTarget.position + offset.normalized * Mathf.Max(SafeRadius, offset.magnitude);
        }
        private Vector3 ResolveObstruction(Vector3 desired, int index)
        {
            Vector3 origin = lookTarget.position;
            Vector3 ray = desired - origin;
            float length = ray.magnitude;
            if (length < SafeRadius || !ClawCameraOrbit.IsFinite(ray)) return KeepOutsideCabinet(desired);
            float probe = ClawCameraOrbit.IsFinite(obstructionProbeRadius) ? Mathf.Clamp(obstructionProbeRadius, 0.01f, 0.25f) : 0.08f;
            int count = Physics.SphereCastNonAlloc(origin, probe, ray / length, obstructionHits, length,
                obstructionLayers, QueryTriggerInteraction.Ignore);
            float allowed = length;
            // The target is inside the cabinet. Its own glass/colliders must not pull a camera inside it.
            Transform machine = lookTarget.root;
            for (int i = 0; i < count; i++)
            {
                Collider hit = obstructionHits[i].collider;
                if (!hit || hit.transform.IsChildOf(machine)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0, obstructionHits[i].distance - 0.04f));
            }
            if (allowed >= SafeRadius) return origin + ray.normalized * allowed;
            // No room exists on this ray outside the cabinet: retain the last safe preset pose.
            Vector3 previous = views[index].transform.position;
            return ClawCameraOrbit.IsFinite(previous) && (previous - origin).magnitude >= SafeRadius ? previous : origin + ray.normalized * SafeRadius;
        }

        private void CreatePipResources()
        {
            if (pipCamera || !pipEnabled || !isActiveAndEnabled || !EnsurePresets()) return;
            int width = pipLowQuality ? 320 : 480;
            int height = pipLowQuality ? 240 : 360;
            pipTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) {
                name = "Claw PIP (owned)", antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            if (!pipTexture.Create())
            {
                ReleasePipResources(); pipEnabled = false;
                Changed?.Invoke(); return;
            }
            var cameraObject = new GameObject("Claw PIP Camera (owned)") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(transform, false);
            pipCamera = cameraObject.AddComponent<UnityEngine.Camera>();
            UpdatePipPose();
            Changed?.Invoke();
        }
        private void UpdatePipPose()
        {
            if (!pipCamera || !pipTexture || !IsConfigured || presetOffsets == null) return;
            int side = selected == 0 ? 1 : 0;
            pipCamera.CopyFrom(views[side]);
            pipCamera.targetTexture = pipTexture;
            pipCamera.rect = new Rect(0, 0, 1, 1);
            pipCamera.allowHDR = false;
            pipCamera.allowMSAA = false;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) pipCamera.cullingMask &= ~(1 << uiLayer);
            pipCamera.transform.position = DesiredPosition(side);
            pipCamera.transform.LookAt(lookTarget.position, transform.up);
            pipCamera.enabled = pipEnabled && isActiveAndEnabled;
        }
        private void ReleasePipResources()
        {
            if (pipCamera)
            {
                pipCamera.enabled = false;
                pipCamera.targetTexture = null;
                DestroyOwned(pipCamera.gameObject);
                pipCamera = null;
            }
            if (pipTexture)
            {
                pipTexture.Release();
                DestroyOwned(pipTexture);
                pipTexture = null;
            }
        }
        private static void DestroyOwned(UnityEngine.Object owned)
        {
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }
        private void OnDisable()
        {
            dragging = false;
            ReleasePipResources();
            Changed?.Invoke();
        }
        private void OnDestroy() => ReleasePipResources();
    }
}
