using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Editor automation entry points per implementation brief:
    ///   Neuralife.VirtualHand.Editor.VirtualHandBuild.GenerateProject
    ///   Neuralife.VirtualHand.Editor.VirtualHandBuild.BuildWindows
    /// Build output: Builds/Windows/VirtualHand.exe; compact results in Results/.
    /// </summary>
    public static class VirtualHandBuild
    {
        const string OutputDir = "Builds/Windows";
        const string ExeName = "VirtualHand.exe";
        const string ScenesPath = "Assets/VirtualHand/Scenes";

        static string[] BuildScenes()
        {
            if (!Directory.Exists(ScenesPath))
                throw new InvalidOperationException($"Missing scenes folder {ScenesPath}. Run GenerateProject first.");
            var scenes = Directory.GetFiles(ScenesPath, "*.unity", SearchOption.TopDirectoryOnly)
                                  .OrderBy(s => Path.GetFileName(s)).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No scenes found to build.");
            return scenes;
        }

        [MenuItem("Neuralife/VirtualHand/Generate Project Assets")]
        public static void GenerateProject()
        {
            EnsureFolders();
            new DemoSceneBuilder().BuildAll();
            AssetDatabase.Refresh();
            Debug.Log("[VirtualHandBuild] GenerateProject done.");
        }

        [MenuItem("Neuralife/VirtualHand/Build Windows x64")]
        public static void BuildWindows()
        {
            EnsureFolders();
            Directory.CreateDirectory(OutputDir);
            var scenes = BuildScenes();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            string summary = $"Build result: {report.summary.result}, size {report.summary.totalSize / 1024 / 1024}MB, " +
                             $"errors {report.summary.totalErrors}, warnings {report.summary.totalWarnings}";
            Debug.Log("[VirtualHandBuild] " + summary);
            File.WriteAllText("Results/build_summary.txt", summary + "\n" + DateTime.Now + "\n");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Build failed: " + summary);
        }

        static void EnsureFolders()
        {
            foreach (var d in new[] { "Results" })
                Directory.CreateDirectory(d);
        }
    }
}
