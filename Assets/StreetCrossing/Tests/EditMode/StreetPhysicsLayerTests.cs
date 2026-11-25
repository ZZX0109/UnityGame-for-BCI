using NUnit.Framework;
using StreetCrossing.Core;
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
    }
}
