using System.Collections.Generic;

namespace StreetCrossing.Presentation
{
    /// <summary>
    /// P0-§2.3: static expectation profile for one imported CC0 prop. Consumed by
    /// <see cref="StreetImportedAssetValidator"/> to catch wrong-axis, wrong-scale, or
    /// mis-anchored FBX placements at edit-test time, before anything reaches the street.
    ///
    /// Derivation of the vertical (up) extent range: every imported prop is spawned through
    /// <c>StreetAssetLibrary.SpawnImported</c> with a call-site <c>targetHeight</c>, so the raw
    /// FBX — measured at a normalised (identity) root transform — should already sit within
    /// roughly a 2x band around the size the game finally displays it at. Hence the default
    /// range targetHeight x [0.5, 1.6]: a broken import (100x node scale, inch/unit mismatch,
    /// toppled -90 deg X root) shows up as a gross outlier instead of silently scaling into
    /// place, while still absorbing ordinary authoring variance between CC0 sources.
    /// </summary>
    public readonly struct StreetImportedAssetProfile
    {
        private static readonly Dictionary<string, StreetImportedAssetProfile> Profiles =
            new Dictionary<string, StreetImportedAssetProfile>(12)
            {
                // Flat disc flush on the road surface (StreetWorld.cs:521, targetHeight .02).
                // Footprint spans the ~1 m cover diameter; the up extent is only plate thickness.
                { "water_manhole_cover", new StreetImportedAssetProfile(
                    "water_manhole_cover", .010f, .032f, .4f, 1.5f, .05f, true, false) },

                // Slim upright pole with a head arm (StreetWorld.cs:778, targetHeight 3.9).
                // Vertical dominance is the §2.2 toppled-lamp detector; the footprint band stays
                // wide so a large lamp-head arm still passes.
                { "street_lamp_01", new StreetImportedAssetProfile(
                    "street_lamp_01", 1.95f, 6.24f, .2f, 1.6f, .25f, true, true) },

                // Pedal bin replacing the procedural envelope (~1.04 m tall x 0.62 m wide,
                // StreetFurnitureBuilder.cs:121, targetHeight 1.04). About as wide as tall, so
                // vertical dominance stays off.
                { "metal_trash_can", new StreetImportedAssetProfile(
                    "metal_trash_can", .52f, 1.664f, .3f, 1.4f, .2f, true, false) },

                // Slim upright hydrant (StreetFurnitureBuilder.cs:195, targetHeight .62).
                { "fire_hydrant", new StreetImportedAssetProfile(
                    "fire_hydrant", .31f, .992f, .15f, .8f, .1f, true, true) },

                // Upright utility cabinet (StreetFurnitureBuilder.cs:213, targetHeight 1.1).
                { "utility_box_01", new StreetImportedAssetProfile(
                    "utility_box_01", .55f, 1.76f, .4f, 1.4f, .2f, true, true) },

                // Smaller upright utility cabinet (StreetFurnitureBuilder.cs:224, targetHeight .85).
                { "utility_box_02", new StreetImportedAssetProfile(
                    "utility_box_02", .425f, 1.36f, .3f, 1.2f, .2f, true, true) },

                // Tyre lying flat on the ground (StreetFurnitureBuilder.cs:235, targetHeight .58).
                // The horizontal diameter dominates the up extent by design.
                { "old_tyre", new StreetImportedAssetProfile(
                    "old_tyre", .29f, .928f, .3f, 1.6f, .15f, true, false) },

                // Wall-mounted lamp on a facade (StreetFurnitureBuilder.cs:243, targetHeight .22).
                // Wall-mounted props are exempt from the base-anchor check.
                { "security_light", new StreetImportedAssetProfile(
                    "security_light", .11f, .352f, .1f, .6f, 0f, false, false) },

                // Roller shutter seated in the facade plane (StreetDistrictBuilder.cs:207,
                // targetHeight 1.9). Wall-mounted, base-anchor exempt.
                { "rollershutter_window_01", new StreetImportedAssetProfile(
                    "rollershutter_window_01", .95f, 3.04f, .5f, 2.4f, 0f, false, false) },

                // Full-height roller shutter in the facade plane (StreetDistrictBuilder.cs:278,
                // targetHeight 2.3). Wall-mounted, base-anchor exempt.
                { "rollershutter_door", new StreetImportedAssetProfile(
                    "rollershutter_door", 1.15f, 3.68f, .6f, 2.6f, 0f, false, false) },

                // Modular fire escape climbing the facade from pavement level
                // (StreetDistrictBuilder.cs:381, targetHeight 6.4). Wall-mounted, base-anchor exempt.
                { "modular_fire_escape", new StreetImportedAssetProfile(
                    "modular_fire_escape", 3.2f, 10.24f, 1f, 4f, 0f, false, false) },

                // Wall-mounted condenser on brackets (StreetDistrictBuilder.cs:341,
                // targetHeight .55). Wall-mounted, base-anchor exempt.
                { "exterior_aircon_unit", new StreetImportedAssetProfile(
                    "exterior_aircon_unit", .275f, .88f, .3f, 1.2f, 0f, false, false) },
            };

        /// <summary>Resource id under <c>StreetCrossing/Imported/Models/</c>, equal to the FBX file name.</summary>
        public readonly string AssetId;

        /// <summary>Expected world bounds.size.y band (metres) at a normalised root transform.</summary>
        public readonly float MinVerticalSize;
        public readonly float MaxVerticalSize;

        /// <summary>Expected band for the footprint max edge, i.e. max(size.x, size.z), in metres.</summary>
        public readonly float MinFootprintMaxEdge;
        public readonly float MaxFootprintMaxEdge;

        /// <summary>
        /// Max allowed distance between bounds.min.y and the instance root. Unused for
        /// wall-mounted props, which are exempt from the base-anchor check.
        /// </summary>
        public readonly float BaseAnchorTolerance;

        /// <summary>False for wall-mounted props; disables the BaseFloating/BaseSunk checks.</summary>
        public readonly bool IsGrounded;

        /// <summary>
        /// True when the prop must be upright and slim (pole, hydrant, cabinet): the up extent
        /// then has to beat the footprint max edge (see the 0.7 dominance ratio in the validator).
        /// </summary>
        public readonly bool IsVerticalDominant;

        private StreetImportedAssetProfile(string assetId, float minVerticalSize, float maxVerticalSize,
            float minFootprintMaxEdge, float maxFootprintMaxEdge, float baseAnchorTolerance,
            bool isGrounded, bool isVerticalDominant)
        {
            AssetId = assetId;
            MinVerticalSize = minVerticalSize;
            MaxVerticalSize = maxVerticalSize;
            MinFootprintMaxEdge = minFootprintMaxEdge;
            MaxFootprintMaxEdge = maxFootprintMaxEdge;
            BaseAnchorTolerance = baseAnchorTolerance;
            IsGrounded = isGrounded;
            IsVerticalDominant = isVerticalDominant;
        }

        /// <summary>Look up the expectation profile registered for an imported prop id.</summary>
        public static bool TryGet(string assetId, out StreetImportedAssetProfile profile)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                profile = default;
                return false;
            }
            return Profiles.TryGetValue(assetId, out profile);
        }
    }
}
