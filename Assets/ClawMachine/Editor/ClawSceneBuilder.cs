using System;
using System.IO;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using System.Linq;
using Object = UnityEngine.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ClawMachine.Editor
{
    public static class ClawSceneBuilder
    {
        public const string ScenePath = "Assets/ClawMachine/Scenes/ClawMachineScene.unity";
        [MenuItem("Claw Machine/Open playable C01")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Claw Machine/Validate scene references")]
        public static void Validate()
        {
            var tasks = Object.FindObjectsOfType<GraspTaskController>(true);
            if (tasks.Length != 1 || !tasks[0].IsConfigured) throw new System.InvalidOperationException("Open ClawMachineScene: one fully referenced task is required.");
            var cameras = Object.FindObjectsOfType<CraneCameraController>(true);
            if (cameras.Length != 1 || !cameras[0].IsConfigured) throw new System.InvalidOperationException("Three camera references are required.");
            Debug.Log("Claw C01 serialized references valid. This is not a physics or playthrough test.");
        }
        [MenuItem("Claw Machine/Add scene to Build Settings")]
        public static void AddToBuildSettings()
        {
            if (EditorBuildSettings.scenes.Any(s => s.path == ScenePath)) return;
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
        }
        [MenuItem("Claw Machine/Prepare and validate resources")]
        public static void PrepareForValidation()
        {
            BCIShared.Editor.BCITMPResourceSetup.EnsureResources();
            BCIShared.Editor.BCITMPResourceSetup.ValidateReady();
            const string fontPath = "Assets/ClawMachine/Fonts/ClawUISans.otf";
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/BCIShared/TMP/Resources/BCIShared/TMP/BCITMPBitmap.shader");
            if (!font || !shader || !File.Exists("Assets/ClawMachine/Fonts/LICENSE.txt") || !File.Exists("Assets/ClawMachine/Fonts/GlyphCoverage.json"))
                throw new InvalidOperationException("The licensed Claw source font, coverage manifest and shared bitmap shader must exist.");
            TMP_FontAsset probe = null;
            try
            {
                probe = TMP_FontAsset.CreateFontAsset(font, 48, 4, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (!probe || !probe.material) throw new InvalidOperationException("The pinned TMP package could not create the Claw bitmap font.");
                probe.material.shader = shader;
                if (!probe.TryAddCharacters("拾光夹物所三轴定位教学薄荷礼盒云朵兔偏心狐狸抓持配重安全接近慢速下降"))
                    throw new InvalidOperationException("Claw Chinese glyph preparation failed; rebuild the licensed font subset before delivery.");
            }
            finally
            {
                if (probe)
                {
                    if (probe.atlasTextures != null) foreach (Texture2D texture in probe.atlasTextures) if (texture) UnityEngine.Object.DestroyImmediate(texture);
                    if (probe.material) UnityEngine.Object.DestroyImmediate(probe.material);
                    UnityEngine.Object.DestroyImmediate(probe);
                }
            }
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var task = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GraspTaskController>(true)).Single();
                var manager = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ClawGameManager>(true)).Single();
                if (!task.IsConfigured || manager.Challenges == null || manager.Challenges.Count != 3)
                    throw new InvalidOperationException("The C01-C03 scene bindings are incomplete.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
            Debug.Log("Claw C01-C03 preparation passed: shared TMP resources, licensed Chinese bitmap font and typed scene bindings. Physics/render/player acceptance remains separate.");
        }
        [MenuItem("Claw Machine/Build Windows C01-C03")]
        public static void BuildWindows()
        {
            PrepareForValidation();
            string folder = "BuildOutput/ClawMachine";
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-buildOutput");
            if (option >= 0)
            {
                if (option + 1 >= args.Length || args[option + 1].StartsWith("-")) throw new ArgumentException("-buildOutput requires an output folder.");
                folder = Path.GetFullPath(args[option + 1]);
            }
            Directory.CreateDirectory(folder);
            string output = Path.Combine(folder, "ClawMachine.exe");
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, output, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Claw Windows build failed; inspect the real Editor log.");
        }
    }
}
