using System.Collections.Generic;
using Neuralife.VirtualHand.Body;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// EditMode regression for the whole-avatar appearance switch.  SetMode must be
    /// idempotent (repeated Blue calls neither duplicate the AvatarVisualStyle component
    /// nor regenerate the fill material) and a Blue→Realistic round trip must restore the
    /// initial visual state exactly: the same BodyRenderers array reference and the
    /// identical shared-material instances in every slot.
    /// </summary>
    public sealed class AppearanceModeIdempotenceTests
    {
        readonly List<Object> ownedMaterials = new List<Object>();

        [Test]
        public void SetMode_BlueTwice_KeepsSingleStyleComponentAndMaterial()
        {
            GameObject root = new GameObject("AppearanceIdempotenceRoot");
            try
            {
                Renderer[] renderers = CreateBodyRenderers(root, rendererCount: 2, slotsPerRenderer: 2);
                FullBodyAppearanceController controller = root.AddComponent<FullBodyAppearanceController>();
                controller.BodyRenderers = renderers;

                controller.SetMode(FullBodyAppearanceMode.Blue);
                Material blueAfterFirstCall = renderers[0].sharedMaterials[0];
                Assert.IsNotNull(blueAfterFirstCall, "Blue mode must fill the renderer slots.");

                controller.SetMode(FullBodyAppearanceMode.Blue);

                AvatarVisualStyle[] styles = root.GetComponents<AvatarVisualStyle>();
                Assert.AreEqual(1, styles.Length,
                    "A repeated SetMode(Blue) call must not duplicate the AvatarVisualStyle component.");
                Assert.AreEqual(2, root.transform.childCount,
                    "Appearance switching must not spawn extra GameObjects.");
                Assert.AreEqual(FullBodyAppearanceMode.Blue, controller.Mode);
                Assert.AreSame(blueAfterFirstCall, renderers[0].sharedMaterials[0],
                    "Repeated Blue calls must reuse the identical cached blue material.");

                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] blue = renderers[i].sharedMaterials;
                    Assert.AreEqual(2, blue.Length, "Blue mode fills material slots in place.");
                    for (int slot = 0; slot < blue.Length; slot++)
                    {
                        Assert.IsNotNull(blue[slot], "Every material slot must be filled in blue mode.");
                        Assert.AreSame(blue[0], blue[slot], "One cached blue material is shared across every slot.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetMode_BlueToRealistic_RestoresOriginalSharedMaterials()
        {
            GameObject root = new GameObject("AppearanceRoundTripRoot");
            try
            {
                Renderer[] renderers = CreateBodyRenderers(root, rendererCount: 3, slotsPerRenderer: 2);
                Material[][] original = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++)
                    original[i] = renderers[i].sharedMaterials;

                FullBodyAppearanceController controller = root.AddComponent<FullBodyAppearanceController>();
                controller.BodyRenderers = renderers;
                Renderer[] assignedBodyRenderers = controller.BodyRenderers;

                controller.SetMode(FullBodyAppearanceMode.Blue);
                Assert.AreEqual(FullBodyAppearanceMode.Blue, controller.Mode);
                controller.SetMode(FullBodyAppearanceMode.Realistic);

                Assert.AreEqual(FullBodyAppearanceMode.Realistic, controller.Mode);
                Assert.AreSame(assignedBodyRenderers, controller.BodyRenderers,
                    "The BodyRenderers array must survive a Blue→Realistic round trip unchanged.");
                for (int i = 0; i < renderers.Length; i++)
                {
                    Material[] restored = renderers[i].sharedMaterials;
                    Assert.AreEqual(original[i].Length, restored.Length,
                        "Restored slot count must match the original.");
                    for (int slot = 0; slot < restored.Length; slot++)
                        Assert.AreSame(original[i][slot], restored[slot],
                            "Realistic mode must restore the original material instance in every slot.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        Renderer[] CreateBodyRenderers(GameObject root, int rendererCount, int slotsPerRenderer)
        {
            Shader standard = Shader.Find("Standard");
            Assert.IsNotNull(standard, "The built-in Standard shader must be available to the editor test.");

            Renderer[] renderers = new Renderer[rendererCount];
            for (int i = 0; i < rendererCount; i++)
            {
                GameObject child = new GameObject("BodyRenderer_" + i);
                child.transform.SetParent(root.transform, false);
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                Material[] materials = new Material[slotsPerRenderer];
                for (int slot = 0; slot < slotsPerRenderer; slot++)
                {
                    Material material = new Material(standard) { name = "Test Skin " + i + "_" + slot };
                    ownedMaterials.Add(material);
                    materials[slot] = material;
                }
                renderer.sharedMaterials = materials;
                renderers[i] = renderer;
            }
            return renderers;
        }

        [TearDown]
        public void DestroyOwnedMaterials()
        {
            foreach (Object material in ownedMaterials)
            {
                if (material != null)
                    Object.DestroyImmediate(material);
            }
            ownedMaterials.Clear();
        }
    }
}
