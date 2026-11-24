using NUnit.Framework;

namespace Neuralife.VirtualHand.Editor.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="FirstPersonFullBodyBuilder.ClassifyPart"/>.
    ///
    /// Pure string-mapping tests: no assets are loaded and no prefab is built, so the suite is safe to
    /// run in the Unity Test Runner (Window > General > Test Runner > EditMode) on any machine.
    ///
    /// Note on side effects: ClassifyPart records unmatched names in a static set and logs a single
    /// warning per unknown name. The unmatched-name test below therefore contributes exactly one entry
    /// to that tally if it runs before a Build invocation in the same editor session; harmless, but do
    /// not treat that tally as "clean" when reading acceptance logs from the same session.
    /// </summary>
    [TestFixture]
    public class FullBodyBuilderTests
    {
        // ------------------------------------------------------------------ required cases

        [Test]
        public void ClassifyPart_SweaterMaterial_GoesToClothing()
        {
            Assert.AreEqual("Clothing", FirstPersonFullBodyBuilder.ClassifyPart("sweater_mat"));
        }

        [Test]
        public void ClassifyPart_ShoeMaterial_GoesToShoes()
        {
            Assert.AreEqual("Shoes", FirstPersonFullBodyBuilder.ClassifyPart("shoe"));
        }

        [Test]
        public void ClassifyPart_SkinMaterial_GoesToBody()
        {
            Assert.AreEqual("Body", FirstPersonFullBodyBuilder.ClassifyPart("skin"));
        }

        // ------------------------------------------------------------------ keyword table coverage

        [TestCase("sweater_mat", "Clothing")]
        [TestCase("denim_pants", "Clothing")]
        [TestCase("wool_jacket", "Clothing")]
        [TestCase("knit_dress", "Clothing")]
        [TestCase("cotton_sock", "Clothing")]
        [TestCase("shoe", "Shoes")]
        [TestCase("leather_boot", "Shoes")]
        [TestCase("sneaker_01", "Shoes")]
        [TestCase("sandal_left", "Shoes")]
        [TestCase("skin", "Body")]
        [TestCase("face", "Body")]
        [TestCase("torso", "Body")]
        [TestCase("eye_white", "Body")]
        [TestCase("hair_mat", "Body")]
        [TestCase("neck_skin", "Body")]
        public void ClassifyPart_KeywordMapping(string materialName, string expectedPart)
        {
            Assert.AreEqual(expectedPart, FirstPersonFullBodyBuilder.ClassifyPart(materialName));
        }

        // ------------------------------------------------------------------ precedence + normalization

        [Test]
        public void ClassifyPart_NameMatchingBothShoesAndClothing_PrefersShoes()
        {
            // "shoe" (Shoes keyword) and "cloth" (Clothing keyword) both occur; Shoes is evaluated first.
            Assert.AreEqual("Shoes", FirstPersonFullBodyBuilder.ClassifyPart("shoe_cloth_mat"));
        }

        [Test]
        public void ClassifyPart_NameMatchingClothingAndBody_PrefersClothing()
        {
            // "sweater" (Clothing) and "skin" (Body) both occur; Clothing is evaluated before Body.
            Assert.AreEqual("Clothing", FirstPersonFullBodyBuilder.ClassifyPart("sweater_skin_blend"));
        }

        [Test]
        public void ClassifyPart_IsCaseInsensitive()
        {
            Assert.AreEqual("Clothing", FirstPersonFullBodyBuilder.ClassifyPart("SWEATER_MAT"));
            Assert.AreEqual("Shoes", FirstPersonFullBodyBuilder.ClassifyPart("Boot_Leather"));
            Assert.AreEqual("Body", FirstPersonFullBodyBuilder.ClassifyPart("Torso_Skin"));
        }

        // ------------------------------------------------------------------ fallback behavior

        [Test]
        public void ClassifyPart_UnknownName_DefaultsToBody()
        {
            // Documented fallback: unmatched names map to Body (and emit one warning).
            Assert.AreEqual("Body", FirstPersonFullBodyBuilder.ClassifyPart("mystery_panel_mat"));
        }

        [Test]
        public void ClassifyPart_NullOrEmpty_DefaultsToBody()
        {
            // Must not throw: null/empty names fall through to the Body default.
            Assert.AreEqual("Body", FirstPersonFullBodyBuilder.ClassifyPart(null));
            Assert.AreEqual("Body", FirstPersonFullBodyBuilder.ClassifyPart(string.Empty));
        }
    }
}
