using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// Lead-only diagnostic v2 for the Game01 wrist NRE. Dumps guid→asset
    /// resolution, MonoScript class binding, and the full component inventory
    /// of the involved prefabs. Read-only. Runs via -executeMethod.
    /// </summary>
    public static class WristDiagnostic
    {
        private const string HandsPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";
        private const string LeftPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_L.prefab";
        private const string RightPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_R.prefab";

        private const string GuidHandRigDriver = "bbb18b175f55a4f4986a980237ceb7a8";
        private const string GuidControllerHandPair = "19611fca4cef26b46965b3d4983694f9";
        private const string GuidLeftPrefab = "714ae2fc76729e34b82899778196734d";
        private const string GuidRightPrefab = "eb7b1c39537c77148bc2751297aee97d";
        private const string GuidHandsPrefab = "aa867d323980c1349a248d1d48339d3f";

        public static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("UnityVersion=" + Application.unityVersion);
            sb.AppendLine("DataPath=" + Application.dataPath);
            DumpGuids(sb);
            DumpHandsPrefab(sb);
            DumpHandPrefab(LeftPrefabPath, sb);
            DumpHandPrefab(RightPrefabPath, sb);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(projectRoot, "Logs", "WristDiagnostic.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("WRIST_DIAGNOSTIC_WRITTEN " + outPath);
        }

        /// <summary>
        /// v3: force-reimports the ElenaFF GLB models and the three prefabs,
        /// then dumps the fresh import results. Run via -executeMethod.
        /// </summary>
        public static void ReimportAndDump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("UnityVersion=" + Application.unityVersion);
            sb.AppendLine("DataPath=" + Application.dataPath);
            sb.AppendLine("== REIMPORT PASS (v3) ==");

            const string glbPath = "Assets/VirtualHand/Models/ElenaFF/ElenaHand_R.glb";
            sb.AppendLine("== GLB DIRECT DIAG (v4) ==");
            string resolved = AssetDatabase.GUIDToAssetPath("d408a0916afd87c4ab831e603864d54e");
            sb.AppendLine("glb guid->path: " + (string.IsNullOrEmpty(resolved) ? "<UNRESOLVED>" : resolved));
            sb.AppendLine("glb mainType=" + AssetDatabase.GetMainAssetTypeAtPath(glbPath));
            AssetImporter glbImporter = AssetImporter.GetAtPath(glbPath);
            sb.AppendLine("glb importerType=" + (glbImporter == null ? "<NULL>" : glbImporter.GetType().FullName));
            sb.AppendLine("Reimporting model by direct path: " + glbPath);
            AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (model == null)
                sb.AppendLine("  model load: <NULL>");
            else
            {
                sb.AppendLine("  model root=" + model.name + " childCount=" + model.transform.childCount);
                DumpHierarchy(model.transform, sb, 1);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (string prefabPath in new[] { LeftPrefabPath, RightPrefabPath, HandsPrefabPath })
            {
                sb.AppendLine("Reimporting prefab: " + prefabPath);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            DumpGuids(sb);
            DumpHandsPrefab(sb);
            DumpHandPrefab(LeftPrefabPath, sb);
            DumpHandPrefab(RightPrefabPath, sb);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(projectRoot, "Logs", "WristDiagnostic.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("WRIST_DIAGNOSTIC_WRITTEN " + outPath);
        }

        private static void DumpGuids(StringBuilder sb)
        {
            sb.AppendLine("== GUID RESOLUTION ==");
            DumpScriptGuid(sb, "HandRigDriver", GuidHandRigDriver);
            DumpScriptGuid(sb, "ControllerHandPair", GuidControllerHandPair);
            DumpPathGuid(sb, "ElenaHand_L.prefab", GuidLeftPrefab);
            DumpPathGuid(sb, "ElenaHand_R.prefab", GuidRightPrefab);
            DumpPathGuid(sb, "FirstPersonHands.prefab", GuidHandsPrefab);
        }

        private static void DumpScriptGuid(StringBuilder sb, string label, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            sb.AppendLine(label + " guid->path: " + (string.IsNullOrEmpty(path) ? "<UNRESOLVED>" : path));
            if (!string.IsNullOrEmpty(path))
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                    sb.AppendLine(label + " monoScript: <NULL>");
                else
                    sb.AppendLine(label + " monoScript class=" + script.GetClass() + " file=" + script.name);
            }
        }

        private static void DumpPathGuid(StringBuilder sb, string label, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            sb.AppendLine(label + " guid->path: " + (string.IsNullOrEmpty(path) ? "<UNRESOLVED>" : path));
        }

        private static void DumpHandsPrefab(StringBuilder sb)
        {
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(HandsPrefabPath);
                sb.AppendLine("== FirstPersonHands LoadPrefabContents ==");
                sb.AppendLine("missingScripts=" + GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(contents));
                var pair = contents.GetComponentInChildren<ControllerHandPair>(true);
                if (pair == null)
                    sb.AppendLine("ControllerHandPair: NONE");
                else
                    sb.AppendLine("ControllerHandPair on: " + GetPath(pair.transform)
                        + " leftHandNull=" + (pair.leftHand == null)
                        + " rightHandNull=" + (pair.rightHand == null));
                foreach (var d in contents.GetComponentsInChildren<HandRigDriver>(true))
                    sb.AppendLine("HandRigDriver path=" + GetPath(d.transform) + " IsMirrored=" + d.IsMirrored);
                sb.AppendLine("-- instance hierarchy (Transforms + component types) --");
                DumpHierarchy(contents.transform, sb, 0);
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void DumpHandPrefab(string path, StringBuilder sb)
        {
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                sb.AppendLine("== " + path + " LoadPrefabContents ==");
                sb.AppendLine("missingScripts=" + GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(contents));
                foreach (var d in contents.GetComponentsInChildren<HandRigDriver>(true))
                    sb.AppendLine("HandRigDriver path=" + GetPath(d.transform) + " IsMirrored=" + d.IsMirrored);
                DumpHierarchy(contents.transform, sb, 0);
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void DumpHierarchy(Transform t, StringBuilder sb, int depth)
        {
            var line = new StringBuilder();
            line.Append(' ', depth * 2);
            line.Append(t.name);
            var comps = t.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null)
                    line.Append(" |<null component>");
                else
                    line.Append(" |" + c.GetType().Name);
            }
            sb.AppendLine(line.ToString());
            foreach (Transform child in t)
                DumpHierarchy(child, sb, depth + 1);
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder();
            sb.Append(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return sb.ToString();
        }
    }
}
