using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// EditMode verification of the two-level street-tree LOD chain: level 0 keeps the refined
    /// FBX renderers, level 1 swaps to a procedural trunk plus the revived low-poly crown parts,
    /// no collider exists anywhere on the LOD subtree (the tree-pit box stays the only collision
    /// surface), and the proxy canopy tops out at the refined model's measured height so the
    /// distance swap does not shrink the silhouette. The stored cull volume must cover that
    /// silhouette, the proxy must reuse the refined tree's own materials instead of introducing
    /// new assets, and re-running the pass over a lodged wrapper must change nothing.
    /// </summary>
    public sealed class StreetTreeLodTests
    {
        private const float Lod0ScreenHeight = 0.15f;
        private const float Lod1ScreenHeight = 0.006f;

        private Transform parent;
        private StreetAssetLibrary library;

        [SetUp]
        public void SetUp()
        {
            parent = new GameObject("Tree LOD test root").transform;
            library = new StreetAssetLibrary();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(parent.gameObject);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LodgedTree_HasSingleTwoLevelLodGroup(int variant)
        {
            Transform tree = library.CreateLodgedTree(parent, new Vector3(13f, 0f, -8.8f), variant);
            Assert.IsTrue(tree != null, "CreateLodgedTree returned no transform");

            LODGroup[] groups = tree.GetComponentsInChildren<LODGroup>();
            Assert.AreEqual(1, groups.Length, "tree must carry exactly one LODGroup");
            LODGroup group = groups[0];

            LOD[] levels = group.GetLODs();
            Assert.AreEqual(2, levels.Length, "LOD chain must define exactly two levels");
            Assert.AreEqual(Lod0ScreenHeight, levels[0].screenRelativeTransitionHeight, 0.0001f, "LOD0 screen height");
            Assert.AreEqual(Lod1ScreenHeight, levels[1].screenRelativeTransitionHeight, 0.0001f, "LOD1 screen height");

            Transform proxy = tree.Find("Distance proxy");
            Assert.IsTrue(proxy != null, "distance proxy container missing");

            Assert.Greater(levels[0].renderers.Length, 0, "LOD0 has no renderers");
            foreach (Renderer renderer in levels[0].renderers)
            {
                Assert.IsFalse(renderer.transform.IsChildOf(proxy),
                    "LOD0 renderers must stay outside the distance proxy");
                Assert.IsTrue(renderer.GetComponent<MeshFilter>().sharedMesh != null,
                    "LOD0 renderer without a mesh");
                Assert.IsTrue(renderer.sharedMaterial != null,
                    "LOD0 renderer without a material");
            }

            // Three revived crown parts plus the procedural trunk.
            Assert.AreEqual(4, levels[1].renderers.Length, "LOD1 proxy renderer count");
            foreach (Renderer renderer in levels[1].renderers)
            {
                Assert.IsTrue(renderer.transform.IsChildOf(proxy),
                    "LOD1 renderers must live under the distance proxy");
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsTrue(mesh != null, "LOD1 renderer without a mesh");
                Assert.Greater(mesh.vertexCount, 0, "LOD1 proxy mesh has no vertices");
                Assert.Greater(mesh.subMeshCount, 0, "LOD1 proxy mesh has no submeshes");
                Assert.IsTrue(renderer.sharedMaterial != null, "LOD1 renderer without material");
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LodgedTree_CarriesNoCollidersOnLodSubtree(int variant)
        {
            Transform tree = library.CreateLodgedTree(parent, new Vector3(-13f, 0f, 8.8f), variant);
            Collider[] colliders = tree.GetComponentsInChildren<Collider>(true);
            Assert.IsEmpty(colliders,
                "the LOD subtree must stay collision-free; the tree pit is the only surface");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LodgedTree_ProxyAnchoredAtBaseAndCanopyTopMatchesTreeHeight(int variant)
        {
            Vector3 position = new Vector3(26f, 0f, -8.8f);
            Transform tree = library.CreateLodgedTree(parent, position, variant);
            Transform proxy = tree.Find("Distance proxy");
            Assert.IsTrue(proxy != null, "distance proxy container missing");

            // The proxy root sits exactly on the trunk base so the swap keeps the silhouette
            // anchored to the pit.
            Assert.AreEqual(position.x, proxy.position.x, 0.01f, "proxy base x drifted");
            Assert.AreEqual(position.z, proxy.position.z, 0.01f, "proxy base z drifted");
            Assert.AreEqual(position.y, proxy.position.y, 0.01f, "proxy base y drifted");

            // The refined wrapper is scaled to its species height; the proxy canopy must reach
            // the same top so the distant tree keeps its full silhouette.
            Bounds treeBounds = new Bounds(tree.position, Vector3.zero);
            bool first = true;
            foreach (Renderer renderer in tree.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.transform.IsChildOf(proxy)) continue;
                if (first) { treeBounds = renderer.bounds; first = false; }
                else treeBounds.Encapsulate(renderer.bounds);
            }
            float expectedTop = treeBounds.max.y;

            // Read the canopy top from Unity's world bounds instead of a single transformed
            // mesh-bounds corner, which can sit below the true vertex top once the proxy
            // rotation is applied.
            Bounds proxyBounds = new Bounds(proxy.position, Vector3.zero);
            bool proxyFirst = true;
            foreach (MeshRenderer proxyRenderer in proxy.GetComponentsInChildren<MeshRenderer>())
            {
                if (proxyFirst) { proxyBounds = proxyRenderer.bounds; proxyFirst = false; }
                else proxyBounds.Encapsulate(proxyRenderer.bounds);
            }
            Assert.IsFalse(proxyFirst, "proxy produced no canopy extent");
            float crownTop = proxyBounds.max.y;

            Assert.LessOrEqual(System.Math.Abs(crownTop - expectedTop), 0.12f * expectedTop,
                "proxy canopy top deviates from the refined tree top");
            Assert.GreaterOrEqual(treeBounds.min.y, -0.1f, "refined tree sinks below the pit");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LodgedTree_CullVolumeCoversTreeHeight(int variant)
        {
            Transform tree = library.CreateLodgedTree(parent, new Vector3(-26f, 0f, 8.8f), variant);
            LODGroup group = tree.GetComponentInChildren<LODGroup>();
            Assert.IsTrue(group != null, "LODGroup missing");

            // Measure the true rendered extent of the whole subtree, proxy included.
            Bounds treeBounds = new Bounds(tree.position, Vector3.zero);
            bool first = true;
            foreach (Renderer renderer in tree.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { treeBounds = renderer.bounds; first = false; }
                else treeBounds.Encapsulate(renderer.bounds);
            }
            Assert.IsFalse(first, "tree has no renderers to measure");
            float treeHeight = treeBounds.size.y;

            // The stored cull volume must already cover the silhouette. A fresh LODGroup
            // defaults to a one-metre cull cube, so a missing explicit recalculation leaves
            // the whole tree culled long before its far LOD takes over.
            Assert.GreaterOrEqual(group.size, treeHeight * 0.9f,
                "stored cull volume is smaller than the tree height");
            Vector3 expectedLocalCentre = tree.InverseTransformPoint(treeBounds.center);
            Assert.LessOrEqual(Vector3.Distance(group.localReferencePoint, expectedLocalCentre),
                treeHeight * 0.3f, "stored cull centre sits far from the tree body");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LodgedTree_ProxyReusesRefinedTreeMaterials(int variant)
        {
            Transform tree = library.CreateLodgedTree(parent, new Vector3(-26f, 0f, -8.8f), variant);
            Transform proxy = tree.Find("Distance proxy");
            Assert.IsTrue(proxy != null, "distance proxy container missing");

            // The allowed palette is exactly what level 0 (the refined FBX) already uses;
            // the proxy must not pull in any additional asset.
            HashSet<Material> palette = new HashSet<Material>();
            foreach (Renderer renderer in tree.GetComponentsInChildren<MeshRenderer>())
                if (!renderer.transform.IsChildOf(proxy) && renderer.sharedMaterial != null)
                    palette.Add(renderer.sharedMaterial);
            Assert.Greater(palette.Count, 0, "refined tree exposed no materials");

            foreach (Renderer renderer in proxy.GetComponentsInChildren<MeshRenderer>())
            {
                Assert.IsTrue(renderer.sharedMaterial != null, "proxy renderer without material");
                Assert.IsTrue(palette.Contains(renderer.sharedMaterial),
                    "proxy introduced a material the refined tree does not use: " +
                    renderer.sharedMaterial.name);
            }
        }

        [Test]
        public void LodgedTree_SecondApplicationIsIdempotent()
        {
            Vector3 position = new Vector3(13f, 0f, 8.8f);
            Transform tree = library.CreateLodgedTree(parent, position, 1);

            // A second pass over the already lodged wrapper must change nothing: the same
            // transform comes back, with one group, one proxy and the original LOD1 set.
            Transform returned = library.ApplyLodChain(tree, position);
            Assert.AreEqual(tree, returned, "idempotent pass returned a different transform");

            LODGroup[] groups = tree.GetComponentsInChildren<LODGroup>();
            Assert.AreEqual(1, groups.Length, "double application stacked LODGroups");
            int proxyCount = 0;
            foreach (Transform child in tree.GetComponentsInChildren<Transform>())
                if (child.name == "Distance proxy") proxyCount++;
            Assert.AreEqual(1, proxyCount, "double application duplicated the proxy container");

            LOD[] levels = groups[0].GetLODs();
            Assert.AreEqual(2, levels.Length, "LOD chain level count changed");
            Assert.AreEqual(4, levels[1].renderers.Length, "LOD1 renderer count changed on re-application");
        }
    }
}
