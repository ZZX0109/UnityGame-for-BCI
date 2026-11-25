using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// M2: tints the body skin so its combined colour matches the Elena hand skin.
    ///
    /// The hand skin material (<c>Elena_Skin_ElenaHand_R.mat</c>) is the source of truth.  At
    /// apply time the live body material's combined colour and the hand material's combined
    /// colour are sampled (see <see cref="SkinColorSampling"/>); the per-channel ratio becomes
    /// the tint written into a runtime clone of the shared embedded body material.  The FBX
    /// sub-asset material itself is never modified.
    ///
    /// Ordering contract: must run BEFORE the first Blue-mode switch, because
    /// <see cref="AvatarVisualStyle"/> snapshots the renderer slots exactly once (on first
    /// Blue entry) and restores that snapshot verbatim — if the tint lands before the
    /// snapshot, every Blue→Realistic cycle restores the tinted clones.  When the controller
    /// is already in Blue mode the apply is deferred (retry from <c>Update</c>) instead of
    /// racing the snapshot.
    ///
    /// Silence contract: success logs nothing — the wiring path is consumed by
    /// <c>FullBodyWiringLogs.ExpectWiringSequence</c> FIFO expectations, so only the
    /// fail-safe sampling path may emit a single <see cref="Debug.LogWarning"/>.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Body/Body Skin Tint Applier")]
    [DisallowMultipleComponent]
    public sealed class BodySkinTintApplier : MonoBehaviour
    {
        [Tooltip("Renderers whose skin slots are tinted. Empty = all child renderers.")]
        public Renderer[] BodyRenderers = new Renderer[0];

        [Tooltip("Hand skin material used as the tint source of truth (combined colour target).")]
        public Material TargetSkinMaterial;

        /// <summary>Seconds between automatic retries while the apply is still pending.</summary>
        const float RetryIntervalSeconds = 1f;

        /// <summary>Max per-channel combined-colour delta for a material to count as skin family.</summary>
        const float SkinFamilyTolerance = 0.02f;

        bool _applied;
        float _retryTimer;
        bool _warnedSamplingFailed;
        readonly HashSet<Material> _clones = new HashSet<Material>();

        /// <summary>True once the tinted clones are in the slots (instance-scoped, not static).</summary>
        public bool Applied { get { return _applied; } }

        void Update()
        {
            if (_applied)
                return;
            _retryTimer += Time.unscaledDeltaTime;
            if (_retryTimer < RetryIntervalSeconds)
                return;
            _retryTimer = 0f;
            Apply();
        }

        void OnDestroy()
        {
            // Renderer destruction does not destroy assigned materials — drop the clones.
            foreach (Material clone in _clones)
            {
                if (clone == null)
                    continue;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(clone);
                else
                    Destroy(clone);
#else
                Destroy(clone);
#endif
            }
            _clones.Clear();
        }

        /// <summary>
        /// Applies the tint.  Idempotent per instance; returns true when the slots hold the
        /// tinted clones (or already did), false when deferred or safely skipped.
        /// </summary>
        public bool Apply()
        {
            if (_applied)
                return true;
            if (this == null || gameObject == null)
                return false;
            _retryTimer = 0f;

            // Blue-mode guard: AvatarVisualStyle snapshots on first Blue entry and never
            // retakes it; applying mid-Blue would restore untinted materials later.
            var appearance = GetComponent<FullBodyAppearanceController>();
            if (appearance != null && appearance.Mode == FullBodyAppearanceMode.Blue)
                return false; // deferred; retried from Update after the Realistic restore

            Material handMaterial = ResolveHandMaterial();
            if (handMaterial == null)
                return false; // hands not spawned yet — silent defer, retry via Update

            Material bodyMaterial = FindDominantSharedMaterial(handMaterial);
            if (bodyMaterial == null)
                return true; // every slot already holds a clone (or nothing to tint): done

            Color bodyCombined;
            Color handCombined;
            if (!SkinColorSampling.TryGetCombinedColor(bodyMaterial, out bodyCombined) ||
                !SkinColorSampling.TryGetCombinedColor(handMaterial, out handCombined))
            {
                if (!_warnedSamplingFailed)
                {
                    _warnedSamplingFailed = true;
                    Debug.LogWarning("[BodySkinTint] skin sampling failed on live materials; " +
                                     "body materials left untouched.");
                }
                return false;
            }

            // One clone per source material, shared by every matching slot, so the restore
            // snapshots stay reference-stable.  The dominant skin material is tinted with its
            // own per-channel ratio; sibling skin materials (lips, ears, nails — same base
            // colour family) are tinted with their own ratio so the whole skin set lands on
            // the hand colour, not just the dominant slots.
            ReplaceSkinMaterial(bodyMaterial, handCombined, bodyCombined);
            foreach (Material member in CollectSkinFamily(bodyMaterial, handMaterial))
            {
                Color memberCombined;
                if (SkinColorSampling.TryGetCombinedColor(member, out memberCombined))
                    ReplaceSkinMaterial(member, handCombined, memberCombined);
            }

            _applied = true; // covers both "clones in slots" and "nothing left to tint"
            return true;
        }

        /// <summary>
        /// Clones <paramref name="source"/>, multiplies into the clone the per-channel ratio
        /// that lands its combined colour on <paramref name="handCombined"/>, and swaps every
        /// reference-matching slot.  Returns the number of replaced slots.
        /// </summary>
        int ReplaceSkinMaterial(Material source, Color handCombined, Color sourceCombined)
        {
            Color factor = new Color(
                SkinColorSampling.ClampedRatio(handCombined.r, sourceCombined.r),
                SkinColorSampling.ClampedRatio(handCombined.g, sourceCombined.g),
                SkinColorSampling.ClampedRatio(handCombined.b, sourceCombined.b),
                1f);

            Material tintedClone = new Material(source);
            SetTint(tintedClone, factor);

            int replaced = ReplaceSlots(source, tintedClone);
            if (replaced == 0)
            {
                Destroy(tintedClone); // nothing matched; drop the clone immediately — no leak
                return 0;
            }
            _clones.Add(tintedClone);
            return replaced;
        }

        /// <summary>
        /// Body materials whose combined colour sits within <see cref="SkinFamilyTolerance"/>
        /// per channel of the dominant skin's combined colour (lips, ears, nails...).
        /// Excludes the dominant itself, the hand material, existing clones and duplicates.
        /// Sampling failures simply skip the candidate — no logging in the tint path.
        /// </summary>
        List<Material> CollectSkinFamily(Material dominant, Material handMaterial)
        {
            var family = new List<Material>();
            Color dominantCombined;
            if (!SkinColorSampling.TryGetCombinedColor(dominant, out dominantCombined))
                return family;

            Renderer[] renderers = GetEffectiveRenderers();
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] slots = renderers[r] != null ? renderers[r].sharedMaterials : null;
                if (slots == null)
                    continue;
                for (int s = 0; s < slots.Length; s++)
                {
                    Material candidate = slots[s];
                    if (candidate == null || ReferenceEquals(candidate, dominant) ||
                        ReferenceEquals(candidate, handMaterial) || _clones.Contains(candidate) ||
                        family.Contains(candidate))
                        continue;

                    if (dominant.mainTexture != null && dominant.name == "Human.body" &&
                        candidate.mainTexture != dominant.mainTexture) continue;
                    Color candidateCombined;
                    if (!SkinColorSampling.TryGetCombinedColor(candidate, out candidateCombined))
                        continue;
                    if (Mathf.Abs(candidateCombined.r - dominantCombined.r) > SkinFamilyTolerance ||
                        Mathf.Abs(candidateCombined.g - dominantCombined.g) > SkinFamilyTolerance ||
                        Mathf.Abs(candidateCombined.b - dominantCombined.b) > SkinFamilyTolerance)
                        continue;

                    family.Add(candidate);
                }
            }
            return family;
        }

        /// <summary>
        /// <summary>
        /// Hand skin material: explicit field first, else the first Elena hand renderer's
        /// glTF-style skin material found in the scene (property-based match, never name/guid
        /// hard-coding).  A renderer qualifies when it or any ancestor is named "ElenaHand_*":
        /// the hand prefabs rename only their variant root, while the renderers live on inner
        /// glTF nodes.  Returns null when the hands are not in the scene yet.
        /// </summary>
        Material ResolveHandMaterial()
        {
            if (TargetSkinMaterial != null)
                return TargetSkinMaterial;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || !HasElenaHandAncestor(renderers[i].transform))
                    continue;
                Material candidate = FindGlTFStyleSkinMaterial(renderers[i]);
                if (candidate != null)
                {
                    TargetSkinMaterial = candidate;
                    return candidate;
                }
            }
            return null;
        }

        static bool HasElenaHandAncestor(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (current.name.StartsWith("ElenaHand_"))
                    return true;
                current = current.parent;
            }
            return false;
        }

        static Material FindGlTFStyleSkinMaterial(Renderer renderer)
        {
            if (renderer == null)
                return null;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null)
                    continue;
                if (m.HasProperty("baseColorFactor") && m.HasProperty("baseColorTexture") &&
                    m.GetTexture("baseColorTexture") != null)
                    return m;
            }
            return null;
        }

        /// <summary>
        /// The body skin material is identified by reference: the shared (asset) material used
        /// by the most renderer slots.  Never name-matching; clones already produced by this
        /// instance and the hand skin material itself are excluded, which keeps the operation
        /// idempotent and prevents the hand material (already present on some slots) from
        /// winning the count and turning the tint into a no-op.
        /// </summary>
        Material FindDominantSharedMaterial(Material handMaterial)
        {
            Renderer[] renderers = GetEffectiveRenderers();
            Material dominant = null;
            int bestCount = 0;
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] slots = renderers[r] != null ? renderers[r].sharedMaterials : null;
                if (slots == null)
                    continue;
                for (int s = 0; s < slots.Length; s++)
                {
                    Material candidate = slots[s];
                    if (candidate == null || _clones.Contains(candidate) ||
                        ReferenceEquals(candidate, handMaterial))
                        continue;

                    if (candidate.name == "Human.body" && candidate.mainTexture != null)
                        return candidate;
                    int count = 0;
                    for (int r2 = r; r2 < renderers.Length; r2++)
                    {
                        Material[] other = renderers[r2] != null ? renderers[r2].sharedMaterials : null;
                        if (other == null)
                            continue;
                        for (int s2 = 0; s2 < other.Length; s2++)
                        {
                            if (ReferenceEquals(other[s2], candidate))
                                count++;
                        }
                    }
                    if (count > bestCount)
                    {
                        bestCount = count;
                        dominant = candidate;
                    }
                }
            }
            return dominant;
        }

        Renderer[] GetEffectiveRenderers()
        {
            if (BodyRenderers != null && BodyRenderers.Length > 0)
                return BodyRenderers;
            return GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>Swaps every slot that reference-matches <paramref name="source"/> to the clone.</summary>
        int ReplaceSlots(Material source, Material replacement)
        {
            int replaced = 0;
            Renderer[] renderers = GetEffectiveRenderers();
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer target = renderers[r];
                if (target == null)
                    continue;
                Material[] slots = target.sharedMaterials;
                bool changed = false;
                for (int s = 0; s < slots.Length; s++)
                {
                    if (ReferenceEquals(slots[s], source))
                    {
                        slots[s] = replacement;
                        changed = true;
                        replaced++;
                    }
                }
                if (changed)
                    target.sharedMaterials = slots;
            }
            return replaced;
        }

        /// <summary>
        /// Multiplies the clone's existing tint by <paramref name="factor"/> per channel, via
        /// whichever convention its shader exposes.  Multiplicative on purpose: the combined
        /// colour is tint × texture average, so the factor must be applied ON TOP of the
        /// source tint already carried by the clone (final = source tint × factor × average).
        /// Alpha is preserved.
        /// </summary>
        static void SetTint(Material material, Color factor)
        {
            if (material.HasProperty("baseColorFactor"))
            {
                Color gltfTint = material.GetColor("baseColorFactor");
                gltfTint.r *= factor.r;
                gltfTint.g *= factor.g;
                gltfTint.b *= factor.b;
                material.SetColor("baseColorFactor", gltfTint);
            }
            if (material.HasProperty("_Color"))
            {
                Color standardTint = material.GetColor("_Color");
                standardTint.r *= factor.r;
                standardTint.g *= factor.g;
                standardTint.b *= factor.b;
                material.SetColor("_Color", standardTint);
            }
        }
    }
}
