using System.Collections.Generic;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    internal static class StreetFootwayMesh
    {
        // Match the 10 cm kerb at the gutter, ease to a flush crossing and leave the
        // inner walking strip at the existing datum (door thresholds and shelters).
        public static float HeightAtDepth(float kerbHeight, float depth) => kerbHeight * Mathf.Clamp01(1 - depth / 1.55f);

        public static Mesh Surface(float roadEdge, int side, float halfOpening, Vector2 sideStreet)
        {
            var cuts = new SortedSet<float> { -44, 44, -halfOpening-1.2f, -halfOpening,
                halfOpening, halfOpening+1.2f };
            bool junction = sideStreet.y > sideStreet.x;
            if (junction)
                foreach (float x in new[] { sideStreet.x-1.2f, sideStreet.x, sideStreet.y, sideStreet.y+1.2f })
                    cuts.Add(x);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            var stone = new List<int>();
            foreach (float x in cuts)
            {
                float height = .10f * Mathf.Clamp01((Mathf.Abs(x)-halfOpening)/1.2f);
                if (junction) height *= Mathf.Clamp01(Mathf.Max(sideStreet.x-x,x-sideStreet.y)/1.2f);
                foreach (float depth in new[] { -.012f, 0f, .18f, 1.55f, 6.4f })
                {
                    float z = side*(roadEdge+Mathf.Max(0, depth));
                    vertices.Add(new Vector3(x, depth < 0 ? -.012f : HeightAtDepth(height, depth), z));
                    // Four staggered pavers per texture repeat and four repeats in the material.
                    uvs.Add(new Vector2(x/8f,z/5.1f));
                }
                int first = vertices.Count-10;
                if (first < 0) continue;
                for (int row=0; row<4; row++)
                {
                    int a=first+row, b=a+1, c=a+5, d=c+1;
                    var indices = row < 2 ? stone : triangles;
                    if (side>0) indices.AddRange(new[] {a,b,c,c,b,d});
                    else indices.AddRange(new[] {a,c,b,c,d,b});
                }
            }
            var mesh = new Mesh { name = "Paving with dropped crossing and flared kerbs" };
            mesh.SetVertices(vertices); mesh.SetUVs(0,uvs); mesh.subMeshCount = 2; mesh.SetTriangles(triangles,0); mesh.SetTriangles(stone,1);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        // The truncated domes share a mesh and renderer; individual studs have no colliders.
        public static Mesh WarningStuds(float halfWidth)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int columns = Mathf.FloorToInt((halfWidth * 2 - .08f)/.065f);
            for (int col=0; col<columns; col++)
            for (int row=0; row<8; row++)
            {
                float x = (col-(columns-1)*.5f)*.065f;
                float z = (row-3.5f)*.065f;
                int start = vertices.Count;
                for (int corner=0; corner<8; corner++)
                {
                    float theta = corner*Mathf.PI*.25f;
                    Vector3 radial = new Vector3(Mathf.Cos(theta),0,Mathf.Sin(theta));
                    Vector3 center = new Vector3(x,0,z);
                    vertices.Add(center+radial*.017f);
                    vertices.Add(center+radial*.011f+Vector3.up*.005f);
                }
                for (int corner=0;corner<8;corner++)
                {
                    int a=start+corner*2, b=start+((corner+1)%8)*2;
                    triangles.AddRange(new[] {a,a+1,b, b,a+1,b+1});
                    if(corner>0 && corner<7) triangles.AddRange(new[] {start+1,b+1,a+1});
                }
            }
            var mesh=new Mesh {name="Truncated warning domes"};
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }
    }
}
