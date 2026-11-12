using System;
using System.Collections.Generic;
using System.IO;
using StreetCrossing.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetCrossing.Editor
{
    public static class StreetSceneBuilder
    {
        public const string ScenePath = "Assets/StreetCrossing/Scenes/StreetCrossingScene.unity";
        public const string ScenarioPath = "Assets/StreetCrossing/Scenarios/S01.asset";
        [MenuItem("Street Crossing/Open S01 Scene")]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Street Crossing/Validate S01 Assets")]
        public static void ValidateAssets()
        {
            StreetScenarioDefinition scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>(ScenarioPath);
            if (scenario == null) throw new InvalidOperationException("Missing S01 asset.");
            scenario.Validate();
            if (AssetDatabase.LoadAssetAtPath<Font>("Assets/StreetCrossing/Resources/StreetCrossing/Fonts/StreetUISans.otf") == null)
                throw new InvalidOperationException("Missing dedicated street font.");
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Missing entry scene.");
            Debug.Log("S01 data and entry assets are valid. This does not constitute a playthrough or build test.");
        }
        [MenuItem("Street Crossing/Recreate Missing Entry Scene")]
        public static void CreateMissingScene()
        {
            ValidateScenario();
            if (File.Exists(ScenePath)) { Debug.Log("Existing scene preserved."); return; }
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject root = new GameObject("StreetCrossing S01 Bootstrap"); SceneManager.MoveGameObjectToScene(root, scene);
            StreetGameManager game = root.AddComponent<StreetGameManager>();
            game.scenario = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>(ScenarioPath);
            game.uiFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/StreetCrossing/Resources/StreetCrossing/Fonts/StreetUISans.otf");
            EditorSceneManager.SaveScene(scene, ScenePath); EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            AssetDatabase.SaveAssets();
        }
        private static void ValidateScenario()
        {
            StreetScenarioDefinition s = AssetDatabase.LoadAssetAtPath<StreetScenarioDefinition>(ScenarioPath);
            if (s == null) throw new InvalidOperationException("Restore serialized S01 asset first."); s.Validate();
        }
        [MenuItem("Street Crossing/Add S01 To Build Settings")]
        public static void AddToBuildSettings()
        {
            ValidateAssets(); var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        [MenuItem("Street Crossing/Build S01 Windows x64")]
        public static void BuildWindows()
        {
            ValidateAssets(); string output = Environment.GetEnvironmentVariable("STREET_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = "BuildOutput/StreetCrossing/Windows/StreetCrossing.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Street Windows build failed: " + report.summary.result);
            Debug.Log("S01 Windows build: " + output);
        }
    }
}
