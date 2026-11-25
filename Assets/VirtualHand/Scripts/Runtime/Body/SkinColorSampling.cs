using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// Shared, runtime-safe skin colour sampling.  This is the single implementation of the
    /// RenderTexture-blit average used by both the editor <c>SkinTintProbe</c> (M2 stage 1)
    /// and the runtime <see cref="BodySkinTintApplier"/> (M2 stage 2), so both always agree
    /// on what "combined skin colour" means: tint (Standard <c>_Color</c> or glTF-style
    /// <c>baseColorFactor</c>) multiplied per-channel with the average of the material's
    /// main texture.
    /// </summary>
    public static class SkinColorSampling
    {
        /// <summary>Side length of the temporary render target used for the average blit.</summary>
        const int SampleSize = 64;

        /// <summary>
        /// Tint of a material: <c>baseColorFactor</c> when the shader exposes it, otherwise
        /// Standard's <c>_Color</c>, otherwise white.  Never throws on unusual shaders.
        /// </summary>
        public static Color GetTint(Material material)
        {
            if (material == null)
                return Color.white;
            if (material.HasProperty("baseColorFactor"))
                return material.GetColor("baseColorFactor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");
            return Color.white;
        }

        /// <summary>Main/base colour texture of a material across the shader conventions in use.</summary>
        public static Texture GetBaseTexture(Material material)
        {
            if (material == null)
                return null;
            Texture tex = material.mainTexture;
            if (tex == null && material.HasProperty("baseColorTexture"))
                tex = material.GetTexture("baseColorTexture");
            if (tex == null && material.HasProperty("_BaseMap"))
                tex = material.GetTexture("_BaseMap");
            if (tex == null && material.HasProperty("_BaseColorMap"))
                tex = material.GetTexture("_BaseColorMap");
            return tex;
        }

        /// <summary>
        /// Per-channel average of <paramref name="texture"/> computed on the GPU (blit into a
        /// temporary render target + ReadPixels), which also works for non-readable textures.
        /// White is returned for a null texture (tint-only materials).
        /// </summary>
        public static bool TryGetTextureAverage(Texture texture, out Color average, bool excludeBlackPadding = false)
        {
            average = Color.white;
            if (texture == null)
                return true; // untinted-by-texture material: average is defined as white

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(SampleSize, SampleSize, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var readback = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false);
                try
                {
                    readback.ReadPixels(new Rect(0f, 0f, SampleSize, SampleSize), 0, 0);
                    readback.Apply(false);
                    Color[] pixels = readback.GetPixels();
                    if (pixels == null || pixels.Length == 0)
                        return false;

                    var sum = new Vector4(0f, 0f, 0f, 0f);
                    int count = 0;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        // Elena's opaque black atlas background covers over half the image
                        // and is outside the mapped skin. Including it darkens the avatar face.
                        if (excludeBlackPadding && pixels[i].maxColorComponent < 0.02f) continue;
                        count++;
                        sum.x += pixels[i].r;
                        sum.y += pixels[i].g;
                        sum.z += pixels[i].b;
                        sum.w += pixels[i].a;
                    }
                    if (count == 0) return false;
                    float inv = 1f / count;
                    average = new Color(sum.x * inv, sum.y * inv, sum.z * inv, sum.w * inv);
                    return true;
                }
                finally
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        Object.DestroyImmediate(readback);
                    else
                        Object.Destroy(readback);
#else
                    Object.Destroy(readback);
#endif
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Combined skin colour of a live material (tint × texture average per channel).
        /// Fails only when the GPU readback itself fails.
        /// </summary>
        public static bool TryGetCombinedColor(Material material, out Color combined)
        {
            combined = Color.white;
            if (material == null)
                return false;

            Color tint = GetTint(material);
            Texture tex = GetBaseTexture(material);
            if (!TryGetTextureAverage(tex, out Color average, tex != null && tex.name == "ElenaHand_BaseColor"))
                return false;

            combined = Color.white;
            if (tex != null)
                combined = tint * average; // Color*Color is per-channel in Unity
            else
                combined = tint;
            combined.a = 1f;
            return true;
        }

        /// <summary>Per-channel ratio a/b, clamped to the M2 tint range; b≈0 yields 1 (no tint).</summary>
        public static float ClampedRatio(float a, float b)
        {
            if (b < 1e-4f)
                return 1f;
            return Mathf.Clamp(a / b, 0.2f, 2.5f);
        }
    }
}
