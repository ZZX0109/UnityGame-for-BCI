using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Editor
{
    /// <summary>
    /// Batch probe of the twelve imported street FBX files. Instantiates each model
    /// exactly as the importer produced it (no rotation or scale reset) and writes
    /// importer settings, the full transform chain and renderer world bounds.
    /// </summary>
    public static class StreetImportedAssetProbe
    {
        private static readonly string[] Assets =
        {
            "street_lamp_01", "fire_hydrant", "metal_trash_can", "water_manhole_cover",
            "utility_box_01", "utility_box_02", "rollershutter_door", "rollershutter_window_01",
            "security_light", "old_tyre", "modular_fire_escape", "exterior_aircon_unit"
        };

        public static void Dump()
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\n  \"assets\": [\n");
            for (int i = 0; i < Assets.Length; i++)
            {
                if (i > 0) sb.Append(",\n");
                DumpOne(sb, Assets[i]);
            }
            sb.Append("\n  ]\n}\n");
            string folder = Path.GetFullPath(Application.dataPath + "/../BuildOutput");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "imported-asset-probe.json");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[ImportedProbe] wrote " + path);
        }

        private static void DumpOne(StringBuilder sb, string assetName)
        {
            string assetPath = "Assets/StreetCrossing/Resources/StreetCrossing/Imported/Models/" + assetName + ".fbx";
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            sb.Append("    {\n");
            sb.Append("      \"id\": \"").Append(assetName).Append("\",\n");
            sb.Append("      \"path\": \"").Append(assetPath).Append("\",\n");
            sb.Append("      \"loaded\": ").Append(prefab != null ? "true" : "false").Append(",\n");
            if (importer != null)
            {
                sb.Append("      \"globalScale\": ").Append(F(importer.globalScale)).Append(",\n");
                sb.Append("      \"useFileScale\": ").Append(importer.useFileScale ? "true" : "false").Append(",\n");
                sb.Append("      \"bakeAxisConversion\": ").Append(importer.bakeAxisConversion ? "true" : "false").Append(",\n");
                sb.Append("      \"fileScale\": ").Append(F(importer.fileScale)).Append(",\n");
            }
            if (prefab == null)
            {
                sb.Append("      \"nodes\": [],\n      \"renderers\": []\n    }");
                return;
            }
            GameObject instance = Object.Instantiate(prefab);
            instance.name = assetName;
            try
            {
                sb.Append("      \"nodes\": [\n");
                bool first = true;
                foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    Vector3 euler = t.localEulerAngles;
                    sb.Append("        {\"path\": \"").Append(PathOf(instance.transform, t)).Append("\"");
                    sb.Append(", \"lp\": [").Append(V(t.localPosition)).Append("]");
                    sb.Append(", \"le\": [").Append(V(euler)).Append("]");
                    sb.Append(", \"ls\": [").Append(V(t.localScale)).Append("]");
                    sb.Append(", \"wp\": [").Append(V(t.position)).Append("]");
                    sb.Append(", \"we\": [").Append(V(t.eulerAngles)).Append("]");
                    sb.Append(", \"ws\": [").Append(V(t.lossyScale)).Append("]}");
                }
                sb.Append("\n      ],\n      \"renderers\": [\n");
                first = true;
                Bounds combined = new Bounds();
                bool any = false;
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    if (!any) { combined = r.bounds; any = true; }
                    else combined.Encapsulate(r.bounds);
                    sb.Append("        {\"path\": \"").Append(PathOf(instance.transform, r.transform)).Append("\"");
                    sb.Append(", \"center\": [").Append(V(r.bounds.center)).Append("]");
                    sb.Append(", \"size\": [").Append(V(r.bounds.size)).Append("]");
                    sb.Append(", \"min\": [").Append(V(r.bounds.min)).Append("]");
                    sb.Append(", \"max\": [").Append(V(r.bounds.max)).Append("]}");
                }
                sb.Append("\n      ],\n");
                sb.Append("      \"combined\": {\"center\": [").Append(V(combined.center)).Append("]");
                sb.Append(", \"size\": [").Append(V(combined.size)).Append("]");
                sb.Append(", \"min\": [").Append(V(combined.min)).Append("]");
                sb.Append(", \"max\": [").Append(V(combined.max)).Append("]}\n");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            sb.Append("    }");
        }

        private static string PathOf(Transform root, Transform t)
        {
            if (t == root) return t.name;
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null && p != root)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => F(v.x) + ", " + F(v.y) + ", " + F(v.z);
    }
}
