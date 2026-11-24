using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StreetCrossing.Editor
{
    /// <summary>
    /// Configures the trimmed Ara/MakeHuman pedestrian FBX for gameplay: generic rig,
    /// four short animation clips, an AnimatorController and a runtime prefab in Resources.
    /// The source FBX stays in Art/External; only the generated prefab goes to Resources.
    /// </summary>
    public static class PedestrianPrefabBuilder
    {
        private const string ModelPath = "Assets/StreetCrossing/Art/External/Pedestrians/StreetPedestrian_Trimmed.fbx";
        private const string PrefabPath = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/StreetPedestrian.prefab";
        private const string ControllerPath = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/StreetPedestrian.controller";
        private static readonly (string clip, string target)[] Clips =
        {
            ("Idle", "Idle"), ("Walk", "Walk"), ("Texting", "Texting"), ("Phone call", "PhoneCall")
        };

        [MenuItem("Street Crossing/Build Street Pedestrian Prefab")]
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing ModelImporter: " + ModelPath);

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.importCameras = false;
            importer.importLights = false;

            var defaults = importer.defaultClipAnimations;
            Debug.Log("[Pedestrian] default clips: " + string.Join(", ", defaults.Select(c => c.name)));
            var configs = new ModelImporterClipAnimation[Clips.Length];
            for (int i = 0; i < Clips.Length; i++)
            {
                var src = defaults.FirstOrDefault(c => c.name.EndsWith("|" + Clips[i].clip + "|BaseLayer")
                                                       || c.name == Clips[i].clip);
                if (src == null) throw new InvalidOperationException("Clip not found: " + Clips[i].clip);
                var cfg = new ModelImporterClipAnimation
                {
                    name = Clips[i].target,
                    firstFrame = src.firstFrame,
                    lastFrame = src.lastFrame,
                    loopTime = Clips[i].target == "Walk" || Clips[i].target == "Idle",
                    loopPose = Clips[i].target == "Walk" || Clips[i].target == "Idle",
                    lockRootRotation = true,
                    keepOriginalPositionY = true,
                    wrapMode = Clips[i].target == "Walk" ? WrapMode.Loop : WrapMode.ClampForever
                };
                configs[i] = cfg;
            }
            importer.clipAnimations = configs;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("Model import failed: " + ModelPath);
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);
            Debug.Log($"[Pedestrian] model bounds centre={bounds.center} size={bounds.size}");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Motion", AnimatorControllerParameterType.Int);
            var sm = controller.layers[0].stateMachine;
            sm.states.ToList().ForEach(s => sm.RemoveState(s.state));
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToList();
            Debug.Log("[Pedestrian] imported clips: " + string.Join(", ", clips.Select(c => c.name)));
            for (int i = 0; i < Clips.Length; i++)
            {
                AnimationClip clip = clips.FirstOrDefault(c => c.name.EndsWith("|" + Clips[i].clip + "|BaseLayer")
                                                               || c.name.Contains(Clips[i].target));
                if (clip == null) throw new InvalidOperationException("AnimationClip missing: " + Clips[i].target);
                var state = sm.AddState(Clips[i].target);
                state.motion = clip;
                var any = sm.AddAnyStateTransition(state);
                any.hasExitTime = false;
                any.duration = 0.12f;
                any.AddCondition(AnimatorConditionMode.Equals, i, "Motion");
            }
            EditorUtility.SetDirty(controller);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = "StreetPedestrian";
            // MakeHuman model imports at real-world metres; normalise only if it is off by >10%.
            float targetHeight = 1.76f;
            if (bounds.size.y > 0.1f && Mathf.Abs(bounds.size.y - targetHeight) / targetHeight > 0.1f)
                instance.transform.localScale *= targetHeight / bounds.size.y;
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Pedestrian] prefab saved: " + PrefabPath);
        }
    }
}
