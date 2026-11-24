using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Development-only preview capture: loads the real hand FBX rigs with
    /// HandRigDriver and the shared rig profile, applies profile/DB8 poses via
    /// the production SetControls path, and captures the actual deformed
    /// skinned mesh to PNG. The actual renderer's conservative bounds frame the
    /// camera; separate CPU-baked bounds remain diagnostic data, not framing proof.
    /// Editor development rendering only — NOT a Play-mode or final art PASS.
    ///
    /// Entry point for -executeMethod (run by Codex):
    ///   Neuralife.VirtualHand.Editor.CaptureRigPreview.Run
    /// </summary>
    public static class CaptureRigPreview
    {
        static string OutputDir = "Results/unity-rig-preview/run18";
        const string ProfileResource = "Rig/hand_rig_profile";
        const string ModelsDir = "Assets/VirtualHand/Models";
        const int CaptureWidth = 1920;
        const int CaptureHeight = 1080;
        const float FrameMargin = 1.25f;   // 25% margin around the subject

        // real deformable control indices (array positions) of the index-finger
        // flex channels — representative DB8 frame = strongest single-channel
        // flex peak among them; the 20 values are passed through verbatim
        static readonly int[] IndexFlexChannels = { 4, 5, 6 };

        /// <summary>Public static entry for -executeMethod.</summary>
        public static void Run()
        {
            RunCapture(false);
        }

        // Six complementary views from a nondegenerate MCP/wrist plane;
        // axis_a/b deliberately do not claim which side is palm before review.
        public static void RunAnatomical()
        {
            RunCapture(true);
        }

        public static void RunAppearance()
        {
            RunCapture(true, true);
        }

        public static void GenerateAndCaptureAppearance()
        {
            GenerateAndCapture(true);
        }

        public static void GenerateAndCaptureAnatomical()
        {
            GenerateAndCapture(false);
        }

        /// <summary>
        /// Final acceptance evidence: regenerate the saved Demo scene, then
        /// capture the full same-camera open/fist/pinch + real DB8 set into a
        /// fresh directory so historical runs remain untouched.
        /// </summary>
        public static void GenerateAndCaptureFinal()
        {
            VirtualHandBuild.GenerateProject();
            if (UnityEngine.Object.FindObjectsOfType<Light>()
                    .Count(l => l.name.StartsWith("Studio.")) != 3)
                throw new InvalidOperationException("Generated Demo must contain three studio lights.");
            Debug.Log("[StudioLighting] Final Demo contains three shared studio lights.");
            RunCapture(false, false, "Results/unity-rig-preview/final-20261118");
        }

        static void GenerateAndCapture(bool appearanceOnly)
        {
            VirtualHandBuild.GenerateProject();
            if (UnityEngine.Object.FindObjectsOfType<Light>()
                    .Count(l => l.name.StartsWith("Studio.")) != 3)
                throw new InvalidOperationException("Generated Demo must contain three studio lights.");
            Debug.Log("[StudioLighting] Generated Demo contains three shared studio lights.");
            RunCapture(true, appearanceOnly);
        }

        // Run in a fresh Editor process after GenerateProject. A saved scene
        // must preserve side selection; preview-only SetMirrored calls cannot
        // demonstrate this and no Play/gameplay acceptance is implied.
        public static void VerifySavedHandedness()
        {
            const string scenePath = "Assets/VirtualHand/Scenes/Demo.unity";
            try
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var replay = UnityEngine.Object.FindObjectOfType<ReplayController>();
                if (replay == null || replay.leftHand == null || replay.rightHand == null)
                    throw new InvalidOperationException("Saved hand bindings are missing.");
                if (!replay.leftHand.IsMirrored || replay.rightHand.IsMirrored)
                    throw new InvalidOperationException("Saved L/R handedness was lost.");
                Directory.CreateDirectory("Results");
                File.WriteAllText("Results/saved-handedness-check.json",
                    "{\n  \"scene\": \"" + scenePath + "\",\n" +
                    "  \"leftMirrored\": true,\n  \"rightMirrored\": false,\n" +
                    "  \"generatedUtc\": \"" + DateTime.UtcNow.ToString("o") + "\",\n" +
                    "  \"scope\": \"Fresh Editor scene load only; no Play or art acceptance\"\n}\n",
                    new System.Text.UTF8Encoding(false));
                Debug.Log("[SavedHandedness] fresh scene load: left=true, right=false.");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        static void RunCapture(bool anatomicalOnly, bool appearanceOnly = false,
                               string outputDir = null)
        {
            OutputDir = outputDir ??
                        (appearanceOnly ? "Results/unity-rig-preview/run22" :
                         anatomicalOnly ? "Results/unity-rig-preview/run23" :
                                          "Results/unity-rig-preview/run18");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var created = new List<GameObject>();
            var summary = new List<CaptureEntry>();
            string boneInfo;
            try
            {
                // ---- profile (shared asset; the driver itself is not modified) ----
                var profileAsset = Resources.Load<TextAsset>(ProfileResource);
                if (profileAsset == null)
                    throw new InvalidOperationException(
                        "Missing " + ProfileResource + " — cannot capture rig preview.");

                // ---- real rigs from the imported FBX ----
                var leftDriver = InstantiateRig("L", profileAsset, created);
                var rightDriver = InstantiateRig("R", profileAsset, created);
                // Display spacing for the wrist-local assets; does not change the Demo scene.
                leftDriver.transform.position = new Vector3(-0.14f, 0f, 0f);
                rightDriver.transform.position = new Vector3(0.14f, 0f, 0f);
                if (!leftDriver.CacheBones() || !rightDriver.CacheBones())
                    throw new InvalidOperationException(
                        "CacheBones failed on a rig; capture would be meaningless.");
                if (!leftDriver.HasProfile || !rightDriver.HasProfile)
                    throw new InvalidOperationException(
                        "Profile not loaded on a rig; capture would be meaningless.");
                boneInfo = "L=True;R=True;profile=loaded";

                // ---- render camera + light rig ----
                var camGo = new GameObject("PreviewCamera");
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.backgroundColor = new Color(0.16f, 0.16f, 0.18f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.orthographic = true;
                SetupLights(created);

                Directory.CreateDirectory(OutputDir);

                // ---- samples (profile-aware defaults, one per pose) ----
                var open = PoseLibrary.OpenPose(leftDriver);
                var fist = PoseLibrary.FistPose(leftDriver);
                var pinch = PoseLibrary.PinchPose(rightDriver);

                if (anatomicalOnly)
                {
                    if (appearanceOnly)
                    {
                        foreach (var side in new[] { "L", "R" })
                        {
                            var driver = side == "L" ? leftDriver : rightDriver;
                            foreach (var axisView in new[] { "axis_a", "axis_b" })
                                summary.Add(Capture(side.ToLowerInvariant() + "_open_" + axisView,
                                    driver, null, open, null, "profile", "", cam, axisView));
                        }
                    }
                    else
                    {
                    foreach (var pose in new[] { "open", "fist", "pinch" })
                    {
                        var controls = pose == "open" ? open : pose == "fist" ? fist : pinch;
                        foreach (var axisView in new[] { "axis_a", "axis_b" })
                            summary.Add(Capture("right_" + pose + "_" + axisView,
                                rightDriver, null, controls, null, "profile", "", cam, axisView));
                    }
                    }
                }
                else
                {

                // per-hand closeups: each hand fills a readable frame (~400px
                // height at 1080p after margin), independent of FBX origin.
                // Single-hand shots pass rightDriver = null.
                summary.Add(Capture("left_open_front", leftDriver, null,
                                    open, null, "profile", "", cam, "front"));
                summary.Add(Capture("right_open_front", rightDriver, null,
                                    open, null, "profile", "", cam, "front"));

                // Bounded correspondence check for the newly authored bone axes:
                // one MCP only, exactly +30 degrees through the shared profile.
                if (!HandRigProfile.TryParseText(profileAsset.text, out var profile))
                    throw new InvalidOperationException("Cannot read index calibration.");
                var indexChannel = profile.Channels[4];
                if (indexChannel.bone != "Index1" || indexChannel.axis != "X" ||
                    Mathf.Abs(indexChannel.scale * indexChannel.sign) < 0.001f)
                    throw new InvalidOperationException("Unexpected index MCP calibration.");
                var index30 = (float[])open.Clone();
                index30[4] = indexChannel.rest + 30f / (indexChannel.scale * indexChannel.sign);
                if (index30[4] < 0f || index30[4] > 1f)
                    throw new InvalidOperationException("Index +30 exceeds normalized controls.");
                summary.Add(Capture("left_rest_side", leftDriver, null,
                                    open, null, "manual", "", cam, "side"));
                summary.Add(Capture("left_index30_side", leftDriver, null,
                                    index30, null, "manual", "", cam, "side"));
                summary.Add(Capture("right_rest_side", rightDriver, null,
                                    open, null, "manual", "", cam, "side"));
                summary.Add(Capture("right_index30_side", rightDriver, null,
                                    index30, null, "manual", "", cam, "side"));

                // two-hand shots: fit the COMBINED deformed bounds so no hand is
                // pushed off-frame by a distant FBX origin
                summary.Add(Capture("both_open_front", leftDriver, rightDriver,
                                    open, open, "profile", "", cam, "front"));
                summary.Add(Capture("both_open_back", leftDriver, rightDriver,
                                    open, open, "profile", "", cam, "back"));
                summary.Add(Capture("both_fist", leftDriver, rightDriver,
                                    fist, fist, "profile", "", cam, "front"));
                summary.Add(Capture("right_fist_close", rightDriver, null,
                                    fist, null, "profile", "", cam, "front"));
                summary.Add(Capture("right_pinch", leftDriver, rightDriver,
                                    open, pinch, "profile", "", cam, "front"));
                summary.Add(Capture("right_pinch_close", rightDriver, null,
                                    pinch, null, "profile", "", cam, "front"));
                summary.Add(Capture("left_pinch_close", leftDriver, null,
                                    pinch, null, "profile", "", cam, "front"));

                // ---- one real DB8 index frame (index-flex peak) ----
                summary.Add(BuildDb8Capture(leftDriver, rightDriver, cam));
                }

                // ---- summary JSON ----
                var wrapper = new SummaryRoot
                {
                    generatedUtc = DateTime.UtcNow.ToString("o"),
                    width = CaptureWidth,
                    height = CaptureHeight,
                    profile = ProfileResource,
                    boneAvailability = boneInfo,
                    gpu = SystemInfo.graphicsDeviceName + " / " +
                          SystemInfo.graphicsDeviceVendor + " / " +
                          SystemInfo.graphicsDeviceType,
                    captures = summary,
                };
                string jsonPath = Path.Combine(OutputDir, "capture-summary.json");
                File.WriteAllText(jsonPath, JsonUtility.ToJson(wrapper, true));
                Debug.Log("[CaptureRigPreview] wrote " + jsonPath + " (" +
                          summary.Count + " captures)");
            }
            finally
            {
                foreach (var go in created)
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                    NewSceneMode.Single); // drop the temp scene without saving
            }
        }

        static void SetupLights(List<GameObject> created)
        {
            VirtualHandStudioLighting.Create(created);
        }

        static HandRigDriver InstantiateRig(string side, TextAsset profileAsset,
            List<GameObject> created)
        {
            string fbx = $"{ModelsDir}/Hand_{side}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (prefab == null)
                throw new InvalidOperationException("Missing imported FBX: " + fbx);
            var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
            created.Add(go);
            go.name = $"CaptureRig.{side}";
            var driver = go.AddComponent<HandRigDriver>();
            driver.SetMirrored(side == "L"); // same side binding as DemoSceneBuilder
            driver.profileJson = profileAsset;   // shared profile, driver untouched
            driver.LoadProfile();
            // real skin material: diffuse + normal + smoothness converted from
            // roughness (roughness RGB is NOT a metal map; skin is non-metal)
            var renderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                renderer.updateWhenOffscreen = true;
                renderer.forceMatrixRecalculationPerRender = true;
                renderer.sharedMaterial = SkinMaterialFactory.GetOrCreate(side);
            }
            return driver;
        }

        /// <summary>Pick the representative DB8 index-clip frame by the strongest
        /// single index-flex channel peak (array positions 4..6). Whole-array
        /// Max-Min would mistake neutral spread for action. The 20 values are
        /// kept verbatim; provenance (file/time/run/source) is recorded.</summary>
        static CaptureEntry BuildDb8Capture(HandRigDriver left, HandRigDriver right,
            Camera cam)
        {
            string path = Path.GetFullPath(Path.Combine(
                "Assets", "StreamingAssets", "Replays", "db8", "db8_S1_index.jsonl"));
            if (!File.Exists(path))
                throw new InvalidOperationException("DB8 index clip missing: " + path);
            float best = -1f;
            HandMotionFrame bestFrame = null;
            foreach (string line in File.ReadLines(path))
            {
                if (!ReplayData.TryParseFrame(line, out var f, out _)) continue;
                if (f.source != "glove" || f.leftJoints == null ||
                    f.leftJoints.Length != 20) continue;
                float flex = IndexFlexChannels.Max(i => f.leftJoints[i]);
                if (flex > best) { best = flex; bestFrame = f; }
            }
            if (bestFrame == null)
                throw new InvalidOperationException("No glove frame found in " + path);
            var rOpen = PoseLibrary.OpenPose(right);   // right hand has no glove
            return Capture("left_db8_index", left, right, bestFrame.leftJoints, rOpen,
                           bestFrame.source, bestFrame.run, cam, "front",
                           Path.GetFileName(path), bestFrame.time);
        }

        static CaptureEntry Capture(string name, HandRigDriver leftDriver,
            HandRigDriver rightDriver, float[] leftJoints, float[] rightJoints,
            string source, string db8Run, Camera cam, string view,
            string db8File = "", double db8Time = -1)
        {
            // production deformation path only; rightDriver may be null for
            // single-hand closeups
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<HandRigDriver>(true))
                candidate.gameObject.SetActive(candidate == leftDriver || candidate == rightDriver);
            if (leftJoints != null) leftDriver.SetControls(leftJoints);
            if (rightDriver != null && rightJoints != null)
                rightDriver.SetControls(rightJoints);

            // real deformed geometry: bake skinned meshes after SetControls
            var bakedBounds = DeformedBounds(leftDriver);
            if (rightDriver != null)
                bakedBounds.Encapsulate(DeformedBounds(rightDriver));
            // This imported rig's CPU-baked and GPU-renderer bounds differ in
            // scale (recorded below). Frame the actual renderer conservatively;
            // this camera choice does not change any bones, poses, or mesh data.
            var combined = leftDriver.GetComponentInChildren<SkinnedMeshRenderer>().bounds;
            if (rightDriver != null)
                combined.Encapsulate(rightDriver.GetComponentInChildren<SkinnedMeshRenderer>().bounds);

            Vector3 c = combined.center;
            cam.orthographic = true;
            // orthographicSize is the HALF height of the frame: fit the subject
            // so it fills most of the vertical extent (plus margin)
            cam.aspect = CaptureWidth / (float)CaptureHeight;
            cam.orthographicSize = Mathf.Max(0.00001f,
                Mathf.Max(combined.extents.y, combined.extents.x / cam.aspect) * FrameMargin);
            cam.nearClipPlane = 0.0001f;
            cam.farClipPlane = 100f;
            bool anatomicalView = view == "side" || view == "axis_a" || view == "axis_b";
            if (anatomicalView)
            {
                var bones = leftDriver.GetComponentsInChildren<Transform>();
                Vector3 wristPosition = bones.First(t => t.name == "Wrist" || t.name.StartsWith("Wrist.")).position;
                Vector3 index = bones.First(t => t.name == "Index1" || t.name.StartsWith("Index1.")).position;
                Vector3 little = bones.First(t => t.name == "Little1" || t.name.StartsWith("Little1.")).position;
                Vector3 middle = bones.First(t => t.name == "Middle1" || t.name.StartsWith("Middle1.")).position;
                Vector3 up = (middle - wristPosition).normalized;
                Vector3 cross = Vector3.Cross(index - wristPosition, little - wristPosition);
                if (cross.sqrMagnitude < 1e-12f)
                    throw new InvalidOperationException("Degenerate wrist/MCP plane.");
                Vector3 normal = cross.normalized;
                Vector3 direction = view == "axis_a" ? normal : view == "axis_b" ? -normal :
                                    Vector3.Cross(normal, up).normalized;
                if (direction.sqrMagnitude < 0.5f)
                    throw new InvalidOperationException("Cannot derive a hand side view.");
                cam.transform.position = c + direction * 10f;
                cam.transform.rotation = Quaternion.LookRotation(-direction, up);
                float halfX = 0f, halfY = 0f;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = Vector3.Scale(combined.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    halfX = Mathf.Max(halfX, Mathf.Abs(Vector3.Dot(offset, cam.transform.right)));
                    halfY = Mathf.Max(halfY, Mathf.Abs(Vector3.Dot(offset, cam.transform.up)));
                }
                cam.orthographicSize = Mathf.Max(0.00001f,
                    Mathf.Max(halfY, halfX / cam.aspect) * FrameMargin);
            }
            else if (view == "back")
            {
                cam.transform.position = new Vector3(c.x, c.y, c.z - 10f);
                cam.transform.rotation = Quaternion.Euler(0, 180, 0);
            }
            else
            {
                cam.transform.position = new Vector3(c.x, c.y, c.z + 10f);
                cam.transform.rotation = Quaternion.identity;
            }
            if (!anatomicalView) cam.transform.LookAt(c);

            string file = Path.Combine(OutputDir, name + ".png");
            var rt = RenderTexture.GetTemporary(CaptureWidth, CaptureHeight, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(CaptureWidth, CaptureHeight,
                TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.DestroyImmediate(tex);

            Debug.Log($"[CaptureRigPreview] {name} -> {file} " +
                      $"(bounds c={c} ext={combined.extents}, ortho={cam.orthographicSize})");
            var wrist = leftDriver.GetComponentsInChildren<Transform>()
                .FirstOrDefault(t => t.name == "Wrist" || t.name.StartsWith("Wrist."));
            // One bounded Blender/Unity correspondence record: actual imported
            // joint positions distinguish a mapping mismatch from a bad preset.
            string[] watchedBones = { "Wrist", "Index1", "Index2", "Index3",
                                      "Thumb1", "Thumb2", "Thumb3", "Little1" };
            var rigTransforms = leftDriver.GetComponentsInChildren<Transform>();
            var jointPositions = watchedBones.Select(b => rigTransforms.First(t =>
                t.name == b || t.name.StartsWith(b + ".")))
                .Select(t => leftDriver.transform.InverseTransformPoint(t.position)).ToArray();
            return new CaptureEntry
            {
                name = name,
                source = source,
                db8Run = db8Run,
                db8File = db8File,
                db8Time = db8Time,
                camera = view,
                leftJoints = leftJoints,
                rightJoints = rightJoints,
                bakedBoundsCenter = $"{bakedBounds.center}",
                bakedBoundsExtents = $"{bakedBounds.extents}",
                rendererBoundsCenter = $"{leftDriver.GetComponentInChildren<SkinnedMeshRenderer>().bounds.center}",
                lossyScale = leftDriver.transform.lossyScale.ToString("F3"),
                camOrthoSize = cam.orthographicSize,
                camPosition = cam.transform.position.ToString("F3"),
                rootWorldPosition = leftDriver.transform.position.ToString("F6"),
                wristRelativeToRoot = wrist == null ? "missing" :
                    leftDriver.transform.InverseTransformPoint(wrist.position).ToString("F6"),
                boneNames = watchedBones,
                bonePositionsInRoot = jointPositions,
            };
        }

        static Bounds DeformedBounds(HandRigDriver driver)
        {
            var combined = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var smr in driver.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked, false);    // renderer-local positions; scale applied once below
                    var toWorld = smr.transform.localToWorldMatrix;
                    var vertices = baked.vertices;
                    // Unreferenced vertices do not render and must not enlarge framing.
                    foreach (var vertexIndex in baked.triangles)
                    {
                        Vector3 wp = toWorld.MultiplyPoint3x4(vertices[vertexIndex]);
                        if (first) { combined = new Bounds(wp, Vector3.zero); first = false; }
                        else combined.Encapsulate(wp);
                    }
                    Debug.Log($"[CaptureRigPreview] {smr.name} vertices={vertices.Length} indices={baked.triangles.Length} local={baked.bounds} renderer={smr.bounds} scale={smr.transform.lossyScale} measured={combined}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
            return combined;
        }

        [Serializable]
        class CaptureEntry
        {
            public string name;
            public string source;
            public string db8Run;
            public string db8File;
            public double db8Time;
            public string camera;
            public float[] leftJoints;
            public float[] rightJoints;
            public string bakedBoundsCenter;
            public string bakedBoundsExtents;
            public string rendererBoundsCenter;
            public string lossyScale;
            public float camOrthoSize;
            public string camPosition;
            public string rootWorldPosition;
            public string wristRelativeToRoot;
            public string[] boneNames;
            public Vector3[] bonePositionsInRoot;
        }

        [Serializable]
        class SummaryRoot
        {
            public string generatedUtc;
            public int width;
            public int height;
            public string profile;
            public string boneAvailability;
            public string gpu;
            public List<CaptureEntry> captures;
        }
    }
}
