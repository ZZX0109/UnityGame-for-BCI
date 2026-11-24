using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Editor
{
    /// <summary>
    /// Post-process imported external FBX assets: extract embedded textures and
    /// rebuild Standard materials so Unity 2022.3 displays the Ara/MTree assets
    /// with their authored textures. Additive; only touches Art/External/*.fbx.
    /// </summary>
    public static class VisualUpgradeImporter
    {
        private const string Root = "Assets/StreetCrossing/Art/External/";

        [MenuItem("Street Crossing/Fix External FBX Materials")]
        public static void FixAll()
        {
            string[] paths = AssetDatabase.FindAssets("t:Model", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                try
                {
                    // Extract embedded textures into a sibling Textures folder.
                    string texDir = Path.GetDirectoryName(path) + "/Textures";
                    if (!AssetDatabase.IsValidFolder(texDir))
                    {
                        string parent = Path.GetDirectoryName(path);
                        string name = Path.GetFileName(texDir);
                        AssetDatabase.CreateFolder(parent, name);
                    }
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                    importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                    importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
                    bool extracted = importer.ExtractTextures(texDir);
                    importer.SaveAndReimport();
                    Debug.Log($"[VisualUpgradeImporter] {path}: extracted={extracted}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VisualUpgradeImporter] {path}: {ex.Message}");
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
