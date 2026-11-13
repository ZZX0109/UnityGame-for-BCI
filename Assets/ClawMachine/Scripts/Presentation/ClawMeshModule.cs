using System.Collections.Generic;
using UnityEngine;

namespace ClawMachine
{
    public enum ClawModuleShape { RoundedBox, Ellipsoid }
    [RequireComponent(typeof(MeshFilter))]
    public sealed class ClawMeshModule : MonoBehaviour
    {
        [SerializeField] private ClawModuleShape shape;
        [SerializeField, Range(0.01f, 0.25f)] private float bevel = 0.08f;
        private Mesh owned, original;
        private void OnEnable()
        {
            MeshFilter filter = GetComponent<MeshFilter>(); original = filter.sharedMesh;
            owned = shape == ClawModuleShape.Ellipsoid ? Ellipsoid(24, 16) : RoundedBox(6, Mathf.Clamp(bevel, 0.01f, 0.25f));
            owned.name = "Claw original modular " + shape; filter.sharedMesh = owned;
        }
        private void OnDisable()
        {
            if (TryGetComponent(out MeshFilter filter) && filter.sharedMesh == owned) filter.sharedMesh = original;
            if (owned) { if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned); } owned = null;
        }
        public static Mesh RoundedBox(int segments, float radius)
        {
            segments = Mathf.Clamp(segments, 2, 16); radius = Mathf.Clamp(radius, 0.01f, 0.25f);
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 normal in faceNormals)
            {
                Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(normal, u); int start = vertices.Count;
                for (int y = 0; y <= segments; y++) for (int x = 0; x <= segments; x++)
                {
                    Vector3 p = normal * 0.5f + u * (x / (float)segments - 0.5f) + v * (y / (float)segments - 0.5f);
                    float inside = 0.5f - radius;
                    Vector3 inner = new Vector3(Mathf.Clamp(p.x, -inside, inside), Mathf.Clamp(p.y, -inside, inside), Mathf.Clamp(p.z, -inside, inside));
                    Vector3 n = (p - inner).normalized; vertices.Add(inner + n * radius); normals.Add(n); uv.Add(new Vector2(x / (float)segments, y / (float)segments));
                }
                for (int y = 0; y < segments; y++) for (int x = 0; x < segments; x++)
                {
                    int a = start + y * (segments + 1) + x, b = a + 1, c = a + segments + 1, d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }
        public static Mesh Ellipsoid(int longitude, int latitude)
        {
            longitude = Mathf.Clamp(longitude, 8, 48); latitude = Mathf.Clamp(latitude, 6, 32);
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int y = 0; y <= latitude; y++) for (int x = 0; x <= longitude; x++)
            {
                float phi = Mathf.PI * y / latitude, theta = Mathf.PI * 2 * x / longitude;
                Vector3 normal = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                vertices.Add(normal * 0.5f); normals.Add(normal); uv.Add(new Vector2(x / (float)longitude, y / (float)latitude));
            }
            for (int y = 0; y < latitude; y++) for (int x = 0; x < longitude; x++)
            {
                int a = y * (longitude + 1) + x, b = a + 1, c = a + longitude + 1, d = c + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }
    }
}
