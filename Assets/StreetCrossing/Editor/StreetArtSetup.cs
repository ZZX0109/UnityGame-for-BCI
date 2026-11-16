using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Editor
{
    /// <summary>Resolves single-mesh OBJ subasset IDs through Unity itself, rather than relying on importer-version IDs.</summary>
    [InitializeOnLoad]
    public static class StreetArtSetup
    {
        static StreetArtSetup() { EditorApplication.delayCall += EnsureModuleMeshes; }
        public static void EnsureModuleMeshes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureMaterialState();
            string folder = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/";
            foreach (string module in new[] { "Sedan", "Hatchback", "Van", "Residential" })
            {
                string path = folder + module + "Module.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path); bool changed = false;
                try
                {
                    foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        string name = filter.gameObject.name;
                        string meshName = module == "Residential"
                            ? name == "Plaster masonry" ? "ResidentialWalls" : name == "Recessed glazing" ? "ResidentialGlass" : "ResidentialTrim"
                            : name == "Painted body" ? module + "Body" : name == "Cabin" ? module + "Roof" : name == "Glazed cabin" ? module + "Glass" : name == "Tyre" ? "Wheel" : "Hub";
                        string meshPath = "Assets/StreetCrossing/Art/" + meshName + ".obj";
                        Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(meshPath).OfType<Mesh>().FirstOrDefault();
                        if (mesh == null) throw new InvalidOperationException("Original mesh did not import: " + meshPath);
                        if (filter.sharedMesh != mesh) { filter.sharedMesh = mesh; changed = true; }
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        /// <summary>Serialize the same non-emissive Standard state that the first material Inspector expects.</summary>
        public static void EnsureMaterialState()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string[] paths = AssetDatabase.FindAssets("t:Material", new[] {
                "Assets/StreetCrossing/Materials", "Assets/StreetCrossing/Resources/StreetCrossing/Modules"
            }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (string path in paths)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material || material.shader.name != "Standard") continue;
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                material.SetColor("_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
                material.shaderKeywords = material.shaderKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray();
                if (material.GetTexture("_BumpMap")) material.EnableKeyword("_NORMALMAP");
                else material.DisableKeyword("_NORMALMAP");
                // Round-trip defaults into saved properties so Inspector validation cannot add them during capture.
                Shader shader = material.shader;
                for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
                {
                    string property = ShaderUtil.GetPropertyName(shader, index);
                    switch (ShaderUtil.GetPropertyType(shader, index))
                    {
                        case ShaderUtil.ShaderPropertyType.Color: material.SetColor(property, material.GetColor(property)); break;
                        case ShaderUtil.ShaderPropertyType.Vector: material.SetVector(property, material.GetVector(property)); break;
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range: material.SetFloat(property, material.GetFloat(property)); break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            material.SetTexture(property, material.GetTexture(property));
                            material.SetTextureScale(property, material.GetTextureScale(property));
                            material.SetTextureOffset(property, material.GetTextureOffset(property)); break;
                    }
                }
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
            foreach (string path in paths)
            {
                string serialized = File.ReadAllText(path, Encoding.UTF8);
                string normalized = string.Join("\n", serialized.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd(' ', '\t')));
                if (serialized == normalized) continue;
                File.WriteAllText(path, normalized, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
        }
        public static void ValidateModules()
        {
            string folder = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/";
            foreach (string name in new[] { "Sedan", "Hatchback", "Van", "Residential" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + "Module.prefab");
                if (prefab == null) throw new InvalidOperationException("Missing reusable module: " + name);
                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh == null || filter.GetComponent<Renderer>().sharedMaterial == null)
                        throw new InvalidOperationException("Unresolved module mesh/material: " + name + "/" + filter.name);
            }
        }
    }
}
