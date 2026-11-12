using System.Collections;
using System.Collections.Generic;
using FirstPersonSokoban.Core;
using FirstPersonSokoban.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonSokoban.Presentation
{
    /// <summary>A shallow visual-only induction target; completion never changes board logic.</summary>
    public sealed class SensorPlatformVisual : MonoBehaviour
    {
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Renderer> rings = new List<Renderer>();
        private LabMaterials palette;
        private Transform sweep;
        private Renderer sweepRenderer;
        private MaterialPropertyBlock block;
        private Coroutine sweepRoutine;
        private bool complete;
        private bool initialized;

        public bool IsSweeping => sweepRoutine != null;
        public bool IsComplete => complete;

        public static SensorPlatformVisual Apply(GoalView view, LabMaterials materials)
        {
            SensorPlatformVisual existing = view.GetComponent<SensorPlatformVisual>();
            if (existing != null) return existing;
            materials.Initialize();
            var visual = view.gameObject.AddComponent<SensorPlatformVisual>();
            visual.palette = materials;
            visual.Build();
            return visual;
        }

        private void Build()
        {
            // Hide the old square indicator without removing any rule-bearing component.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var root = new GameObject("Embedded Sensor Platform");
            root.transform.SetParent(transform, false);
            Primitive(root.transform, "Recessed Sensor Housing", PrimitiveType.Cylinder,
                new Vector3(0, 0.012f, 0), new Vector3(1.78f, 0.009f, 1.78f), palette.Dark);
            Ring(root.transform, "Machined Sensor Bezel", 0.855f, 0.89f, 0.023f, palette.Metal);
            rings.Add(Ring(root.transform, "Sensor Activation Ring", 0.765f, 0.795f, 0.029f, palette.GoalActive));
            Ring(root.transform, "Inner Sensor Track", 0.61f, 0.615f, 0.025f, palette.Trim);
            for (int i = 0; i < 4; i++)
            {
                Quaternion angle = Quaternion.Euler(0, i * 90f, 0);
                Transform tick = Primitive(root.transform, "Sensor Alignment Tick", PrimitiveType.Cube,
                    angle * new Vector3(0, 0.029f, 0.69f), new Vector3(0.028f, 0.005f, 0.10f), palette.GoalActive).transform;
                tick.localRotation = angle;
                rings.Add(tick.GetComponent<Renderer>());
            }
            Primitive(root.transform, "Sensor Centre X", PrimitiveType.Cube,
                new Vector3(0, 0.025f, 0), new Vector3(0.24f, 0.004f, 0.012f), palette.Trim);
            Primitive(root.transform, "Sensor Centre Y", PrimitiveType.Cube,
                new Vector3(0, 0.025f, 0), new Vector3(0.012f, 0.004f, 0.24f), palette.Trim);
            sweepRenderer = Ring(root.transform, "Completion Sweep", 0.80f, 0.817f, 0.033f, palette.GoalComplete);
            sweep = sweepRenderer.transform;
            sweep.gameObject.SetActive(false);
            block = new MaterialPropertyBlock();
            SetComplete(false);
        }

        private Renderer Ring(Transform parent, string name, float inner, float outer, float height, Material material)
        {
            const int segments = 64;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = direction * inner;
                vertices[i * 2 + 1] = direction * outer;
                uv[i * 2] = new Vector2((float)i / segments, 0);
                uv[i * 2 + 1] = new Vector2((float)i / segments, 1);
                if (i == segments) continue;
                int t = i * 6;
                int v = i * 2;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var ring = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = Vector3.up * height;
            ring.GetComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = ring.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static Renderer Primitive(Transform parent, string name, PrimitiveType type,
            Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        public void SetComplete(bool value, bool animate = true)
        {
            if (initialized && complete == value) return;
            initialized = true;
            complete = value;
            if (palette == null) return;
            foreach (Renderer ring in rings)
                if (ring != null) ring.sharedMaterial = value ? palette.GoalComplete : palette.GoalActive;
            StopSweep();
            if (value && animate && isActiveAndEnabled && sweep != null)
                sweepRoutine = StartCoroutine(CompletionSweep());
        }

        private IEnumerator CompletionSweep()
        {
            sweep.gameObject.SetActive(true);
            const float duration = 0.65f;
            Color success = new Color(0.447f, 0.886f, 0.541f);
            for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
            {
                float fraction = Mathf.Clamp01(elapsed / duration);
                float scale = GameSettings.ComfortMode ? 1f : Mathf.Lerp(0.55f, 1.08f, fraction);
                sweep.localScale = new Vector3(scale, 1, scale);
                // One gentle confirmation, not a repeated flash. No material allocation.
                Color tint = Color.Lerp(success, new Color(0.12f, 0.18f, 0.18f), fraction);
                block.SetColor("_Color", tint);
                block.SetColor("_EmissionColor", success * (1f - fraction) * 0.45f);
                sweepRenderer.SetPropertyBlock(block);
                yield return null;
            }
            sweep.gameObject.SetActive(false);
            sweepRoutine = null;
        }

        private void StopSweep()
        {
            if (sweepRoutine != null) StopCoroutine(sweepRoutine);
            sweepRoutine = null;
            if (sweep != null) sweep.gameObject.SetActive(false);
        }

        private void OnDisable() => StopSweep();

        private void OnDestroy()
        {
            foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
        }
    }
}
