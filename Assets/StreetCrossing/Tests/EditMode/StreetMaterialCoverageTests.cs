using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// Building/street material regression coverage extending StreetMaterialPersistenceTests: the
    /// refined-residence, cone-reflector and cafe ceramics materials are held to the same Standard
    /// inspector invariant, sky materials are pinned to their skybox shaders, and every material
    /// under Assets/StreetCrossing must stay on a loadable, explicitly recognised shader.
    /// </summary>
    public sealed class StreetMaterialCoverageTests
    {
        private const string StandardShader = "Standard";

        private static readonly string[] StandardMaterials =
        {
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/PavingRefined.mat",
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/PlasterRefined.mat",
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/StreetConeReflector.mat",
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/KayKitRestaurant/CafeCeramics.mat"
        };

        private static readonly string[] SkyMaterials =
        {
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/RefinedSky.mat",
            "Assets/StreetCrossing/Resources/StreetCrossing/Modules/SkyMorning.mat"
        };

        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/PavingRefined.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/PlasterRefined.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/StreetConeReflector.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/KayKitRestaurant/CafeCeramics.mat")]
        public void RefinedAndPropMaterialsSurviveTheStandardInspector(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.NotNull(material, path);
            Assert.AreEqual(StandardShader, material.shader.name, path);
            Assert.AreEqual(MaterialGlobalIlluminationFlags.EmissiveIsBlack, material.globalIlluminationFlags, path);
            Assert.IsFalse(material.IsKeywordEnabled("_EMISSION"), path);
            string[] keywords = material.shaderKeywords;
            Type standardGui = typeof(ShaderGUI).Assembly.GetType("UnityEditor.StandardShaderGUI");
            Assert.NotNull(standardGui);
            var validator = standardGui.GetMethod("ValidateMaterial");
            Assert.NotNull(validator);
            validator.Invoke(Activator.CreateInstance(standardGui), new object[] { material });
            Assert.AreEqual(MaterialGlobalIlluminationFlags.EmissiveIsBlack, material.globalIlluminationFlags, path);
            CollectionAssert.AreEquivalent(keywords, material.shaderKeywords, path + ": keywords may not change on inspector open.");
        }

        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/RefinedSky.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/SkyMorning.mat")]
        public void SkyMaterialsStayOnSkyboxShaders(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.NotNull(material, path);
            Assert.That(material.shader.name, Does.Contain("Skybox").IgnoreCase, path + " must keep a skybox shader or the sky disappears.");
        }

        [Test]
        public void EveryStreetCrossingMaterialLoadsOnAKnownShader()
        {
            Assert.That(Directory.Exists("Assets/StreetCrossing"), Is.True);
            foreach (string file in Directory.GetFiles("Assets/StreetCrossing", "*.mat", SearchOption.AllDirectories))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(file);
                Assert.That(material != null, "Unreadable material regression: " + file);
                Assert.That(material.shader != null, "Missing shader (magenta in play): " + file);
            }
        }
    }
}
