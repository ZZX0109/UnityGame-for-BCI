using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds a woven short-forearm sleeve and wrist cuff from the imported hand's wrist landmarks
    /// and baked mesh bounds. Generated assets are updated in place on later builds, preserving GUIDs.
    /// </summary>
    public static class FirstPersonWristCuffBuilder
    {
        public const string CuffObjectName = "Woven wrist cuff";
        public const string AssetDirectory = "Assets/VirtualHand/Models/ElenaFF/Generated";
        public const string MaterialAssetPath = AssetDirectory + "/FirstPersonWristCuff.mat";

        const int RadialSegments = 64;
        const int ForearmRingCount = 15;
        const float ForearmExtensionLengthMetres = .16f;
        const float ForearmFlareScale = 1.30f;
        const string MaterialName = "First Person Woven Cuff";
        static readonly float[] ProfileU = { 0f, .035f, .08f, .15f, .24f, .34f, .72f, .88f, .93f, .97f, 1f };
        // Keep both cuff ends at full circumference so their hems meet the sleeve and wrist cleanly.
        static readonly float[] ProfileRadius = { 1f, 1.006f, 1.014f, 1.02f, 1.018f, 1f, 1f, 1.018f, 1.02f, 1.006f, 1f };

        struct CrossSectionPoint
        {
            public float axial;
            public float lateral;
            public float vertical;
        }

        struct WristSection
        {
            public float cut;
            public float radiusLateral;
            public float radiusVertical;
            public float centerLateral;
            public float centerVertical;
        }

        public static void Build(Transform visualRoot, Transform wrist, Transform middle,
            Transform index, Transform pinky, bool left)
        {
            if (visualRoot == null || wrist == null || middle == null || index == null || pinky == null)
                throw new ArgumentNullException("Wrist cuff landmarks and visual root are required.");

            EnsureAssetDirectory();

            // Size in metres. Imported hand roots include a signed model scale.
            var mount = visualRoot.parent;
            Vector3 wristPoint = mount.InverseTransformPoint(wrist.position);
            Vector3 axis = (mount.InverseTransformPoint(middle.position) - wristPoint).normalized;
            Vector3 across = mount.InverseTransformPoint(pinky.position) - mount.InverseTransformPoint(index.position);
            Vector3 lateral = Vector3.ProjectOnPlane(across, axis).normalized;
            Vector3 vertical = Vector3.Cross(axis, lateral).normalized;
            WristSection section = MeasureWristSection(visualRoot, mount, wristPoint, axis, lateral, vertical);
            float innerLateral = section.radiusLateral + .001f, innerVertical = section.radiusVertical + .001f;
            const float width = .044f;
            Vector3 center = wristPoint + lateral * section.centerLateral + vertical * section.centerVertical + axis * section.cut;
            Mesh generated = CreateGarmentMesh(center, axis, lateral, vertical, width, ForearmExtensionLengthMetres,
                innerLateral, innerVertical, innerLateral + .0025f, innerVertical + .0025f, left);
            // Geometry acceptance: verify human-scale length and open annular openings at both ends.
            ValidateGarmentGeometry(generated, center, axis, innerLateral, innerVertical, width, ForearmExtensionLengthMetres);
            var toAsset = visualRoot.worldToLocalMatrix * mount.localToWorldMatrix;
            var points = generated.vertices;
            for (int i = 0; i < points.Length; i++) points[i] = toAsset.MultiplyPoint3x4(points[i]);
            generated.vertices = points;
            if (toAsset.determinant < 0f)
                for (int sub = 0; sub < generated.subMeshCount; sub++)
                {
                    var triangles = generated.GetTriangles(sub);
                    for (int k = 0; k < triangles.Length; k += 3)
                    {
                        int swap = triangles[k]; triangles[k] = triangles[k+1]; triangles[k+1] = swap;
                    }
                    generated.SetTriangles(triangles, sub, false);
                }
            generated.RecalculateNormals();
            generated.RecalculateBounds();
            generated.RecalculateTangents();
            Debug.Log($"Forearm garment {left}: cut {section.cut:F4} m, wrist half-width {innerLateral:F4} m, extension {ForearmExtensionLengthMetres:F3} m.");

            string side = left ? "L" : "R";
            string meshPath = AssetDirectory + "/FirstPersonWristCuff_" + side + ".asset";
            Mesh mesh = SaveMeshInPlace(generated, meshPath);
            Material material = LoadOrCreateMaterial();

            Transform previous = visualRoot.Find(CuffObjectName);
            if (previous != null)
                UnityEngine.Object.DestroyImmediate(previous.gameObject);

            var cuff = new GameObject(CuffObjectName);
            cuff.layer = visualRoot.gameObject.layer;
            cuff.transform.SetParent(visualRoot, false);
            var filter = cuff.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = cuff.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material, material, material };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;

            EditorUtility.SetDirty(cuff);
            AssetDatabase.SaveAssets();
        }

        static WristSection MeasureWristSection(Transform visualRoot, Transform mount, Vector3 wrist,
            Vector3 axis, Vector3 lateral, Vector3 vertical)
        {
            var points = new List<CrossSectionPoint>();
            float cut = float.PositiveInfinity;
            foreach (var skin in visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                var weights = mesh.boneWeights;
                var binds = mesh.bindposes;
                var bones = skin.bones;
                var maps = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) maps[i] = mount.worldToLocalMatrix * bones[i].localToWorldMatrix * binds[i];
                var vertices = mesh.vertices;
                foreach (int i in new HashSet<int>(mesh.triangles))
                {
                    var v = vertices[i];
                    var w = weights[i];
                    var point = maps[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0 +
                        maps[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1 + maps[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2 +
                        maps[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3 - wrist;
                    var sample = new CrossSectionPoint { axial = Vector3.Dot(point,axis),
                        lateral = Vector3.Dot(point,lateral), vertical = Vector3.Dot(point,vertical) };
                    points.Add(sample);
                    cut = Mathf.Min(cut, sample.axial);
                }
            }
            float x0 = float.PositiveInfinity, y0=x0, x1=float.NegativeInfinity, y1=x1;
            foreach (var p in points)
            {
                if (p.axial > cut + .024f) continue;
                x0 = Mathf.Min(x0,p.lateral); x1 = Mathf.Max(x1,p.lateral);
                y0 = Mathf.Min(y0,p.vertical); y1 = Mathf.Max(y1,p.vertical);
            }
            if (x1 <= x0 || y1 <= y0) throw new InvalidOperationException("No renderable wrist cross-section.");
            return new WristSection { cut = cut, centerLateral=(x0+x1)*.5f,centerVertical=(y0+y1)*.5f,
                radiusLateral=(x1-x0)*.53f,radiusVertical=(y1-y0)*.53f };
        }

        static Mesh CreateGarmentMesh(Vector3 center, Vector3 axis, Vector3 lateral, Vector3 vertical,
            float cuffWidth, float forearmLength, float innerLateral, float innerVertical,
            float outerLateral, float outerVertical, bool left)
        {
            int stride = RadialSegments + 1;
            int cuffRingCount = ProfileU.Length;
            int cuffSurfaceVertexCount = stride * cuffRingCount;
            int forearmSurfaceVertexCount = stride * ForearmRingCount;
            int cuffInnerOffset = cuffSurfaceVertexCount;
            int forearmOuterOffset = cuffSurfaceVertexCount * 2;
            int forearmInnerOffset = forearmOuterOffset + forearmSurfaceVertexCount;
            var vertices = new Vector3[cuffSurfaceVertexCount * 2 + forearmSurfaceVertexCount * 2];
            var uv = new Vector2[vertices.Length];

            // The cuff remains centered on the measured wrist cut and spans it on both sides.
            for (int ring = 0; ring < cuffRingCount; ring++)
            {
                float u = ProfileU[ring];
                float profile = ProfileRadius[ring];
                float axial = Mathf.Lerp(-cuffWidth * .5f, cuffWidth * .5f, u);
                float taper = 1f - .022f * Mathf.Abs(2f * u - 1f);
                for (int segment = 0; segment <= RadialSegments; segment++)
                {
                    int index = ring * stride + segment;
                    float angle = (float)segment / RadialSegments * Mathf.PI * 2f;
                    float cosine = Mathf.Cos(angle);
                    float sine = Mathf.Sin(angle);
                    float wovenRib = 1f + .009f * Mathf.Cos(angle * 24f) + .0015f * Mathf.Cos(u * Mathf.PI * 16f);
                    float outerX = outerLateral * profile * taper * wovenRib;
                    float outerY = outerVertical * profile * taper * wovenRib;
                    float innerX = innerLateral * profile * taper;
                    float innerY = innerVertical * profile * taper;

                    vertices[index] = center + axis * axial + lateral * (cosine * outerX) + vertical * (sine * outerY);
                    vertices[cuffInnerOffset + index] = center + axis * (axial + .00025f)
                        + lateral * (cosine * innerX) + vertical * (sine * innerY);
                    uv[index] = new Vector2((float)segment / RadialSegments, u);
                    uv[cuffInnerOffset + index] = uv[index];
                }
            }

            // Continue proximally toward the elbow. The distal sleeve ring matches the cuff's proximal hem.
            for (int ring = 0; ring < ForearmRingCount; ring++)
            {
                float t = (float)ring / (ForearmRingCount - 1);
                float eased = t * t * (3f - 2f * t);
                float scale = Mathf.Lerp(ForearmFlareScale, 1f - .022f, eased);
                float axial = Mathf.Lerp(-cuffWidth * .5f - forearmLength, -cuffWidth * .5f, t);
                float textileV = axial / cuffWidth + .5f;
                for (int segment = 0; segment <= RadialSegments; segment++)
                {
                    int index = ring * stride + segment;
                    float angle = (float)segment / RadialSegments * Mathf.PI * 2f;
                    float cosine = Mathf.Cos(angle);
                    float sine = Mathf.Sin(angle);
                    float wovenRib = 1f + .009f * Mathf.Cos(angle * 24f) + .0015f * Mathf.Cos(textileV * Mathf.PI * 16f);
                    float outerX = outerLateral * scale * wovenRib;
                    float outerY = outerVertical * scale * wovenRib;
                    float innerX = innerLateral * scale;
                    float innerY = innerVertical * scale;

                    vertices[forearmOuterOffset + index] = center + axis * axial
                        + lateral * (cosine * outerX) + vertical * (sine * outerY);
                    vertices[forearmInnerOffset + index] = center + axis * (axial + .00025f)
                        + lateral * (cosine * innerX) + vertical * (sine * innerY);
                    var ringUv = new Vector2((float)segment / RadialSegments, textileV);
                    uv[forearmOuterOffset + index] = ringUv;
                    uv[forearmInnerOffset + index] = ringUv;
                }
            }

            var outerTriangles = new List<int>(RadialSegments * (cuffRingCount + ForearmRingCount - 2) * 6);
            var innerTriangles = new List<int>(RadialSegments * (cuffRingCount + ForearmRingCount - 2) * 6);
            var hemTriangles = new List<int>(RadialSegments * 18);

            for (int ring = 0; ring < cuffRingCount - 1; ring++)
            {
                int row = ring * stride;
                int nextRow = (ring + 1) * stride;
                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    int a = row + segment;
                    int b = a + 1;
                    int c = nextRow + segment + 1;
                    int d = nextRow + segment;
                    AddQuad(outerTriangles, a, b, c, d, false);
                    AddQuad(innerTriangles, cuffInnerOffset + a, cuffInnerOffset + b,
                        cuffInnerOffset + c, cuffInnerOffset + d, true);
                }
            }

            for (int ring = 0; ring < ForearmRingCount - 1; ring++)
            {
                int row = forearmOuterOffset + ring * stride;
                int nextRow = row + stride;
                int innerRow = forearmInnerOffset + ring * stride;
                int nextInnerRow = innerRow + stride;
                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    int a = row + segment;
                    int b = a + 1;
                    int c = nextRow + segment + 1;
                    int d = nextRow + segment;
                    AddQuad(outerTriangles, a, b, c, d, false);
                    AddQuad(innerTriangles, innerRow + segment, innerRow + segment + 1,
                        nextInnerRow + segment + 1, nextInnerRow + segment, true);
                }
            }

            // Turn the cloth across the two cuff rims and the proximal sleeve opening; these are hems, not caps.
            AddHem(hemTriangles, 0, stride, cuffInnerOffset, false);
            AddHem(hemTriangles, cuffRingCount - 1, stride, cuffInnerOffset, true);
            AddHemAtRows(hemTriangles, forearmOuterOffset, forearmInnerOffset, stride, false);

            var mesh = new Mesh { name = "First Person Woven Wrist Cuff " + (left ? "L" : "R") };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(outerTriangles, 0, false);
            mesh.SetTriangles(innerTriangles, 1, false);
            mesh.SetTriangles(hemTriangles, 2, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void AddHemAtRows(List<int> triangles, int outerRow, int innerRow, int stride, bool positiveAxis)
        {
            for (int segment = 0; segment < RadialSegments; segment++)
            {
                int outerA = outerRow + segment;
                int outerB = outerA + 1;
                int innerA = innerRow + segment;
                int innerB = innerA + 1;
                if (positiveAxis)
                {
                    triangles.Add(outerA); triangles.Add(outerB); triangles.Add(innerB);
                    triangles.Add(outerA); triangles.Add(innerB); triangles.Add(innerA);
                }
                else
                {
                    triangles.Add(outerA); triangles.Add(innerA); triangles.Add(innerB);
                    triangles.Add(outerA); triangles.Add(innerB); triangles.Add(outerB);
                }
            }
        }

        static void ValidateGarmentGeometry(Mesh mesh, Vector3 center, Vector3 axis,
            float innerLateral, float innerVertical, float cuffWidth, float forearmLength)
        {
            if (mesh == null || mesh.subMeshCount != 3 || axis.sqrMagnitude < .99f)
                throw new InvalidOperationException("Forearm garment geometry is incomplete.");
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                if (mesh.GetIndexCount(submesh) < 3)
                    throw new InvalidOperationException("Forearm garment is missing a cloth surface.");

            float minAxial = float.PositiveInfinity;
            float maxAxial = float.NegativeInfinity;
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 offset = vertices[i] - center;
                float axial = Vector3.Dot(offset, axis);
                if (float.IsNaN(axial) || float.IsInfinity(axial))
                    throw new InvalidOperationException("Forearm garment contains a non-finite vertex.");
                minAxial = Mathf.Min(minAxial, axial);
                maxAxial = Mathf.Max(maxAxial, axial);
            }

            float expectedMin = -forearmLength - cuffWidth * .5f;
            float expectedMax = cuffWidth * .5f + .00025f;
            if (Mathf.Abs(minAxial - expectedMin) > .001f || Mathf.Abs(maxAxial - expectedMax) > .001f)
                throw new InvalidOperationException("Forearm extension is outside its expected human-scale length.");

            float openingRadius = Mathf.Min(innerLateral, innerVertical) * .70f;
            ValidateOpenAnnularEnd(vertices, center, axis, expectedMin, openingRadius, "proximal");
            ValidateOpenAnnularEnd(vertices, center, axis, cuffWidth * .5f, openingRadius, "distal");
        }

        static void ValidateOpenAnnularEnd(Vector3[] vertices, Vector3 center, Vector3 axis,
            float endAxial, float minimumOpeningRadius, string endName)
        {
            int ringVertexCount = 0;
            float smallestRadius = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 offset = vertices[i] - center;
                float axial = Vector3.Dot(offset, axis);
                if (Mathf.Abs(axial - endAxial) > .0005f) continue;
                Vector3 radial = offset - axis * axial;
                smallestRadius = Mathf.Min(smallestRadius, radial.magnitude);
                ringVertexCount++;
            }
            if (ringVertexCount < (RadialSegments + 1) * 2 || smallestRadius < minimumOpeningRadius)
                throw new InvalidOperationException("Forearm garment " + endName + " end is capped or has lost its annular opening.");
        }

        static void AddQuad(List<int> triangles, int a, int b, int c, int d, bool reverse)
        {
            if (reverse)
            {
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
            }
            else
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }

        static void AddHem(List<int> triangles, int ring, int stride, int innerOffset, bool positiveAxis)
        {
            int row = ring * stride;
            for (int segment = 0; segment < RadialSegments; segment++)
            {
                int outerA = row + segment;
                int outerB = outerA + 1;
                int innerA = innerOffset + outerA;
                int innerB = innerOffset + outerB;
                if (positiveAxis)
                {
                    triangles.Add(outerA); triangles.Add(outerB); triangles.Add(innerB);
                    triangles.Add(outerA); triangles.Add(innerB); triangles.Add(innerA);
                }
                else
                {
                    triangles.Add(outerA); triangles.Add(innerA); triangles.Add(innerB);
                    triangles.Add(outerA); triangles.Add(innerB); triangles.Add(outerB);
                }
            }
        }

        static Mesh SaveMeshInPlace(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                EditorUtility.SetDirty(generated);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = generated.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        static Material LoadOrCreateMaterial()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("The Built-in Standard shader is required for the first-person wrist cuff.");

            var generated = new Material(shader)
            {
                name = MaterialName,
                color = new Color(.16f, .22f, .25f, 1f),
                mainTexture = BuildWeaveTexture(),
                enableInstancing = true
            };
            generated.SetFloat("_Metallic", .025f);
            generated.SetFloat("_Glossiness", .24f);
            generated.SetFloat("_Smoothness", .24f);

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, MaterialAssetPath);
                EditorUtility.SetDirty(generated);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = MaterialName;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        static Texture2D BuildWeaveTexture()
        {
            const int size = 128;
            string path = AssetDirectory + "/FirstPersonCuffWeave.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool create = !texture;
            if (create) texture = new Texture2D(size, size, TextureFormat.RGB24, true);
            texture.name = "Wrist cuff knit";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Fine alternating stitches over a wider, continuous rib.
                    float phase = 2f * Mathf.PI;
                    float rib = Mathf.Cos(phase * x * 24f / size);
                    float stitch = Mathf.Cos(phase * (y * 12f / size + .25f * rib));
                    float value = .84f + .10f * rib + .06f * stitch;
                    pixels[y * size + x] = new Color(value,value,value,1f);
                }
            texture.SetPixels(pixels);
            texture.Apply(true);
            if (create) AssetDatabase.CreateAsset(texture,path);
            else EditorUtility.SetDirty(texture);
            return texture;
        }

        static void EnsureAssetDirectory()
        {
            if (!Directory.Exists(AssetDirectory))
            {
                Directory.CreateDirectory(AssetDirectory);
                AssetDatabase.Refresh();
            }
        }
    }
}
