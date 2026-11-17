using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClawMachine.Editor
{
    /// <summary>Transient edit-view meshes only. Never executes task or prize physics.</summary>
    [InitializeOnLoad]
    public static class ClawMeshPreview
    {
        private sealed class Preview
        {
            public MeshFilter Filter;
            public Mesh Original, Generated;
            public ClawModuleShape Shape;
            public float Bevel;
            public int SceneHandle;
        }
        private static readonly Dictionary<ClawMeshModule, Preview> previews = new Dictionary<ClawMeshModule, Preview>();
        private static volatile bool refreshRequested = true;
        public static int OwnedPreviewCount => previews.Count;

        static ClawMeshPreview()
        {
            ClawMeshModule.VisualDefinitionChanged += RequestRefresh;
            EditorApplication.hierarchyChanged += RequestRefresh;
            Undo.undoRedoPerformed += RequestRefresh;
            EditorApplication.update += Update;
            EditorSceneManager.sceneOpened += (scene, mode) => RequestRefresh();
            EditorSceneManager.sceneClosing += (scene, removing) => ReleaseScene(scene);
            EditorSceneManager.sceneSaving += (scene, path) => ReleaseScene(scene);
            EditorSceneManager.sceneSaved += scene => RequestRefresh();
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingEditMode) ReleaseAll();
                else if (state == PlayModeStateChange.EnteredEditMode) RequestRefresh();
            };
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.quitting += ReleaseAll;
        }
        private static void RequestRefresh(ClawMeshModule module) { refreshRequested = true; }
        private static void RequestRefresh() { refreshRequested = true; }
        private static void Update()
        {
            if (!refreshRequested || EditorApplication.isPlayingOrWillChangePlaymode) return;
            refreshRequested = false; RefreshNow();
        }

        [MenuItem("Claw Machine/Refresh edit preview")]
        public static void RefreshNow()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var eligible = new HashSet<ClawMeshModule>();
            foreach (ClawMeshModule module in Resources.FindObjectsOfTypeAll<ClawMeshModule>())
                if (module && module.isActiveAndEnabled && !EditorUtility.IsPersistent(module) && module.gameObject.scene.IsValid() && module.gameObject.scene.isLoaded &&
                    !EditorSceneManager.IsPreviewScene(module.gameObject.scene) && PrefabStageUtility.GetPrefabStage(module.gameObject) == null)
                    eligible.Add(module);
            var remove = new List<ClawMeshModule>();
            foreach (var pair in previews)
                if (!pair.Key || !eligible.Contains(pair.Key) || !pair.Value.Filter) remove.Add(pair.Key);
            foreach (ClawMeshModule module in remove) Release(module);
            foreach (ClawMeshModule module in eligible)
            {
                MeshFilter filter = module.GetComponent<MeshFilter>();
                if (!filter) continue;
                if (previews.TryGetValue(module, out Preview previous))
                {
                    if (previous.Generated && previous.Shape == module.Shape && Mathf.Approximately(previous.Bevel, module.Bevel) && filter.sharedMesh == previous.Generated) continue;
                    Release(module);
                }
                Mesh generated = module.CreateVisualMesh();
                generated.hideFlags = HideFlags.HideAndDontSave;
                previews.Add(module, new Preview { Filter = filter, Original = filter.sharedMesh, Generated = generated,
                    Shape = module.Shape, Bevel = module.Bevel, SceneHandle = module.gameObject.scene.handle });
                filter.sharedMesh = generated;
            }
            SceneView.RepaintAll();
        }
        public static void ReleaseScene(Scene scene)
        {
            var remove = new List<ClawMeshModule>();
            foreach (var pair in previews) if (pair.Value.SceneHandle == scene.handle) remove.Add(pair.Key);
            foreach (ClawMeshModule module in remove) Release(module);
        }
        public static void ReleaseAll()
        {
            refreshRequested = false;
            var remove = new List<ClawMeshModule>(previews.Keys);
            foreach (ClawMeshModule module in remove) Release(module);
        }
        private static void Release(ClawMeshModule module)
        {
            Preview preview = previews[module]; previews.Remove(module);
            if (preview.Filter && preview.Filter.sharedMesh == preview.Generated) preview.Filter.sharedMesh = preview.Original;
            if (preview.Generated) Object.DestroyImmediate(preview.Generated);
        }
    }
}
