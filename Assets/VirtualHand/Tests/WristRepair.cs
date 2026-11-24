using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// Lead-only repair for the Game01 wrist NRE. Root cause: project
    /// import-context divergence in nested stripped-override-stub resolution —
    /// the outer FirstPersonHands.prefab referenced HandRigDriver instances
    /// that live as added-component overrides inside ElenaHand_L/R.prefab
    /// (themselves overriding the GLB nested instance). That two-level stub
    /// chain fails to resolve at instantiate time in this project.
    ///
    /// v2 (instrumented, adaptive): run 1 of this tool aborted silently —
    /// wrist.visualRoot deserialized null in THIS project's import context
    /// (same stub-resolution failure family as the drivers). So the host is
    /// now located by name ("ElenaHand*" under the wrist) instead of trusting
    /// wrist.visualRoot, visualRoot is (re)assigned when null, and every exit
    /// path writes Logs/WristRepair.txt.
    ///
    /// Runs via -executeMethod: Neuralife.VirtualHand.Tests.WristRepair.Repair
    /// </summary>
    public static class WristRepair
    {
        private const string HandsPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";
        private const string NativeProfilePath = "Assets/VirtualHand/Resources/Rig/ElenaFF/ElenaNativeRig_R.asset";

        private static void WriteReport(StringBuilder sb)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outPath = Path.Combine(projectRoot, "Logs", "WristRepair.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("WRIST_REPAIR_REPORT " + outPath);
        }

        public static void Repair()
        {
            Debug.Log("WRIST_REPAIR_BEGIN");
            var sb = new StringBuilder();
            sb.AppendLine("UnityVersion=" + Application.unityVersion);
            sb.AppendLine("DataPath=" + Application.dataPath);
            // Write an immediate BEGIN marker so a mid-run crash/hang is distinguishable
            // from "executeMethod never ran" (both previously produced zero artifacts).
            WriteReport(sb);
            try
            {
                RepairInner(sb);
            }
            catch (System.Exception ex)
            {
                sb.AppendLine("EXCEPTION: " + ex);
                WriteReport(sb);
                Debug.LogException(ex);
                throw;
            }
            finally
            {
                WriteReport(sb);
            }
        }

        private static string PathOf(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void RepairInner(StringBuilder sb)
        {
            var profile = AssetDatabase.LoadAssetAtPath<ElenaNativeRigProfile>(NativeProfilePath);
            sb.AppendLine("profile=" + (profile != null ? NativeProfilePath : "NULL!"));

            var root = PrefabUtility.LoadPrefabContents(HandsPrefabPath);

            sb.AppendLine("--- hierarchy dump (loaded contents) ---");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool partOfInstance = PrefabUtility.IsPartOfPrefabInstance(t.gameObject);
                sb.AppendLine("GO " + PathOf(t) + " childCount=" + t.childCount +
                              " partOfInstance=" + partOfInstance);
            }
            sb.AppendLine("--- end hierarchy dump ---");

            try
            {
                var wrists = root.GetComponentsInChildren<FirstPersonWrist>(true);
                sb.AppendLine("wristCount=" + wrists.Length);

                HandRigDriver leftDriver = null, rightDriver = null;
                foreach (var wrist in wrists)
                {
                    bool isLeft = wrist.gameObject.name == "Left wrist";
                    bool isRight = wrist.gameObject.name == "Right wrist";
                    if (!isLeft && !isRight) continue;

                    var visualRoot = wrist.visualRoot;
                    sb.AppendLine("wrist=" + wrist.gameObject.name +
                                  " serializedVisualRoot=" + (visualRoot != null ? visualRoot.name : "NULL"));

                    GameObject hostGo;
                    if (visualRoot != null)
                    {
                        hostGo = visualRoot.gameObject;
                    }
                    else
                    {
                        hostGo = FindElenaHost(wrist.transform, sb);
                        if (hostGo == null)
                        {
                            sb.AppendLine("ABORT(side): no visualRoot and no ElenaHand* child under " +
                                          wrist.gameObject.name);
                            continue;
                        }
                        wrist.visualRoot = hostGo.transform;
                        sb.AppendLine("visualRoot assigned -> " + PathOf(hostGo.transform));
                    }

                    var existing = hostGo.GetComponent<HandRigDriver>();
                    if (existing != null)
                    {
                        sb.AppendLine("host=" + hostGo.name + " already has HandRigDriver; reusing");
                        leftDriver = isLeft ? existing : leftDriver;
                        rightDriver = isRight ? existing : rightDriver;
                        continue;
                    }

                    var driver = hostGo.AddComponent<HandRigDriver>();
                    driver.nativeProfile = profile;
                    driver.nativeOppositionStrength = 1f;
                    driver.maxFlexionDeg = 90f;
                    driver.maxSpreadDeg = 10f;
                    driver.SetMirrored(isLeft);
                    if (isLeft) leftDriver = driver; else rightDriver = driver;

                    sb.AppendLine("added HandRigDriver on " + PathOf(hostGo.transform) +
                                  " IsMirrored=" + driver.IsMirrored +
                                  " profileSet=" + (driver.nativeProfile != null) +
                                  " boneCountUnderHost=" + hostGo.GetComponentsInChildren<Transform>(true).Length);
                }

                var pair = root.GetComponentInChildren<ControllerHandPair>(true);
                if (pair == null)
                {
                    sb.AppendLine("ABORT: no ControllerHandPair");
                    return;
                }
                if (leftDriver == null || rightDriver == null)
                {
                    sb.AppendLine("ABORT: missing drivers left=" + (leftDriver != null) +
                                  " right=" + (rightDriver != null));
                    return;
                }

                pair.leftHand = leftDriver;
                pair.rightHand = rightDriver;
                sb.AppendLine("pair wired: leftHand=" + pair.leftHand.name +
                              " IsMirrored=" + pair.leftHand.IsMirrored +
                              " rightHand=" + pair.rightHand.name +
                              " IsMirrored=" + pair.rightHand.IsMirrored);

                PrefabUtility.SaveAsPrefabAsset(root, HandsPrefabPath);
                sb.AppendLine("SAVED " + HandsPrefabPath);
                AssetDatabase.SaveAssets();
                sb.AppendLine("REPAIR_COMPLETED");
                Debug.Log("WRIST_REPAIR_COMPLETED");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject FindElenaHost(Transform wrist, StringBuilder sb)
        {
            // Direct child first (Game04-proven layout: Left wrist/ElenaHand_L).
            foreach (Transform child in wrist)
            {
                if (child.name.StartsWith("ElenaHand"))
                {
                    sb.AppendLine("host candidate (direct child): " + PathOf(child));
                    return child.gameObject;
                }
            }
            // Deep fallback.
            foreach (Transform t in wrist.GetComponentsInChildren<Transform>(true))
            {
                if (t != wrist && t.name.StartsWith("ElenaHand"))
                {
                    sb.AppendLine("host candidate (deep): " + PathOf(t));
                    return t.gameObject;
                }
            }
            return null;
        }
    }
}
