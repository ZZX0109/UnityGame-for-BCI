using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// §4.1/§4.3 layer contract: the six responsibility layers stay mapped to the
    /// TagManager slots the constants claim, and the project collision matrix keeps
    /// every new layer pair enabled (additive-permissive), so introducing the layers
    /// cannot change what collides with what. Any future LayerMask narrowing needs
    /// its own regression test on top of this floor.
    /// </summary>
    public class StreetPhysicsLayerTests
    {
        [TestCase("WalkableGround", StreetPhysicsLayers.WalkableGround)]
        [TestCase("PlayerObstacle", StreetPhysicsLayers.PlayerObstacle)]
        [TestCase("VehicleObstacle", StreetPhysicsLayers.VehicleObstacle)]
        [TestCase("TrafficVisibilityOccluder", StreetPhysicsLayers.TrafficVisibilityOccluder)]
        [TestCase("VisualOnly", StreetPhysicsLayers.VisualOnly)]
        [TestCase("TriggerZone", StreetPhysicsLayers.TriggerZone)]
        public void LayerNamesStayInSyncWithTagManager(string name, int slot)
        {
            Assert.AreEqual(slot, LayerMask.NameToLayer(name),
                "TagManager.asset slot for '" + name + "' drifted from StreetPhysicsLayers.");
        }

        [Test]
        public void NewLayersStayCollisionPermissive()
        {
            int[] slots =
            {
                StreetPhysicsLayers.WalkableGround, StreetPhysicsLayers.PlayerObstacle,
                StreetPhysicsLayers.VehicleObstacle, StreetPhysicsLayers.TrafficVisibilityOccluder,
                StreetPhysicsLayers.VisualOnly, StreetPhysicsLayers.TriggerZone,
            };
            foreach (int slot in slots)
            {
                Assert.IsFalse(Physics.GetIgnoreLayerCollision(slot, 0),
                    "layer " + slot + " must keep colliding with the Default layer");
                Assert.IsFalse(Physics.GetIgnoreLayerCollision(slot, slot),
                    "layer " + slot + " must keep colliding with itself");
            }
        }

        [Test]
        public void MasksMatchSlotConstants()
        {
            Assert.AreEqual(1 << StreetPhysicsLayers.WalkableGround, StreetPhysicsLayers.WalkableGroundMask);
            Assert.AreEqual(1 << StreetPhysicsLayers.PlayerObstacle, StreetPhysicsLayers.PlayerObstacleMask);
            Assert.AreEqual(1 << StreetPhysicsLayers.VehicleObstacle, StreetPhysicsLayers.VehicleObstacleMask);
            Assert.AreEqual(1 << StreetPhysicsLayers.TrafficVisibilityOccluder, StreetPhysicsLayers.TrafficVisibilityOccluderMask);
            Assert.AreEqual(1 << StreetPhysicsLayers.VisualOnly, StreetPhysicsLayers.VisualOnlyMask);
            Assert.AreEqual(1 << StreetPhysicsLayers.TriggerZone, StreetPhysicsLayers.TriggerZoneMask);
        }

        /// <summary>
        /// §4.2: the LOD tree renders no collider, so the stem needs its own world-true blocker
        /// next to the (possibly scaled) tree root, outside the LOD subtree the tree tests keep
        /// collider-free.
        /// </summary>
        [Test]
        public void CreatedTreesGetWorldTrueTrunkColliderOutsideTheLodSubtree()
        {
            var parent = new GameObject("Tree layer test root").transform;
            try
            {
                var library = new StreetAssetLibrary();
                var position = new Vector3(13f, 0f, -8.8f);
                Transform tree = library.CreateLodgedTree(parent, position, 0);
                Assert.IsTrue(tree != null, "CreateLodgedTree returned no transform");

                Transform bounds = parent.Find("Tree trunk bounds");
                Assert.IsTrue(bounds != null, "the tree needs a trunk blocker next to its root");
                Assert.AreEqual(StreetPhysicsLayers.PlayerObstacle, bounds.gameObject.layer);
                Assert.IsTrue(bounds.GetComponent<BoxCollider>() != null, "trunk blocker must be a box collider");
                Assert.That(Vector3.Distance(bounds.localPosition, position + new Vector3(0, 1.3f, 0)),
                    Is.LessThan(.0001f), "trunk blocker must stay centred on the pit at world scale");
                Assert.IsTrue(bounds.GetComponent<BoxCollider>().size.x <= .3f
                    && bounds.GetComponent<BoxCollider>().size.z <= .3f,
                    "the trunk blocker must stay slim and must not wall off the grate");
                Assert.AreEqual(0, tree.GetComponentsInChildren<Collider>(true).Length,
                    "the LOD subtree itself stays collider-free (StreetTreeLodTests contract)");
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }
    }
}
