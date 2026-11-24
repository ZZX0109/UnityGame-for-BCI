using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Samples the imported Elena FF native Open/Close clip in Unity space and
    /// writes a ScriptableObject consumed by HandRigDriver. This avoids applying
    /// Blender-authored angles through a second FBX axis conversion.
    /// </summary>
    public static class ElenaNativeProfileBuilder
    {
        const string ModelPath = "Assets/VirtualHand/Models/ElenaFF/ElenaHand_R.glb";
        const string OutputPath = "Assets/VirtualHand/Resources/Rig/ElenaFF/ElenaNativeRig_R.asset";

        [MenuItem("Neuralife/VirtualHand/Build Elena Native Rig Profile")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null)
                throw new InvalidOperationException("Imported native GLB prefab missing: " + ModelPath);

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Could not instantiate imported native GLB: " + ModelPath);

            try
            {
                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (renderer == null || renderer.bones == null || renderer.bones.Length == 0)
                    throw new InvalidOperationException("Native GLB has no skinned bones.");
                var clip = FindOpenCloseClip(instance);
                if (clip == null)
                    throw new InvalidOperationException("Native GLB has no imported Open/Close animation clip.");
                if (clip.length <= 0f)
                    throw new InvalidOperationException("Native GLB Open/Close animation clip has no duration.");

                var bones = renderer.bones.Where(b => b != null).ToArray();
                if (bones.Length == 0)
                    throw new InvalidOperationException("Native GLB skinned renderer has no non-null bones.");

                var open = Sample(bones, instance, clip, 0f);
                var closed = Sample(bones, instance, clip, Mathf.Min(1.333333f, clip.length));
                var generated = ScriptableObject.CreateInstance<ElenaNativeRigProfile>();
                generated.name = "ElenaNativeRig_R";
                generated.bones = bones.Select((bone, i) => new ElenaNativeRigProfile.NativeBone
                {
                    path = RelativePath(instance.transform, bone),
                    name = bone.name,
                    openLocal = open[i],
                    closedLocal = closed[i],
                }).ToArray();
                generated.bindings = BuildBindings(instance.transform, bones);
                generated.fingerSpheres = BuildFingerSpheres(renderer, bones);
                generated.openControls = OpenControls();
                generated.fistControls = FistControls();
                generated.pinchControls = PinchControls();

                EnsureAssetFolder(Path.GetDirectoryName(OutputPath).Replace('\\', '/'));
                var existing = AssetDatabase.LoadAssetAtPath<ElenaNativeRigProfile>(OutputPath);
                ElenaNativeRigProfile profile;
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(generated, OutputPath);
                    profile = generated;
                }
                else
                {
                    // Update the existing asset in place so its .meta GUID stays stable.
                    EditorUtility.CopySerialized(generated, existing);
                    existing.name = generated.name;
                    EditorUtility.SetDirty(existing);
                    UnityEngine.Object.DestroyImmediate(generated);
                    profile = existing;
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[ElenaNative] profile written: " + OutputPath +
                          " (" + profile.bones.Length + " bones, " + profile.bindings.Length +
                          " bindings, clip length " + clip.length.ToString("0.000") + "s)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static ElenaNativeRigProfile.FingerSphere[] BuildFingerSpheres(SkinnedMeshRenderer renderer, Transform[] profileBones)
        {
            var result = new List<ElenaNativeRigProfile.FingerSphere>();
            var mesh = renderer.sharedMesh;
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            var bind = mesh.bindposes;
            var bones = renderer.bones;
            foreach (var digit in new[] { "thumb", "index", "middle", "ring", "pinky" })
            foreach (var segment in new[] { "_02", "_03" })
            {
                int bone = Array.FindIndex(bones, b => b && ElenaNativeRigProfile.MatchesBone(b.name, digit + segment));
                int end = Array.FindIndex(bones, b => b && ElenaNativeRigProfile.MatchesBone(b.name,
                    digit + (segment == "_02" ? "_03" : "_03.R_end")));
                if (bone < 0 || end < 0) throw new InvalidOperationException("Missing contact landmarks for " + digit + segment);
                var localAxis = bones[bone].InverseTransformPoint(bones[end].position).normalized;
                var points = new List<Vector3>();
                for (int i = 0; i < verts.Length; i++)
                    if (weights[i].boneIndex0 == bone && weights[i].weight0 >= 0.5f)
                        points.Add(bind[bone].MultiplyPoint3x4(verts[i]));
                if (points.Count == 0) throw new InvalidOperationException("Missing contact surface for " + digit + segment);
                var low = points.Min(v => Vector3.Dot(v, localAxis));
                var high = points.Max(v => Vector3.Dot(v, localAxis));
                // Cross sections follow the width and thickness of the authored mesh.
                for (int slice = 0; slice < 4; slice++)
                {
                    var section = points.Where(v => Mathf.Min(3, (int)(4 * Mathf.InverseLerp(low, high, Vector3.Dot(v, localAxis)))) == slice).ToList();
                    if (section.Count == 0) continue;
                    var bounds = new Bounds(section[0], Vector3.zero);
                    foreach (var point in section) bounds.Encapsulate(point);
                    var center = bounds.center;
                    result.Add(new ElenaNativeRigProfile.FingerSphere { boneIndex = Array.IndexOf(profileBones, bones[bone]),
                        thumb = digit == "thumb", center = center, radius = section.Max(v => (v - center).magnitude) });
                }
            }
            return result.ToArray();
        }

        static void EnsureAssetFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next) &&
                    string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[i])))
                    throw new InvalidOperationException("Could not create asset folder: " + next);
                current = next;
            }
        }

        static AnimationClip FindOpenCloseClip(GameObject go)
        {
            var clips = new List<AnimationClip>();
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
                clips.AddRange(animator.runtimeAnimatorController.animationClips);
            var animation = go.GetComponentInChildren<Animation>(true);
            if (animation != null)
                foreach (AnimationState state in animation)
                    if (state.clip != null) clips.Add(state.clip);
            clips.AddRange(AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>());

            return clips.Where(c => c != null)
                        .Distinct()
                        .OrderByDescending(c => c.length)
                        .FirstOrDefault(c => c.name.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             c.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static Quaternion[] Sample(Transform[] bones, GameObject root, AnimationClip clip, float time)
        {
            clip.SampleAnimation(root, time);
            return bones.Select(b => b.localRotation).ToArray();
        }

        static string RelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            if (current != root)
                throw new InvalidOperationException("A native GLB skin bone is outside the imported prefab root: " + target.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static ElenaNativeRigProfile.ControlBinding[] BuildBindings(Transform root, Transform[] bones)
        {
            var list = new List<ElenaNativeRigProfile.ControlBinding>();
            void Add(int control, string contains, bool centered = false, float influence = 1f)
            {
                var bone = Array.Find(bones, t => t != null && ElenaNativeRigProfile.MatchesBone(t.name, contains));
                if (bone == null)
                    throw new InvalidOperationException("Native GLB is missing required control bone: " + contains);
                list.Add(new ElenaNativeRigProfile.ControlBinding
                {
                    control = control,
                    bonePath = RelativePath(root, bone),
                    centered = centered,
                    influence = influence,
                });
            }

            Add(0, "thumb_base");
            Add(1, "thumb_01");
            Add(1, "thumb_02");
            Add(2, "thumb_03");
            Add(3, "thumb_base", centered: true, influence: 0.5f);
            string[][] chains =
            {
                new[] { "index_base", "index_01", "index_02", "index_03" },
                new[] { "middle_base", "middle_01", "middle_02", "middle_03" },
                new[] { "ring_base", "ring_01", "ring_02", "ring_03" },
                new[] { "pinky_base", "pinky_01", "pinky_02", "pinky_03" },
            };
            for (int f = 0; f < chains.Length; f++)
            {
                int o = (f + 1) * 4;
                Add(o, chains[f][1]);
                Add(o + 1, chains[f][2]);
                Add(o + 2, chains[f][3]);
                Add(o + 3, chains[f][1], centered: true);
            }

            var missing = Enumerable.Range(0, 20).Where(c => !list.Any(b => b.control == c)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Native rig does not bind all 20 controls. Missing: " +
                                                    string.Join(", ", missing));
            return list.ToArray();
        }

        static float[] OpenControls()
        {
            var p = new float[20];
            for (int i = 3; i < 20; i += 4) p[i] = 0.5f;
            return p;
        }

        static float[] FistControls()
        {
            var p = Enumerable.Repeat(1f, 20).ToArray();
            for (int i = 3; i < 20; i += 4) p[i] = 0.5f;
            return p;
        }

        static float[] PinchControls()
        {
            var p = OpenControls();
            p[0] = 0.22f; p[1] = 0.22f; p[2] = 0.4f; p[3] = 1f;
            p[4] = 0.55f; p[5] = 0.8f; p[6] = 0.55f; p[7] = 0.5f;
            for (int f = 2; f < 5; f++)
            {
                int o = f * 4;
                p[o] = 0.25f; p[o + 1] = 0.30f; p[o + 2] = 0.25f;
                p[o + 3] = 0.5f;
            }
            return p;
        }
    }
}
