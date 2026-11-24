#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VirtualHand.Editor.Tests
{
    public sealed class AvatarMeshRegionBuilderTests
    {
        private GameObject rigRoot;
        private SkinnedMeshRenderer sourceRenderer;
        private Mesh sourceMesh;
        private string outputFolder;
        private Vector3[] expectedBlendShapeVertices;

        [Test]
        public void BuildAssets_PreservesMeshDataAndKeepsBoundaryTriangleOnBody()
        {
            rigRoot = new GameObject("SyntheticAvatarRig");
            Transform headBone = CreateBone("HeadBone");
            Transform handBone = CreateBone("HandBone");
            Transform bodyBone = CreateBone("BodyBone");

            GameObject sourceObject = new GameObject("SyntheticSourceRenderer");
            sourceObject.transform.SetParent(rigRoot.transform, false);
            sourceObject.transform.localPosition = new Vector3(1f, 2f, 3f);
            sourceObject.transform.localRotation = Quaternion.Euler(4f, 9f, 14f);
            sourceObject.transform.localScale = new Vector3(1.1f, 0.9f, 1.2f);
            sourceRenderer = sourceObject.AddComponent<SkinnedMeshRenderer>();

            sourceMesh = CreateSyntheticMesh();
            sourceRenderer.sharedMesh = sourceMesh;
            sourceRenderer.bones = new[] { headBone, handBone, bodyBone };
            sourceRenderer.rootBone = bodyBone;
            sourceRenderer.sharedMaterials = new Material[] { null, null };

            outputFolder = AvatarMeshRegionBuilder.GeneratedAssetRoot + "/Tests_" + Guid.NewGuid().ToString("N");
            AvatarMeshRegionBuildResult result = AvatarMeshRegionBuilder.BuildAssets(
                sourceRenderer,
                new[] { headBone },
                new[] { handBone },
                outputFolder,
                "SyntheticAvatar",
                0.5f);

            Assert.AreEqual(9, result.BodyMesh.vertexCount);
            Assert.AreEqual(2, result.BodyMesh.subMeshCount);
            Assert.AreEqual(2, result.HeadMesh.subMeshCount);
            Assert.AreEqual(2, result.HandsMesh.subMeshCount);

            CollectionAssert.AreEqual(new[] { 0, 1, 6 }, result.BodyMesh.GetTriangles(0));
            CollectionAssert.AreEqual(Array.Empty<int>(), result.BodyMesh.GetTriangles(1));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, result.HeadMesh.GetTriangles(0));
            CollectionAssert.AreEqual(Array.Empty<int>(), result.HeadMesh.GetTriangles(1));
            CollectionAssert.AreEqual(Array.Empty<int>(), result.HandsMesh.GetTriangles(0));
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, result.HandsMesh.GetTriangles(1));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 6 }, sourceMesh.GetTriangles(0));

            AssertMeshChannelsAndBlendShapePreserved(sourceMesh, result.BodyMesh);
            AssertBoneWeightsPreserved(sourceMesh, result.BodyMesh);
            Assert.AreEqual(sourceMesh.bindposes, result.BodyMesh.bindposes);

            AvatarMeshRegionRenderers renderers = AvatarMeshRegionBuilder.CreateRendererCopies(
                sourceRenderer,
                result,
                rigRoot.transform,
                "SyntheticAvatarRegions");
            Assert.AreSame(result.BodyMesh, renderers.Body.sharedMesh);
            Assert.AreSame(result.HeadMesh, renderers.Head.sharedMesh);
            Assert.AreSame(result.HandsMesh, renderers.ImportedHands.sharedMesh);
            CollectionAssert.AreEqual(sourceRenderer.bones, renderers.Head.bones);
            Assert.AreSame(sourceRenderer.rootBone, renderers.Head.rootBone);
            CollectionAssert.AreEqual(sourceRenderer.sharedMaterials, renderers.Head.sharedMaterials);
            Assert.AreEqual(sourceRenderer.transform.localPosition, renderers.Head.transform.localPosition);
            Assert.AreEqual(sourceRenderer.transform.localRotation, renderers.Head.transform.localRotation);
            Assert.AreEqual(sourceRenderer.transform.localScale, renderers.Head.transform.localScale);

            renderers.SetElenaHandsReplacementActive(true);
            Assert.IsFalse(renderers.ImportedHands.enabled);
            Assert.IsTrue(sourceRenderer.enabled, "The helper must not change the live source renderer.");
            renderers.SetElenaHandsReplacementActive(false);
            Assert.IsTrue(renderers.ImportedHands.enabled);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(outputFolder) && AssetDatabase.IsValidFolder(outputFolder))
                AssetDatabase.DeleteAsset(outputFolder);
            if (rigRoot != null)
                UnityEngine.Object.DestroyImmediate(rigRoot);
            if (sourceMesh != null && !AssetDatabase.Contains(sourceMesh))
                UnityEngine.Object.DestroyImmediate(sourceMesh);
            AssetDatabase.Refresh();
        }

        private Transform CreateBone(string boneName)
        {
            GameObject bone = new GameObject(boneName);
            bone.transform.SetParent(rigRoot.transform, false);
            return bone.transform;
        }

        private Mesh CreateSyntheticMesh()
        {
            Mesh mesh = new Mesh { name = "SyntheticSourceMesh" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(2f, 1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(4f, 0f, 0f), new Vector3(4f, 1f, 0f)
            };
            mesh.normals = new Vector3[9];
            mesh.tangents = new Vector4[9];
            mesh.uv = new Vector2[9];
            mesh.uv2 = new Vector2[9];
            mesh.colors32 = new Color32[9];
            for (int i = 0; i < 9; i++)
            {
                mesh.normals[i] = Vector3.up;
                mesh.tangents[i] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[i] = new Vector2(i / 9f, 0.25f);
                mesh.uv2[i] = new Vector2(i / 9f, 0.75f);
                mesh.colors32[i] = new Color32((byte)(i * 10), 20, 30, 255);
            }

            BoneWeight[] boneWeights = new BoneWeight[9];
            for (int i = 0; i < boneWeights.Length; i++)
            {
                int boneIndex = i <= 2 ? 0 : (i <= 6 ? 1 : 2);
                boneWeights[i] = new BoneWeight
                {
                    boneIndex0 = boneIndex,
                    weight0 = 1f
                };
            }
            mesh.boneWeights = boneWeights;
            mesh.bindposes = new[]
            {
                Matrix4x4.Translate(new Vector3(1f, 0f, 0f)),
                Matrix4x4.Translate(new Vector3(0f, 1f, 0f)),
                Matrix4x4.Translate(new Vector3(0f, 0f, 1f))
            };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 1, 6 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);

            expectedBlendShapeVertices = new Vector3[9];
            expectedBlendShapeVertices[0] = new Vector3(0f, 0.1f, 0f);
            Vector3[] deltaNormals = new Vector3[9];
            Vector3[] deltaTangents = new Vector3[9];
            mesh.AddBlendShapeFrame("SyntheticSmile", 100f, expectedBlendShapeVertices, deltaNormals, deltaTangents);
            return mesh;
        }

        private void AssertMeshChannelsAndBlendShapePreserved(Mesh expected, Mesh actual)
        {
            CollectionAssert.AreEqual(expected.vertices, actual.vertices);
            CollectionAssert.AreEqual(expected.normals, actual.normals);
            CollectionAssert.AreEqual(expected.tangents, actual.tangents);
            CollectionAssert.AreEqual(expected.uv, actual.uv);
            CollectionAssert.AreEqual(expected.uv2, actual.uv2);
            CollectionAssert.AreEqual(expected.colors32, actual.colors32);
            Assert.AreEqual(1, actual.blendShapeCount);
            Assert.AreEqual("SyntheticSmile", actual.GetBlendShapeName(0));
            Assert.AreEqual(1, actual.GetBlendShapeFrameCount(0));
            Assert.AreEqual(100f, actual.GetBlendShapeFrameWeight(0, 0));

            Vector3[] actualDeltaVertices = new Vector3[actual.vertexCount];
            Vector3[] actualDeltaNormals = new Vector3[actual.vertexCount];
            Vector3[] actualDeltaTangents = new Vector3[actual.vertexCount];
            actual.GetBlendShapeFrameVertices(0, 0, actualDeltaVertices, actualDeltaNormals, actualDeltaTangents);
            CollectionAssert.AreEqual(expectedBlendShapeVertices, actualDeltaVertices);
        }

        private static void AssertBoneWeightsPreserved(Mesh expected, Mesh actual)
        {
            BoneWeight[] expectedWeights = expected.boneWeights;
            BoneWeight[] actualWeights = actual.boneWeights;
            Assert.AreEqual(expectedWeights.Length, actualWeights.Length);
            for (int i = 0; i < expectedWeights.Length; i++)
            {
                Assert.AreEqual(expectedWeights[i].boneIndex0, actualWeights[i].boneIndex0);
                Assert.AreEqual(expectedWeights[i].boneIndex1, actualWeights[i].boneIndex1);
                Assert.AreEqual(expectedWeights[i].boneIndex2, actualWeights[i].boneIndex2);
                Assert.AreEqual(expectedWeights[i].boneIndex3, actualWeights[i].boneIndex3);
                Assert.AreEqual(expectedWeights[i].weight0, actualWeights[i].weight0);
                Assert.AreEqual(expectedWeights[i].weight1, actualWeights[i].weight1);
                Assert.AreEqual(expectedWeights[i].weight2, actualWeights[i].weight2);
                Assert.AreEqual(expectedWeights[i].weight3, actualWeights[i].weight3);
            }
        }
    }
}
#endif