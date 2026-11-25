using System;
using System.Globalization;
using System.IO;
using System.Text;
using Neuralife.VirtualHand.Body;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// M2 stage-1 probe: measures the combined skin colour (tint × texture average) of every
    /// distinct material used by the FirstPersonFullBody prefab and of the Elena hand skin
    /// material, then computes the per-channel tint factor (hand / body) that would align
    /// each body material with the hand skin.  Read-only: instances are created in a throwaway
    /// empty scene and destroyed in the finally block; no asset is modified.
    /// </summary>
    internal static class SkinTintProbe
    {
        const string OutputPath = "Results/skin-tint-report.json";
        const string BodyPrefabPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";
        const string HandPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_R.prefab";

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var report = new StringBuilder();
            report.Append("{\n");
            Append(report, "unityVersion", Application.unityVersion, true);
            Append(report, "measuredAtUtc", DateTime.UtcNow.ToString("o"), true);
            Append(report, "bodyPrefabPath", BodyPrefabPath, true);
            Append(report, "handPrefabPath", HandPrefabPath, true);

            GameObject body = null;
            GameObject hand = null;
            try
            {
                GameObject bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);
                GameObject handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabPath);
                if (bodyPrefab == null || handPrefab == null)
                {
                    Append(report, "error", bodyPrefab == null ? "body prefab not found" : "hand prefab not found", true);
                    report.Append("  \"status\": \"missing-prefab\"\n}\n");
                    WriteReport(report);
                    return;
                }

                body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab);
                hand = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab);

                // --- hand source of truth -------------------------------------------
                Material handMat = FindHandSkinMaterial(hand);
                report.Append("  \"hand\": {\n");
                Append(report, "resolved", handMat != null, true);
                if (handMat != null)
                {
                    AppendColor(report, "tint", SkinColorSampling.GetTint(handMat), true);
                    Texture handTex = SkinColorSampling.GetBaseTexture(handMat);
                    Append(report, "texture", handTex != null ? handTex.name : "null", true);
                    if (SkinColorSampling.TryGetTextureAverage(handTex, out Color handAvg))
                        AppendColor(report, "textureAverage", handAvg, true);
                    else
                        Append(report, "textureAverage", "sampling-failed", true);
                    if (SkinColorSampling.TryGetCombinedColor(handMat, out Color handCombined))
                        AppendColor(report, "combined", handCombined, true);
                    else
                        Append(report, "combined", "sampling-failed", true);
                    AppendColor(report, "factorForWhiteBody", Factor(handCombined, Color.white), false);
                }
                report.Append("  },\n");

                // --- body renderers --------------------------------------------------
                report.Append("  \"bodyRenderers\": [\n");
                Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
                bool firstRenderer = true;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;
                    Material[] slots = renderer.sharedMaterials;
                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        Material mat = slots[slot];
                        if (mat == null)
                            continue;
                        if (!firstRenderer)
                            report.Append(",\n");
                        firstRenderer = false;
                        report.Append("    {\n");
                        Append(report, "renderer", renderer.name, true);
                        Append(report, "slot", slot, true);
                        Append(report, "material", mat.name, true);
                        Append(report, "guid", TryGetIdentity(mat, out string guid, out long localId) ? guid : "?", true);
                        Append(report, "localId", TryGetIdentity(mat, out _, out localId)
                            ? localId.ToString(CultureInfo.InvariantCulture) : "?", true);
                        Append(report, "shader", mat.shader != null ? mat.shader.name : "null", true);
                        AppendColor(report, "tint", SkinColorSampling.GetTint(mat), true);
                        Texture tex = SkinColorSampling.GetBaseTexture(mat);
                        Append(report, "texture", tex != null ? tex.name : "null", true);
                        if (SkinColorSampling.TryGetTextureAverage(tex, out Color avg))
                            AppendColor(report, "textureAverage", avg, true);
                        else
                            Append(report, "textureAverage", "sampling-failed", true);
                        if (SkinColorSampling.TryGetCombinedColor(mat, out Color combined))
                            AppendColor(report, "combined", combined, false);
                        else
                            Append(report, "combined", "sampling-failed", false);
                        report.Append("    }");
                    }
                }
                report.Append("\n  ],\n");

                // --- per-distinct-material tint factors ------------------------------
                report.Append("  \"tintFactors\": {\n");
                var distinct = new System.Collections.Generic.HashSet<Material>();
                bool firstFactor = true;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;
                    foreach (Material mat in renderer.sharedMaterials)
                    {
                        if (mat == null || !distinct.Add(mat))
                            continue;
                        if (!firstFactor)
                            report.Append(",\n");
                        firstFactor = false;
                        Color combined = SkinColorSampling.TryGetCombinedColor(mat, out Color c)
                            ? c : Color.white;
                        Color factor = handMat != null &&
                                       SkinColorSampling.TryGetCombinedColor(handMat, out Color h)
                            ? Factor(h, combined)
                            : new Color(1f, 1f, 1f, 1f);
                        report.Append("    \"").Append(Escape(mat.name)).Append("\": {\"factor\": ");
                        AppendColorInline(report, factor);
                        report.Append(", \"combined\": ");
                        AppendColorInline(report, combined);
                        report.Append("}");
                    }
                }
                report.Append("\n  },\n");
                Append(report, "status", "ok", false);
                report.Append("}\n");
                WriteReport(report);
            }
            catch (Exception ex)
            {
                try
                {
                    Append(report, "error", ex.ToString(), false);
                    report.Append("  \"status\": \"exception\"\n}\n");
                    WriteReport(report);
                }
                catch
                {
                    Debug.LogException(ex);
                }
            }
            finally
            {
                if (body != null) UnityEngine.Object.DestroyImmediate(body);
                if (hand != null) UnityEngine.Object.DestroyImmediate(hand);
            }
        }

        static void WriteReport(StringBuilder report)
        {
            Directory.CreateDirectory("Results");
            File.WriteAllText(OutputPath, report.ToString());
            Debug.Log("[SkinTintProbe] wrote " + OutputPath + "\n" + report);
        }

        static Material FindHandSkinMaterial(GameObject hand)
        {
            foreach (Renderer renderer in hand.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat == null)
                        continue;
                    if (mat.HasProperty("baseColorFactor") && mat.HasProperty("baseColorTexture") &&
                        mat.GetTexture("baseColorTexture") != null)
                        return mat;
                }
            }
            return null;
        }

        static bool TryGetIdentity(Material mat, out string guid, out long localId)
        {
            guid = null;
            localId = 0;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mat, out guid, out localId);
        }

        static Color Factor(Color hand, Color body)
        {
            return new Color(
                SkinColorSampling.ClampedRatio(hand.r, body.r),
                SkinColorSampling.ClampedRatio(hand.g, body.g),
                SkinColorSampling.ClampedRatio(hand.b, body.b),
                1f);
        }

        static void AppendColor(StringBuilder sb, string key, Color value, bool comma)
        {
            sb.Append("    \"").Append(key).Append("\": ");
            AppendColorInline(sb, value);
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        static void AppendColorInline(StringBuilder sb, Color value)
        {
            sb.Append("{\"r\": ").Append(Num(value.r)).Append(", \"g\": ").Append(Num(value.g))
                .Append(", \"b\": ").Append(Num(value.b)).Append(", \"a\": ").Append(Num(value.a)).Append('}');
        }

        static string Num(float v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static void Append(StringBuilder sb, string key, string value, bool comma)
        {
            sb.Append("  \"").Append(key).Append("\": \"").Append(Escape(value)).Append('"');
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        static void Append(StringBuilder sb, string key, int value, bool comma)
        {
            sb.Append("  \"").Append(key).Append("\": ").Append(value.ToString(CultureInfo.InvariantCulture));
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        static void Append(StringBuilder sb, string key, bool value, bool comma)
        {
            sb.Append("  \"").Append(key).Append("\": ").Append(value ? "true" : "false");
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        static string Escape(string s)
        {
            if (s == null)
                return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
