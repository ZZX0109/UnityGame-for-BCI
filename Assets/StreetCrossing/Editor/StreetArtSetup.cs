using System;
using System.Linq;
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
