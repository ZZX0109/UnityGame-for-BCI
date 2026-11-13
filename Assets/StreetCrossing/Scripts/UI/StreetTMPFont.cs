using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace StreetCrossing.UI
{
    /// <summary>Owns the dynamic font/atlases. Explicit Resources assets remove the TMP Essential Resources import step.</summary>
    public sealed class StreetTMPFont : IDisposable
    {
        public const string ShaderResource = "BCIShared/TMP/BCITMPBitmap";
        public TMP_FontAsset Asset { get; }
        public StreetTMPFont(Font source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (TMP_Settings.instance == null) throw new InvalidOperationException("Missing Resources/TMP Settings.asset.");
            Shader shader = Resources.Load<Shader>(ShaderResource);
            Shader compatibility = Resources.Load<Shader>("BCIShared/TMP/BCITMPBitmapCompatibility");
            if (shader == null || compatibility == null || !shader.isSupported) throw new InvalidOperationException("Shared UI bitmap shader is missing or unsupported.");
            // TMP 3.0.6's bitmap factory looks up its canonical shader before returning a material.
            // Load our referenced implementation first; then explicitly assign it to remove lookup ambiguity.
            Asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (Asset == null || Asset.material == null) throw new InvalidOperationException("Could not create the Street UI font.");
            Asset.name = "Street UI Sans Dynamic";
            Asset.material.name = "Street UI Bitmap Runtime";
            Asset.material.shader = shader;
            if (Asset.material.HasProperty("_FaceColor")) Asset.material.SetColor("_FaceColor", Color.white);
            Asset.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");
        }
        public void Dispose()
        {
            if (Asset == null) return;
            HashSet<Texture2D> textures = new HashSet<Texture2D>(Asset.atlasTextures);
            foreach (Texture2D texture in textures) if (texture != null) UnityEngine.Object.Destroy(texture);
            if (Asset.material != null) UnityEngine.Object.Destroy(Asset.material);
            UnityEngine.Object.Destroy(Asset);
        }
    }
}
