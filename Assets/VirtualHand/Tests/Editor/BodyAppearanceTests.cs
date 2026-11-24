using NUnit.Framework;
using UnityEngine;
using Neuralife.VirtualHand.Body;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Covers the body appearance/visibility switching components with pure
    /// GameObject construction (no project assets), mirroring the
    /// HandAppearanceSwitchTests pattern: direct method calls, try/finally cleanup,
    /// and assertions on renderer state, materials and transforms only.
    /// </summary>
    public class BodyAppearanceTests
    {
        [Test]
        public void FirstPersonSwitchRestoresInitialEnabledStates()
        {
            GameObject root = null;
            try
            {
                root = new GameObject("First Person Test Root");

                // The head starts enabled; an optional extra starts disabled. Both must
                // come back to exactly these states after leaving first person.
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);
                var headRenderer = head.AddComponent<SkinnedMeshRenderer>();
                headRenderer.enabled = true;

                var extra = new GameObject("Helmet");
                extra.transform.SetParent(root.transform, false);
                var extraRenderer = extra.AddComponent<MeshRenderer>();
                extraRenderer.enabled = false;

                var visibility = root.AddComponent<FirstPersonVisibility>();
                visibility.HiddenRenderers = new SkinnedMeshRenderer[] { headRenderer };
                visibility.AdditionalHidden = new Renderer[] { extraRenderer };

                Assert.IsFalse(visibility.FirstPersonActive, "visibility should start in third person");

                visibility.SetFirstPerson(true);
                Assert.IsTrue(visibility.FirstPersonActive, "FirstPersonActive did not follow the switch");
                Assert.IsFalse(headRenderer.enabled, "head renderer stayed visible in first person");
                Assert.IsFalse(extraRenderer.enabled, "additional renderer stayed visible in first person");

                visibility.SetFirstPerson(false);
                Assert.IsFalse(visibility.FirstPersonActive, "FirstPersonActive did not follow the switch");
                Assert.IsTrue(headRenderer.enabled, "head renderer did not return to its initial enabled state");
                Assert.IsFalse(extraRenderer.enabled, "additional renderer did not return to its initial disabled state");

                // Repeated switches keep returning to the initial state.
                visibility.SetFirstPerson(true);
                Assert.IsFalse(headRenderer.enabled, "second first-person switch failed to hide the head");
                visibility.SetFirstPerson(false);
                Assert.IsTrue(headRenderer.enabled, "second restore did not return the head to visible");

                // Render-only concern: transforms are never touched.
                Assert.AreEqual(Vector3.zero, head.transform.localPosition, "first-person switching moved the head");
            }
            finally
            {
                Destroy(root);
            }
        }

        [Test]
        public void SetModeIsIdempotentAndRoundTrips()
        {
            Shader standard = Shader.Find("Standard");
            Assert.IsNotNull(standard, "the Built-in Standard shader is unavailable");

            GameObject root = null;
            Material skinA = null, skinB = null;
            try
            {
                skinA = new Material(standard) { name = "Test Skin A" };
                skinB = new Material(standard) { name = "Test Skin B" };

                root = new GameObject("Appearance Controller Test Root");
                var probe = new GameObject("Body Mesh");
                probe.transform.SetParent(root.transform, false);
                var renderer = probe.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new Material[] { skinA, skinB };

                var controller = root.AddComponent<FullBodyAppearanceController>();
                controller.BodyRenderers = new Renderer[] { renderer };

                Assert.AreEqual(FullBodyAppearanceMode.Realistic, controller.Mode,
                    "controller should start in the realistic mode");

                // Switching to the current mode must not touch anything.
                controller.SetMode(FullBodyAppearanceMode.Realistic);
                Assert.AreSame(skinA, renderer.sharedMaterials[0], "redundant Realistic switch changed slot 0");
                Assert.AreSame(skinB, renderer.sharedMaterials[1], "redundant Realistic switch changed slot 1");

                controller.SetMode(FullBodyAppearanceMode.Blue);
                Assert.AreEqual(FullBodyAppearanceMode.Blue, controller.Mode, "Mode did not follow the switch");
                var blue = renderer.sharedMaterials;
                Assert.AreEqual(2, blue.Length, "Blue mode changed the renderer's material slot count");
                Assert.IsNotNull(blue[0], "Blue mode left an empty material slot");
                Assert.AreEqual(AvatarVisualStyle.ShaderName, blue[0].shader.name,
                    "Blue mode did not apply the blue outline shader");
                Material firstBlueMaterial = blue[0];

                // Repeating the same mode must reuse the exact same material instance.
                controller.SetMode(FullBodyAppearanceMode.Blue);
                Assert.AreSame(firstBlueMaterial, renderer.sharedMaterials[0],
                    "repeated Blue switch created a new material instance instead of reusing the first one");
                Assert.AreEqual(2, renderer.sharedMaterials.Length, "repeated Blue switch changed the slot count");

                controller.SetMode(FullBodyAppearanceMode.Realistic);
                Assert.AreEqual(FullBodyAppearanceMode.Realistic, controller.Mode, "Mode did not follow the switch");
                Assert.AreSame(skinA, renderer.sharedMaterials[0], "Realistic switch did not restore slot 0");
                Assert.AreSame(skinB, renderer.sharedMaterials[1], "Realistic switch did not restore slot 1");

                controller.SetMode(FullBodyAppearanceMode.Realistic);
                Assert.AreSame(skinA, renderer.sharedMaterials[0], "redundant Realistic switch after Blue changed slot 0");

                // Rendering-only concern: transforms are never touched.
                Assert.AreEqual(Vector3.zero, probe.transform.localPosition, "appearance switching moved the body");
            }
            finally
            {
                Destroy(root);
                Destroy(skinA);
                Destroy(skinB);
            }
        }

        [Test]
        public void RealisticModeRestoresTheSnapshotMaterialsExactly()
        {
            Shader standard = Shader.Find("Standard");
            Assert.IsNotNull(standard, "the Built-in Standard shader is unavailable");

            GameObject root = null;
            Material[] originals = null;
            try
            {
                originals = new[]
                {
                    new Material(standard) { name = "Torso Skin" },
                    new Material(standard) { name = "Limb Skin" },
                    new Material(standard) { name = "Detail Skin" }
                };

                root = new GameObject("Snapshot Test Root");
                var probe = new GameObject("Torso Mesh");
                probe.transform.SetParent(root.transform, false);
                var renderer = probe.AddComponent<MeshRenderer>();
                var snapshot = new Material[originals.Length];
                originals.CopyTo(snapshot, 0);
                renderer.sharedMaterials = snapshot;

                var controller = root.AddComponent<FullBodyAppearanceController>();
                controller.BodyRenderers = new Renderer[] { renderer };

                controller.SetMode(FullBodyAppearanceMode.Blue);
                Assert.AreEqual(AvatarVisualStyle.ShaderName, renderer.sharedMaterials[0].shader.name,
                    "Blue mode did not apply the blue outline shader");

                controller.SetMode(FullBodyAppearanceMode.Realistic);
                var restored = renderer.sharedMaterials;
                Assert.AreEqual(originals.Length, restored.Length,
                    "restore changed the renderer's material slot count");
                for (int i = 0; i < originals.Length; i++)
                    Assert.AreSame(originals[i], restored[i],
                        "slot " + i + " was not restored to the snapshot material");
            }
            finally
            {
                Destroy(root);
                if (originals != null)
                    foreach (Material material in originals)
                        Destroy(material);
            }
        }

        [Test]
        public void FirstPersonSwitchWithEmptyArraysDoesNotThrow()
        {
            GameObject root = null;
            try
            {
                root = new GameObject("Empty Wiring Test Root");
                var visibility = root.AddComponent<FirstPersonVisibility>();
                visibility.HiddenRenderers = new SkinnedMeshRenderer[0];
                visibility.AdditionalHidden = new Renderer[0];

                Assert.DoesNotThrow(() => visibility.SetFirstPerson(true),
                    "empty wiring must not throw when entering first person");
                Assert.IsTrue(visibility.FirstPersonActive);
                Assert.DoesNotThrow(() => visibility.SetFirstPerson(false),
                    "empty wiring must not throw when leaving first person");
                Assert.IsFalse(visibility.FirstPersonActive);

                // Null arrays must behave like empty ones.
                visibility.HiddenRenderers = null;
                visibility.AdditionalHidden = null;
                Assert.DoesNotThrow(() => visibility.SetFirstPerson(true),
                    "null wiring must not throw when entering first person");
                Assert.DoesNotThrow(() => visibility.SetFirstPerson(false),
                    "null wiring must not throw when leaving first person");

                // A controller with no body renderers must also be safe to switch.
                var controller = root.AddComponent<FullBodyAppearanceController>();
                Assert.DoesNotThrow(() => controller.SetMode(FullBodyAppearanceMode.Blue),
                    "empty body renderer list must not throw when switching to Blue");
                Assert.DoesNotThrow(() => controller.SetMode(FullBodyAppearanceMode.Realistic),
                    "empty body renderer list must not throw when switching to Realistic");
            }
            finally
            {
                Destroy(root);
            }
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o)
                UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
