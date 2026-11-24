using System;
using NUnit.Framework;
using UnityEngine;
using Neuralife.VirtualHand;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Covers spec section 10.4: repeated switching between the realistic and blue
    /// transparent styles.
    ///
    /// The three things that would actually hurt a player are checked directly:
    ///   1. the appearance path must never create a growing pile of material
    ///      instances (a per-toggle leak would bloat memory and break batching);
    ///   2. it must not disturb transforms, so bones are not reset and a held object
    ///      does not move or drop;
    ///   3. the original materials must come back intact, so switching back really is
    ///      "realistic" again.
    /// </summary>
    public class HandAppearanceSwitchTests
    {
        const int SwitchCount = 20;

        [Test]
        public void TwentyConsecutiveBlueTogglesDoNotLeakMaterialInstancesOrMoveObjects()
        {
            Shader standard = Shader.Find("Standard");
            Assert.IsNotNull(standard, "the Built-in Standard shader is unavailable");

            GameObject root = null;
            GameObject heldObject = null;
            Material skinA = null, skinB = null;

            try
            {
                skinA = new Material(standard) { name = "Test Skin A" };
                skinB = new Material(standard) { name = "Test Skin B" };

                root = new GameObject("Appearance Switch Test Root");

                // A two-slot renderer, so a slot-count regression is visible too.
                var probe = new GameObject("Hand Mesh");
                probe.transform.SetParent(root.transform, false);
                var renderer = probe.AddComponent<MeshRenderer>();
                var originals = new Material[] { skinA, skinB };
                renderer.sharedMaterials = originals;

                // Something the player might be holding: any appearance change that
                // touched physics would show up here.
                heldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                heldObject.name = "Held Object";
                heldObject.transform.SetParent(root.transform, false);
                heldObject.transform.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
                var body = heldObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.velocity = Vector3.zero;

                var style = root.AddComponent<AvatarVisualStyle>();
                style.targets = new Renderer[] { renderer };

                Vector3 probePosition = probe.transform.localPosition;
                Quaternion probeRotation = probe.transform.localRotation;
                Vector3 heldPosition = heldObject.transform.position;

                Material firstBlueMaterial = null;

                for (int i = 0; i < SwitchCount; i++)
                {
                    style.SetBlueMode(true);

                    var blue = renderer.sharedMaterials;
                    Assert.AreEqual(originals.Length, blue.Length,
                        "switch " + i + " changed the renderer's material slot count");
                    Assert.IsNotNull(blue[0], "switch " + i + " left an empty material slot");
                    Assert.AreEqual(AvatarVisualStyle.ShaderName, blue[0].shader.name,
                        "switch " + i + " did not apply the blue outline shader");
                    Assert.AreSame(blue[0], blue[1],
                        "switch " + i + " did not share one blue material across the submeshes");

                    if (firstBlueMaterial == null)
                        firstBlueMaterial = blue[0];
                    else
                        Assert.AreSame(firstBlueMaterial, blue[0],
                            "switch " + i + " created a new material instance instead of reusing the first one");

                    style.SetBlueMode(false);

                    var restored = renderer.sharedMaterials;
                    Assert.AreEqual(originals.Length, restored.Length,
                        "switch " + i + " changed the slot count on restore");
                    Assert.AreSame(skinA, restored[0], "switch " + i + " did not restore slot 0");
                    Assert.AreSame(skinB, restored[1], "switch " + i + " did not restore slot 1");
                }

                Assert.IsNotNull(firstBlueMaterial);
                Assert.AreNotSame(skinA, firstBlueMaterial);

                // Appearance must be a rendering-only concern.
                Assert.AreEqual(probePosition, probe.transform.localPosition, "appearance switching moved the hand");
                Assert.AreEqual(probeRotation, probe.transform.localRotation, "appearance switching rotated the hand");
                Assert.AreEqual(heldPosition, heldObject.transform.position, "appearance switching moved the held object");
                Assert.AreEqual(Vector3.zero, body.velocity, "appearance switching changed the held object's velocity");
                Assert.IsFalse(body.isKinematic == false && body.detectCollisions == false,
                    "appearance switching disabled collision on the held object");
            }
            finally
            {
                Destroy(root);
                Destroy(heldObject);
                Destroy(skinA);
                Destroy(skinB);
            }
        }

        [Test]
        public void LeavingBlueModeRestoresTheAuthoredMaterialsExactly()
        {
            Shader standard = Shader.Find("Standard");
            Assert.IsNotNull(standard);

            GameObject root = null;
            Material skin = null;
            try
            {
                skin = new Material(standard) { name = "Authored Skin" };

                root = new GameObject("Restore Test Root");
                var probe = new GameObject("Hand Mesh");
                probe.transform.SetParent(root.transform, false);
                var renderer = probe.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new Material[] { skin };

                var style = root.AddComponent<AvatarVisualStyle>();
                style.targets = new Renderer[] { renderer };

                style.SetBlueMode(true);
                Assert.AreNotSame(skin, renderer.sharedMaterials[0]);

                style.RestoreOriginalMaterials();
                Assert.AreSame(skin, renderer.sharedMaterials[0],
                    "RestoreOriginalMaterials did not put the authored material back");
            }
            finally
            {
                Destroy(root);
                Destroy(skin);
            }
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o)
                UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
