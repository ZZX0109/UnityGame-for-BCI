using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetMaterialPersistenceTests
    {
        [TestCase("Assets/StreetCrossing/Materials/StreetSurface.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/Asphalt.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/Paving.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/Plaster.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/FacadeTrim.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/Glazing.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/TyreRubber.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/VehiclePaint.mat")]
        [TestCase("Assets/StreetCrossing/Resources/StreetCrossing/Modules/WheelAlloy.mat")]
        public void StandardInspectorKeepsNonEmissiveAssetsInTheirPreparedState(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.NotNull(material);
            Assert.AreEqual("Standard", material.shader.name);
            Assert.AreEqual(MaterialGlobalIlluminationFlags.EmissiveIsBlack, material.globalIlluminationFlags);
            Assert.IsFalse(material.IsKeywordEnabled("_EMISSION"));
            string[] keywords = material.shaderKeywords;
            Type standardGui = typeof(ShaderGUI).Assembly.GetType("UnityEditor.StandardShaderGUI");
            Assert.NotNull(standardGui, "Validate the same Standard inspector used by this Editor.");
            var validator = standardGui.GetMethod("ValidateMaterial");
            Assert.NotNull(validator);
            validator.Invoke(Activator.CreateInstance(standardGui), new object[] { material });
            Assert.AreEqual(MaterialGlobalIlluminationFlags.EmissiveIsBlack, material.globalIlluminationFlags);
            CollectionAssert.AreEquivalent(keywords, material.shaderKeywords, "Opening the first material Inspector may not repair keywords during capture.");
            Assert.AreEqual(material.GetTexture("_BumpMap") != null, material.IsKeywordEnabled("_NORMALMAP"));
        }
    }
}
