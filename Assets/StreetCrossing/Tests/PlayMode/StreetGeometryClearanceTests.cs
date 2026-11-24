using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
using StreetCrossing.Traffic;
using StreetCrossing.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StreetCrossing.Tests.PlayMode
{    public sealed class StreetGeometryClearanceTests
    {
        [UnityTest]
        public IEnumerator CafeFurnitureUsesOriginalMeshesAndLeavesTheDoorstepOpen()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            var game = Object.FindObjectOfType<StreetGameManager>(); game.SelectScenario(2);
            yield return null; yield return null;
            Transform seating = null;
            foreach (Transform item in game.GetComponentsInChildren<Transform>())
                if (item.name == "Cafe pavement seating") seating = item;
            Assert.IsNotNull(seating);
            // Cafe furniture now has simplified colliders so players cannot walk through it.
            Collider[] colliders = seating.GetComponentsInChildren<Collider>();
            Assert.That(colliders.Length, Is.GreaterThanOrEqualTo(3), "Table and two chairs should each have at least one collider.");
            Assert.AreEqual(3, seating.childCount);
            var bounds = new Bounds(seating.position, Vector3.zero);
            int triangles = 0;
            foreach (var filter in seating.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                Assert.IsNotNull(renderer.sharedMaterial.mainTexture);
                bounds.Encapsulate(renderer.bounds);
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                    triangles += (int)filter.sharedMesh.GetIndexCount(sub) / 3;
            }
            Assert.AreEqual(1612, triangles);
            foreach (Transform piece in seating)
            {
                bool isTable = piece.name == "CafeTable";
                var filter = piece.GetComponentInChildren<MeshFilter>();
                var probe = filter.gameObject.AddComponent<MeshCollider>();
                probe.sharedMesh = filter.sharedMesh;
                var origin = piece.position + Vector3.up * 2f;
                if (isTable) origin += piece.right * .32f; // Sample beside the condiment bottles.
                Assert.IsTrue(probe.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 3f), piece.name);
                float surfaceHeight = hit.point.y - piece.position.y;
                if (isTable)
                {
                    Assert.That(surfaceHeight, Is.InRange(.70f, .76f));
                    Assert.Less(filter.GetComponent<Renderer>().bounds.max.y - hit.point.y, .26f, "Tabletop props");
                }
                else Assert.That(surfaceHeight, Is.InRange(.42f, .48f), "Seat height");
                Object.Destroy(probe);
            }
            Assert.Greater(bounds.min.x, StreetMissionGeometry.Doorstep(StreetDestinationKind.Cafe).x + .6f);
        }

        [UnityTest]
        public IEnumerator BothCrossingMouthsHaveFlushTactileLandingsAndFlaredPhysicalKerbs()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            for (int index = 0; index < 3; index++)
            {
                game.SelectScenario(index);
                yield return null; yield return null;
                Physics.SyncTransforms();
                int surfaces = 0;
                foreach (var surface in game.GetComponentsInChildren<MeshCollider>())
                {
                    if (surface.name != "Paved sidewalk") continue;
                    surfaces++;
                    int side = surface.bounds.center.z > 0 ? 1 : -1;
                    float z = side * (game.scenario.roadHalfWidth + .04f);
                    float edge = game.scenario.crosswalkHalfWidth + .3f;
                    int awayFromJunction = game.scenario.DirectionForLane(0);
                    float previous = -1;
                    foreach (float x in new[] { 0f, edge + .6f, edge + 1.3f })
                    {
                        var ray = new Ray(new Vector3(awayFromJunction * x, 1, z), Vector3.down);
                        Assert.IsTrue(surface.Raycast(ray, out RaycastHit hit, 2));
                        if (x == 0) Assert.That(hit.point.y, Is.EqualTo(0).Within(.005f));
                        else Assert.Greater(hit.point.y, previous + .025f);
                        Assert.Greater(hit.normal.y, .98f);
                        previous = hit.point.y;
                    }
                    Assert.That(previous, Is.InRange(.085f, .105f));
                }
                Assert.AreEqual(2, surfaces);
            }
        }

        [UnityTest]
        public IEnumerator S03TurnEnvelopeDoesNotPenetrateCurbsFurnitureOrBuildings()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>(); game.SelectScenario(2);
            yield return null; yield return null; Physics.SyncTransforms();
            for (int index = game.scenario.StraightLaneCount; index < game.scenario.StraightLaneCount + 2; index++)
            {
                VehicleLanePath path = game.Traffic.Routes[index];
                for (float distance = path.StopDistance - 3; distance <= path.JunctionExit + 3; distance += .12f)
                {
                    Vector3 centre = path.Position(distance) + Vector3.up * 1.08f;
                    Collider[] overlaps = Physics.OverlapBox(centre, new Vector3(1.08f, .94f, 2.4f), path.RotationAt(distance), ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider hit in overlaps)
                        if (hit.GetComponentInParent<VehicleController>() == null)
                            Assert.Fail("Unchanged turning vehicle body penetrates " + hit.name + " at " + path.Position(distance));
                    // Wheel-level footprint detects the old 10cm curb penetration even though the chassis sits higher.
                    overlaps = Physics.OverlapBox(path.Position(distance) + Vector3.up * .075f,
                        new Vector3(1.08f, .015f, 2.4f), path.RotationAt(distance), ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider hit in overlaps) Assert.That(hit.name.StartsWith("Kerb"), Is.False, "Wheel envelope penetrates " + hit.name);
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RaisedTreeGratesAndNorthCornerRemainOutsideExistingSolidGeometry()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>(); game.SelectScenario(2);
            yield return null; yield return null; Physics.SyncTransforms();
            Transform environment = game.transform.Find("S03 Street Environment");
            int grateCount = 0;
            foreach (Transform group in environment.GetComponentsInChildren<Transform>())
                if (group.name == "Raised tree grating")
                {
                    grateCount++;
                    Collider pit = null;
                    foreach (Collider candidate in environment.GetComponentsInChildren<Collider>())
                        if (candidate.name == "Tree pit" && Vector2.Distance(new Vector2(candidate.bounds.center.x, candidate.bounds.center.z), new Vector2(group.position.x, group.position.z)) < .01f) pit = candidate;
                    Assert.That(pit, Is.Not.Null);
                    foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>())
                        Assert.That(renderer.bounds.min.y, Is.GreaterThan(pit.bounds.max.y + .0001f), "A grate must stand above, not inside, its original 14cm planter.");
                }
            Assert.That(grateCount, Is.EqualTo(6));
            Transform northCorner = null; BoxCollider westNeighbour = null;
            foreach (Transform group in environment.GetComponentsInChildren<Transform>())
            {
                if (group.name == "Corner building Pharmacy") northCorner = group;
                BoxCollider box = group.GetComponent<BoxCollider>();
                if (box != null && Mathf.Abs(group.position.x + 26) < .01f && Mathf.Abs(group.position.z - StreetLayout.SidewalkZ(11.3f)) < .01f) westNeighbour = box;
            }
            Assert.That(northCorner, Is.Not.Null); Assert.That(westNeighbour, Is.Not.Null);
            Bounds interior = westNeighbour.bounds; interior.Expand(-.01f);
            foreach (MeshFilter filter in northCorner.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    Assert.That(interior.Contains(filter.transform.TransformPoint(vertex)), Is.False, "The corner side frontage penetrates its west neighbour.");
            LogAssert.NoUnexpectedReceived();
        }

        private sealed class SeatEvidenceInput : IStreetInput
        {
            public Vector2 Movement => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }

        [System.Serializable]
        private sealed class SeatPiece        {
            public string name;
            public Vector3 min, max;
            public bool rendererEnabled;
            public int colliderCount;
            public Bounds Bounds() => new Bounds((min + max) * .5f, max - min);
        }

        [System.Serializable]
        private sealed class SeatShot
        {
            public string view, file;
            public int width, height;
            public float fieldOfView, aspect, eyeHeight;
            public Vector3 playerFeet, eyePosition, lookTarget, targetViewport;
            public Quaternion cameraRotation;
            public bool simulationFrozen, baseSeatRendererEnabled, routeBoardClear;
            public float pavingHitY, colliderTopY, colliderBottomY;
            public string pavingColliderName;
            public Vector3 colliderCenter, colliderSize;
            public int sceneColliderCount, shelterColliderCount;
            public List<string> pavingHits = new List<string>();
            public List<SeatPiece> slats = new List<SeatPiece>();
            public List<SeatPiece> supports = new List<SeatPiece>();
        }

        [System.Serializable]
        private sealed class SeatManifest
        {
            public int schemaVersion = 1;
            public string scenarioId, mode, graphicsApi, sourceSha256, gameState, trafficPhase;
            public float worldTime;
            public bool timeScaleFrozen;
            public List<SeatShot> shots = new List<SeatShot>();
        }

        /// <summary>
        /// S03 far-side Detailed bus shelter seat evidence: fixed eye-height camera measurement plus capture.
        /// Opt-in; runs as baseline (record-only) or accept (strict 0.45 m seat assertions) via environment.
        /// </summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator S03DetailedBusSeatHeightAndSupportEvidence()
        {
            string directory = Environment.GetEnvironmentVariable("BCI_SEAT_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) Assert.Ignore("BCI_SEAT_CAPTURE_DIR is not set; seat evidence capture is opt-in.");
            string mode = Environment.GetEnvironmentVariable("BCI_SEAT_MODE");
            Assert.That(mode, Is.EqualTo("baseline").Or.EqualTo("accept"), "BCI_SEAT_MODE must be baseline or accept.");
            bool accept = mode == "accept";
            const string sourceHash = "BCI_SEAT_SOURCE_HASH";
            string sourceSha = Environment.GetEnvironmentVariable(sourceHash);
            Assert.That(sourceSha, Is.Not.Empty, "BCI_SEAT_SOURCE_HASH must identify the tested source state.");
            const int width = 1920, height = 1080;
            const float fieldOfView = 76f, eyeHeight = 1.77f;
            // Finished seat: 0.45 m top above the y=0 footway datum under the shelter (HeightAtDepth >= 1.55 m).
            const float seatTop = 0.450f, slatThickness = 0.055f, legacySlatTop = 0.6775f, legacyColliderTop = 0.63f;

            string pngPath = Path.Combine(directory, (accept ? "after" : "before") + "-seat-evidence.png");
            string manifestPath = Path.Combine(directory, (accept ? "after" : "before") + "-bus-seat-manifest.json");
            Directory.CreateDirectory(directory);
            Assert.IsFalse(File.Exists(pngPath), "Refusing to overwrite " + pngPath);
            Assert.IsFalse(File.Exists(manifestPath), "Refusing to overwrite " + manifestPath);
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType, "Real-frame capture needs an initialized graphics device.");

            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            yield return null; yield return null;
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            Assert.That(game, Is.Not.Null);
            game.AutoPauseOnFocusLoss = false;
            game.PersistMissionProgress = false;
            game.SelectScenario(2);
            game.SelectMission(0);
            game.SelectVariant(0);
            game.SetInputSource(new SeatEvidenceInput());
            game.StartPractice();
            Time.timeScale = 0f;
            yield return null; yield return null;
            Time.timeScale = 0f;
            Assert.AreEqual("S03", game.scenario.scenarioId);
            Assert.That(game.IsPlaying, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "Traffic and world time must stay frozen during evidence capture.");

            StreetUIController ui = game.GetComponent<StreetUIController>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.RuntimeCanvas, Is.Not.Null);
            ui.RuntimeCanvas.enabled = false;
            game.Player.enabled = false;

            Transform environment = game.transform.Find("S03 Street Environment");
            Assert.That(environment, Is.Not.Null, "S03 environment root missing.");
            Transform shelter = environment.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Detailed bus shelter");
            Assert.That(shelter, Is.Not.Null, "The S03 far-side Detailed bus shelter is missing.");
            Assert.That(shelter.position.x, Is.EqualTo(7f).Within(.01f), "Shelter root x drifted from the S03 far-side placement.");
            Assert.That(shelter.position.y, Is.EqualTo(0f).Within(.01f), "Shelter root must sit on the paving datum.");
            Assert.That(shelter.position.z, Is.EqualTo(game.scenario.roadHalfWidth + 2f).Within(.01f), "Shelter root z drifted from the S03 far-side placement.");
            int shelterColliderCount = shelter.GetComponentsInChildren<Collider>(true).Length;
            Assert.AreEqual(0, shelterColliderCount, "The detailed shelter furniture must stay visual-only.");

            Transform baseSeat = environment.Find("Bus seat");
            Assert.That(baseSeat, Is.Not.Null, "The original generic bus seat is missing.");
            Renderer baseSeatRenderer = baseSeat.GetComponent<Renderer>();
            BoxCollider baseSeatCollider = baseSeat.GetComponent<BoxCollider>();
            Assert.That(baseSeatCollider, Is.Not.Null);
            Assert.IsTrue(baseSeatCollider.enabled, "The original seat collider must stay enabled.");

            // Paving identity under the seat comes from the actual Paved sidewalk collider, not the first hit.
            RaycastHit paving = default;
            var pavingHits = new List<string>();
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(7f, 1.5f, 8.85f), Vector3.down, 2f))
            {
                pavingHits.Add(hit.collider.name + "@" + hit.point.y.ToString("F4", CultureInfo.InvariantCulture));
                if (hit.collider.name == "Paved sidewalk" && (paving.collider == null || hit.point.y > paving.point.y))
                    paving = hit;
            }
            Assert.That(paving.collider, Is.Not.Null, "No Paved sidewalk hit under the shelter seat.");
            Assert.That(paving.point.y, Is.EqualTo(0f).Within(.005f), "The footway datum under the shelter must stay at y=0.");
            Physics.SyncTransforms();

            var shot = new SeatShot {
                view = "far-side-seat", file = Path.GetFileName(pngPath), width = width, height = height,
                fieldOfView = fieldOfView, aspect = (float)width / height, eyeHeight = eyeHeight,
                playerFeet = new Vector3(4.5f, 0.12f, 7.2f), lookTarget = new Vector3(7f, 0.28f, 8.85f),
                simulationFrozen = Time.timeScale == 0f,
                pavingHitY = paving.point.y, pavingColliderName = paving.collider.name,
                baseSeatRendererEnabled = baseSeatRenderer.enabled,
                colliderCenter = baseSeatCollider.bounds.center, colliderSize = baseSeatCollider.bounds.size,
                colliderTopY = baseSeatCollider.bounds.max.y, colliderBottomY = baseSeatCollider.bounds.min.y,
                sceneColliderCount = Object.FindObjectsOfType<Collider>(true).Length,
                shelterColliderCount = shelterColliderCount,
                pavingHits = pavingHits
            };
            foreach (Renderer slat in shelter.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Bus seat timber slat"))
                shot.slats.Add(SeatPieceOf(slat));
            Assert.AreEqual(5, shot.slats.Count, "The detailed shelter must keep exactly five separate seat slats.");
            foreach (Renderer support in shelter.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Bus seat support")))
                shot.supports.Add(SeatPieceOf(support));

            // Fixed pose: feet (4.5,0.12,7.2) with the eye at a 1.77 m world height, aimed at the under-seat zone.
            game.Player.ResetPose(shot.playerFeet, 0f);
            game.Player.enabled = false;
            Camera camera = game.Player.View;
            Assert.That(camera, Is.Not.Null);
            camera.fieldOfView = fieldOfView;
            camera.aspect = shot.aspect;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.transform.position = new Vector3(shot.playerFeet.x, eyeHeight, shot.playerFeet.z);
            camera.transform.rotation = Quaternion.LookRotation(shot.lookTarget - camera.transform.position, Vector3.up);
            Physics.SyncTransforms();
            yield return null;
            shot.eyePosition = camera.transform.position;
            shot.cameraRotation = camera.transform.rotation;
            Assert.That(camera.transform.position.y, Is.EqualTo(eyeHeight).Within(.001f), "Camera must sit at the fixed 1.77 m world eye height.");
            Vector3 viewport = camera.WorldToViewportPoint(shot.lookTarget);
            shot.targetViewport = viewport;
            Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane), "The aim target fell behind the camera.");
            Assert.That(viewport.x, Is.InRange(.03f, .97f), "The aim target left the frame horizontally.");
            Assert.That(viewport.y, Is.InRange(.03f, .97f), "The aim target left the frame vertically.");

            Collider routeBoard = environment.Find("Bus stop board")?.GetComponent<Collider>();
            Assert.That(routeBoard, Is.Not.Null, "The far-side route board is missing.");
            float targetDistance = Vector3.Distance(camera.transform.position, shot.lookTarget);
            shot.routeBoardClear = !routeBoard.Raycast(new Ray(camera.transform.position, (shot.lookTarget - camera.transform.position).normalized), out RaycastHit boardHit, targetDistance);
            Assert.IsTrue(shot.routeBoardClear, "The route board occludes the seat view: hit at " + boardHit.point);

            Bounds seatBounds = shot.slats[0].Bounds();
            foreach (SeatPiece piece in shot.slats.Concat(shot.supports)) seatBounds.Encapsulate(piece.Bounds());
            foreach (Vector3 corner in Corners(seatBounds))
            {
                Vector3 projected = camera.WorldToViewportPoint(corner);
                Assert.That(projected.z, Is.GreaterThan(0f), "Seat/support corner behind the camera: " + corner);
                Assert.That(projected.x, Is.InRange(0f, 1f), "Seat/support corner outside the frame horizontally: " + corner);
                Assert.That(projected.y, Is.InRange(0f, 1f), "Seat/support corner outside the frame vertically: " + corner);
            }

            if (accept)
            {
                float slatUnder = shot.slats.Min(s => s.min.y);
                foreach (SeatPiece slat in shot.slats)
                {
                    Assert.That(slat.max.y, Is.EqualTo(paving.point.y + seatTop).Within(.005f), "Slat top must sit 0.450 m above the actual paving hit.");
                    Assert.That(slat.min.y, Is.GreaterThanOrEqualTo(paving.point.y + seatTop - slatThickness - .005f), "Slat body sank below its envelope.");
                }
                Assert.IsFalse(baseSeatRenderer.enabled, "The overlapping generic seat block must stop rendering.");
                Assert.That(baseSeatCollider.bounds.max.y, Is.EqualTo(paving.point.y + seatTop).Within(.005f), "Retained seat collider top must match the 0.450 m finished surface.");
                Assert.That(baseSeatCollider.bounds.min.y, Is.EqualTo(slatUnder).Within(.005f), "Collider underside must sit flush with the visual slat underside.");
                Assert.That(baseSeatCollider.bounds.size.x, Is.LessThanOrEqualTo(2.801f), "Collider grew beyond its former envelope.");
                Assert.That(baseSeatCollider.bounds.size.y, Is.LessThanOrEqualTo(.161f), "Collider grew beyond its former envelope.");
                Assert.That(baseSeatCollider.bounds.size.z, Is.LessThanOrEqualTo(.501f), "Collider grew beyond its former envelope.");
                Assert.That(shot.supports.Count, Is.GreaterThanOrEqualTo(4), "At least four visual-only seat supports are required.");
                foreach (SeatPiece support in shot.supports)
                {
                    Assert.AreEqual(0, support.colliderCount, "Seat supports must stay visual-only.");
                    if (support.name.EndsWith("leg"))
                    {
                        Assert.That(support.min.y, Is.EqualTo(paving.point.y).Within(.005f), support.name + " must meet the local paving top.");
                        Assert.That(support.max.y, Is.EqualTo(slatUnder).Within(.005f), support.name + " must touch the seat underside.");
                    }
                    else if (support.name.EndsWith("shoe"))
                        Assert.That(support.min.y, Is.EqualTo(paving.point.y).Within(.005f), support.name + " must meet the local paving top.");
                    else if (support.name.EndsWith("bracket") || support.name.EndsWith("rail"))
                        Assert.That(support.max.y, Is.EqualTo(slatUnder).Within(.005f), support.name + " must touch the seat underside.");
                    else if (support.name.EndsWith("stretcher"))
                    {
                        Assert.That(support.min.y, Is.GreaterThan(paving.point.y + .02f), support.name + " is a cross tie, not a ground contact.");
                        Assert.That(support.max.y, Is.LessThan(slatUnder), support.name + " must stay below the seat underside.");
                    }
                }
                string baselineColliders = Environment.GetEnvironmentVariable("BCI_SEAT_BASELINE_SCENE_COLLIDERS");
                if (!string.IsNullOrWhiteSpace(baselineColliders))
                    Assert.AreEqual(int.Parse(baselineColliders, CultureInfo.InvariantCulture), shot.sceneColliderCount,
                        "The seat rework must not change the scene-wide collider count.");
            }
            else
            {
                Assert.IsTrue(baseSeatRenderer.enabled, "Baseline must show the original enabled generic seat block.");
                Assert.That(baseSeatCollider.bounds.max.y, Is.EqualTo(legacyColliderTop).Within(.005f), "Baseline seat collider top drifted from the legacy 0.63 m envelope.");
                Assert.That(shot.slats[0].max.y, Is.EqualTo(legacySlatTop).Within(.005f), "Baseline slat top drifted from the legacy 0.6775 m surface.");
            }

            yield return null;
            WriteCameraFrame(camera, width, height, pngPath);
            SeatManifest manifest = new SeatManifest {
                scenarioId = game.scenario.scenarioId, mode = mode,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), sourceSha256 = sourceSha,
                gameState = game.State.ToString(), trafficPhase = game.Intersection.Phase.ToString(),
                worldTime = game.WorldTime, timeScaleFrozen = Time.timeScale == 0f
            };
            manifest.shots.Add(shot);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            ui.RuntimeCanvas.enabled = true;
            Time.timeScale = 1f;
            LogAssert.NoUnexpectedReceived();
        }

        [System.Serializable]
        private sealed class RouteExtent
        {
            public string material;
            public int triangles;
            public float minX, maxX, minZ, maxZ;
        }

        [System.Serializable]
        private sealed class RouteSurfaceStep
        {
            public float x, fromY, toY;
            public string surface;
        }

        [System.Serializable]
        private sealed class RouteSweepStep
        {
            public float x, groundY;
            public string ground, blocking;
        }

        [System.Serializable]
        private sealed class RouteShotInfo
        {
            public string view, file;
            public Vector3 cameraPosition, lookTarget;
            public float fieldOfView, eyeHeight;
            public int width, height;
            public Vector3 approachEndViewport, departureStartViewport, warningCentreViewport;
        }

        [System.Serializable]
        private sealed class RouteManifest
        {
            public int schemaVersion = 1;
            public string scenarioId, mode, graphicsApi, sourceSha256;
            public bool timeScaleFrozen, cornerRouteConnected, straightGuideBandConnected;
            public float worldTime, measuredGapMetres;
            public float westGuideEndX = float.MinValue, eastGuideStartX = float.MaxValue;
            public float warningMinX = float.MaxValue, warningMaxX = float.MinValue;
            public int sceneColliderCount, reachedCells;
            public Vector3 cornerRegionMin, cornerRegionMax;
            public string trafficPhase;
            public List<RouteExtent> extents = new List<RouteExtent>();
            public List<RouteExtent> walkSurfaces = new List<RouteExtent>();
            public List<RouteSurfaceStep> surfaceSteps = new List<RouteSurfaceStep>();
            public List<RouteSweepStep> sweep = new List<RouteSweepStep>();
            public List<RouteShotInfo> shots = new List<RouteShotInfo>();
        }

        private static RouteExtent RouteExtentOf(Dictionary<string, RouteExtent> source, string name)
        {
            if (!source.TryGetValue(name, out RouteExtent extent))
            {
                extent = new RouteExtent { material = name, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue };
                source.Add(name, extent);
            }
            return extent;
        }

        private static void Stretch(RouteExtent extent, Vector3 centre)
        {
            extent.triangles++;
            extent.minX = Mathf.Min(extent.minX, centre.x); extent.maxX = Mathf.Max(extent.maxX, centre.x);
            extent.minZ = Mathf.Min(extent.minZ, centre.z); extent.maxZ = Mathf.Max(extent.maxZ, centre.z);
        }

        private static bool HexMaterial(string materialName, string hex) => materialName != null && materialName.Contains(hex);

        /// <summary>Marks every coverage cell whose centre projects inside the world-space triangle, so long
        /// thin boxes register along their whole length instead of only at sparse centroids.</summary>
        private static void RasterizeTriangle(Vector3 a, Vector3 b, Vector3 c, Transform source, Bounds region, float cell, int gridX, int gridZ, bool[] tactile)
        {
            Vector3 A = source.TransformPoint(a), B = source.TransformPoint(b), C = source.TransformPoint(c);
            int cx0 = Mathf.Clamp((int)((Mathf.Min(A.x, Mathf.Min(B.x, C.x)) - region.min.x) / cell), 0, gridX - 1);
            int cx1 = Mathf.Clamp((int)((Mathf.Max(A.x, Mathf.Max(B.x, C.x)) - region.min.x) / cell), 0, gridX - 1);
            int cz0 = Mathf.Clamp((int)((Mathf.Min(A.z, Mathf.Min(B.z, C.z)) - region.min.z) / cell), 0, gridZ - 1);
            int cz1 = Mathf.Clamp((int)((Mathf.Max(A.z, Mathf.Max(B.z, C.z)) - region.min.z) / cell), 0, gridZ - 1);
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    Vector2 p = new Vector2(region.min.x + (cx + .5f) * cell, region.min.z + (cz + .5f) * cell);
                    float d1 = (p.x - B.x) * (A.z - B.z) - (A.x - B.x) * (p.y - B.z);
                    float d2 = (p.x - C.x) * (B.z - C.z) - (B.x - C.x) * (p.y - C.z);
                    float d3 = (p.x - A.x) * (C.z - A.z) - (C.x - A.x) * (p.y - A.z);
                    if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) tactile[cz * gridX + cx] = true;
                }
        }

        private static bool IsGuideMaterial(string materialName) => HexMaterial(materialName, "B9A16B") || HexMaterial(materialName, "C5AE7B");
        private static bool IsTactileMaterial(string materialName) => IsGuideMaterial(materialName) || HexMaterial(materialName, "C1A56C");

        /// <summary>
        /// S03 x=-10 corner tactile guidance: two fixed-pose sidewalk captures plus rendered-geometry measurement.
        /// Opt-in evidence: baseline (record-only) or accept (continuity + clearance assertions) via environment.
        /// Search windows are derived from the authored layout; every reported value is measured from the built scene.
        /// </summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator S03CornerTactileRouteContinuityEvidence()
        {
            string directory = Environment.GetEnvironmentVariable("BCI_ROUTE_TACTILE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) Assert.Ignore("BCI_ROUTE_TACTILE_DIR is not set; corner route evidence capture is opt-in.");
            string mode = Environment.GetEnvironmentVariable("BCI_ROUTE_TACTILE_MODE");
            Assert.That(mode, Is.EqualTo("baseline").Or.EqualTo("accept"), "BCI_ROUTE_TACTILE_MODE must be baseline or accept.");
            bool accept = mode == "accept";
            string sourceSha = Environment.GetEnvironmentVariable("BCI_ROUTE_TACTILE_SOURCE_SHA");
            Assert.That(sourceSha, Is.Not.Empty, "BCI_ROUTE_TACTILE_SOURCE_SHA must identify the tested source state.");
            const int width = 1920, height = 1080;
            const float fieldOfView = 76f, eyeHeight = 1.77f;
            // Search windows around the authored S03 corner (guide line z=±10, side-street mouth x=-9±3.3, warning line z=±10.8).
            Bounds spawnRegion = new Bounds(new Vector3(-10f, .025f, -10.4f), new Vector3(13f, .15f, 2.4f));
            string tag = accept ? "after" : "before";
            string pngApproach = Path.Combine(directory, tag + "-corner-approach.png");
            string pngDeparture = Path.Combine(directory, tag + "-corner-departure.png");
            string manifestPath = Path.Combine(directory, tag + "-corner-route-manifest.json");
            Directory.CreateDirectory(directory);
            Assert.IsFalse(File.Exists(pngApproach), "Refusing to overwrite " + pngApproach);
            Assert.IsFalse(File.Exists(pngDeparture), "Refusing to overwrite " + pngDeparture);
            Assert.IsFalse(File.Exists(manifestPath), "Refusing to overwrite " + manifestPath);
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType, "Real-frame capture needs an initialized graphics device.");

            RouteManifest baseline = null;
            if (accept)
            {
                string baselinePath = Environment.GetEnvironmentVariable("BCI_ROUTE_TACTILE_BASELINE");
                Assert.That(baselinePath, Is.Not.Empty, "Accept mode must point BCI_ROUTE_TACTILE_BASELINE at the baseline manifest.");
                baseline = JsonUtility.FromJson<RouteManifest>(File.ReadAllText(baselinePath));
                Assert.That(baseline, Is.Not.Null);
                Assert.That(baseline.shots, Has.Count.EqualTo(2), "Baseline manifest must hold the two fixed shots.");
            }

            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            yield return null; yield return null;
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            Assert.That(game, Is.Not.Null);
            game.AutoPauseOnFocusLoss = false;
            game.PersistMissionProgress = false;
            game.SelectScenario(2);
            game.SelectMission(0);
            game.SelectVariant(0);
            game.SetInputSource(new SeatEvidenceInput());
            game.StartPractice();
            Assert.AreEqual("S03", game.scenario.scenarioId);
            Time.timeScale = 0f;
            yield return null; yield return null;
            Assert.That(game.IsPlaying, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "Traffic and world time must stay frozen during evidence capture.");

            StreetUIController ui = game.GetComponent<StreetUIController>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.RuntimeCanvas, Is.Not.Null);
            ui.RuntimeCanvas.enabled = false;

            RouteManifest manifest = new RouteManifest {
                scenarioId = game.scenario.scenarioId, mode = mode,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), sourceSha256 = sourceSha,
                trafficPhase = game.Intersection.Phase.ToString(),
                worldTime = game.WorldTime, timeScaleFrozen = Time.timeScale == 0f,
                cornerRegionMin = spawnRegion.min, cornerRegionMax = spawnRegion.max,
                sceneColliderCount = Object.FindObjectsOfType<Collider>(true).Length
            };

            // Rendered-geometry scan: every world-space triangle centroid inside the corner windows is
            // attributed to its material and, for tactile surfaces, to a 0.1 m coverage grid.
            const float cell = .1f;
            var walkSurfaces = new Dictionary<string, RouteExtent>();
            for (int side = -1; side <= 1; side += 2)
            {
                Bounds region = new Bounds(new Vector3(-10f, .025f, side * 10.4f), new Vector3(13f, .15f, 2.4f));
                bool spawnSide = side == -1;
                var sideExtents = new Dictionary<string, RouteExtent>();
                int gridX = Mathf.CeilToInt(region.size.x / cell), gridZ = Mathf.CeilToInt(region.size.z / cell);
                bool[] tactile = spawnSide ? new bool[gridX * gridZ] : null;
                foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>(false))
                {
                    Bounds bounds = renderer.bounds;
                    if (bounds.min.x > region.max.x || bounds.max.x < region.min.x || bounds.min.z > region.max.z || bounds.max.z < region.min.z
                        || bounds.min.y > region.max.y || bounds.max.y < region.min.y) continue;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    Mesh mesh = filter.sharedMesh;
                    Vector3[] vertices = mesh.vertices;
                    Transform source = renderer.transform;
                    Material[] materials = renderer.sharedMaterials;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Material subMaterial = materials[Mathf.Min(sub, materials.Length - 1)];
                        string materialName = subMaterial != null ? subMaterial.name : "(null)";
                        int[] triangles = mesh.GetTriangles(sub);
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            Vector3 centre = (source.TransformPoint(vertices[triangles[i]])
                                + source.TransformPoint(vertices[triangles[i + 1]])
                                + source.TransformPoint(vertices[triangles[i + 2]])) / 3f;
                            if (centre.x < region.min.x || centre.x > region.max.x || centre.y < region.min.y || centre.y > region.max.y
                                || centre.z < region.min.z || centre.z > region.max.z) continue;
                            Stretch(RouteExtentOf(sideExtents, materialName), centre);
                            bool tactileSurface = centre.y < .06f && IsTactileMaterial(materialName);
                            if (spawnSide)
                            {
                                if (centre.y < .06f && Mathf.Abs(centre.z + 10f) <= .25f && IsGuideMaterial(materialName))
                                {
                                    if (centre.x < -10f) manifest.westGuideEndX = Mathf.Max(manifest.westGuideEndX, centre.x);
                                    else if (centre.x > -8f) manifest.eastGuideStartX = Mathf.Min(manifest.eastGuideStartX, centre.x);
                                }
                                if (centre.y < .06f && Mathf.Abs(centre.z + 10.8f) <= .3f && HexMaterial(materialName, "C1A56C"))
                                {
                                    manifest.warningMinX = Mathf.Min(manifest.warningMinX, centre.x);
                                    manifest.warningMaxX = Mathf.Max(manifest.warningMaxX, centre.x);
                                }
                                if (tactileSurface)
                                    RasterizeTriangle(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]], source, region, cell, gridX, gridZ, tactile);
                            }
                        }
                    }
                }
                if (spawnSide)
                {
                    Assert.Greater(manifest.westGuideEndX, region.min.x, "No rendered guide surface found west of the side street.");
                    Assert.Less(manifest.eastGuideStartX, region.max.x, "No rendered guide surface found east of the side street.");
                    manifest.measuredGapMetres = manifest.eastGuideStartX - manifest.westGuideEndX;
                    // Flood fill over tactile-covered cells: can rendered tactile geometry connect the two
                    // guide segments through the corner by any detour (including the side-street warning strip)?
                    float westEnd = manifest.westGuideEndX, eastStart = manifest.eastGuideStartX;
                    var queue = new Queue<int>();
                    var seen = new bool[tactile.Length];
                    for (int cz = 0; cz < gridZ; cz++)
                        for (int cx = 0; cx < gridX; cx++)
                        {
                            if (!tactile[cz * gridX + cx]) continue;
                            float x0 = region.min.x + cx * cell, x1 = x0 + cell;
                            if (x1 >= westEnd - .05f && x0 <= westEnd + .45f) { queue.Enqueue(cz * gridX + cx); seen[cz * gridX + cx] = true; }
                        }
                    while (queue.Count > 0)
                    {
                        int index = queue.Dequeue();
                        int cx = index % gridX, cz = index / gridX;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            int nx = cx + dx, nz = cz + dz;
                            if (nx < 0 || nx >= gridX || nz < 0 || nz >= gridZ) continue;
                            int next = nz * gridX + nx;
                            if (seen[next] || !tactile[next]) continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                    // Connection: any reached cell whose band touches the east guide start.
                    for (int cz = 0; cz < gridZ && !manifest.cornerRouteConnected; cz++)
                        for (int cx = 0; cx < gridX && !manifest.cornerRouteConnected; cx++)
                        {
                            if (!seen[cz * gridX + cx]) continue;
                            float x0 = region.min.x + cx * cell, x1 = x0 + cell;
                            if (x1 >= eastStart - .45f && x0 <= eastStart + .05f) manifest.cornerRouteConnected = true;
                        }
                    for (int i = 0; i < seen.Length; i++) if (seen[i]) manifest.reachedCells++;
                    // Straight-band continuity: is the guide band row itself covered end to end?
                    bool straight = true;
                    int bandZ = Mathf.Clamp((int)((-10f - region.min.z) / cell), 0, gridZ - 1);
                    for (float x = westEnd + .1f; straight && x <= eastStart - .1f; x += cell)
                        if (!tactile[bandZ * gridX + Mathf.Clamp((int)((x - region.min.x) / cell), 0, gridX - 1)]) straight = false;
                    manifest.straightGuideBandConnected = straight;
                }
                foreach (KeyValuePair<string, RouteExtent> pair in sideExtents)
                {
                    if (!spawnSide) pair.Value.material += " (far sidewalk mirror)";
                    manifest.extents.Add(pair.Value);
                }
            }

            // Walkable-surface evidence (physics, not visuals): extents per collider name plus the height
            // steps along the guide row itself.
            for (float x = spawnRegion.min.x; x <= spawnRegion.max.x; x += cell)
                for (float z = spawnRegion.min.z; z <= spawnRegion.max.z; z += cell)
                {
                    if (!Physics.Raycast(new Vector3(x, 1f, z), Vector3.down, out RaycastHit hit, 2f)) continue;
                    Stretch(RouteExtentOf(walkSurfaces, hit.collider.name), hit.point);
                }
            float previousY = float.NaN;
            string previousName = null;
            for (float x = spawnRegion.min.x; x <= spawnRegion.max.x; x += cell)
            {
                if (!Physics.Raycast(new Vector3(x, 1f, -10f), Vector3.down, out RaycastHit hit, 2f)) continue;
                if (!float.IsNaN(previousY) && (Mathf.Abs(hit.point.y - previousY) > .008f || hit.collider.name != previousName))
                    manifest.surfaceSteps.Add(new RouteSurfaceStep { x = Mathf.Round(x * 100f) / 100f, fromY = previousY, toY = hit.point.y, surface = hit.collider.name });
                previousY = hit.point.y;
                previousName = hit.collider.name;
            }
            manifest.walkSurfaces.AddRange(walkSurfaces.Values);

            // Player-capsule sweep along the guide walking line: any static blocking above ground contact.
            for (float x = -16f; x <= -4f; x += .05f)
            {
                if (!Physics.Raycast(new Vector3(x, 1f, -10f), Vector3.down, out RaycastHit ground, 2f)) continue;
                float y = ground.point.y;
                var blocking = new List<string>();
                foreach (Collider hit in Physics.OverlapCapsule(new Vector3(x, y + .3f, -10f), new Vector3(x, y + 1.5f, -10f), .3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit == ground.collider) continue; // the walking surface itself is not an obstacle
                    if (hit.GetComponentInParent<VehicleController>() != null) continue;
                    if (hit.transform.IsChildOf(game.Player.transform)) continue;
                    if (hit.bounds.max.y <= y + .07f) continue; // ground contact is not a blocker
                    blocking.Add(hit.name);
                }
                manifest.sweep.Add(new RouteSweepStep { x = Mathf.Round(x * 100f) / 100f, groundY = y, ground = ground.collider?.name, blocking = string.Join("|", blocking) });
            }

            // Two fixed sidewalk poses: approaching and departing the corner along the guide line.
            Camera camera = game.Player.View;
            Assert.That(camera, Is.Not.Null);
            game.Player.ResetPose(new Vector3(-17.5f, .02f, -10f), 90f);
            game.Player.enabled = false;
            Vector3[] anchors = { new Vector3(manifest.westGuideEndX, .02f, -10f), new Vector3(manifest.eastGuideStartX, .02f, -10f), new Vector3((manifest.warningMinX + manifest.warningMaxX) * .5f, .04f, -10.8f) };
            foreach ((Vector3 feet, Vector3 look, string view) in new[] {
                (new Vector3(-17.5f, .02f, -10f), new Vector3(-9f, .02f, -10f), "approach"),
                (new Vector3(-3f, .02f, -10f), new Vector3(-9f, .02f, -10.8f), "departure") })
            {
                game.Player.ResetPose(feet, 0f);
                game.Player.enabled = false;
                camera.fieldOfView = fieldOfView;
                camera.aspect = (float)width / height;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.transform.position = new Vector3(feet.x, eyeHeight, feet.z);
                camera.transform.rotation = Quaternion.LookRotation(look - camera.transform.position, Vector3.up);
                Physics.SyncTransforms();
                yield return null;
                RouteShotInfo shot = new RouteShotInfo {
                    view = view, file = Path.GetFileName(view == "approach" ? pngApproach : pngDeparture),
                    width = width, height = height, fieldOfView = fieldOfView, eyeHeight = eyeHeight,
                    cameraPosition = camera.transform.position, lookTarget = look,
                    approachEndViewport = camera.WorldToViewportPoint(anchors[0]),
                    departureStartViewport = camera.WorldToViewportPoint(anchors[1]),
                    warningCentreViewport = camera.WorldToViewportPoint(anchors[2])
                };
                foreach (Vector3 anchor in new[] { shot.approachEndViewport, shot.departureStartViewport, shot.warningCentreViewport })
                {
                    Assert.Greater(anchor.z, 0f, "A measured corner anchor fell behind the camera.");
                    Assert.That(anchor.x, Is.InRange(.02f, .98f), "A measured corner anchor left the frame horizontally.");
                    Assert.That(anchor.y, Is.InRange(.02f, .98f), "A measured corner anchor left the frame vertically.");
                }
                WriteCameraFrame(camera, width, height, view == "approach" ? pngApproach : pngDeparture);
                manifest.shots.Add(shot);
            }

            // Write the measured manifest before asserting so a failed acceptance still records its numbers.
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            if (accept)
            {
                Assert.IsTrue(manifest.cornerRouteConnected, "Rendered tactile geometry still does not connect the two guide segments through the corner.");
                Assert.AreEqual(baseline.sceneColliderCount, manifest.sceneColliderCount, "The corner repair must not change the scene-wide collider count.");
                Assert.AreEqual(baseline.sweep.Count, manifest.sweep.Count, "Capsule sweep step count changed.");
                for (int i = 0; i < manifest.sweep.Count; i++)
                    Assert.AreEqual(baseline.sweep[i].blocking, manifest.sweep[i].blocking,
                        "New static blocking for the player capsule at x=" + manifest.sweep[i].x);
                Assert.That(manifest.warningMaxX - manifest.warningMinX, Is.GreaterThanOrEqualTo(.3f), "Corner warning area lost its measured extent.");
                for (int i = 0; i < manifest.shots.Count; i++)
                {
                    Assert.That(manifest.shots[i].cameraPosition, Is.EqualTo(baseline.shots[i].cameraPosition).Within(.001f), "Camera pose drifted from the baseline shot.");
                    Assert.That(manifest.shots[i].lookTarget, Is.EqualTo(baseline.shots[i].lookTarget).Within(.001f), "Camera aim drifted from the baseline shot.");
                }
            }
            ui.RuntimeCanvas.enabled = true;
            Time.timeScale = 1f;
            LogAssert.NoUnexpectedReceived();
        }

        private static SeatPiece SeatPieceOf(Renderer renderer)
        {
            Bounds bounds = renderer.bounds;
            return new SeatPiece {
                name = renderer.name, min = bounds.min, max = bounds.max,
                rendererEnabled = renderer.enabled, colliderCount = renderer.GetComponents<Collider>().Length
            };
        }

        private static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float y in new[] { bounds.min.y, bounds.max.y })
            foreach (float z in new[] { bounds.min.z, bounds.max.z })
                yield return new Vector3(x, y, z);
        }

        private static void WriteCameraFrame(Camera camera, int width, int height, string path)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = target;
                camera.Render(); // Warm-up pass settles on-demand shader variants before the saved frame.
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);
                byte[] png = image.EncodeToPNG();
                Assert.Greater(png.Length, 2048, "Rendered frame is unexpectedly empty.");
                File.WriteAllBytes(path, png);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }
    }
}
