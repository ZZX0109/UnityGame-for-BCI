using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonSokoban.Presentation
{
    /// <summary>
    /// A self-contained built-in-renderer palette. No downloaded textures and no
    /// per-renderer material clones: one palette owns the shared room materials.
    /// The menu preview can create a separate owner and release it independently.
    /// </summary>
    public sealed class LabMaterials : MonoBehaviour
    {
        public static readonly Color AccentColor = Hex("46D7C4");
        public static readonly Color SuccessColor = Hex("72E28A");
        public static readonly Color WarningColor = Hex("FFB357");

        private readonly List<Material> materials = new List<Material>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private bool initialized;

        public Material Floor { get; private set; }
        public Material Wall { get; private set; }
        public Material Trim { get; private set; }
        public Material Dark { get; private set; }
        public Material Metal { get; private set; }
        public Material Module { get; private set; }
        public Material ModuleComplete { get; private set; }
        public Material ModuleDenied { get; private set; }
        public Material GoalActive { get; private set; }
        public Material GoalComplete { get; private set; }
        public Material Glass { get; private set; }
        public Material Light { get; private set; }
        public Material Warning { get; private set; }
        public int GeneratedTextureCount => textures.Count;
        public int GeneratedMaterialCount => materials.Count;

        public void Initialize()
        {
            if (initialized) return;
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                throw new System.InvalidOperationException("The laboratory presentation requires the built-in Standard shader.");
            initialized = true;

            Texture2D floorGrain = Grain("Floor fine aggregate", 0, false);
            Texture2D wallGrain = Grain("Wall powder coat", 1, false);
            Texture2D metalGrain = Grain("Brushed metal", 2, false);
            Texture2D floorRoughness = Grain("Floor roughness", 0, true);
            Texture2D wallRoughness = Grain("Powder coat roughness", 1, true);
            Texture2D metalRoughness = Grain("Metal roughness", 2, true);

            Floor = Surface(shader, "Lab Floor", "718087", 0.08f, 0.28f, floorGrain, floorRoughness);
            Wall = Surface(shader, "Lab Wall", "DADDD8", 0.03f, 0.22f, wallGrain, wallRoughness);
            Trim = Surface(shader, "Lab Trim", "64777D", 0.35f, 0.32f, metalGrain, metalRoughness);
            Dark = Surface(shader, "Lab Graphite", "263841", 0.16f, 0.25f, wallGrain, wallRoughness);
            Metal = Surface(shader, "Lab Satin Metal", "A0ADB0", 0.48f, 0.38f, metalGrain, metalRoughness);
            Module = Surface(shader, "Training Module Shell", "D9DEDB", 0.12f, 0.30f, wallGrain, wallRoughness);
            ModuleComplete = Surface(shader, "Training Module Confirmed", "D5E5DC", 0.12f, 0.30f, wallGrain, wallRoughness);
            ModuleDenied = Surface(shader, "Training Module Attention", "E6D7C2", 0.12f, 0.30f, wallGrain, wallRoughness);
            GoalActive = Emissive(shader, "Sensor Standby", AccentColor, 0.65f);
            GoalComplete = Emissive(shader, "Sensor Confirmed", SuccessColor, 0.85f);
            Glass = Surface(shader, "Observation Glass", "294451", 0.52f, 0.78f, null, null);
            Light = Emissive(shader, "Ceiling Diffuser", Hex("EBF3EF"), 1.05f);
            Warning = Emissive(shader, "Warm Status", WarningColor, 0.35f);
        }

        private Material Surface(Shader shader, string name, string color, float metallic,
            float smoothness, Texture2D grain, Texture2D roughness)
        {
            var material = new Material(shader)
            {
                name = name + " (Runtime)",
                color = Hex(color),
                mainTexture = grain,
                enableInstancing = true
            };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            if (roughness != null)
            {
                material.SetTexture("_MetallicGlossMap", roughness);
                material.SetFloat("_GlossMapScale", smoothness);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            materials.Add(material);
            return material;
        }

        private Material Emissive(Shader shader, string name, Color color, float strength)
        {
            Material material = Surface(shader, name, "FFFFFF", 0.1f, 0.35f, null, null);
            material.color = color;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * strength);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return material;
        }

        private Texture2D Grain(string name, int kind, bool roughness)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, roughness)
            {
                name = name + " (Runtime)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Deterministic fine grain, kept deliberately below the contrast of
                // the boxes and targets. The map repeats on each existing grid tile.
                float noise = Hash(x, y, kind);
                float striation = kind == 2 ? Mathf.Sin(y * 0.8f) * 0.017f : 0f;
                float value = 0.94f + (noise - 0.5f) * 0.055f + striation;
                if (kind == 0 && (x < 2 || y < 2 || x > size - 3 || y > size - 3)) value *= 0.86f;
                if (roughness)
                {
                    float metal = kind == 2 ? 0.45f : kind == 0 ? 0.08f : 0.06f;
                    pixels[y * size + x] = new Color(metal, metal, metal, 0.68f + noise * 0.20f);
                }
                else pixels[y * size + x] = new Color(value, value, value, 1f);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            textures.Add(texture);
            return texture;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint value = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                value = (value ^ (value >> 13)) * 1274126177u;
                return (value ^ (value >> 16)) / (float)uint.MaxValue;
            }
        }

        public static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials) Release(material);
            foreach (Texture2D texture in textures) Release(texture);
            materials.Clear();
            textures.Clear();
        }

        private static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
