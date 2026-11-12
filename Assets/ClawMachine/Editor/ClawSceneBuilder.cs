using System.IO;
using System.Linq;
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
        [MenuItem("Claw Machine/Build Windows C01")]
        public static void BuildWindows()
        {
            const string output = "BuildOutput/ClawMachine/ClawMachine.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, output, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.InvalidOperationException("Claw Windows build failed; inspect the real Editor log.");
        }
    }
}
