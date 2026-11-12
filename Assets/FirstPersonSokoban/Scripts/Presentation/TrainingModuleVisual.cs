using System.Collections.Generic;
using FirstPersonSokoban.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonSokoban.Presentation
{
    /// <summary>Cosmetic shell for a training module. The existing grid collider stays authoritative.</summary>
    public sealed class TrainingModuleVisual : MonoBehaviour
    {
        private readonly List<Renderer> statusStrips = new List<Renderer>();
        private readonly List<LabelFit> labels = new List<LabelFit>();
        private struct LabelFit
        {
            public Renderer Renderer;
            public Vector2 Size;
        }
        private Mesh shellMesh;
        private readonly List<Mesh> detailMeshes = new List<Mesh>();
        private LabMaterials palette;
        private bool onGoal;
        private bool highlighted;
        private bool denied;

        public static TrainingModuleVisual Apply(BoxView view, int index, LabMaterials materials)
        {
            TrainingModuleVisual existing = view.GetComponent<TrainingModuleVisual>();
            if (existing != null) return existing;
            materials.Initialize();
            var visual = view.gameObject.AddComponent<TrainingModuleVisual>();
            visual.palette = materials;
            visual.Build(index);
            return visual;
        }

        private void Build(int index)
        {
            Transform body = transform.Find("Body");
            if (body != null)
            {
                MeshFilter filter = body.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    shellMesh = BevelledCube();
                    filter.sharedMesh = shellMesh;
                }
                Renderer renderer = body.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = palette.Module;
            }
            foreach (Transform child in transform)
            {
                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer == null || child.name == "Body") continue;
                renderer.sharedMaterial = child.name.StartsWith("Corner") ? palette.Metal : palette.Trim;
                if (child.name == "Top Frame") renderer.enabled = false;
            }
            var details = new GameObject("Training Module Shell");
            details.transform.SetParent(transform, false);
            Cube(details.transform, "Inset Top Deck", new Vector3(0, 1.709f, 0),
                new Vector3(1.56f, 0.012f, 1.56f), palette.Module);
            for (int i = -1; i <= 1; i += 2)
                Cube(details.transform, "Top Grip Channel", new Vector3(i * 0.36f, 1.718f, 0),
                    new Vector3(0.045f, 0.003f, 1.24f), palette.Dark);
            for (int side = 0; side < 4; side++)
            {
                var face = new GameObject("Service Face " + side);
                face.transform.SetParent(details.transform, false);
                face.transform.localRotation = Quaternion.Euler(0, side * 90f, 0);
                Face(face.transform, index);
            }
            BatchStaticDetails(details.transform);
            UpdateStrips();
        }

        private void BatchStaticDetails(Transform details)
        {
            var groups = new Dictionary<Material, List<MeshFilter>>();
            foreach (MeshFilter filter in details.GetComponentsInChildren<MeshFilter>())
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null || statusStrips.Contains(renderer)) continue;
                if (!groups.TryGetValue(renderer.sharedMaterial, out List<MeshFilter> group))
                {
                    group = new List<MeshFilter>();
                    groups.Add(renderer.sharedMaterial, group);
                }
                group.Add(filter);
            }
            foreach (KeyValuePair<Material, List<MeshFilter>> group in groups)
            {
                var parts = new CombineInstance[group.Value.Count];
                for (int i = 0; i < parts.Length; i++)
                {
                    MeshFilter filter = group.Value[i];
                    parts[i] = new CombineInstance { mesh = filter.sharedMesh,
                        transform = details.worldToLocalMatrix * filter.transform.localToWorldMatrix };
                    filter.GetComponent<Renderer>().enabled = false;
                    Destroy(filter.gameObject);
                }
                var mesh = new Mesh { name = "Training Module Shared Surface Batch" };
                mesh.CombineMeshes(parts);
                detailMeshes.Add(mesh);
                var batch = new GameObject("Module Surface Batch", typeof(MeshFilter), typeof(MeshRenderer));
                batch.transform.SetParent(details, false);
                batch.GetComponent<MeshFilter>().sharedMesh = mesh;
                batch.GetComponent<Renderer>().sharedMaterial = group.Key;
            }
        }

        private void Face(Transform face, int index)
        {
            // Panels sit within +/-0.98, matching the unchanged 1.96-wide physics shell.
            Cube(face, "Panel Recess", new Vector3(0, 0.86f, -0.953f),
                new Vector3(1.62f, 1.24f, 0.012f), palette.Trim);
            Cube(face, "Ceramic Service Panel", new Vector3(0, 0.86f, -0.963f),
                new Vector3(1.54f, 1.16f, 0.008f), palette.Module);
            Cube(face, "Recessed Grip", new Vector3(0, 1.29f, -0.971f),
                new Vector3(1.02f, 0.10f, 0.008f), palette.Dark);
            Cube(face, "Grip Lip", new Vector3(0, 1.325f, -0.977f),
                new Vector3(0.91f, 0.023f, 0.004f), palette.Metal);
            Cube(face, "Identification Inset", new Vector3(-0.48f, 0.64f, -0.971f),
                new Vector3(0.34f, 0.27f, 0.008f), palette.Dark);
            Renderer strip = Cube(face, "Status Strip", new Vector3(0.1f, 0.35f, -0.971f),
                new Vector3(0.94f, 0.026f, 0.008f), palette.Trim);
            statusStrips.Add(strip);
            Cube(face, "Panel Divider", new Vector3(0.42f, 0.82f, -0.971f),
                new Vector3(0.006f, 0.43f, 0.006f), palette.Trim);
            Label(face, "M" + (index + 1).ToString("00"), new Vector3(-0.48f, 0.64f, -0.978f),
                new Vector2(0.26f, 0.14f), new Color(0.80f, 0.87f, 0.88f));
            Label(face, "SPATIAL / MODULE", new Vector3(0.06f, 0.47f, -0.978f),
                new Vector2(0.82f, 0.055f), new Color(0.18f, 0.26f, 0.29f));
        }

        private void Label(Transform parent, string text, Vector3 position, Vector2 size, Color color)
        {
            Font font = Resources.Load<Font>("Fonts/SokobanUISans");
            if (font == null) return;
            var label = new GameObject("Module Marking", typeof(TextMesh));
            label.transform.SetParent(parent, false);
            label.transform.localPosition = position;
            var mesh = label.GetComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 48;
            mesh.characterSize = 0.1f;
            font.RequestCharactersInTexture(text, 48, FontStyle.Normal);
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            Renderer renderer = mesh.GetComponent<Renderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            labels.Add(new LabelFit { Renderer = renderer, Size = size });
            label.transform.localScale = Vector3.one * 0.1f;
            FitLabel(renderer, size);
        }

        private void Start()
        {
            // The native TextMesh can finish its first layout after construction.
            foreach (LabelFit label in labels) FitLabel(label.Renderer, label.Size);
        }

        private static void FitLabel(Renderer renderer, Vector2 size)
        {
            if (renderer == null) return;
            Vector3 bounds = renderer.localBounds.size;
            if (bounds.x <= 0.0001f || bounds.y <= 0.0001f) return;
            float fit = Mathf.Min(size.x / bounds.x, size.y / bounds.y);
            renderer.transform.localScale = Vector3.one * fit;
        }

        private static Renderer Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            Collider collider = cube.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        public void SetState(bool complete, bool selected)
        {
            onGoal = complete;
            highlighted = selected;
            UpdateStrips();
        }

        public void SetDenied(bool value)
        {
            denied = value;
            UpdateStrips();
        }

        private void UpdateStrips()
        {
            if (palette == null) return;
            Material material = denied ? palette.Warning : onGoal ? palette.GoalComplete :
                highlighted ? palette.GoalActive : palette.Trim;
            foreach (Renderer strip in statusStrips)
                if (strip != null) strip.sharedMaterial = material;
        }

        private static Mesh BevelledCube()
        {
            const int steps = 6;
            const float bevel = 0.045f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 axis in axes)
            {
                Vector3 tangent = Mathf.Abs(axis.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 bitangent = Vector3.Cross(axis, tangent);
                int start = vertices.Count;
                for (int y = 0; y <= steps; y++)
                for (int x = 0; x <= steps; x++)
                {
                    Vector3 point = axis * 0.5f + tangent * ((float)x / steps - 0.5f) +
                        bitangent * ((float)y / steps - 0.5f);
                    float extent = 0.5f - bevel;
                    Vector3 core = new Vector3(Mathf.Clamp(point.x, -extent, extent),
                        Mathf.Clamp(point.y, -extent, extent), Mathf.Clamp(point.z, -extent, extent));
                    Vector3 normal = (point - core).normalized;
                    vertices.Add(core + normal * bevel);
                    normals.Add(normal);
                    uv.Add(new Vector2((float)x / steps, (float)y / steps));
                }
                for (int y = 0; y < steps; y++)
                for (int x = 0; x < steps; x++)
                {
                    int a = start + y * (steps + 1) + x;
                    int b = a + 1;
                    int c = a + steps + 1;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
            var mesh = new Mesh { name = "Training Module Bevelled Shell" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (shellMesh != null) Destroy(shellMesh);
            foreach (Mesh mesh in detailMeshes) if (mesh != null) Destroy(mesh);
        }
    }
}
