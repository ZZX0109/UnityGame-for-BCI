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
        [TestCase("water_manhole_cover", .02f)]
        [TestCase("street_lamp_01", 3.9f)]
        [TestCase("metal_trash_can", 1.04f)]
        [TestCase("fire_hydrant", .62f)]
        [TestCase("utility_box_01", 1.1f)]
        [TestCase("utility_box_02", .85f)]
        [TestCase("old_tyre", .58f)]
        [TestCase("security_light", .22f)]
        [TestCase("rollershutter_window_01", 1.9f)]
        [TestCase("rollershutter_door", 2.3f)]
        [TestCase("exterior_aircon_unit", .55f)]
        [TestCase("modular_fire_escape", 6.4f)]
        public void ImportedModelMeasuresWithinPlacementProfile(string assetId, float height)
        {
            var parent = new GameObject("Placement test");
            try
            {
                var library = new StreetAssetLibrary();
                Transform mount = library.SpawnImported(parent.transform, assetId, Vector3.zero, height);
                Assert.NotNull(mount, assetId);
                var renderers = mount.GetComponentsInChildren<Renderer>();
                Assert.IsNotEmpty(renderers);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(.01f), assetId);
                Assert.That(bounds.size.y, Is.EqualTo(height).Within(.01f), assetId);
                Assert.IsTrue(StreetImportedAssetProfile.TryGet(assetId, out var profile));
                var report = new ImportedAssetReport { AssetId = assetId, WorldBounds = bounds, RendererCount = renderers.Length };
                LogMeasurement(assetId, report);
                Assert.IsEmpty(StreetImportedAssetValidator.Validate(report, profile), assetId);
            }
            finally { Object.DestroyImmediate(parent); }
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

        /// <summary>
        /// §2.2 acceptance lock: the standardised spawn must produce an upright 3.9 m lamp whose
        /// base rests on the placement point — measured from the spawned world bounds, not assumed.
        /// A null return means the procedural fallback engaged (§2.2 escape clause) and fails here
        /// so a silently broken FBX chain can never pass as "fixed".
        /// </summary>
        [Test]
        public void StreetLampStandardisedSpawnIsUprightAtTargetHeight()
        {
            var library = new StreetAssetLibrary();
            var parent = new GameObject("Lamp standardisation test root").transform;
            try
            {
                var placement = new Vector3(2f, 0f, 3f);
                Transform lamp = library.SpawnImported(parent, "street_lamp_01", placement, 3.9f);
                Assert.NotNull(lamp,
                    "street_lamp_01 must standardise upright; null means neither import interpretation was vertical-dominant");

                Bounds world = default;
                bool hasBounds = false;
                foreach (Renderer renderer in lamp.GetComponentsInChildren<Renderer>())
                {
                    if (!hasBounds) { world = renderer.bounds; hasBounds = true; }
                    else world.Encapsulate(renderer.bounds);
                }
                Assert.IsTrue(hasBounds, "the standardised lamp must render something");
                TestContext.Out.WriteLine(string.Format(
                    "street_lamp_01 standardised: size=({0:F3}, {1:F3}, {2:F3}) minY={3:F3} renderers={4}",
                    world.size.x, world.size.y, world.size.z, world.min.y,
                    lamp.GetComponentsInChildren<Renderer>().Length));

                Assert.AreEqual(3.9f, world.size.y, .05f,
                    "up extent must equal the 3.9 m target height after standardisation");
                Assert.That(lamp.localPosition, Is.EqualTo(placement));
                MeshRenderer lampRenderer = lamp.GetComponentInChildren<MeshRenderer>();
                Material[] slots = lampRenderer.sharedMaterials;
                Assert.That(slots.Length, Is.EqualTo(3));
                Assert.That(new HashSet<Material>(slots).Count, Is.EqualTo(3), "Post, glass and bulb need separate materials.");
                int bulbSlot = System.Array.FindIndex(slots, mat => mat != null && mat.name.EndsWith("_bulb"));
                Assert.GreaterOrEqual(bulbSlot, 0);
                Mesh lampMesh = lampRenderer.GetComponent<MeshFilter>().sharedMesh;
                float headY = lampRenderer.transform.TransformPoint(lampMesh.GetSubMesh(bulbSlot).bounds.center).y;
                Assert.Greater(headY - placement.y, 3f, "The bulb must be at the top of the pole.");
                float footprint = Mathf.Max(world.size.x, world.size.z);
                Assert.GreaterOrEqual(world.size.y, .7f * footprint,
                    "the standardised lamp must be vertical-dominant (not toppled)");
                Assert.LessOrEqual(footprint, 1.6f,
                    "the lamp footprint must not be abnormally widened (the 'giant lamp' signature)");
                Assert.AreEqual(placement.y, world.min.y, .05f,
                    "the lamp base must rest on the placement height");
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
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
