using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neuralife.XR
{
    [DefaultExecutionOrder(-8000)]
    public sealed class PicoCanvas : MonoBehaviour
    {
        internal static readonly List<PicoCanvas> All = new List<PicoCanvas>();
        public Canvas Canvas { get; private set; }
        private bool reposition = true;
        private Camera view;
        private RectTransform rect;
        private Vector3 lastHead;
        private float nextStyle;
        private readonly List<Graphic> graphics = new List<Graphic>();
        private readonly Dictionary<Material, Material> overlays = new Dictionary<Material, Material>();
        private readonly HashSet<Material> ownedMaterials = new HashSet<Material>();

        public static void Configure(Canvas canvas, Vector2 resolution)
        {
            if (!PicoXR.Requested || !canvas || canvas.GetComponent<PicoCanvas>()) return;
            var item = canvas.gameObject.AddComponent<PicoCanvas>();
            item.Canvas = canvas; item.rect = canvas.GetComponent<RectTransform>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 30000;
            item.rect.anchorMin = item.rect.anchorMax = item.rect.pivot = Vector2.one * 0.5f;
            item.rect.sizeDelta = resolution;
            item.rect.localScale = Vector3.one * (2.6f / resolution.x);
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1; scaler.dynamicPixelsPerUnit = 2; }
            if (!canvas.GetComponent<GraphicRaycaster>()) canvas.gameObject.AddComponent<GraphicRaycaster>();
            InstallInputModule();
            if (!canvas.GetComponent<PicoMenuNavigation>()) canvas.gameObject.AddComponent<PicoMenuNavigation>();
        }

        private static void InstallInputModule()
        {
            EventSystem events = EventSystem.current;
            if (!events) events = new GameObject("PICO UI EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            foreach (BaseInputModule module in events.GetComponents<BaseInputModule>())
                if (!(module is PicoRayInputModule)) module.enabled = false;
            if (!events.GetComponent<PicoRayInputModule>()) events.gameObject.AddComponent<PicoRayInputModule>();
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); reposition = true; }
        private void OnDisable() { All.Remove(this); }
        public static void RecenterAll() { foreach (PicoCanvas item in All) if (item) item.reposition = true; }
        private void LateUpdate()
        {
            if (!Canvas) return;
            if (Time.unscaledTime >= nextStyle) { ApplyOverlayMaterials(); nextStyle = Time.unscaledTime + 0.25f; }
            if (!view || !view.isActiveAndEnabled) view = Camera.main;
            if (!view) return;
            Canvas.worldCamera = view;
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = transform.forward;
            bool menuOpen = PicoMenuNavigation.Current && PicoMenuNavigation.Current.HasMenu;
            if (reposition || (menuOpen && (Vector3.Distance(lastHead, view.transform.position) > 0.75f || Vector3.Angle(transform.forward, forward) > 55)))
            {
                transform.position = view.transform.position + forward * 1.6f + Vector3.down * 0.05f;
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                lastHead = view.transform.position; reposition = false;
            }
        }
        public void ApplyOverlayMaterials()
        {
            graphics.Clear(); GetComponentsInChildren(true, graphics);
            foreach (Graphic graphic in graphics)
            {
                if (!(graphic is Image) && !(graphic is Text) && !(graphic is RawImage)) continue;
                Material original = graphic.material;
                if (!original || ownedMaterials.Contains(original)) continue;
                if (!overlays.TryGetValue(original, out Material overlay))
                {
                    overlay = new Material(original) { name = original.name + " VR overlay" };
                    if (graphic is Image || graphic is Text || graphic is RawImage)
                        overlay.shader = Resources.Load<Shader>("NeuralifeVRUI");
                    if (overlay.HasProperty("_ZTest")) overlay.SetInt("_ZTest", 8);
                    overlay.renderQueue = 3000;
                    overlays.Add(original, overlay); ownedMaterials.Add(overlay);
                }
                graphic.material = overlay;
            }
        }
        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials) if (material) Destroy(material);
            ownedMaterials.Clear(); overlays.Clear();
        }
    }
}
