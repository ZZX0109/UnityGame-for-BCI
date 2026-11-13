using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawImportedAssetTests
    {
        [TestCase("MintParcel")]
        [TestCase("CloudBunny")]
        [TestCase("AmberFox")]
        public void PrizePrefab_ImportsEveryMeshAndCompoundCollider(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ClawMachine/Prefabs/Prize/" + name + ".prefab");
            Assert.NotNull(root);
            Assert.NotNull(root.GetComponent<Rigidbody>());
            Assert.Greater(root.GetComponentsInChildren<Collider>(true).Length, 0);
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            Assert.Greater(filters.Length, 0);
            foreach (MeshFilter filter in filters) Assert.NotNull(filter.sharedMesh, name + ": " + filter.name);
            Assert.Greater(root.GetComponentsInChildren<ClawMeshModule>(true).Length, 0, "The visual template must include an original authored mesh module.");
        }
        [Test]
        public void FullMachinePrefab_HasTypedPhysicsTaskAndFontShaderBindingsAfterImport()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ClawMachine/Prefabs/Machine/ClawMachine.prefab");
            Assert.NotNull(root);
            var task = root.GetComponentInChildren<GraspTaskController>(true);
            Assert.NotNull(task); Assert.IsTrue(task.IsConfigured);
            Assert.GreaterOrEqual(task.Prizes.Count, 3);
            Assert.IsTrue(task.Gripper.IsConfigured);
            foreach (SphereCollider pad in task.Gripper.ContactColliders) Assert.IsFalse(pad.isTrigger);
            var manager = root.GetComponentInChildren<ClawGameManager>(true);
            Assert.AreEqual(3, manager.Challenges.Count);
            foreach (var challenge in manager.Challenges)
            {
                Assert.NotNull(challenge);
                foreach (PrizeSpawnDefinition spawn in challenge.spawns) Assert.NotNull(spawn.graspProfile);
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) Assert.NotNull(filter.sharedMesh, filter.name);
            var ui = root.GetComponentInChildren<ClawGameUI>(true);
            Assert.NotNull(ui);
            var serialized = new SerializedObject(ui);
            Assert.NotNull(serialized.FindProperty("uiFont").objectReferenceValue);
            Assert.NotNull(serialized.FindProperty("bitmapShader").objectReferenceValue);
            Assert.NotNull(serialized.FindProperty("bitmapCompatibilityShader").objectReferenceValue);
        }
        [Test]
        public void OriginalMeshes_HaveUvAndVisibleOutwardTriangles()
        {
            Mesh[] meshes = { ClawMeshModule.RoundedBox(6, 0.08f), ClawMeshModule.Ellipsoid(24, 16),
                ClawMeshModule.PlushBody(32, 24), ClawMeshModule.CurvedTail(28, 16) };
            try
            {
                foreach (Mesh mesh in meshes)
                {
                    Assert.AreEqual(mesh.vertexCount, mesh.normals.Length);
                    Assert.AreEqual(mesh.vertexCount, mesh.uv.Length);
                    Vector3[] vertices = mesh.vertices, normals = mesh.normals; int[] indices = mesh.triangles;
                    foreach (Vector3 point in vertices)
                    {
                        Assert.IsFalse(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z));
                        Assert.IsFalse(float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z));
                        Assert.LessOrEqual(Mathf.Abs(point.x), 0.501f);
                        Assert.LessOrEqual(Mathf.Abs(point.y), 0.501f);
                        Assert.LessOrEqual(Mathf.Abs(point.z), 0.501f);
                    }
                    int visible = 0;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        if (face.sqrMagnitude < 0.00000001f) continue; // Sphere poles may have degenerate seam triangles.
                        Assert.Greater(Vector3.Dot(face.normalized, (normals[a] + normals[b] + normals[c]).normalized), 0, "A reversed face would disappear under backface culling.");
                        visible++;
                    }
                    Assert.Greater(visible, 100);
                }
            }
            finally { foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh); }
        }
    }
}
