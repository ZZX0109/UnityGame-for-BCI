using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BCIShared.Editor
{
    /// <summary>Owns only missing bootstrap settings. Never imports the TMP sample package or replaces project settings.</summary>
    [InitializeOnLoad]
    public static class BCITMPResourceSetup
    {
        public const string SettingsPath = "Assets/BCIShared/TMP/Resources/TMP Settings.asset";
        static BCITMPResourceSetup() { EditorApplication.delayCall += EnsureResources; }

        public static void EnsureResources()
        {
            // Resources is a project-wide namespace. Reuse a canonical settings asset
            // from another game instead of creating a competing global singleton.
            if (Resources.Load<TMP_Settings>("TMP Settings")) return;
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_Settings"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path.Contains("/Resources/") && Path.GetFileNameWithoutExtension(path) == "TMP Settings") return;
            }
            if (AssetDatabase.LoadMainAssetAtPath(SettingsPath))
                throw new InvalidOperationException("BCI TMP settings path is occupied by a different asset. Preserve it and resolve the conflict.");
            if (!AssetDatabase.IsValidFolder("Assets/BCIShared/TMP/Resources"))
                AssetDatabase.CreateFolder("Assets/BCIShared/TMP", "Resources");
            var settings = ScriptableObject.CreateInstance<TMP_Settings>();
            settings.name = "TMP Settings";
            // Let Unity serialize the actual installed package's MonoScript GUID.
            // No hard-coded Library/PackageCache path or guessed package GUID.
            var data = new SerializedObject(settings);
            SetBool(data, "m_enableWordWrapping", true);
            SetBool(data, "m_enableKerning", true);
            SetBool(data, "m_enableParseEscapeCharacters", true);
            SetBool(data, "m_EnableRaycastTarget", false);
            SetBool(data, "m_GetFontFeaturesAtRuntime", false);
            SetBool(data, "m_warningsDisabled", false);
            SetFloat(data, "m_defaultFontSize", 24);
            SetFloat(data, "m_defaultAutoSizeMinRatio", 0.5f);
            SetFloat(data, "m_defaultAutoSizeMaxRatio", 2);
            var fallback = data.FindProperty("m_fallbackFontAssets");
            if (fallback != null) fallback.ClearArray();
            var defaultFont = data.FindProperty("m_defaultFontAsset");
            if (defaultFont != null) defaultFont.objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(SettingsPath, ImportAssetOptions.ForceSynchronousImport);
        }

        public static void ValidateReady()
        {
            EnsureResources();
            if (!Resources.Load<TMP_Settings>("TMP Settings")) throw new InvalidOperationException("A canonical Resources/TMP Settings asset is required before tests or player build.");
            if (!AssetDatabase.LoadAssetAtPath<Shader>("Assets/BCIShared/TMP/Resources/BCIShared/TMP/BCITMPBitmap.shader"))
                throw new InvalidOperationException("The shared original TMP bitmap shader is missing.");
        }

        private static void SetBool(SerializedObject data, string name, bool value) { var p = data.FindProperty(name); if (p != null) p.boolValue = value; }
        private static void SetFloat(SerializedObject data, string name, float value) { var p = data.FindProperty(name); if (p != null) p.floatValue = value; }
    }
}
