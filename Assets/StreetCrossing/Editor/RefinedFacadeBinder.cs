using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Editor
{
    /// <summary>
    /// Binds the refined stucco texture to the refined near-field residence's masonry
    /// material in its runtime prefab. Other FBX sub-materials keep their original colours.
    /// </summary>
    public static class RefinedFacadeBinder
    {
        private const string PrefabPath = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/RefinedResidence.prefab";
        private const string StuccoPath = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/PlasterRefined.mat";

        [MenuItem("Street Crossing/Bind Refined Facade Texture")]
        public static void Bind()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Material stucco = AssetDatabase.LoadAssetAtPath<Material>(StuccoPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (stucco == null || prefab == null)
                throw new InvalidOperationException("Missing refined stucco material or residence prefab.");

            int changed = 0;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || materials[i].name != "FacadeMasonry") continue;
                    materials[i] = stucco;
                    changed++;
                }
                if (changed > 0) renderer.sharedMaterials = materials;
            }
            if (changed == 0) throw new InvalidOperationException("FacadeMasonry material not found in " + PrefabPath);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[RefinedFacade] stucco bound to " + changed + " masonry slots in " + PrefabPath);
        }
    }
}
