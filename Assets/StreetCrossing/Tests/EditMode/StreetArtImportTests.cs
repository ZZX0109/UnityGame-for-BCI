using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class StreetArtImportTests
    {
        [TestCase("SedanModule")]
        [TestCase("HatchbackModule")]
        [TestCase("VanModule")]
        [TestCase("ResidentialModule")]
        public void ReusableModulesImportRealMeshSubassetsAndSupportedMaterials(string module)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StreetCrossing/Resources/StreetCrossing/Modules/" + module + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            MeshFilter[] meshes = prefab.GetComponentsInChildren<MeshFilter>(true); Assert.That(meshes.Length, Is.GreaterThanOrEqualTo(3));
            foreach (MeshFilter filter in meshes)
            {
                Assert.That(filter.sharedMesh, Is.Not.Null, module + "/" + filter.name + " mesh did not import.");
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0)); Assert.That(filter.sharedMesh.bounds.size.sqrMagnitude, Is.GreaterThan(.001f));
                Assert.That(filter.sharedMesh.subMeshCount, Is.GreaterThan(0)); Assert.That(filter.sharedMesh.GetIndexCount(0), Is.GreaterThan(0));
                Material material = filter.GetComponent<Renderer>().sharedMaterial;
                Assert.That(material, Is.Not.Null); Assert.That(material.shader, Is.Not.Null); Assert.That(material.shader.name, Does.Not.Contain("Error"));
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null), "Real delivery requires a graphics device for material support.");
                Assert.That(material.shader.isSupported, Is.True);
            }
        }
        [TestCase("Asphalt")]
        [TestCase("Paving")]
        [TestCase("Plaster")]
        public void RoadAndFacadeMaterialsImportTileableAlbedoAndNormalMaps(string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetCrossing/Resources/StreetCrossing/Modules/" + name + ".mat");
            Assert.That(material, Is.Not.Null); Assert.That(material.GetTexture("_MainTex"), Is.Not.Null); Assert.That(material.GetTexture("_BumpMap"), Is.Not.Null);
            Texture2D normal = (Texture2D)material.GetTexture("_BumpMap");
            TextureImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
            Assert.That(importer, Is.Not.Null); Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(importer.sRGBTexture, Is.False); Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(material.GetFloat("_Glossiness"), Is.InRange(0, 1));
        }
    }
}
