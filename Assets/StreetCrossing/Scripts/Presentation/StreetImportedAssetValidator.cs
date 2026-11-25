using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>
    /// World-space measurement snapshot of one imported prefab instance, filled in by
    /// <see cref="StreetImportedAssetValidator.MeasurePrefab"/>.
    /// </summary>
    public struct ImportedAssetReport
    {
        public string AssetId;

        /// <summary>Number of renderable renderers found under the instance.</summary>
        public int RendererCount;

        /// <summary>Merged world-space renderer bounds with the instance root normalised to the origin.</summary>
        public Bounds WorldBounds;

        /// <summary>
        /// Raw FBX transform chain (root plus the first three levels), one line per node with
        /// localRotation.eulerAngles and localScale. Diagnostic material for the §2.2
        /// orientation work; the measured bounds ignore the root transform because
        /// <c>StreetAssetLibrary.SpawnImported</c> wipes it before scaling.
        /// </summary>
        public string TransformChainText;
    }

    /// <summary>
    /// P0-§2.3: measures an imported FBX prefab in world space and validates the measurement
    /// against its <see cref="StreetImportedAssetProfile"/>. Pure static runtime code so the
    /// edit-mode test assembly can exercise it without touching the Editor assembly.
    /// </summary>
    public static class StreetImportedAssetValidator
    {
        /// <summary>
        /// A vertical-dominant prop fails the axis check when its up extent drops below
        /// this ratio of the footprint max edge — the signature of a toppled or sideways model.
        /// </summary>
        private const float MinVerticalDominanceRatio = .7f;

        /// <summary>Upper bound on chain nodes emitted into the report text.</summary>
        private const int MaxChainNodes = 24;

        /// <summary>Transform depth (root = 0) included in the report text.</summary>
        private const int MaxChainLevels = 3;

        /// <summary>
        /// Instantiate <paramref name="prefabAsset"/> at a temporary origin location, merge all
        /// renderer world bounds, and fill the report. Mirrors
        /// <c>StreetAssetLibrary.SpawnImported</c> by normalising the instance root to
        /// position zero / identity rotation / unit scale before measuring, so the bounds match
        /// what the spawner sees right before its uniform targetHeight scaling. Child nodes keep
        /// their raw FBX transforms — exactly what this gate must observe.
        /// The caller owns <paramref name="tempInstance"/> and must Object.DestroyImmediate it.
        /// </summary>
        public static ImportedAssetReport MeasurePrefab(GameObject prefabAsset, out GameObject tempInstance)
        {
            var report = new ImportedAssetReport();
            tempInstance = null;
            if (prefabAsset == null)
            {
                return report; // RendererCount stays 0 so Validate reports NoRenderers.
            }

            report.AssetId = prefabAsset.name;
            report.TransformChainText = BuildTransformChainText(prefabAsset.transform);

            tempInstance = Object.Instantiate(prefabAsset);
            tempInstance.transform.localPosition = Vector3.zero;

            Bounds merged = new Bounds();
            bool hasBounds = false;
            foreach (Renderer renderer in tempInstance.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null)
                {
                    continue;
                }
                if (hasBounds)
                {
                    merged.Encapsulate(renderer.bounds);
                }
                else
                {
                    merged = renderer.bounds;
                    hasBounds = true;
                }
                report.RendererCount++;
            }
            if (hasBounds)
            {
                report.WorldBounds = merged;
            }
            return report;
        }

        /// <summary>
        /// Check a measurement against a profile. Returns one entry per violation, each naming
        /// the rule and quoting measured versus expected values; empty means the placement
        /// profile is satisfied.
        /// </summary>
        public static List<string> Validate(ImportedAssetReport report, StreetImportedAssetProfile profile)
        {
            var violations = new List<string>();
            if (report.RendererCount == 0)
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture,
                    "NoRenderers: '{0}' exposes no renderable geometry, bounds are undefined", report.AssetId));
                return violations;
            }

            float upExtent = report.WorldBounds.size.y;
            float footprintMaxEdge = Mathf.Max(report.WorldBounds.size.x, report.WorldBounds.size.z);

            if (upExtent < profile.MinVerticalSize || upExtent > profile.MaxVerticalSize)
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture,
                    "UpExtentOutOfRange: '{0}' up extent {1:F3} m outside the expected {2:F3}-{3:F3} m band",
                    report.AssetId, upExtent, profile.MinVerticalSize, profile.MaxVerticalSize));
            }

            if (footprintMaxEdge < profile.MinFootprintMaxEdge || footprintMaxEdge > profile.MaxFootprintMaxEdge)
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture,
                    "FootprintOutOfRange: '{0}' footprint max edge {1:F3} m outside the expected {2:F3}-{3:F3} m band",
                    report.AssetId, footprintMaxEdge, profile.MinFootprintMaxEdge, profile.MaxFootprintMaxEdge));
            }

            if (profile.IsGrounded)
            {
                // The instance root is normalised to the origin, so bounds.min.y doubles as the
                // base offset from the anchor point SpawnImported seats on localPosition.y.
                float baseOffset = report.WorldBounds.min.y;
                if (baseOffset > profile.BaseAnchorTolerance)
                {
                    violations.Add(string.Format(CultureInfo.InvariantCulture,
                        "BaseFloating: '{0}' base sits {1:F3} m above the root origin (tolerance {2:F3} m)",
                        report.AssetId, baseOffset, profile.BaseAnchorTolerance));
                }
                else if (baseOffset < -profile.BaseAnchorTolerance)
                {
                    violations.Add(string.Format(CultureInfo.InvariantCulture,
                        "BaseSunk: '{0}' base hangs {1:F3} m below the root origin (tolerance {2:F3} m)",
                        report.AssetId, baseOffset, profile.BaseAnchorTolerance));
                }
            }

            if (profile.IsVerticalDominant && upExtent < MinVerticalDominanceRatio * footprintMaxEdge)
            {
                violations.Add(string.Format(CultureInfo.InvariantCulture,
                    "AxisDominanceMismatch: '{0}' up extent {1:F3} m below {2:F3}x its footprint max edge {3:F3} m; the model is likely toppled or sideways",
                    report.AssetId, upExtent, MinVerticalDominanceRatio, footprintMaxEdge));
            }

            return violations;
        }

        /// <summary>
        /// Dump the raw asset transform chain, root first, up to <see cref="MaxChainLevels"/>
        /// levels deep. This is the evidence text §2.2 needs before locking the orientation.
        /// </summary>
        private static string BuildTransformChainText(Transform root)
        {
            var text = new StringBuilder();
            int remainingNodes = MaxChainNodes;
            AppendTransformNode(text, root, 0, ref remainingNodes);
            return text.ToString();
        }

        private static void AppendTransformNode(StringBuilder text, Transform node, int depth, ref int remainingNodes)
        {
            if (node == null || remainingNodes <= 0)
            {
                return;
            }
            --remainingNodes;

            Vector3 euler = node.localRotation.eulerAngles;
            Vector3 scale = node.localScale;
            if (text.Length > 0)
            {
                text.Append(';');
            }
            text.AppendFormat(CultureInfo.InvariantCulture,
                "{0}'{1}' rot=({2:F3}, {3:F3}, {4:F3}) scale=({5:F3}, {6:F3}, {7:F3})",
                text.Length == 0 ? string.Empty : " ", node.name, euler.x, euler.y, euler.z,
                scale.x, scale.y, scale.z);

            if (depth >= MaxChainLevels)
            {
                return;
            }
            foreach (Transform child in node)
            {
                AppendTransformNode(text, child, depth + 1, ref remainingNodes);
            }
        }
    }
}
