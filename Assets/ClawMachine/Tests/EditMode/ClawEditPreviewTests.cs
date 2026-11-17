using System;
using System.Collections.Generic;
using System.IO;
using ClawMachine.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawEditPreviewTests
    {
        private Scene scene, previousActive;
        private string temporaryPath;

        [SetUp]
        public void OpenIsolatedScene()
        {
            previousActive = SceneManager.GetActiveScene();
            // The EditMode runner starts in an untitled scene, for which additive
            // NewScene is illegal. A fresh single fixture scene is runner-owned.
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        [TearDown]
        public void ReleaseTemporaryMeshesAndScene()
        {
            if (scene.IsValid()) { ClawMeshPreview.ReleaseScene(scene); EditorSceneManager.CloseScene(scene, true); }
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            if (!string.IsNullOrEmpty(temporaryPath)) AssetDatabase.DeleteAsset(temporaryPath);
        }
        private ClawMeshModule CreateModule(out MeshFilter filter)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(root, scene);
            filter = root.GetComponent<MeshFilter>();
            return root.AddComponent<ClawMeshModule>();
        }

        [Test]
        public void Preview_ReusesItsMeshAndReleasesOnDisableWithoutChangingGeometryOrPhysics()
        {
            ClawMeshModule module = CreateModule(out MeshFilter filter);
            Mesh original = filter.sharedMesh;
            Vector3 position = module.transform.position, scale = module.transform.localScale;
            BoxCollider collider = module.GetComponent<BoxCollider>(); Vector3 size = collider.size, center = collider.center;
            ClawMeshPreview.RefreshNow();
            Mesh preview = filter.sharedMesh;
            Assert.AreNotSame(original, preview); Assert.Greater(preview.vertexCount, original.vertexCount);
            Assert.AreEqual(HideFlags.HideAndDontSave, preview.hideFlags); Assert.IsFalse(EditorUtility.IsPersistent(preview));
            int owned = ClawMeshPreview.OwnedPreviewCount;
            for (int i = 0; i < 30; i++) ClawMeshPreview.RefreshNow();
            Assert.AreSame(preview, filter.sharedMesh); Assert.AreEqual(owned, ClawMeshPreview.OwnedPreviewCount);
            Assert.AreEqual(position, module.transform.position); Assert.AreEqual(scale, module.transform.localScale);
            Assert.AreEqual(size, collider.size); Assert.AreEqual(center, collider.center);
            module.enabled = false; ClawMeshPreview.RefreshNow();
            Assert.AreSame(original, filter.sharedMesh); Assert.IsFalse(preview);
            Assert.AreEqual(owned - 1, ClawMeshPreview.OwnedPreviewCount);
            module.enabled = true; ClawMeshPreview.RefreshNow();
            Assert.AreNotSame(original, filter.sharedMesh); Assert.AreEqual(owned, ClawMeshPreview.OwnedPreviewCount);
            Mesh second = filter.sharedMesh;
            ClawMeshPreview.ReleaseScene(scene);
            Assert.AreSame(original, filter.sharedMesh); Assert.IsFalse(second);
        }

        [Test]
        public void SavingPreview_RestoresOriginalMeshBeforeSerializationAndLeavesNoTransientReference()
        {
            CreateModule(out MeshFilter filter); Mesh original = filter.sharedMesh;
            temporaryPath = "Assets/ClawMachine/Tests/EditMode/TemporaryPreview-" + Guid.NewGuid().ToString("N") + ".unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, temporaryPath));
            string initial = File.ReadAllText(temporaryPath);
            ClawMeshPreview.RefreshNow(); Mesh generated = filter.sharedMesh;
            Assert.AreNotSame(original, generated);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, temporaryPath));
            Assert.IsFalse(generated); Assert.AreSame(original, filter.sharedMesh);
            Assert.AreEqual(initial, File.ReadAllText(temporaryPath), "Edit-view meshes must not leak into a saved scene.");
            ClawMeshPreview.RefreshNow();
            Assert.AreNotSame(original, filter.sharedMesh, "Preview can be recreated after saving.");
        }

        [Test]
        public void Preview_DoesNotMutatePersistentPrefabAssets()
        {
            const string path = "Assets/ClawMachine/Prefabs/Machine/ClawMachine.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Assert.NotNull(prefab);
            string bytes = File.ReadAllText(path);
            var originals = new Dictionary<MeshFilter, Mesh>();
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true)) originals.Add(filter, filter.sharedMesh);
            ClawMeshPreview.RefreshNow();
            foreach (var pair in originals) Assert.AreSame(pair.Value, pair.Key.sharedMesh, pair.Key.name);
            Assert.AreEqual(bytes, File.ReadAllText(path));
        }

        [Test]
        public void SerializedScene_DefaultVisiblePrizesAndPoseMatchC01BeforePlay()
        {
            Scene source = EditorSceneManager.OpenScene(ClawSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                GraspTaskController task = null;
                foreach (GameObject root in source.GetRootGameObjects())
                { task = root.GetComponentInChildren<GraspTaskController>(true); if (task) break; }
                Assert.NotNull(task); Assert.AreEqual("C01", task.Challenge.challengeId);
                int active = 0;
                foreach (PrizePhysics prize in task.Prizes)
                {
                    PrizeSpawnDefinition spawn = Array.Find(task.Challenge.spawns, item => item.prizeId == prize.SlotId);
                    Assert.AreEqual(spawn != null, prize.gameObject.activeSelf, prize.SlotId);
                    if (spawn == null) continue;
                    active++;
                    Vector3 expected = ClawChallengeDefinition.PositionForSeed(spawn.spawnPosition, task.Challenge.seed, spawn.prizeId);
                    Assert.Less(Vector3.Distance(expected, prize.transform.localPosition), 0.000001f);
                    Assert.Less(Quaternion.Angle(Quaternion.Euler(spawn.spawnEuler), prize.transform.localRotation), 0.0001f);
                }
                Assert.AreEqual(1, active);
            }
            finally { ClawMeshPreview.ReleaseScene(source); EditorSceneManager.CloseScene(source, true); }
        }
    }
}
