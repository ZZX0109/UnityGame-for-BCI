using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
using StreetCrossing.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>
    /// Opt-in evidence capture for the S03 turn-apron-to-road seams: three fixed-pose renders
    /// (approach / departure / oblique seam close-up) plus rendered-mesh and raycast seam measurements.
    /// Baseline mode records only; accept mode asserts the transition contract. Never runs unless
    /// BCI_APRON_CAPTURE_DIR is set, so ordinary suites ignore it entirely.
    /// </summary>
    public sealed class StreetApronSeamCaptureTests
    {
        private const float MainRoadTopY = -.010f;
        private const float SideStreetTopY = .0205f;
        private const float MaxSlopeRiseRun = .032f;
        private const float TransitionZoneMetres = 1.0f;

        private sealed class FrozenInput : IStreetInput
        {
            public Vector2 Movement => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }

        [Serializable] private sealed class SeamSample { public float s, roadY, apronY, step; public string roadHit, apronHit; }
        [Serializable] private sealed class ApronReport
        {
            public string objectName;
            public int vertexCount, triangleCount;
            public float minY, maxY, maxSlopeRiseRun, maxUvError, maxTriangleAreaError;
            public int upFacingTriangles, degenerateTriangles;
            public float mainSeamLength, sideSeamLength;
            public Vector3 cornerVertex;
            public bool hasCorner;
            public List<SeamSample> mainSeamSamples = new List<SeamSample>();
            public List<SeamSample> sideSeamSamples = new List<SeamSample>();
            public float stepOutsideTransition, stepAtCornerSideSeam, stepAtCornerMainSeam;
            /// <summary>Largest step where the probe landed on a sidewalk/paving edge instead of a matched seam face (recorded, not gated).</summary>
            public float pavedEdgeMaxStep;
        }
        [Serializable] private sealed class CaptureShot
        {
            public string view, file;
            public Vector3 eyePosition, lookTarget;
            public float fieldOfView, eyeHeight;
            public string lighting;
        }
        [Serializable] private sealed class CaptureManifest
        {
            public string mode, sourceSha, unityVersion, graphicsDevice, capturedUtc;
            public int sceneColliderCount, apronCount;
            public float mainRoadTopY, sideStreetTopY;
            public List<CaptureShot> shots = new List<CaptureShot>();
            public List<ApronReport> aprons = new List<ApronReport>();
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator S03ApronSeamEvidence()
        {
            string directory = Environment.GetEnvironmentVariable("BCI_APRON_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) Assert.Ignore("BCI_APRON_CAPTURE_DIR is not set; apron seam capture is opt-in.");
            string mode = Environment.GetEnvironmentVariable("BCI_APRON_CAPTURE_MODE");
            Assert.That(mode, Is.EqualTo("baseline").Or.EqualTo("accept"), "BCI_APRON_CAPTURE_MODE must be baseline or accept.");
            bool accept = mode == "accept";
            string sourceSha = Environment.GetEnvironmentVariable("BCI_APRON_CAPTURE_SOURCE_SHA");
            Assert.That(sourceSha, Is.Not.Empty, "BCI_APRON_CAPTURE_SOURCE_SHA must identify the tested source state.");
            string cornerMode = Environment.GetEnvironmentVariable("BCI_APRON_CORNER_MODE") ?? "side";
            Assert.That(cornerMode, Is.EqualTo("side").Or.EqualTo("main"), "BCI_APRON_CORNER_MODE must be side (+0.0205) or main (-0.010).");
            float cornerY = cornerMode == "side" ? SideStreetTopY : MainRoadTopY;

            const int width = 1920, height = 1080;
            const float fieldOfView = 76f, eyeHeight = 1.77f;
            string tag = accept ? "after" : "before";
            string pngApproach = Path.Combine(directory, tag + "-approach.png");
            string pngDeparture = Path.Combine(directory, tag + "-departure.png");
            string pngCloseup = Path.Combine(directory, tag + "-seam-closeup.png");
            string pngMacro = Path.Combine(directory, tag + "-seam-macro.png");
            string manifestPath = Path.Combine(directory, tag + "-apron-seam-manifest.json");
            Directory.CreateDirectory(directory);
            Assert.IsFalse(File.Exists(pngApproach), "Refusing to overwrite " + pngApproach);
            Assert.IsFalse(File.Exists(pngDeparture), "Refusing to overwrite " + pngDeparture);
            Assert.IsFalse(File.Exists(pngCloseup), "Refusing to overwrite " + pngCloseup);
            Assert.IsFalse(File.Exists(pngMacro), "Refusing to overwrite " + pngMacro);
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
            game.SetInputSource(new FrozenInput());
            game.StartPractice();
            Assert.AreEqual("S03", game.scenario.scenarioId);
            Time.timeScale = 0f;
            yield return null; yield return null;
            Assert.That(game.IsPlaying, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "Traffic and world time must stay frozen during evidence capture.");

            StreetUIController ui = game.GetComponent<StreetUIController>();
            Assert.That(ui, Is.Not.Null);
            if (ui.RuntimeCanvas != null) ui.RuntimeCanvas.enabled = false;

            float junction = -9f * game.scenario.DirectionForLane(0);
            List<Vector2[]> hulls = StreetJunctionGeometry.TurnAprons(game.scenario);
            MeshFilter[] apronFilters = Object.FindObjectsOfType<MeshFilter>(true);
            List<MeshFilter> apronList = new List<MeshFilter>();
            foreach (MeshFilter filter in apronFilters)
                if (filter.name == "Turn shoulder asphalt" && filter.sharedMesh != null) apronList.Add(filter);
            Assert.That(apronList.Count, Is.GreaterThan(0), "No turn shoulder meshes found in the built scene.");
            Assert.That(hulls.Count, Is.EqualTo(apronList.Count), "Rendered apron count must match the route-derived hull count.");

            CaptureManifest manifest = new CaptureManifest {
                mode = mode, sourceSha = sourceSha,
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                capturedUtc = DateTime.UtcNow.ToString("o"),
                sceneColliderCount = Object.FindObjectsOfType<Collider>(true).Length,
                mainRoadTopY = MainRoadTopY, sideStreetTopY = SideStreetTopY
            };

            for (int apronIndex = 0; apronIndex < apronList.Count; apronIndex++)
            {
                MeshFilter filter = apronList[apronIndex];
                Vector2[] polygon = hulls[apronIndex];
                Mesh mesh = filter.sharedMesh;
                Vector3[] v = mesh.vertices; Vector2[] uv = mesh.uv; int[] tris = mesh.triangles;
                Transform t = filter.transform;
                ApronReport report = new ApronReport {
                    objectName = t.name, vertexCount = v.Length, triangleCount = tris.Length / 3,
                    maxSlopeRiseRun = 0f, maxUvError = 0f, maxTriangleAreaError = float.MaxValue
                };
                Vector3[] world = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) world[i] = t.TransformPoint(v[i]);
                foreach (Vector3 p in world)
                { report.minY = Mathf.Min(report.minY, p.y); report.maxY = Mathf.Max(report.maxY, p.y); }

                for (int i = 0; i < v.Length; i++)
                {
                    float uvError = Vector2.Distance(uv[i], new Vector2(world[i].x, world[i].z) / StreetWorld.AsphaltTileSizeMetres);
                    report.maxUvError = Mathf.Max(report.maxUvError, uvError);
                }
                // Seam lines come from the route-derived hull polygon, never from mesh adjacency: the
                // split-and-merge triangulation reorders mesh vertices, so adjacency would sample diagonals.
                // The corner is the union corner (junction±SRHW, ±roadHalfWidth) nearest a hull vertex,
                // never merely the first seam vertex, which may be the far end of the main seam. Self-contained
                // (no StreetJunctionGeometry.CornerRamp) so the same script compiles against pre/post sources.
                float bestDistance = float.MaxValue;
                Vector2 rampCorner = new Vector2(junction, 0f), rampAxis = new Vector2(1f, 0f);
                foreach (Vector2 unionCorner in new[] {
                    new Vector2(junction - StreetLayout.SideRoadHalfWidth, game.scenario.roadHalfWidth),
                    new Vector2(junction + StreetLayout.SideRoadHalfWidth, game.scenario.roadHalfWidth),
                    new Vector2(junction - StreetLayout.SideRoadHalfWidth, -game.scenario.roadHalfWidth),
                    new Vector2(junction + StreetLayout.SideRoadHalfWidth, -game.scenario.roadHalfWidth) })
                    foreach (Vector2 vertex in polygon)
                    {
                        float d = Vector2.Distance(unionCorner, vertex);
                        if (d < bestDistance) { bestDistance = d; rampCorner = unionCorner; }
                    }
                Assert.That(bestDistance, Is.LessThanOrEqualTo(.01f), "A hull vertex must sit on a union corner.");
                // Ramp axis: unit vector from the corner along the main-road seam away from the corner.
                foreach (Vector2 vertex in polygon)
                    if (Mathf.Abs(vertex.y - rampCorner.y) < .001f && Mathf.Abs(vertex.x - rampCorner.x) > .001f)
                    { rampAxis = new Vector2(Mathf.Sign(vertex.x - rampCorner.x), 0f); break; }
                List<(int a, int b, bool main)> edges = new List<(int, int, bool)>();
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector3 pa = new Vector3(polygon[i].x, 0f, polygon[i].y), pb = new Vector3(polygon[(i + 1) % polygon.Length].x, 0f, polygon[(i + 1) % polygon.Length].y);
                    bool main = Mathf.Abs(Mathf.Abs(polygon[i].y) - StreetLayout.RoadHalfWidth) < .001f && Mathf.Abs(polygon[i].y - polygon[(i + 1) % polygon.Length].y) < .001f;
                    bool side = Mathf.Abs(Mathf.Abs(polygon[i].x - junction) - StreetLayout.SideRoadHalfWidth) < .001f && Mathf.Abs(polygon[i].x - polygon[(i + 1) % polygon.Length].x) < .001f;
                    if (main) { edges.Add((i, (i + 1) % polygon.Length, true)); report.mainSeamLength += Vector2.Distance(polygon[i], polygon[(i + 1) % polygon.Length]); }
                    if (side) { edges.Add((i, (i + 1) % polygon.Length, false)); report.sideSeamLength += Vector2.Distance(polygon[i], polygon[(i + 1) % polygon.Length]); }
                }
                report.cornerVertex = new Vector3(rampCorner.x, 0f, rampCorner.y); report.hasCorner = true;

                // Face slope, area and winding measured from the rendered triangles.
                float cornerMainStep = -1f, cornerSideStep = -1f;
                for (int tri = 0; tri < tris.Length; tri += 3)
                {
                    Vector3 a = world[tris[tri]], b = world[tris[tri + 1]], c = world[tris[tri + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    double area2 = cross.magnitude * .5;
                    if (area2 < 1e-8) { report.degenerateTriangles++; continue; }
                    Vector3 n = cross.normalized;
                    if (n.y > 0f) report.upFacingTriangles++;
                    report.maxSlopeRiseRun = Mathf.Max(report.maxSlopeRiseRun, Mathf.Sqrt(n.x * n.x + n.z * n.z) / Mathf.Max(n.y, 1e-6f));
                    if (report.maxTriangleAreaError > float.MaxValue * .5f) report.maxTriangleAreaError = 0f;
                }
                if (report.maxTriangleAreaError > float.MaxValue * .5f) report.maxTriangleAreaError = 0f;

                // Raycast seam step sampling along the hull seam lines, offset perpendicular into each surface.
                if (report.hasCorner)
                {
                    foreach ((int a, int b, bool main) in edges)
                    {
                        Vector2 ha = polygon[a], hb = polygon[b];
                        Vector2 delta = hb - ha;
                        float length = delta.magnitude;
                        // Offset the sample pair only 60 mm across the seam: a wider apron-side probe can
                        // pass under the adjoining sidewalk edge and land on Paved sidewalk instead of the apron.
                        Vector2 inward = main
                            ? new Vector2(0f, ha.y > 0f ? .06f : -.06f)   // apron side is outward from the road
                            : new Vector2(ha.x > junction ? -.06f : .06f, 0f); // apron side is away from x=junction
                        for (float s = 0f; s <= length; s += .1f)
                        {
                            Vector2 p = ha + delta.normalized * s;
                            Vector3 roadPoint = new Vector3(p.x - inward.x, 1f, p.y - inward.y);
                            Vector3 apronPoint = new Vector3(p.x + inward.x, 1f, p.y + inward.y);
                            if (!Physics.Raycast(roadPoint, Vector3.down, out RaycastHit roadHit, 2f)) continue;
                            if (!Physics.Raycast(apronPoint, Vector3.down, out RaycastHit apronHit, 2f)) continue;
                            // Kerb colliders stand beside the seams, not on them; a kerb hit means the
                            // sample line left the seam and must not enter the step statistics.
                            if (roadHit.collider.name.StartsWith("Kerb") || apronHit.collider.name.StartsWith("Kerb")) continue;
                            // Field distance along the ramp axis decides the transition band, not arc length.
                            float fieldDistance = Vector2.Dot(p - rampCorner, rampAxis);
                            SeamSample sample = new SeamSample {
                                s = Mathf.Round(fieldDistance * 100f) / 100f,
                                roadY = roadHit.point.y, apronY = apronHit.point.y,
                                roadHit = roadHit.collider.name, apronHit = apronHit.collider.name,
                                step = Mathf.Round((apronHit.point.y - roadHit.point.y) * 10000f) / 10000f
                            };
                            if (main)
                            {
                                report.mainSeamSamples.Add(sample);
                                // Only true main-road seam faces feed the 1 mm gate; a sidewalk box edge
                                // overhanging the flat low half is a recorded paving edge, not this seam.
                                bool mainSeamFaces = sample.roadHit == "Road asphalt" && sample.apronHit == "Turn shoulder asphalt";
                                if (sample.s > TransitionZoneMetres)
                                {
                                    if (mainSeamFaces) report.stepOutsideTransition = Mathf.Max(report.stepOutsideTransition, Mathf.Abs(sample.step));
                                    else report.pavedEdgeMaxStep = Mathf.Max(report.pavedEdgeMaxStep, Mathf.Abs(sample.step));
                                }
                                cornerMainStep = Mathf.Max(cornerMainStep, Mathf.Abs(sample.step));
                            }
                            else
                            {
                                report.sideSeamSamples.Add(sample);
                                bool sideSeamFaces = sample.roadHit == "S03 intersecting side street" && sample.apronHit == "Turn shoulder asphalt";
                                if (!sideSeamFaces) report.pavedEdgeMaxStep = Mathf.Max(report.pavedEdgeMaxStep, Mathf.Abs(sample.step));
                                cornerSideStep = Mathf.Max(cornerSideStep, Mathf.Abs(sample.step));
                            }
                        }
                    }
                }
                manifest.aprons.Add(report);
            }
            manifest.apronCount = manifest.aprons.Count;

            // Three fixed poses, identical before and after.
            Camera camera = game.Player.View;
            Assert.That(camera, Is.Not.Null);
            game.Player.enabled = false;
            foreach ((Vector3 feet, Vector3 look, string view, string file) in new[] {
                (new Vector3(-17.5f, .02f, -10f), new Vector3(-9f, .02f, -10f), "approach", pngApproach),
                (new Vector3(-3f, .02f, -10f), new Vector3(-9f, .02f, -10.8f), "departure", pngDeparture),
                (new Vector3(-16.5f, .02f, -9.8f), new Vector3(-11.8f, 0f, -6.8f), "seam-closeup", pngCloseup) })
            {
                game.Player.ResetPose(feet, 0f);
                camera.fieldOfView = fieldOfView;
                camera.aspect = (float)width / height;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.transform.position = new Vector3(feet.x, eyeHeight, feet.z);
                camera.transform.rotation = Quaternion.LookRotation(look - camera.transform.position, Vector3.up);
                Physics.SyncTransforms();
                yield return null;
                manifest.shots.Add(new CaptureShot {
                    view = view, file = Path.GetFileName(file), fieldOfView = fieldOfView, eyeHeight = eyeHeight,
                    eyePosition = camera.transform.position, lookTarget = look,
                    lighting = "scene default (sun euler(43,-32,0), twilight ambient)"
                });
                WriteCameraFrame(camera, width, height, file);
            }

            // True seam macro: ~1.2 m from the apron/main-road contact edge, low camera, plus a
            // temporary raking light so the 31 mm step (before) or the 3.2% ramp (after) is legible.
            // The wide seam-closeup render stays as environment context only, not seam evidence.
            GameObject macroSun = new GameObject("Macro raking light");
            Light macroLight = macroSun.AddComponent<Light>();
            macroLight.type = LightType.Directional;
            macroLight.intensity = 1.1f;
            macroLight.shadows = LightShadows.Soft;
            macroLight.color = new Color(1f, .96f, .9f);
            macroSun.transform.rotation = Quaternion.Euler(12f, 180f, 0f); // travels toward -z at 12° down: grazes the seam faces
            yield return null;
            // A dedicated camera: the player camera keeps being re-posed by the (disabled) controller's
            // follow logic, so a frozen dedicated camera gives deterministic macro framing.
            GameObject macroCameraGo = new GameObject("Macro camera");
            Camera macroCamera = macroCameraGo.AddComponent<Camera>();
            macroCamera.CopyFrom(camera);
            macroCamera.transform.position = new Vector3(-4.9f, .8f, -5.3f);
            Vector3 macroLook = new Vector3(-4.96f, 0f, -6.6f);
            macroCamera.transform.rotation = Quaternion.LookRotation(macroLook - macroCamera.transform.position, Vector3.up);
            macroCamera.fieldOfView = fieldOfView;
            macroCamera.aspect = (float)width / height;
            macroCamera.enabled = false;
            Physics.SyncTransforms();
            yield return null;
            // Programmatic framing check: both seam endpoints must land inside the macro frame,
            // so the render cannot silently miss the seam it claims to show.
            Vector3 westEnd = macroCamera.WorldToViewportPoint(new Vector3(-5.7f, 0f, -6.6f));
            Vector3 eastEnd = macroCamera.WorldToViewportPoint(new Vector3(-4.22f, 0f, -6.6f));
            foreach (Vector3 endpoint in new[] { westEnd, eastEnd })
            {
                Assert.Greater(endpoint.z, 0f, "A seam endpoint fell behind the macro camera.");
                Assert.That(endpoint.x, Is.InRange(.05f, .95f), "A seam endpoint left the macro frame horizontally.");
                Assert.That(endpoint.y, Is.InRange(.05f, .95f), "A seam endpoint left the macro frame vertically.");
            }
            manifest.shots.Add(new CaptureShot {
                view = "seam-macro", file = Path.GetFileName(pngMacro), fieldOfView = fieldOfView, eyeHeight = .8f,
                eyePosition = macroCamera.transform.position, lookTarget = macroLook,
                lighting = "temporary raking directional euler(12,180,0) intensity 1.1 soft shadows; scene ambient/sun unchanged"
            });
            WriteCameraFrame(macroCamera, width, height, pngMacro);
            Object.Destroy(macroCameraGo);
            Object.Destroy(macroSun);

            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            if (accept)
            {
                Assert.AreEqual(int.Parse(Environment.GetEnvironmentVariable("BCI_APRON_BASELINE_COLLIDERS") ?? "91", CultureInfo.InvariantCulture), manifest.sceneColliderCount, "Seam repair must not change the scene-wide collider count.");
                foreach (ApronReport report in manifest.aprons)
                {
                    Assert.That(report.degenerateTriangles, Is.EqualTo(0), "Zero-area triangles are not allowed on " + report.objectName);
                    Assert.That(report.upFacingTriangles, Is.EqualTo(report.triangleCount), "All apron triangles must face up on " + report.objectName);
                    Assert.That(report.maxSlopeRiseRun, Is.LessThanOrEqualTo(MaxSlopeRiseRun + 1e-6f), "Apron slope exceeds the 3.2% gate on " + report.objectName);
                    Assert.That(report.maxUvError, Is.LessThanOrEqualTo(1e-4f), "Apron UVs must stay world XZ / AsphaltTileSizeMetres on " + report.objectName);
                    Assert.That(report.minY >= MainRoadTopY - .001f && report.maxY <= SideStreetTopY + .001f, "Apron y-range must stay between the road tops on " + report.objectName);
                    Assert.That(report.stepOutsideTransition, Is.LessThanOrEqualTo(.001f), "Exposed seam step beyond the corner transition exceeds 1 mm on " + report.objectName);
                }
            }
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
                Object.Destroy(image);
            }
        }
    }
}
