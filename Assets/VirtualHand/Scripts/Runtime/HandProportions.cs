using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Per-finger length multipliers for the Elena-native skeleton, applied when
    /// ElenaHandPrefabBuilder builds the hand prefabs.
    ///
    /// Why an asset instead of manual Transform edits: the hand rig driver only
    /// ever writes bone <c>localRotation</c>, so bone <c>localPosition</c> edits
    /// survive playback — but manual edits are lost every time the prefab is
    /// rebuilt.  Keeping the multipliers in an asset makes the tuning
    /// reproducible, reviewable and reversible.
    ///
    /// All scales default to 1.0 (model default), so an untouched asset changes
    /// nothing.  Values are applied to the bone's local translation, which moves
    /// the joint; the skinned mesh follows the bones, so the finger visibly
    /// lengthens.  Keep adjustments small (roughly 0.85–1.15) — large changes
    /// stretch the skin around the joint.
    ///
    /// Not covered by this asset: finger thickness and pad shape (mesh only),
    /// and the contact spheres in ElenaNativeRigProfile, which are still sampled
    /// from the untouched GLB.  Re-run "Build Elena FF Hand Prefabs and Verify"
    /// after editing so the prefabs and the profile stay in step.
    /// </summary>
    [CreateAssetMenu(menuName = "Neuralife/VirtualHand/Hand Proportions",
                     fileName = "HandProportions")]
    public class HandProportions : ScriptableObject
    {
        [Serializable]
        public sealed class FingerLength
        {
            [Tooltip("Elena bone prefix: thumb, index, middle, ring or pinky.")]
            public string finger = "index";

            [Range(0.5f, 1.5f)]
            [Tooltip("Multiplies every phalanx of this finger. 1 = model default.")]
            public float lengthScale = 1f;

            [Range(0.5f, 1.5f)]
            [Tooltip("Extra multiplier for the proximal phalanx (_01).")]
            public float proximalScale = 1f;

            [Range(0.5f, 1.5f)]
            [Tooltip("Extra multiplier for the middle phalanx (_02).")]
            public float middleScale = 1f;

            [Range(0.5f, 1.5f)]
            [Tooltip("Extra multiplier for the distal phalanx and fingertip (_03, _03_end).")]
            public float distalScale = 1f;
        }

        [Tooltip("Multiplies the metacarpal bones (palm length). 1 = model default.")]
        [Range(0.5f, 1.5f)]
        public float metacarpalScale = 1f;

        public FingerLength[] fingers =
        {
            new FingerLength { finger = "thumb" },
            new FingerLength { finger = "index" },
            new FingerLength { finger = "middle" },
            new FingerLength { finger = "ring" },
            new FingerLength { finger = "pinky" }
        };

        /// <summary>True when every multiplier is exactly 1, i.e. a no-op.</summary>
        public bool IsIdentity
        {
            get
            {
                if (fingers == null)
                    return Mathf.Approximately(metacarpalScale, 1f);

                foreach (var f in fingers)
                {
                    if (f == null) continue;
                    if (!Mathf.Approximately(f.lengthScale, 1f) ||
                        !Mathf.Approximately(f.proximalScale, 1f) ||
                        !Mathf.Approximately(f.middleScale, 1f) ||
                        !Mathf.Approximately(f.distalScale, 1f))
                        return false;
                }

                return Mathf.Approximately(metacarpalScale, 1f);
            }
        }

        /// <summary>
        /// Multiplier for one Elena bone name.  Control-rig bones are never
        /// touched (they carry no skin weights in this model) and unknown names
        /// return 1.
        /// </summary>
        public float ScaleFor(string boneName)
        {
            if (string.IsNullOrEmpty(boneName) || fingers == null)
                return 1f;

            // Ctrl rig and tip locators are not deform bones.
            if (boneName.IndexOf("Ctrl", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1f;

            foreach (var f in fingers)
            {
                if (f == null || string.IsNullOrEmpty(f.finger))
                    continue;

                if (!boneName.StartsWith(f.finger + "_", StringComparison.OrdinalIgnoreCase))
                    continue;

                float segment = 1f;
                if (boneName.IndexOf("_01.", StringComparison.OrdinalIgnoreCase) >= 0)
                    segment = f.proximalScale;
                else if (boneName.IndexOf("_02.", StringComparison.OrdinalIgnoreCase) >= 0)
                    segment = f.middleScale;
                else if (boneName.IndexOf("_03.", StringComparison.OrdinalIgnoreCase) >= 0)
                    segment = f.distalScale;
                else if (boneName.IndexOf("_base.", StringComparison.OrdinalIgnoreCase) >= 0)
                    // Metacarpal. Must be tested here, inside the finger branch:
                    // every *_base name starts with the finger prefix, so falling
                    // through to the check after the loop would never be reached.
                    return metacarpalScale;
                else
                    return 1f;   // e.g. a finger-prefixed helper bone we do not scale

                return f.lengthScale * segment;
            }

            // Defensive: a *_base name that somehow did not match a finger entry.
            if (boneName.IndexOf("_base.", StringComparison.OrdinalIgnoreCase) >= 0)
                return metacarpalScale;

            return 1f;
        }
    }
}