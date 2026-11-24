using System;
using System.IO;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Calibration data shared between the Blender build pipeline and the Unity
    /// runtime. One JSON file (Resources/Rig/hand_rig_profile.json) is exported
    /// by ArtSource/export_rig_profile.py and loaded here, so both sides drive
    /// the same bones with the same axes/signs/scales and the same authored
    /// 20-value pose samples. Core 20-value semantics are unchanged.
    /// </summary>
    [Serializable]
    public class HandRigProfile
    {
        [Serializable]
        public class Channel
        {
            public int index;        // 0..19 control index
            public string bone;      // bone name without side suffix
            public string axis;      // "X" | "Y" | "Z" (bone-local)
            public float scale;      // degrees at |control - rest| = 1.0
            public int sign;         // +1 / -1
            public float rest;       // control value that means "at rest" (0 flex, 0.5 twist)
            public string note;
        }

        [Serializable]
        class Root
        {
            public int version;
            public Channel[] channels;
            public Samples samples;
        }

        [Serializable]
        class Samples
        {
            public float[] open;
            public float[] fist;
            public float[] pinch;
        }

        public Channel[] Channels { get; private set; }
        public float[] Open { get; private set; }
        public float[] Fist { get; private set; }
        public float[] Pinch { get; private set; }

        /// <summary>Load from the Resources/Rig JSON (TextAsset) — same file the build exports.</summary>
        public static bool TryLoad(out HandRigProfile profile)
        {
            profile = null;
            TextAsset ta = Resources.Load<TextAsset>("Rig/hand_rig_profile");
            if (ta == null)
            {
                Debug.LogWarning("[HandRigProfile] Resources/Rig/hand_rig_profile.json not found; " +
                                 "run ArtSource/export_rig_profile.py and place the JSON under Assets/VirtualHand/Resources/Rig/.");
                return false;
            }
            return TryParseText(ta.text, out profile);
        }

        /// <summary>Parse profile JSON from raw text (also usable with Editor-loaded files).</summary>
        public static bool TryParseText(string json, out HandRigProfile profile)
        {
            profile = null;
            try
            {
                var root = JsonUtility.FromJson<Root>(json);
                if (root?.channels == null || root.channels.Length != 20)
                {
                    Debug.LogWarning("[HandRigProfile] channel count != 20; refusing to apply.");
                    return false;
                }
                profile = new HandRigProfile
                {
                    Channels = root.channels,
                    Open = root.samples?.open,
                    Fist = root.samples?.fist,
                    Pinch = root.samples?.pinch,
                };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HandRigProfile] parse failed: " + e.Message);
                return false;
            }
        }
    }
}
