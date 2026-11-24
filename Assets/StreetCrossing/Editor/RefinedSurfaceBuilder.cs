using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetCrossing.Editor
{
    /// <summary>
    /// Additive presentation refinement: build materials from the refined paving/plaster
    /// textures and a procedural gradient sky, then bind them to the shipping scene.
    /// Original Paving.mat / Plaster.mat and the scene's gameplay settings are preserved;
    /// only Resources material references and RenderSettings.skybox change.
    /// </summary>
    public static class RefinedSurfaceBuilder
    {
        private const string Art = "Assets/StreetCrossing/Art/Refined/";
        private const string Modules = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/";
        private const string Scene = "Assets/StreetCrossing/Scenes/StreetCrossingScene.unity";
        private const string Sky = Modules + "RefinedSky.mat";

        [MenuItem("Street Crossing/Build Refined Surfaces And Sky")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            CreateOrUpdateSurface("PavingRefined", Art + "PavingAlbedo_Refined.png",
                "StreetCrossing/Modules/Paving", 4f, 0.28f, 0.03f);
            CreateOrUpdateSurface("PlasterRefined", Art + "PlasterAlbedo_Refined.png",
                "StreetCrossing/Modules/Plaster", 3f, 0.12f, 0.01f);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(Sky);
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null) throw new InvalidOperationException("Built-in Skybox/Procedural shader is missing.");
            if (sky == null)
            {
                sky = new Material(skyShader) { name = "RefinedSky" };
                AssetDatabase.CreateAsset(sky, Sky);
            }
            sky.shader = skyShader;
            sky.SetColor("_SkyTint", new Color(0.55f, 0.69f, 0.84f));
            sky.SetColor("_GroundColor", new Color(0.78f, 0.75f, 0.68f));
            sky.SetFloat("_AtmosphereThickness", 1.05f);
            sky.SetFloat("_Exposure", 1.05f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssets();

            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.74f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.53f, 0.59f, 0.63f);
            RenderSettings.ambientGroundColor = new Color(0.39f, 0.39f, 0.35f);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[RefinedSurface] refined materials and sky bound to " + Scene);
        }

        private static void CreateOrUpdateSurface(string name, string texturePath, string sourceMaterial,
            float tiling, float glossiness, float metallic)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Material source = Resources.Load<Material>(sourceMaterial);
            if (texture == null || source == null)
                throw new InvalidOperationException("Missing refined texture or source material: " + name);
            string path = Modules + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(source.shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = source.shader;
            mat.mainTexture = texture;
            mat.mainTextureScale = new Vector2(tiling, tiling);
            mat.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
            mat.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
            mat.SetFloat("_BumpScale", .22f);
            mat.SetFloat("_Glossiness", glossiness);
            mat.SetFloat("_Metallic", metallic);
            mat.color = Color.white;
            EditorUtility.SetDirty(mat);
        }
    }
}
