using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Read-only diagnostic: loads the committed FirstPersonFullBody.prefab, temporarily
    /// instantiates it, and reports the Elena-hand wrist landmark alignment delta exactly
    /// as FirstPersonFullBodyBuilder.AttachHand does at build time. Never modifies the
    /// prefab asset; the temporary instance is destroyed before exit.
    /// </summary>
    public static class WristDeltaDump
    {
        const string LogPrefix = "[WristDeltaDump] ";
        const string PrefabPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";
        const string EvidencePath = "Logs/WristDeltaDump.txt";

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"WristDeltaDump {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC; Unity {Application.unityVersion}");
            sb.AppendLine($"Prefab: {PrefabPath}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"{LogPrefix}Prefab missing at '{PrefabPath}'.");
                WriteEvidence(sb.AppendLine("RESULT: MISSING_PREFAB"));
                EditorApplication.Exit(2);
                return;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (root == null)
            {
                Debug.LogError($"{LogPrefix}Prefab instantiation failed.");
                WriteEvidence(sb.AppendLine("RESULT: INSTANTIATE_FAILED"));
                EditorApplication.Exit(2);
                return;
            }

            try
            {
                int missing = 0;
                foreach (var left in new[] { true, false })
                {
                    string wristBoneName = left ? "hand_l" : "hand_r";
                    string sideLabel = left ? "LEFT" : "RIGHT";
                    var wristBone = FindWristBone(root.transform, wristBoneName);
                    if (wristBone == null)
                    {
                        sb.AppendLine($"{sideLabel}: body skeleton wrist bone '{wristBoneName}' NOT FOUND");
                        missing++;
                        continue;
                    }

                    // Enumerate EVERY Elena wrist landmark under the root, with its
                    // owning prefab-instance source, mirroring the builder fallback
                    // (exact side match, then any "hand"/"wrist" segment).
                    var exact = left ? "hand.L_02" : "hand.R_02";
                    var matches = new System.Collections.Generic.List<Transform>();
                    foreach (var t in root.transform.GetComponentsInChildren<Transform>(true))
                    {
                        if (ElenaNativeRigProfile.MatchesBone(t.name, exact) ||
                            (ElenaNativeRigProfile.MatchesBone(t.name, "hand") || t.name.ToLowerInvariant().Contains("wrist")))
                        {
                            // skip body-side FBX bones (hand_l/hand_r) from the fallback
                            string n = t.name.ToLowerInvariant();
                            if (n == "hand_l" || n == "hand_r" || n.StartsWith("hand_l.") || n.StartsWith("hand_r.")) continue;
                            matches.Add(t);
                        }
                    }

                    if (matches.Count == 0)
                    {
                        sb.AppendLine($"{sideLabel}: no Elena wrist landmark found under root");
                        missing++;
                        continue;
                    }

                    foreach (var m in matches)
                    {
                        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(m);
                        string source = "not-a-prefab-instance";
                        if (prefabRoot != null)
                        {
                            var sourceObj = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
                            if (sourceObj != null) source = AssetDatabase.GetAssetPath(sourceObj);
                        }
                        var d = m.position - wristBone.position;
                        sb.AppendLine($"{sideLabel} CANDIDATE: '{m.name}' at '{GetPath(m)}' | instance source '{source}' | body wrist '{wristBone.name}' | wrist delta {d.magnitude:F4} m {d}");
                    }
                }
                sb.AppendLine(missing == 0 ? "RESULT: OK" : $"RESULT: OK_WITH_MISSING ({missing})");
                Debug.Log($"{LogPrefix}{sb.ToString()}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            WriteEvidence(sb);
            EditorApplication.Exit(0);
        }

        static Transform FindWristBone(Transform root, string wristBoneName)
        {
            string target = wristBoneName.ToLowerInvariant();
            Transform best = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string lowered = t.name.ToLowerInvariant();
                if (lowered == target || lowered == target.Replace('_', '.') || lowered.StartsWith(target)) { best = t; break; }
            }
            return best;
        }

        static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }

        static void WriteEvidence(StringBuilder sb)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath));
                File.WriteAllText(EvidencePath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix}Evidence write failed: {e.Message}");
            }
        }
    }
}
