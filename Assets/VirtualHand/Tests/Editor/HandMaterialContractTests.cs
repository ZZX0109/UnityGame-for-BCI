#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// M3 contract: the live-bound Elena hand skin material
    /// (Assets/VirtualHand/Materials/Generated/Elena_Skin_ElenaHand_R.mat,
    /// GUID e2111a21da30ec94dbbfe94d82fcd04f) must keep roughnessFactor 1
    /// (matte); the builder ElenaHandPrefabBuilder writes 1f. The test reads
    /// the .mat YAML text directly so it runs in plain edit mode without
    /// instantiating the asset.
    /// </summary>
    public sealed class HandMaterialContractTests
    {
        const string HandSkinMaterialGuid = "e2111a21da30ec94dbbfe94d82fcd04f";
        const string HandSkinMaterialFileName = "Elena_Skin_ElenaHand_R.mat";

        [Test]
        public void ElenaHandSkinMaterialIsMatte()
        {
            string path = AssetDatabase.GUIDToAssetPath(HandSkinMaterialGuid);
            Assert.That(path, Is.Not.Null.And.Not.Empty,
                $"GUID {HandSkinMaterialGuid} did not resolve to an asset path; " +
                "the hand skin material is missing from the project or its GUID changed.");
            Assert.That(path, Does.EndWith(HandSkinMaterialFileName),
                $"Expected GUID {HandSkinMaterialGuid} to resolve to {HandSkinMaterialFileName} " +
                $"but it resolved to '{path}'.");

            string text = File.ReadAllText(path);

            // Accepts 1 or 1.0 (trailing zeros) at end of line; rejects 0.9, 0.5, 1.5, etc.
            var matte = new Regex(@"roughnessFactor:\s*1(\.0+)?\s*$", RegexOptions.Multiline);
            Assert.That(matte.IsMatch(text), Is.True,
                "M3 contract violation: the live-bound hand skin must keep roughnessFactor 1 (matte); " +
                "the builder ElenaHandPrefabBuilder writes 1f. " +
                $"No line in '{path}' matches 'roughnessFactor: 1' (or 1.0) at end of line.");

            // Belt-and-braces: no non-matte roughnessFactor 0 anywhere in the file.
            var nonMatte = new Regex(@"roughnessFactor:\s*0", RegexOptions.Multiline);
            Assert.That(nonMatte.IsMatch(text), Is.False,
                "M3 contract violation: the live-bound hand skin must keep roughnessFactor 1 (matte); " +
                "the builder ElenaHandPrefabBuilder writes 1f. " +
                $"'{path}' contains 'roughnessFactor: 0'.");
        }
    }
}
#endif
