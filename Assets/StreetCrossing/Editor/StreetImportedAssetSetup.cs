using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.EditorTools
{
    /// <summary>
    /// One-click preparation for the CC0 street props imported from Poly Haven.
    ///
    /// Unity has no built-in glTF importer, so the models ship as FBX (which Unity imports natively,
    /// with colliders/cameras/lights off by default) and the runtime builds their Standard PBR
    /// materials from the 1k maps under Resources/StreetCrossing/Imported/Textures.
    ///
    /// The one thing no runtime code can fix is per-texture import settings, so this tool sets them:
    ///
    /// * `*_nor.jpg` becomes a NormalMap. Imported as default sRGB colour the normal is sampled
    ///   wrong and the prop lights up incorrectly.
    /// * every map is capped at 1024 px and compressed for the PICO target.
    ///
    /// It edits no mesh, prefab or scene geometry, and is safe to re-run.
    /// </summary>
    public static class StreetImportedAssetSetup
    {
        private const string TextureFolder = "Assets/StreetCrossing/Resources/StreetCrossing/Imported/Textures";
        private const int MaxTextureSize = 1024;

        [MenuItem("Street Crossing/Prepare imported CC0 street props")]
        public static void Prepare()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder))
            {
                Debug.LogWarning("Imported texture folder is missing: " + TextureFolder);
                return;
            }
            int changed = 0, scanned = 0;
            var withoutImporter = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                scanned++;
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { withoutImporter.Add(path); continue; }
                bool isNormal = Path.GetFileNameWithoutExtension(path).EndsWith("_nor");
                TextureImporterType wanted = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType == wanted && importer.maxTextureSize == MaxTextureSize
                    && importer.textureCompression == TextureImporterCompression.Compressed) continue;
                importer.textureType = wanted;
                importer.sRGBTexture = !isNormal;
                importer.maxTextureSize = MaxTextureSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                changed++;
            }
            AssetDatabase.SaveAssets();
            if (withoutImporter.Count > 0)
                Debug.LogWarning("No TextureImporter found on: " + string.Join(", ", withoutImporter));
            Debug.Log(string.Format(
                "Street imported props prepared: {0} of {1} texture(s) reconfigured (normal maps corrected, {2} px cap, compressed).",
                changed, scanned, MaxTextureSize));
        }
    }
}
