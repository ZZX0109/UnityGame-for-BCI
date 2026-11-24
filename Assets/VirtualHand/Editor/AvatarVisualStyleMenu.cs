using Neuralife.VirtualHand;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>Editor commands for creating the blue style asset and configuring selected styles.</summary>
    public static class AvatarVisualStyleMenu
    {
        private const string MaterialFolder = "Assets/VirtualHand/Materials/Avatar";
        private const string MaterialPath = MaterialFolder + "/AvatarBlueOutline.mat";
        private const string CreateMaterialMenu = "Tools/Neuralife/Virtual Hand/Create Avatar Blue Material";
        private const string InitializeStylesMenu = "Tools/Neuralife/Virtual Hand/Initialize Selected Avatar Visual Styles";

        [MenuItem(CreateMaterialMenu)]
        private static void CreateAvatarBlueMaterial()
        {
            Material material = LoadOrCreateBlueMaterial();
            if (!material)
                return;

            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
        }

        [MenuItem(InitializeStylesMenu)]
        private static void InitializeSelectedStyles()
        {
            AvatarVisualStyle[] styles = Selection.GetFiltered<AvatarVisualStyle>(SelectionMode.Editable);
            if (styles.Length == 0)
                return;

            Material material = LoadOrCreateBlueMaterial();
            if (!material)
                return;

            int initializedCount = 0;
            for (int i = 0; i < styles.Length; i++)
            {
                AvatarVisualStyle style = styles[i];
                if (!style)
                    continue;

                Undo.RecordObject(style, "Initialize Avatar Visual Style");
                style.targets = style.GetComponentsInChildren<Renderer>(true);

                SerializedObject serializedStyle = new SerializedObject(style);
                SerializedProperty shaderProperty = serializedStyle.FindProperty("blueShader");
                if (shaderProperty == null)
                {
                    Debug.LogError("AvatarVisualStyle is missing its serialized blueShader field.", style);
                    continue;
                }

                shaderProperty.objectReferenceValue = material.shader;
                serializedStyle.ApplyModifiedProperties();
                EditorUtility.SetDirty(style);
                PrefabUtility.RecordPrefabInstancePropertyModifications(style);
                initializedCount++;
            }

            Debug.Log("Initialized " + initializedCount + " selected AvatarVisualStyle component(s).", Selection.activeObject);
        }

        [MenuItem(InitializeStylesMenu, true)]
        private static bool CanInitializeSelectedStyles()
        {
            return Selection.GetFiltered<AvatarVisualStyle>(SelectionMode.Editable).Length > 0;
        }

        private static Material LoadOrCreateBlueMaterial()
        {
            Shader shader = Shader.Find(AvatarVisualStyle.ShaderName);
            if (!shader)
            {
                Debug.LogError("Could not find shader '" + AvatarVisualStyle.ShaderName + "'.");
                return null;
            }

            EnsureAssetFolder(MaterialFolder);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader)
                {
                    name = "Avatar Blue Outline"
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                Undo.RecordObject(material, "Update Avatar Blue Material Shader");
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string nextPath = currentPath + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                currentPath = nextPath;
            }
        }
    }
}
