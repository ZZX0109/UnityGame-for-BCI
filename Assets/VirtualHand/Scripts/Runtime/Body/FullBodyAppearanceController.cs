using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>Whole-avatar appearance used by the realistic/blue-mode switch in the games.</summary>
    public enum FullBodyAppearanceMode
    {
        Realistic = 0,
        Blue = 1
    }

    /// <summary>
    /// Applies the whole-avatar appearance mode.  Only the visual representation changes:
    /// no motion, collision or pose side effects, mirroring
    /// <c>GameIntegrationAdapter.SetAppearanceMode</c>.  Blue mode delegates to the shared
    /// <c>AvatarVisualStyle</c> mechanism, which fills one cached translucent blue material
    /// (Fresnel rim + translucent interior from the AvatarBlueOutline shader) into every
    /// material slot of every body renderer; Realistic mode restores the snapshot
    /// shared-material arrays exactly.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Body/Full Body Appearance Controller")]
    [DisallowMultipleComponent]
    public sealed class FullBodyAppearanceController : MonoBehaviour
    {
        [Tooltip("Body renderers driven by the appearance mode.")]
        public Renderer[] BodyRenderers = new Renderer[0];

        AvatarVisualStyle style;
        bool blueApplied;

        /// <summary>Currently applied appearance mode.</summary>
        public FullBodyAppearanceMode Mode { get; private set; }

        /// <summary>Applies an appearance mode and remembers it for later queries.</summary>
        public void SetMode(FullBodyAppearanceMode mode)
        {
            if (mode == FullBodyAppearanceMode.Blue)
            {
                EnsureStyle();
                style.targets = BodyRenderers != null ? BodyRenderers : new Renderer[0];
                // Snapshot (kept per renderer reference), material configuration and the
                // per-slot fill are all owned by AvatarVisualStyle; the generated material
                // is cached there, so repeated Blue calls reuse the identical instance.
                style.SetBlueMode(true);
                blueApplied = true;
            }
            else if (blueApplied)
            {
                // Restore the snapshot exactly.  Without a prior Blue application this
                // branch is skipped, so redundant Realistic calls never touch the slots.
                style.SetBlueMode(false);
                blueApplied = false;
            }

            Mode = mode;
        }

        void OnDestroy()
        {
            // Play-mode safety net: put the original materials back when this controller is
            // destroyed.  AvatarVisualStyle restores again through its own lifecycle; the
            // double restore is idempotent.
            if (blueApplied && style != null)
            {
                style.SetBlueMode(false);
                blueApplied = false;
            }
        }

        void EnsureStyle()
        {
            if (style == null)
                style = GetComponent<AvatarVisualStyle>();
            if (style == null)
                style = gameObject.AddComponent<AvatarVisualStyle>();
        }
    }
}
