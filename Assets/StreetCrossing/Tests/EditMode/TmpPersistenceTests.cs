using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    public sealed class TmpPersistenceTests
    {
        private const string Settings = "Assets/BCIShared/TMP/Resources/TMP Settings.asset";

        [TestCase("m_leadingCharacters", "LineBreakingLeading.txt", '，')]
        [TestCase("m_followingCharacters", "LineBreakingFollowing.txt", '（')]
        public void ReimportedSettingsUsePersistentImportedText(string field, string filename, char required)
        {
            AssetDatabase.ImportAsset(Settings, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Settings);
            Assert.NotNull(settings, "Settings must survive a fresh YAML import.");
            var data = new SerializedObject(settings);
            var table = data.FindProperty(field).objectReferenceValue as TextAsset;
            Assert.NotNull(table);
            Assert.AreEqual("Assets/BCIShared/TMP/Resources/BCIShared/TMP/" + filename, AssetDatabase.GetAssetPath(table));
            Assert.IsTrue(table.text.Contains(required.ToString()));
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(Settings))
                Assert.IsFalse(asset is TextAsset, "Punctuation must use the text importer rather than serialized TextAsset sub-assets.");
        }
    }
}
