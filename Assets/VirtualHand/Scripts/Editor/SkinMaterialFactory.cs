using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds (once) and returns stable skin Material assets for the generated
    /// Demo scene. Semantics fixed per integration review: roughness PNG bytes
    /// are NOT a metal map — the channel is converted to a smoothness ramp
    /// (alpha = 1 − roughness, RGB = 0, i.e. non-metal skin), the normal map
    /// is actually assigned to _BumpMap, and diffuse is the real 2K texture.
    /// Materials and the smoothness texture are saved under
    /// Assets/VirtualHand/Materials/Generated so they survive scene reloads.
    /// Asset import/normal-importer settings are applied when Codex runs
    /// GenerateProject (Editor context); this code does not run Unity itself.
    /// </summary>
    public static class SkinMaterialFactory
    {
        const string OutputDir = "Assets/VirtualHand/Materials/Generated";
        const string TextureDir = OutputDir + "/Textures";
        const string ModelsDir = "Assets/VirtualHand/Models";

        /// <summary>Create-or-update the stable skin material for one hand side.
        /// Existing assets are repaired/updated in place (not just returned),
        /// so older wrong configurations are corrected on re-runs.</summary>
        public static Material GetOrCreate(string side)
        {
            EnsureDirectories();
            string matPath = $"{OutputDir}/Hand_{side}_Skin.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard")) { name = $"Hand_{side}_Skin" };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelsDir}/Hand_{side}_diffuse.png");
            var nrm = EnsureNormalImporter($"{ModelsDir}/Hand_{side}_normal.png");
            var rgh = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelsDir}/Hand_{side}_roughness.png");

            mat.name = $"Hand_{side}_Skin";
            if (diff != null) mat.mainTexture = diff; else mat.mainTexture = null;

            if (nrm != null)
            {
                mat.SetTexture("_BumpMap", nrm);          // actually assigned
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
            }

            if (rgh != null)
            {
                var smooth = GetOrCreateSmoothnessTexture(side, rgh);
                mat.SetTexture("_MetallicGlossMap", smooth);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_Metallic", 0f);            // non-metal skin
                mat.SetFloat("_Glossiness", 0.5f);        // scale for the alpha ramp
                mat.SetTextureScale("_MetallicGlossMap", Vector2.one);
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.DisableKeyword("_METALLICGLOSSMAP");
            }

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[SkinMaterialFactory] material updated: " + matPath);
            return mat;
        }

        /// <summary>Ensure OutputDir and TextureDir exist as valid AssetDatabase
        /// folders before any WritePNG/CreateAsset call.</summary>
        static void EnsureDirectories()
        {
            foreach (var dir in new[] { OutputDir, TextureDir })
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (AssetDatabase.LoadAssetAtPath<DefaultAsset>(dir) == null)
                    AssetDatabase.ImportAsset(dir, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>Force the source normal PNG to a real NormalMap importer
        /// type (sRGB off) and re-import before use. Source PNG bytes are not
        /// modified — only the importer settings. Returns the re-imported
        /// texture, or null when the asset is missing.</summary>
        static Texture2D EnsureNormalImporter(string assetPath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) return null;
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return tex;
            bool dirty = false;
            if (importer.textureType != TextureImporterType.NormalMap)
            { importer.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (importer.sRGBTexture)
            { importer.sRGBTexture = false; dirty = true; }
            if (dirty)
            {
                importer.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                Debug.Log("[SkinMaterialFactory] normal importer fixed: " + assetPath);
            }
            return tex;
        }

        /// <summary>Rebuild the smoothness asset from the CURRENT source PNG
        /// every call: RGBA32, RGB = 0 (metal zero), alpha = 1 − roughness.
        /// The source PNG is not modified; the converted ramp is a separate
        /// generated asset re-imported at the same path (meta/GUID kept). No
        /// early-return on existing assets — A may have updated the source
        /// roughness since the last run, so correctness beats caching.</summary>
        static Texture2D GetOrCreateSmoothnessTexture(string side, Texture2D rgh)
        {
            EnsureDirectories();
            string texPath = $"{TextureDir}/Hand_{side}_smoothness.png";

            var smooth = new Texture2D(rgh.width, rgh.height,
                TextureFormat.RGBA32, false, true)   // linear, no sRGB
            {
                name = $"Hand_{side}_smoothness"
            };

            bool converted = false;
            // byte path: source PNG is often not CPU-readable after import
            string srcPath = Path.GetFullPath($"{ModelsDir}/Hand_{side}_roughness.png");
            if (File.Exists(srcPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(srcPath);
                    var src = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(src, bytes, false)) // non-readable flag bypassed
                    {
                        var srcPixels = src.GetPixels();
                        var outPixels = new Color[srcPixels.Length];
                        for (int i = 0; i < srcPixels.Length; i++)
                            outPixels[i] = new Color(0f, 0f, 0f,
                                1f - srcPixels[i].r);  // alpha = 1 - roughness
                        smooth.Reinitialize(src.width, src.height);
                        smooth.SetPixels(outPixels);
                        smooth.Apply(false, false);
                        converted = true;
                        UnityEngine.Object.DestroyImmediate(src);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SkinMaterialFactory] roughness conversion " +
                                     $"failed, using constant ramp: {e.Message}");
                }
            }
            if (!converted)
            {
                // conservative fallback: uniform mid smoothness, size matches
                // the declared texture (2x2 as constructed)
                var pixels = new Color[4];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(0f, 0f, 0f, 0.6f);
                smooth.Reinitialize(2, 2);
                smooth.SetPixels(pixels);
                smooth.Apply(false, false);
            }

            // PNG bytes: Unity re-imports as sRGB; the shader samples alpha the
            // same either way for a grey-scale ramp, so this is safe for v1
            File.WriteAllBytes(texPath, ImageConversion.EncodeToPNG(smooth));
            UnityEngine.Object.DestroyImmediate(smooth);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        /// <summary>Heuristic removed (rev 14): smoothness assets are now
        /// rebuilt from the current source PNG on every GenerateProject run,
        /// so no guesswork about existing assets is needed.</summary>
    }
}
