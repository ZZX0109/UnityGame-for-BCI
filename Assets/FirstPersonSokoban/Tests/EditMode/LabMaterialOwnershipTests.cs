using FirstPersonSokoban.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FirstPersonSokoban.Tests.EditMode
{
    public sealed class LabMaterialOwnershipTests
    {
        [Test]
        public void WorldTextSharesAnOwnedMaterialAndDoesNotModifyTheUiFontMaterial()
        {
            var owner = new GameObject("World text ownership test");
            Material world = null;
            Font font = Resources.Load<Font>("Fonts/SokobanUISans");
            Assert.NotNull(font);
            Shader original = font.material.shader;
            try
            {
                LabMaterials palette = owner.AddComponent<LabMaterials>();
                world = palette.WorldText(font);
                Assert.AreSame(world, palette.WorldText(font));
                Assert.AreNotSame(font.material, world);
                Assert.AreEqual("FirstPersonSokoban/GalleryWorldText", world.shader.name);
                Assert.AreSame(original, font.material.shader);
            }
            finally { Object.DestroyImmediate(owner); }
            Assert.IsTrue(world == null, "The palette must release its world text clone.");
            Assert.IsTrue(font.material != null, "Destroying the clone must preserve the UI font material.");
        }

        [Test]
        public void PaletteInitializationIsIdempotentAndOwnedResourcesAreReleased()
        {
            var owner = new GameObject("Palette ownership test");
            Material floor = null;
            Texture texture = null;
            try
            {
                LabMaterials palette = owner.AddComponent<LabMaterials>();
                palette.Initialize();
                floor = palette.Floor;
                texture = floor.mainTexture;
                Assert.NotNull(floor);
                Assert.NotNull(texture);
                int materialCount = palette.GeneratedMaterialCount;
                int textureCount = palette.GeneratedTextureCount;
                palette.Initialize();
                Assert.AreSame(floor, palette.Floor);
                Assert.AreEqual(materialCount, palette.GeneratedMaterialCount);
                Assert.AreEqual(textureCount, palette.GeneratedTextureCount);
                Assert.AreNotSame(palette.Module, palette.ModuleComplete,
                    "Completion must not mutate the shared normal material.");
            }
            finally { Object.DestroyImmediate(owner); }
            Assert.IsTrue(floor == null, "The owner must release its generated material.");
            Assert.IsTrue(texture == null, "The owner must release its generated texture.");
        }
    }
}
