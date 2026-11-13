using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClawMachine
{
    /// <summary>Bounded showroom fill lights and one reflection capture, owned by the scene.</summary>
    public sealed class ClawShowroomLighting : MonoBehaviour
    {
        private GameObject ownedRoot;
        public ReflectionProbe Probe { get; private set; }
        private void OnEnable()
        {
            if (ownedRoot) return;
            ownedRoot = new GameObject("Claw showroom lighting (owned)");
            ownedRoot.transform.SetParent(transform, false);
            Fill("Cabinet softbox", new Vector3(0, 1.89f, 0.10f), new Color(1, 0.95f, 0.87f), 0.58f, 2.2f);
            Fill("Console fill", new Vector3(-0.85f, 1.2f, -0.9f), new Color(0.81f, 0.94f, 1), 0.24f, 2.4f);
            var probeObject = new GameObject("One-shot showroom reflection");
            probeObject.transform.SetParent(ownedRoot.transform, false);
            probeObject.transform.localPosition = new Vector3(0, 1.2f, 0);
            Probe = probeObject.AddComponent<ReflectionProbe>();
            Probe.mode = ReflectionProbeMode.Realtime;
            Probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            Probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            Probe.resolution = 128; Probe.size = new Vector3(9, 5, 9);
            Probe.boxProjection = true; Probe.intensity = 0.55f;
            Probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            Probe.backgroundColor = new Color(0.76f, 0.79f, 0.73f);
            Probe.cullingMask = ~(1 << 5); Probe.nearClipPlane = 0.1f; Probe.farClipPlane = 15;
            StartCoroutine(CaptureAfterSceneSetup());
        }
        private IEnumerator CaptureAfterSceneSetup()
        {
            // Mesh OnEnable and scene light setup have completed. Do not recapture
            // every frame or add six extra camera renders to ordinary gameplay.
            yield return null;
            if (Probe && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Probe.RenderProbe();
        }
        private void Fill(string label, Vector3 position, Color color, float intensity, float range)
        {
            var node = new GameObject(label); node.transform.SetParent(ownedRoot.transform, false);
            node.transform.localPosition = position;
            Light source = node.AddComponent<Light>(); source.type = LightType.Point;
            source.color = color; source.intensity = intensity; source.range = range;
            source.shadows = LightShadows.None; source.renderMode = LightRenderMode.ForcePixel;
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            if (ownedRoot)
            {
                ownedRoot.SetActive(false);
                if (Application.isPlaying) Destroy(ownedRoot); else DestroyImmediate(ownedRoot);
            }
            ownedRoot = null; Probe = null;
        }
    }
}
