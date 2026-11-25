using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Neuralife.XR.Editor
{
    public static class PicoProject
    {
        private const string Loader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private static readonly string[] AndroidProfiles = { "PICO4ControllerProfile", "PICONeo3ControllerProfile", "PICO4UltraControllerProfile" };
        private static readonly string[] WindowsProfiles = { "OculusTouchControllerProfile", "ValveIndexControllerProfile", "HTCViveControllerProfile", "MicrosoftMotionControllerProfile", "KHRSimpleControllerProfile" };

        [MenuItem("Neuralife/PICO XR/Configure project")]
        public static void Configure()
        {
            Directory.CreateDirectory("Assets/XR/Settings"); AssetDatabase.Refresh();
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || !perTarget)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/Settings/NeuralifeXR.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            ConfigureTarget(perTarget, BuildTargetGroup.Standalone);
            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)) ConfigureTarget(perTarget, BuildTargetGroup.Android);
            else Debug.LogWarning("Android OpenXR configuration deferred: Android Build Support is not installed.");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.neuralife." + GameId() + ".vr");
            EditorUtility.SetDirty(perTarget); AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("PICO project configured. Desktop mode remains opt-in; PICO build commands enable VR by default.");
        }

        private static void ConfigureTarget(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group)
        {
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            general.Manager.automaticLoading = general.Manager.automaticRunning = false;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, Loader, group))
                throw new BuildFailedException("Cannot assign OpenXR loader for " + group);
            FeatureHelpers.RefreshFeatures(group);
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (!settings) throw new BuildFailedException("OpenXR settings were not created for " + group);
            foreach (OpenXRFeature feature in settings.GetFeatures<OpenXRFeature>())
            {
                if (feature is UnityEngine.XR.OpenXR.Features.OpenXRInteractionFeature)
                    feature.enabled = (group == BuildTargetGroup.Android ? AndroidProfiles : WindowsProfiles).Contains(feature.GetType().Name);
                else if (feature.GetType().Name == "PICOFeature") feature.enabled = group == BuildTargetGroup.Android;
                else if (feature.GetType().Name == "HandTracking") feature.enabled = true; // com.unity.xr.hands: optical joints on Android and Standalone VR runtimes that support it.
                EditorUtility.SetDirty(feature);
            }
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager);
        }

        [MenuItem("Neuralife/PICO XR/Validate project")]
        public static void Validate()
        {
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                if (group == BuildTargetGroup.Android && !BuildPipeline.IsBuildTargetSupported(group, BuildTarget.Android)) continue;
                XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (!general || !general.Manager || !general.Manager.activeLoaders.Any(loader => loader && loader.GetType().FullName == Loader) || general.InitManagerOnStart)
                    throw new BuildFailedException("Missing manual OpenXR loader for " + group);
                OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (!settings || !settings.GetFeatures<OpenXRInteractionFeature>().Any(f => f.enabled))
                    throw new BuildFailedException("No controller interaction profile for " + group);
                if (group == BuildTargetGroup.Android &&
                    (!settings.GetFeatures<OpenXRFeature>().Any(f => f.GetType().Name == "PICOFeature" && f.enabled) ||
                     !settings.GetFeatures<OpenXRFeature>().Any(f => f.GetType().Name == "PICO4ControllerProfile" && f.enabled)))
                    throw new BuildFailedException("PICO support / PICO4 controller profile is missing.");
            }
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP || PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                throw new BuildFailedException("PICO Android requires ARM64 / IL2CPP.");
            Debug.Log("PICO configuration validation passed (configuration only, no headset acceptance).");
        }

        [InitializeOnLoadMethod]
        private static void WatchPreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    SessionState.EraseBool(PicoXR.PreviewKey);
            };
        }

        [MenuItem("Neuralife/PICO XR/Play in VR")]
        public static void EnablePreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(PicoXR.PreviewKey, true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Neuralife/PICO XR/Play in VR", true)]
        private static bool CanPreview() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Neuralife/PICO XR/Build Windows PCVR")]
        public static void BuildWindows() => Build(false, false);
        [MenuItem("Neuralife/PICO XR/Build Android PICO")]
        public static void BuildAndroid() => Build(true, false);

        /// <summary>
        /// Second deployable flavor: the Hand Sync Lab inspection app. Distinct
        /// package (com.neuralife.handsync.vr) and executable product name; the
        /// normal street build entries and every project setting they touch are
        /// restored afterwards.
        /// </summary>
        [MenuItem("Neuralife/PICO XR/Build Hand Lab Windows PCVR")]
        public static void BuildHandLabWindows() => Build(false, true);
        [MenuItem("Neuralife/PICO XR/Build Hand Lab Android PICO")]
        public static void BuildHandLabAndroid() => Build(true, true);

        const string HandLabPackage = "com.neuralife.handsync.vr";
        const string HandLabProductName = "Neuralife Hand Sync Lab";
        const string HandLabScenePath = "Assets/HandSyncLab/Scenes/HandSyncLab.unity";

        private static void Build(bool android, bool lab)
        {
            BuildTarget target = android ? BuildTarget.Android : BuildTarget.StandaloneWindows64;
            if (!BuildPipeline.IsBuildTargetSupported(android ? BuildTargetGroup.Android : BuildTargetGroup.Standalone, target))
                throw new BuildFailedException("Install " + (android ? "Android Build Support (SDK / NDK / OpenJDK)" : "Windows Build Support") + " for this exact Unity Editor.");
            // Capture before Configure(): it rewrites the Android identifier, and the
            // lab flavor must restore the true prior state afterwards.
            ColorSpace previousColor = PlayerSettings.colorSpace;
            bool previousBundle = EditorUserBuildSettings.buildAppBundle;
            string previousIdentifier = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            string previousProduct = PlayerSettings.productName;
            Configure();
            if (lab) EnsureHandLabScene();
            string scenePath = lab ? HandLabScenePath : Scene();
            if (!File.Exists(scenePath))
                throw new BuildFailedException("Scene is missing: " + scenePath);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Argument("-picoOutput") ?? Path.Combine(root, "BuildOutput", (lab ? "HandLab-" : "PICO-") + (android ? "Android-" : "PCVR-") + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
            output = Path.GetFullPath(output);
            if (!output.StartsWith(Path.Combine(root, "BuildOutput") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
                throw new BuildFailedException("Use a new output directory inside BuildOutput.");
            Directory.CreateDirectory(output);
            try
            {
                if (android) { PlayerSettings.colorSpace = ColorSpace.Linear; EditorUserBuildSettings.buildAppBundle = false; }
                if (lab)
                {
                    PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, HandLabPackage);
                    PlayerSettings.productName = HandLabProductName;
                }
                string executable = lab ? "Neuralife-HandSyncLab-PICO" : "Neuralife-" + GameId() + "-PICO";
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { scenePath }, target = target, locationPathName = Path.Combine(output, executable + (android ? ".apk" : ".exe")),
                    extraScriptingDefines = new[] { "NEURALIFE_PICO_XR" }, options = BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("PICO build failed: " + report.summary.result);
                string[] files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
                File.WriteAllText(Path.Combine(output, "build-manifest.json"), JsonUtility.ToJson(new Manifest {
                    game = lab ? "handsync" : GameId(), target = target.ToString(), builtAtUtc = DateTime.UtcNow.ToString("o"), unity = Application.unityVersion,
                    sourceFingerprint = Fingerprint(root),
                    files = files.Select(p => new Entry { path = p.Substring(output.Length + 1).Replace('\\', '/'), sha256 = Hash(File.ReadAllBytes(p)) }).ToArray()
                }, true), new UTF8Encoding(false));
                Debug.Log("PICO build succeeded: " + output);
            }
            finally
            {
                if (android) EditorUserBuildSettings.buildAppBundle = previousBundle;
                PlayerSettings.colorSpace = previousColor;
                if (lab)
                {
                    PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, previousIdentifier);
                    PlayerSettings.productName = previousProduct;
                }
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// The lab scene builder lives in Assembly-CSharp-Editor (it wires street/virtual-hand
        /// assets), which this package assembly must not reference — call it reflectively.
        /// </summary>
        private static void EnsureHandLabScene()
        {
            const string builderType = "HandSyncLab.Editor.HandSyncLabSceneBuilder";
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type type = assembly.GetType(builderType);
                if (type == null) continue;
                var method = type.GetMethod("EnsureScene", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null) throw new BuildFailedException(builderType + " has no EnsureScene method.");
                method.Invoke(null, null);
                return;
            }
            throw new BuildFailedException("Hand Sync Lab scene builder is missing (expected " + builderType + ").");
        }

        [Serializable] private sealed class Entry { public string path, sha256; }
        [Serializable] private sealed class Manifest
        {
            public string game, target, builtAtUtc, unity, sourceFingerprint;
            public string sdk = "PICO OpenXR 1.4.1 @ 3aa3e62bff41df618529eeb60ff02c29a515dafe";
            public string headsetAcceptance = "NOT_TESTED";
            public Entry[] files;
        }
        private static string Scene() => File.Exists("Assets/ClawMachine/Scenes/ClawMachineScene.unity") ? "Assets/ClawMachine/Scenes/ClawMachineScene.unity" :
            File.Exists("Assets/StreetCrossing/Scenes/StreetCrossingScene.unity") ? "Assets/StreetCrossing/Scenes/StreetCrossingScene.unity" : "Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity";
        private static string GameId() => Scene().Contains("ClawMachine") ? "claw" : Scene().Contains("StreetCrossing") ? "street" : "sokoban";
        private static string Argument(string key) { string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Fingerprint(string root)
        {
            string content = string.Concat(new[] { "Assets", "Packages", "ProjectSettings" }.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
                .OrderBy(p => p, StringComparer.Ordinal).Select(p => p.Substring(root.Length + 1).Replace('\\', '/') + "\t" + Hash(File.ReadAllBytes(p)) + "\n"));
            return Hash(Encoding.UTF8.GetBytes(content));
        }
    }
}
