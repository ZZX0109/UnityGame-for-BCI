using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Records the performance figures from spec 10.7 that can actually be measured on
    /// this machine.
    ///
    /// What this deliberately does NOT claim: it is not a device frame-time measurement.
    /// It runs in the editor, so GPU frame cost, headset refresh margin and stereo
    /// overhead are out of scope. The report it writes states that explicitly rather than
    /// letting the numbers be mistaken for device results. Anything that needs the PICO
    /// headset stays HARDWARE_NOT_TESTED.
    ///
    /// Run: -executeMethod Neuralife.VirtualHand.Editor.HandPerformanceProbe.Run
    /// </summary>
    public static class HandPerformanceProbe
    {
        const string HandPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_R.prefab";
        const string OutputPath = "Results/hand-performance.json";
        const int PoseIterations = 2000;
        const int ToggleIterations = 100;

        public static void Run()
        {
            var report = new StringBuilder();
            report.Append("{\n");

            Append(report, "unityVersion", Application.unityVersion, true);
            Append(report, "platform", Application.platform.ToString(), true);
            Append(report, "deviceModel", SystemInfo.deviceModel, true);
            Append(report, "processorType", SystemInfo.processorType, true);
            Append(report, "processorCount", SystemInfo.processorCount.ToString(), true);
            Append(report, "systemMemoryMB", SystemInfo.systemMemorySize.ToString(), true);
            Append(report, "graphicsDeviceName", SystemInfo.graphicsDeviceName, true);
            Append(report, "graphicsDeviceType", SystemInfo.graphicsDeviceType.ToString(), true);
            Append(report, "measuredAtUtc", DateTime.UtcNow.ToString("o"), true);
            Append(report, "measurementContext",
                "Unity editor, not a built player. Not a device frame-time measurement.", true);
            Append(report, "burstAotState",
                "StandaloneWindows Burst AOT is disabled in this project; editor timings are " +
                "not representative of an AOT build.", true);
            Append(report, "deviceFrameBudgetMs", "not measured (no headset)", true);

            // ---- static geometry ----
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabPath);
            if (!prefab)
                throw new InvalidOperationException("Missing hand prefab: " + HandPrefabPath);

            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (!renderer)
                    throw new InvalidOperationException("Hand prefab has no SkinnedMeshRenderer.");

                var mesh = renderer.sharedMesh;
                Append(report, "skinnedMeshRendererCount",
                    instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length.ToString(), true);
                Append(report, "meshVertexCount", mesh ? mesh.vertexCount.ToString() : "n/a", true);
                Append(report, "meshTriangleCount", mesh ? (mesh.triangles.Length / 3).ToString() : "n/a", true);
                Append(report, "meshSubMeshCount", mesh ? mesh.subMeshCount.ToString() : "n/a", true);
                Append(report, "meshBlendShapeCount", mesh ? mesh.blendShapeCount.ToString() : "n/a", true);
                Append(report, "boneCount", renderer.bones != null ? renderer.bones.Length.ToString() : "0", true);
                Append(report, "materialSlotCount",
                    renderer.sharedMaterials != null ? renderer.sharedMaterials.Length.ToString() : "0", true);
                Append(report, "totalTransformCount",
                    instance.GetComponentsInChildren<Transform>(true).Length.ToString(), true);
                Append(report, "totalRendererCount",
                    instance.GetComponentsInChildren<Renderer>(true).Length.ToString(), true);

                // ---- CPU cost of the pose path ----
                var driver = instance.GetComponentInChildren<HandRigDriver>(true);
                if (!driver)
                    throw new InvalidOperationException("Hand prefab has no HandRigDriver.");

                driver.CacheBones();

                float[] open = PoseLibrary.OpenPose(driver);
                float[] fist = PoseLibrary.FistPose(driver);
                if (open == null || fist == null)
                    throw new InvalidOperationException("PoseLibrary did not produce poses.");

                // Warm up so JIT and the first-touch costs are excluded.
                for (int i = 0; i < 200; i++)
                    driver.SetControls(i % 2 == 0 ? open : fist);

                driver.detectSelfIntersection = true;
                double withDetection = TimeSetControls(driver, open, fist);
                Append(report, "setControlsMsWithSelfIntersection", withDetection.ToString("F5"), true);

                driver.detectSelfIntersection = false;
                double withoutDetection = TimeSetControls(driver, open, fist);
                Append(report, "setControlsMsWithoutSelfIntersection", withoutDetection.ToString("F5"), true);
                driver.detectSelfIntersection = true;

                Append(report, "selfIntersectionOverheadMs",
                    Math.Max(0.0, withDetection - withoutDetection).ToString("F5"), true);
                Append(report, "setControlsIterations", PoseIterations.ToString(), true);

                // ---- appearance toggle cost ----
                var style = instance.AddComponent<AvatarVisualStyle>();
                style.targets = new[] { (Renderer)renderer };
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                    "Assets/VirtualHand/Shaders/AvatarBlueOutline.shader");
                if (shader)
                {
                    var so = new SerializedObject(style);
                    var prop = so.FindProperty("blueShader");
                    if (prop != null)
                    {
                        prop.objectReferenceValue = shader;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                style.SetBlueMode(true);   // allocate once
                Material firstBlue = ((Renderer)renderer).sharedMaterials[0];

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < ToggleIterations; i++)
                {
                    style.SetBlueMode(false);
                    style.SetBlueMode(true);
                }
                sw.Stop();

                Material lastBlue = ((Renderer)renderer).sharedMaterials[0];

                Append(report, "appearanceToggleIterations", ToggleIterations.ToString(), true);
                Append(report, "appearanceToggleAverageMs",
                    (sw.Elapsed.TotalMilliseconds / ToggleIterations).ToString("F5"), true);
                Append(report, "appearanceToggleTotalMs", sw.Elapsed.TotalMilliseconds.ToString("F3"), true);
                Append(report, "appearanceMaterialReusedAcrossToggles",
                    ReferenceEquals(firstBlue, lastBlue).ToString().ToLowerInvariant(), true);
                Append(report, "appearanceMaterialGrowth",
                    ReferenceEquals(firstBlue, lastBlue)
                        ? "none: the same generated material instance is reused across toggles"
                        : "WARNING: toggling produced a different material instance", true);

                // ---- what could not be measured ----
                Append(report, "xrHandUpdateFrequency", "not measured (no XR subsystem available)", true);
                Append(report, "gpuFrameTimeMs", "not measured (editor, no device)", true);
                Append(report, "gcAllocPerFrame", "not measured (needs a device/profiler run)", true);
                Append(report, "finalDeviceFrameTimeMs", "HARDWARE_NOT_TESTED", false);
                report.Append("}\n");

                Directory.CreateDirectory("Results");
                File.WriteAllText(OutputPath, report.ToString());

                Debug.Log("[HandPerformanceProbe] wrote " + OutputPath + "\n" + report);
            }
            finally
            {
                if (instance) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static double TimeSetControls(HandRigDriver driver, float[] a, float[] b)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < PoseIterations; i++)
                driver.SetControls(i % 2 == 0 ? a : b);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / PoseIterations;
        }

        static void Append(StringBuilder sb, string key, string value, bool comma)
        {
            sb.Append("  \"").Append(key).Append("\": \"").Append(Escape(value)).Append('"');
            if (comma) sb.Append(',');
            sb.Append('\n');
        }

        static string Escape(string s) =>
            s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
