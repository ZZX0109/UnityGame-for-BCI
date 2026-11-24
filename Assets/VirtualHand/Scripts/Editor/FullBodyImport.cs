using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.EditorTools
{
    /// <summary>
    /// Round 9 Stage 1: import the MPFB rev2 full-body FBX, configure the
    /// importer for Built-in RP Generic rig, and dump a verification report
    /// (bones, SMRs, materials, bounds) for the import acceptance checklist.
    /// </summary>
    public static class FullBodyImport
    {
        private const string ModelPath = "Assets/VirtualHand/Models/FullBody/neutral_adult_avatar.fbx";

        public static void ImportAndVerify()
        {
            var log = new StringBuilder();
            try
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
                if (importer == null)
                {
                    Debug.LogError($"[FullBodyImport] Cannot load importer for {ModelPath}");
                    EditorApplication.Exit(2);
                    return;
                }

                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importBlendShapes = true;
                importer.importVisibility = false;
                importer.isReadable = true; // needed later for head/body mesh split
                importer.SaveAndReimport();
                AssetDatabase.Refresh();

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (root == null)
                {
                    Debug.LogError("[FullBodyImport] Model asset failed to load.");
                    EditorApplication.Exit(2);
                    return;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(root);
                try
                {
                    log.AppendLine($"[FullBodyImport] Model root: {instance.name}");

                    var animator = instance.GetComponent<Animator>();
                    log.AppendLine($"[FullBodyImport] Animator: {(animator != null ? animator.gameObject.name : "none")}");

                    foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var mesh = smr.sharedMesh;
                        log.AppendLine($"[FullBodyImport] SMR '{GetPath(smr.transform)}' mesh='{(mesh != null ? mesh.name : "NULL")}' " +
                                       $"verts={(mesh != null ? mesh.vertexCount.ToString() : "-")} " +
                                       $"submeshes={(mesh != null ? mesh.subMeshCount.ToString() : "-")} " +
                                       $"bones={(smr.bones != null ? smr.bones.Length.ToString() : "-")} " +
                                       $"bounds={smr.bounds.size:F3}m");
                        var mats = smr.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            var shader = m != null && m.shader != null ? m.shader.name : "MISSING";
                            log.AppendLine($"[FullBodyImport]   mat[{i}] '{(m != null ? m.name : "NULL")}' shader='{shader}'");
                        }
                    }

                    var bones = instance.GetComponentsInChildren<Transform>(true);
                    log.AppendLine($"[FullBodyImport] Total transforms: {bones.Length}");
                    foreach (var t in bones)
                    {
                        if (t.name == "Root" || t.name == "pelvis" || t.name == "head" ||
                            t.name == "hand_l" || t.name == "hand_r" || t.name == "foot_l")
                        {
                            log.AppendLine($"[FullBodyImport] Bone '{t.name}' path='{GetPath(t)}' worldPos={t.position:F4}");
                        }
                    }

                    var boundsRenderers = instance.GetComponentsInChildren<Renderer>(true);
                    var combined = new Bounds(Vector3.zero, Vector3.zero);
                    bool first = true;
                    foreach (var r in boundsRenderers)
                    {
                        if (first) { combined = r.bounds; first = false; }
                        else combined.Encapsulate(r.bounds);
                    }
                    log.AppendLine($"[FullBodyImport] Combined renderer bounds size={combined.size:F3}m center={combined.center:F3}");

                    File.WriteAllText("Logs/fullbody-import-report.txt", log.ToString());
                    Debug.Log("[FullBodyImport] DONE");
                    EditorApplication.Exit(0);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"[FullBodyImport] EXCEPTION: {ex}");
                try { File.WriteAllText("Logs/fullbody-import-report.txt", log.ToString()); } catch { }
                Debug.LogError($"[FullBodyImport] Failed: {ex}");
                EditorApplication.Exit(3);
            }
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return sb.ToString();
        }
    }
}
