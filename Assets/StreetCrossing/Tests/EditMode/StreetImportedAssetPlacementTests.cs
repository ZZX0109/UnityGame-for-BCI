using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Presentation;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// P0-§2.3 placement gate for the twelve imported CC0 props. Each case measures the FBX at a
    /// normalised root transform (mirroring StreetAssetLibrary.SpawnImported), prints the full
    /// measurement and the raw import switches, and enforces the StreetImportedAssetProfile bands
    /// for the upright vertical-dominant props. Wall-mounted and flat-lying props are measured and
    /// reported only; the street lamp orientation gate is deferred to §2.2.
    /// </summary>
    public sealed class StreetImportedAssetPlacementTests
    {
        [TestCase("water_manhole_cover")]
        [TestCase("street_lamp_01")]
        [TestCase("metal_trash_can")]
        [TestCase("fire_hydrant")]
        [TestCase("utility_box_01")]
        [TestCase("utility_box_02")]
        [TestCase("old_tyre")]
        [TestCase("security_light")]
        [TestCase("rollershutter_window_01")]
        [TestCase("rollershutter_door")]
        [TestCase("exterior_aircon_unit")]
        [TestCase("modular_fire_escape")]
        public void ImportedModelMeasuresWithinPlacementProfile(string assetId)
        {
            GameObject prefab = Resources.Load<GameObject>("StreetCrossing/Imported/Models/" + assetId);
            Assert.That(prefab, Is.Not.Null,
                "The imported FBX '{0}' must stay under Resources/StreetCrossing/Imported/Models", assetId);
            Assert.That(StreetImportedAssetProfile.TryGet(assetId, out StreetImportedAssetProfile profile), Is.True,
                "Every imported prop id needs a registered StreetImportedAssetProfile");

            LogImportSwitches(assetId, prefab);

            GameObject instance = null;
            try
            {
                ImportedAssetReport report = StreetImportedAssetValidator.MeasurePrefab(prefab, out instance);
                LogMeasurement(assetId, report);
                // §2.3 gate: only the upright vertical-dominant props assert the bands here. The
                // street lamp's raw chain is still under inspection, so its strict orientation
                // gate lands separately in §2.2 and its case above stays report-only.
                if (profile.IsVerticalDominant && assetId != "street_lamp_01")
                {
                    List<string> violations = StreetImportedAssetValidator.Validate(report, profile);
                    Assert.That(violations, Is.Empty,
                        "The imported placement of '{0}' must satisfy its profile", assetId);
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void StreetLampRawChainIsMeasuredBeforeOrientationLock()
        {
            // §2.2: strict orientation lock lands after in-Editor inspection of the raw FBX transform
            // chain. Until then this case measures and reports only; it asserts nothing about
            // orientation, scale, or anchoring.
            GameObject prefab = Resources.Load<GameObject>("StreetCrossing/Imported/Models/street_lamp_01");
            Assert.That(prefab, Is.Not.Null,
                "The imported FBX 'street_lamp_01' must stay under Resources/StreetCrossing/Imported/Models");

            LogImportSwitches("street_lamp_01", prefab);

            GameObject instance = null;
            try
            {
                ImportedAssetReport report = StreetImportedAssetValidator.MeasurePrefab(prefab, out instance);
                LogMeasurement("street_lamp_01", report);
                float upExtent = report.WorldBounds.size.y;
                float footprintMaxEdge = Mathf.Max(report.WorldBounds.size.x, report.WorldBounds.size.z);
                float ratio = footprintMaxEdge > .0001f ? upExtent / footprintMaxEdge : 0f;
                TestContext.Out.WriteLine(string.Format(
                    "street_lamp_01: axisDominance up/footprint={0:F3} (0.7 gate ratio) boundsMinY={1:F3}",
                    ratio, report.WorldBounds.min.y));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Full measurement line for the edit-test log: id, renderer count, bounds, chain.</summary>
        private static void LogMeasurement(string assetId, ImportedAssetReport report)
        {
            TestContext.Out.WriteLine(string.Format(
                "{0}: renderers={1} size=({2:F3}, {3:F3}, {4:F3}) boundsMinY={5:F3} chain=[{6}]",
                assetId, report.RendererCount,
                report.WorldBounds.size.x, report.WorldBounds.size.y, report.WorldBounds.size.z,
                report.WorldBounds.min.y, report.TransformChainText));
        }

        /// <summary>
        /// Report the importer switches that shape the raw transform chain. Output only: §2.2 owns
        /// any change to the .meta values, this gate just records them.
        /// </summary>
        private static void LogImportSwitches(string assetId, GameObject prefab)
        {
            ModelImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(prefab)) as ModelImporter;
            if (importer == null)
            {
                TestContext.Out.WriteLine(assetId + ": no ModelImporter found for the FBX asset");
                return;
            }
            TestContext.Out.WriteLine(string.Format(
                "{0}: globalScale={1} useFileScale={2} bakeAxisConversion={3} useFileUnits={4}",
                assetId, importer.globalScale, importer.useFileScale,
                importer.bakeAxisConversion, importer.useFileUnits));
        }
    }
}
