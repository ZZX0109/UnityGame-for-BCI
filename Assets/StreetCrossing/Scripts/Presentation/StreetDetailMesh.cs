using System.Collections.Generic;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Original metre-scale rounded silhouette, separate front/side normals; no physics components.</summary>
    public static class StreetDetailMesh
    {
        public static Mesh RoundedBox(Vector3 size, float radius)
        {
            radius = Mathf.Clamp(radius, 0.001f, Mathf.Min(size.x, size.y) * 0.49f);
            const int segments = 4;
            var outline = new List<Vector2>();
            for (int corner = 0; corner < 4; corner++)
            {
                float sx = corner == 0 || corner == 3 ? 1 : -1;
                float sy = corner < 2 ? 1 : -1;
                Vector2 centre = new Vector2(sx * (size.x * .5f - radius), sy * (size.y * .5f - radius));
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (corner * 90 + step * 90f / segments) * Mathf.Deg2Rad;
                    outline.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
            int count = outline.Count;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (int face = 0; face < 2; face++)
            {
                vertices.Add(new Vector3(0, 0, (face == 0 ? -1 : 1) * size.z * .5f)); uv.Add(Vector2.one * .5f);
                foreach (Vector2 p in outline)
                { vertices.Add(new Vector3(p.x, p.y, (face == 0 ? -1 : 1) * size.z * .5f)); uv.Add(new Vector2(p.x / size.x + .5f, p.y / size.y + .5f)); }
                int offset = face * (count + 1);
                for (int n = 0; n < count; n++)
                {
                    int a = offset + n + 1, b = offset + (n + 1) % count + 1;
                    triangles.Add(offset); triangles.Add(face == 0 ? b : a); triangles.Add(face == 0 ? a : b);
                }
            }
            for (int n = 0; n < count; n++)
            {
                int start = vertices.Count; Vector2 a = outline[n], b = outline[(n + 1) % count];
                vertices.Add(new Vector3(a.x, a.y, -size.z * .5f)); vertices.Add(new Vector3(b.x, b.y, -size.z * .5f));
                vertices.Add(new Vector3(b.x, b.y, size.z * .5f)); vertices.Add(new Vector3(a.x, a.y, size.z * .5f));
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.one); uv.Add(Vector2.up);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh { name = "Original rounded street detail" }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
