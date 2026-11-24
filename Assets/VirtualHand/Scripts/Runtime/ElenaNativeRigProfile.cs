using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Native Elena FF Open/Close sampling in Unity-imported bone-local space.
    /// It is generated in the Editor from the imported GLB clip, so FBX/glTF
    /// axis conversion cannot silently drift away from Blender-authored angles.
    /// The 20 control values remain the frozen visual-control contract.
    /// </summary>
    [CreateAssetMenu(menuName = "Neuralife/VirtualHand/Elena Native Rig Profile",
                     fileName = "ElenaNativeRigProfile")]
    public class ElenaNativeRigProfile : ScriptableObject
    {
        [Serializable]
        public class NativeBone
        {
            public string path;
            public string name;
            public Quaternion openLocal;
            public Quaternion closedLocal;
        }

        [Serializable]
        public class ControlBinding
        {
            [Range(0, 19)] public int control;
            public string bonePath;
            public bool centered;
            [Range(0f, 2f)] public float influence = 1f;
        }

        public string modelName = "Elena FF Rigged hand";
        public string license = "CC BY-SA 4.0";
        public NativeBone[] bones = Array.Empty<NativeBone>();
        public ControlBinding[] bindings = Array.Empty<ControlBinding>();
        public float[] openControls;
        public float[] fistControls;
        public float[] pinchControls;
        [Serializable]
        public struct FingerSphere
        {
            public int boneIndex;
            public bool thumb;
            public Vector3 center;
            public float radius;
        }
        public FingerSphere[] fingerSpheres = Array.Empty<FingerSphere>();

        // glTFast appends a numeric node id to Elena's semantic names.
        public static bool MatchesBone(string name, string segment)
        {
            if (string.Equals(name, segment, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = segment.Contains(".R_") ? segment + "_" : segment + ".R_";
            if (name == null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                name.Length == prefix.Length) return false;
            for (int i = prefix.Length; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return false;
            return true;
        }

        public bool IsValid => bones != null && bones.Length > 0 &&
                               bindings != null && bindings.Length > 0 &&
                               openControls != null && openControls.Length == 20 &&
                               fistControls != null && fistControls.Length == 20 &&
                               pinchControls != null && pinchControls.Length == 20;

        public NativeBone Find(string path, string name)
        {
            if (bones == null) return null;
            for (int i = 0; i < bones.Length; i++)
            {
                if (!string.IsNullOrEmpty(path) && bones[i].path == path) return bones[i];
                if (!string.IsNullOrEmpty(name) && bones[i].name == name) return bones[i];
            }
            return null;
        }
    }
}
