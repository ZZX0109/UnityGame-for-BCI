using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreetCrossing.Editor
{
    /// <summary>One per-component record captured by the spatial audit.</summary>
    [Serializable]
    public class StreetSpatialAuditRecord
    {
        public string name;
        public string path;
        /// <summary>"renderer" for visible components, "collider" when the object also has a mesh, "proxy" for pure physics bodies.</summary>
        public string role;
        public string componentType;
        public bool isPrefabInstance;
        public string prefabSourcePath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public Vector3 aabbCenter;
        public Vector3 aabbSize;
        public bool isTrigger;
        public bool isEnabled;
    }

    /// <summary>One audit finding; pair findings store the intersection box in the AABB fields.</summary>
    [Serializable]
    public class StreetSpatialAuditFinding
    {
        public string type;
        /// <summary>"error" (red), "warning" (yellow) or "info" (structural, green).</summary>
        public string severity;
        public string target;
        /// <summary>Second member of a pair finding (overlapping proxies), empty otherwise.</summary>
        public string other;
        public string message;
        public Vector3 aabbCenter;
        public Vector3 aabbSize;
        /// <summary>Metric behind the finding: IoU, scale axis, ground gap or overlap volume share.</summary>
        public float value;
        /// <summary>True when Physics.OverlapBox confirmed the pair; AABB-only suspects stay false.</summary>
        public bool physicsConfirmed;

        // P0-§3 round 2, task 10: additive metadata only. JsonUtility leaves these
        // at their type defaults when reading pre-round-2 JSON files, so existing
        // reports stay loadable and no existing field changed meaning.
        /// <summary>Scene the finding was captured in; empty for legacy reports.</summary>
        public string sceneId;
        /// <summary>World rotation of the owning GameObject at audit time.</summary>
        public Quaternion worldRotation;
        /// <summary>World scale (lossyScale) of the owning GameObject at audit time.</summary>
        public Vector3 worldSize;
        /// <summary>Prefab asset path for prefab instances; empty for pure scene objects.</summary>
        public string assetSource;
        /// <summary>Optional repair hint keyed by finding type; empty string means no suggestion.</summary>
        public string suggestedFix;
    }

    /// <summary>Full audit payload as written to the JSON report.</summary>
    [Serializable]
    public class StreetSpatialAuditResult
    {
        public string timestamp;
        public string scene;
        public List<StreetSpatialAuditRecord> records = new List<StreetSpatialAuditRecord>();
        /// <summary>Paths of pure physics bodies (colliders without any visible mesh), listed separately.</summary>
        public List<string> proxies = new List<string>();
        public List<StreetSpatialAuditFinding> findings = new List<StreetSpatialAuditFinding>();
    }

    /// <summary>Static store the audit window reads for its scene-view overlay.</summary>
    public static class StreetSpatialAuditCache
    {
        public static StreetSpatialAuditResult LastResult;
    }

    /// <summary>
    /// Read-only scene geometry audit (rectification item 2.1). Collects every
    /// Renderer and Collider of the active scene and reports collider/mesh
    /// mismatch, extreme or non-uniform scales, floating meshes, walking-corridor
    /// intrusions and overlapping collider proxies. The audit never mutates any
    /// scene object; results go to BuildOutput/spatial-audit and into
    /// StreetSpatialAuditCache for the gizmo window.
    /// </summary>
    public static class StreetSpatialAudit
    {
        private const string LogPrefix = "[SpatialAudit] ";
        private const string OutputRelativeFolder = "BuildOutput/spatial-audit";

        // Detection thresholds (rectification item 2.1).
        private const float ColliderMeshIouThreshold = 0.5f;  // check 1: collider/mesh AABB IoU below this = mismatch
        private const float ExtremeScaleMax = 10f;            // check 2: any local axis above this
        private const float ExtremeScaleMin = 0.01f;          // check 2: any local axis below this
        private const float NonUniformRatioThreshold = 3f;    // check 3: max/min local axis ratio
        private const float GroundGapThreshold = 0.05f;       // check 4: metres between AABB bottom and y=0
        private const float CorridorInnerZ = StreetLayout.RoadHalfWidth; // check 5: road/sidewalk boundary at |z| = 6.6
        private const float CorridorHalfX = 40f;              // check 5: corridor strip extent along x
        private const float OverlapShareThreshold = 0.8f;     // check 6: intersection volume / smaller volume

        // check 5: ground/roadway bodies never count as corridor intrusions.
        private static readonly string[] CorridorExemptNames = { "kerb", "footway", "sidewalk", "paving", "road", "crossing" };
        // check 5: kerbside fixtures deliberately placed just past the kerb line (bus shelter and its
        // signage parts, gate, hydrant, security light, kerb bollards). They still intrude on paper,
        // so instead of skipping them the finding is downgraded to "info": the JSON keeps the
        // evidence while the error count stays meaningful for genuinely misplaced props.
        // Repositioning, not exemption, fixes real cases.
        private static readonly string[] CorridorFixtureNames = { "shelter", "bus seat", "bus stop", "bus sign", "maintenance", "security light", "hydrant", "bollard", "information panel", "timetable" };
        // check 4: deliberate overhangs are exempt from the ground-gap check.
        private static readonly string[] OverhangNames = { "signal", "arm", "roof", "shelter_top", "awning" };
        // check 6: connector parts whose overlaps are structural, not erroneous.
        private static readonly string[] StructuralNames = { "bolt", "gusset", "bracket", "pole", "arm" };

        /// <summary>P0-§3: dynamic actors move at runtime, so their AABBs are not static-placement errors.</summary>
        private static readonly string[] DynamicActorNames = { "street vehicle", "reusable vehicle", "animated pedestrian" };

        // P0-§3 round 2 (checks a-h, k, l): category tables and thresholds.
        private const float TippedUprightRatio = 0.7f;        // check b: size.y < 0.7 * max(size.x, size.z) = tipped
        private const float ColliderOffsetDistance = 0.35f;   // check f: merged renderer/collider AABB centre drift
        private const float ColliderSizeRatio = 1.8f;         // check f: merged AABB max-dimension ratio
        private const float DuplicateColliderIou = 0.7f;      // check g: IoU above this inside one hierarchy = duplicate
        private const float DoorstepSetback = 0.40f;          // check h: doorstep clearance band half-width
        private const float DoorstepClearance = 0.325f;       // check h: half of the 0.65 m deep walking channel
        private const float CorridorBlockerHalfX = 38f;       // check h: |x| < 38 matches the tyre/prop extent (StreetWorld.cs:723)
        private const float MissionSampleStep = 0.25f;        // check k: route sampling step in metres
        private const float SignalEyeHeight = 1.65f;          // check l: waiting pedestrian eye height
        private const float SignalKerbOffset = 0.45f;         // check l: waiting point behind the kerb (StreetWorld.cs:380-382)
        private const int ColliderGroupLevels = 2;            // checks f/g: "same hierarchy" = ancestor up to 2 levels

        /// <summary>
        /// check a (render_bounds_abnormal): plausible world-AABB size bands per static-prop
        /// category, matched by lowercase name substring (first hit in RenderCategoryOrder wins).
        /// Bands are derived from the actual builders and kept generous so only genuinely
        /// mis-scaled or misplaced props trigger:
        ///   building: StreetWorld.cs:309 (heights 6.8/7.7/8.6) and StreetWorld.cs:660-684
        ///             (envelope 10.3 x height x 4; corner block up to 10.2 tall, StreetWorld.cs:730-736)
        ///   tree:     trunk/stake 1.30 tall (StreetFurnitureBuilder.cs:75) with procedural crown
        ///             from StreetAssetLibrary.CreateRefinedTree (StreetWorld.cs:55-56)
        ///   lamp:     StreetWorld.cs:779-815 (pole d0.18, blade 0.55, diffuser at y 4.21)
        ///   shelter:  corridor fixture "shelter_top" overhang family (StreetSpatialAudit OverhangNames)
        ///   bench:    StreetWorld.cs:694-702 (2.25 x 1.0 x 0.65)
        ///   bin:      StreetWorld.cs:713 (litter bin 0.62 x 1.04 x 0.62)
        ///   hydrant:  StreetWorld.cs:706-709 (0.30 x 0.62 x 0.30)
        ///   box:      StreetWorld.cs:717-718 (utility cabinets 0.56-0.72 wide, 0.86-1.12 tall)
        ///   sign:     StreetWorld.cs:784-815 (street blade 0.55 x 0.14 x 0.025, mounted at y+3.02)
        /// Vector3[0] = min size, Vector3[1] = max size; axes are world AABB x/y/z.
        /// </summary>
        private static readonly Dictionary<string, Vector3[]> RenderSizeBands = new Dictionary<string, Vector3[]>
        {
            { "building", new[] { new Vector3(6f, 5f, 2.5f), new Vector3(16f, 12f, 8f) } },
            { "tree", new[] { new Vector3(0.4f, 1f, 0.4f), new Vector3(6f, 9f, 6f) } },
            { "lamp", new[] { new Vector3(0.1f, 2f, 0.1f), new Vector3(1.4f, 5.4f, 1.4f) } },
            { "shelter", new[] { new Vector3(1.5f, 1.5f, 0.6f), new Vector3(8f, 4f, 4f) } },
            { "bench", new[] { new Vector3(1.6f, 0.3f, 0.4f), new Vector3(3.2f, 1.6f, 1.4f) } },
            { "bin", new[] { new Vector3(0.3f, 0.6f, 0.3f), new Vector3(1.2f, 1.8f, 1.2f) } },
            { "hydrant", new[] { new Vector3(0.15f, 0.35f, 0.15f), new Vector3(0.8f, 1.2f, 0.8f) } },
            { "box", new[] { new Vector3(0.25f, 0.5f, 0.2f), new Vector3(1.6f, 1.6f, 1.2f) } },
            { "sign", new[] { new Vector3(0.2f, 0.08f, 0.01f), new Vector3(1.2f, 0.6f, 0.4f) } }
        };

        /// <summary>Category match priority; more specific fixtures must win over "sign"/"box" substrings.</summary>
        private static readonly string[] RenderCategoryOrder = { "shelter", "building", "tree", "lamp", "bench", "bin", "hydrant", "box", "sign" };

        /// <summary>Small ground/anchor accessories whose part AABBs are legitimately tiny or flat.</summary>
        private static readonly string[] RenderPartExemptNames = { "grate", "stake", "strap", "mulch", "pit" };

        /// <summary>check b: categories whose props are vertical by design.</summary>
        private static readonly string[] TippedCategories = { "lamp", "hydrant", "box", "post" };

        /// <summary>check c/d: categories whose parts must rest on the ground plane.</summary>
        private static readonly string[] GroundLandingCategories = { "building", "tree", "lamp", "bench", "bin", "hydrant", "box", "shelter" };

        /// <summary>check h: categories whose props must not crowd the doorstep corridors.</summary>
        private static readonly string[] CorridorBlockerCategories = { "building", "tree", "lamp", "bench" };

        /// <summary>check k: colliders that may legitimately block a mission route (buildings and seats).</summary>
        private static readonly string[] MissionBlockerNames = { "building", "bench", "seat" };

        /// <summary>
        /// check l: the signal's own assembly parts are not occluders of its heads.
        /// "Pedestrian housing"/"Vehicle housing" (TrafficSignalView.cs:48,97) are the
        /// heads under test; everything else in this list is the pole/bracket self-exemption.
        /// </summary>
        private static readonly string[] SignalSelfNames = { "signal", "pole", "arm", "gusset", "bracket", "housing", "repeater", "bezel", "lens", "visor", "backplate", "screen", "collar", "cover", "channel" };

        [MenuItem("Street Crossing/Spatial Audit/Run Full Audit")]
        public static void RunFullAudit()
        {
            Scene scene = SceneManager.GetActiveScene();
            StreetSpatialAuditResult result = new StreetSpatialAuditResult
            {
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                scene = string.IsNullOrEmpty(scene.name) ? "(unsaved scene)" : scene.name
            };

            Renderer[] renderers = FilterScene(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None), scene);
            Collider[] colliders = FilterScene(UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None), scene);
            Transform[] transforms = FilterScene(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None), scene);

            // Corridor strip |z| in [RoadHalfWidth, outer edge]; the outer edge is the
            // larger of the sidewalk band (StreetLayout.SidewalkZ of the road edge,
            // 9.9) and the mission corridor edge (StreetMissionGeometry.CorridorZ,
            // 10.2 positive / 9.95 negative).
            float sidewalkEdge = Mathf.Abs(StreetLayout.SidewalkZ(StreetLayout.RoadHalfWidth));
            float outerPos = Mathf.Max(sidewalkEdge, Mathf.Abs(StreetMissionGeometry.CorridorZ(1)));
            float outerNeg = Mathf.Max(sidewalkEdge, Mathf.Abs(StreetMissionGeometry.CorridorZ(-1)));

            foreach (Renderer r in renderers)
            {
                StreetSpatialAuditRecord record = BuildRecord(r.gameObject, "renderer", r.GetType().Name, r.bounds);
                record.isEnabled = r.enabled;
                result.records.Add(record);

                // check 4: ground gap between the renderer world AABB bottom and y=0;
                // deliberate overhangs (signals, arms, roofs, ...) are exempt.
                if (!NameContains(r.gameObject.name, OverhangNames) && r.bounds.size.y > 0f)
                {
                    float gap = r.bounds.min.y;
                    if (Mathf.Abs(gap) > GroundGapThreshold)
                    {
                        StreetSpatialAuditFinding f = Finding("ground_gap", "warning", record.path,
                            $"world AABB bottom at y={gap:0.000}; |gap| exceeds {GroundGapThreshold:0.00} m");
                        f.value = gap;
                        f.aabbCenter = r.bounds.center;
                        f.aabbSize = r.bounds.size;
                        result.findings.Add(f);
                    }
                }
            }

            foreach (Collider c in colliders)
            {
                Renderer mesh = c.GetComponent<MeshRenderer>();
                if (mesh == null) mesh = c.GetComponent<SkinnedMeshRenderer>();
                bool hasMesh = mesh != null;
                StreetSpatialAuditRecord record = BuildRecord(c.gameObject, hasMesh ? "collider" : "proxy", c.GetType().Name, c.bounds);
                record.isTrigger = c.isTrigger;
                record.isEnabled = c.enabled;
                result.records.Add(record);
                if (!hasMesh) result.proxies.Add(record.path); // pure physics body: listed separately, never a check-1 deviation

                // check 1: collider vs. visible mesh AABB deviation on the same object.
                if (hasMesh)
                {
                    float iou = BoundsIou(c.bounds, mesh.bounds);
                    if (iou < ColliderMeshIouThreshold)
                    {
                        StreetSpatialAuditFinding f = Finding("collider_mesh_mismatch", "warning", record.path,
                            $"collider/mesh AABB IoU {iou:0.00} below {ColliderMeshIouThreshold:0.00}; collider {c.bounds.size:0.00} vs mesh {mesh.bounds.size:0.00}");
                        f.value = iou;
                        f.aabbCenter = c.bounds.center;
                        f.aabbSize = c.bounds.size;
                        result.findings.Add(f);
                    }
                }
            }

            // checks 2 and 3: scale anomalies over every transform of the active scene.
            foreach (Transform t in transforms)
            {
                Vector3 s = t.localScale;
                string transformPath = PathOf(t);
                if (s.x < 0f || s.y < 0f || s.z < 0f)
                    result.findings.Add(Finding("negative_scale", "error", transformPath, $"negative localScale {s}"));

                float ax = Mathf.Abs(s.x), ay = Mathf.Abs(s.y), az = Mathf.Abs(s.z);
                if (ax > ExtremeScaleMax || ay > ExtremeScaleMax || az > ExtremeScaleMax
                    || ax < ExtremeScaleMin || ay < ExtremeScaleMin || az < ExtremeScaleMin)
                {
                    StreetSpatialAuditFinding f = Finding("extreme_scale", "warning", transformPath,
                        $"localScale {s} outside [{ExtremeScaleMin:0.###}, {ExtremeScaleMax:0}] on at least one axis");
                    f.value = Mathf.Max(ax, Mathf.Max(ay, az));
                    result.findings.Add(f);
                }

                float minAxis = Mathf.Min(ax, Mathf.Min(ay, az));
                float maxAxis = Mathf.Max(ax, Mathf.Max(ay, az));
                if (minAxis > 1e-6f && maxAxis / minAxis > NonUniformRatioThreshold)
                {
                    StreetSpatialAuditFinding f = Finding("nonuniform_scale", "warning", transformPath,
                        $"non-uniform localScale {s}: max/min axis ratio {(maxAxis / minAxis):0.0} exceeds {NonUniformRatioThreshold:0}");
                    f.value = maxAxis / minAxis;
                    result.findings.Add(f);
                }
            }

            // check 5: walking-corridor intrusion for every captured AABB (renderer + collider).
            foreach (StreetSpatialAuditRecord record in result.records)
            {
                if (NameContains(record.name, CorridorExemptNames)) continue;
                Bounds bounds = new Bounds(record.aabbCenter, record.aabbSize);
                if (IntersectsCorridor(bounds, outerPos, outerNeg, out string side))
                {
                    bool established = NameContains(record.name, CorridorFixtureNames);
                    StreetSpatialAuditFinding f = Finding("corridor_intrusion", established ? "info" : "error", record.path,
                        established
                            ? $"[{record.role}] established kerbside fixture inside the corridor strip on the {side} z side (reviewed anchor, evidence only)"
                            : $"[{record.role}] AABB intrudes into the walking corridor on the {side} z side (strip |z| {CorridorInnerZ:0.0}-{outerPos:0.0}+, |x| < {CorridorHalfX:0})");
                    f.aabbCenter = record.aabbCenter;
                    f.aabbSize = record.aabbSize;
                    result.findings.Add(f);
                }
            }

            // check 6: overlapping collider proxies - broad-phase AABB sweep first (O(n^2)
            // is fine for the few hundred scene objects), then Physics.OverlapBox recheck.
            List<BoxCollider> boxes = new List<BoxCollider>();
            foreach (Collider c in colliders)
                if (c is BoxCollider && !c.isTrigger && c.enabled) boxes.Add((BoxCollider)c);

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    BoxCollider a = boxes[i], b = boxes[j];
                    Vector3 minCorner = Vector3.Max(a.bounds.min, b.bounds.min);
                    Vector3 maxCorner = Vector3.Min(a.bounds.max, b.bounds.max);
                    Vector3 d = maxCorner - minCorner;
                    if (d.x <= 0f || d.y <= 0f || d.z <= 0f) continue;
                    float smaller = Mathf.Min(Volume(a.bounds), Volume(b.bounds));
                    if (smaller <= 1e-8f) continue;
                    float share = d.x * d.y * d.z / smaller;
                    if (share <= OverlapShareThreshold) continue;
                    if (a.gameObject.name == b.gameObject.name) continue; // spec: different names only

                    bool structural = IsStructural(a, b);
                    bool confirmed = PhysicsConfirms(a, b, (minCorner + maxCorner) * 0.5f, d);
                    string message;
                    if (structural) message = $"structural overlap with '{b.gameObject.name}': volume share {share:0.00} of the smaller box";
                    else if (confirmed) message = $"overlapping collider proxy with '{b.gameObject.name}': volume share {share:0.00} of the smaller box";
                    else message = $"suspected overlap with '{b.gameObject.name}' (share {share:0.00}); physics recheck did not confirm";
                    string severity = structural ? "info" : confirmed ? "error" : "warning";
                    StreetSpatialAuditFinding f = Finding(structural ? "structural_overlap" : "collider_overlap", severity, PathOf(a.transform), message);
                    f.other = PathOf(b.transform);
                    f.value = share;
                    f.aabbCenter = (minCorner + maxCorner) * 0.5f;
                    f.aabbSize = d;
                    f.physicsConfirmed = confirmed;
                    result.findings.Add(f);
                }
            }

            // P0-§3 checks i/j: static objects intruding into the carriageway or
            // blocking a route-derived turning envelope. This is an incremental
            // loop; the existing check 5 corridor scan above is untouched.
            StreetGameManager[] auditManagers = FilterScene(UnityEngine.Object.FindObjectsByType<StreetGameManager>(FindObjectsSortMode.None), scene);
            foreach (StreetGameManager manager in auditManagers)
            {
                if (manager == null || manager.scenario == null) continue;
                StreetScenarioDefinition auditScenario = manager.scenario;
                List<Vector2[]> aprons = StreetJunctionGeometry.TurnAprons(auditScenario);
                foreach (Renderer r in renderers)
                {
                    if (r == null) continue;
                    string name = r.gameObject.name;
                    if (NameContains(name, DynamicActorNames) || NameContains(name, CorridorExemptNames)) continue;
                    Bounds box = r.bounds;

                    // check i: carriageway_intrusion
                    if (SpatialAuditPredicates.IntrudesIntoCarriageway(box, auditScenario.roadHalfLength, StreetLayout.RoadHalfWidth))
                    {
                        StreetSpatialAuditFinding f = Finding("carriageway_intrusion", "warning", PathOf(r.transform),
                            $"static renderer AABB intrudes into the carriageway band |z| < {StreetLayout.RoadHalfWidth:0.###} within |x| < {auditScenario.roadHalfLength:0.###}");
                        f.aabbCenter = box.center;
                        f.aabbSize = box.size;
                        result.findings.Add(f);
                    }

                    // check j: turn_envelope_block (no-op when scenario.turningTraffic is false: aprons empty)
                    if (aprons.Count > 0 && !NameContains(name, OverhangNames) && !NameContains(name, StructuralNames))
                    {
                        bool blocked = false;
                        foreach (Vector2[] hull in aprons)
                        {
                            if (SpatialAuditPredicates.OverlapsPlanar(box, hull)) { blocked = true; break; }
                        }
                        if (blocked)
                        {
                            StreetSpatialAuditFinding f = Finding("turn_envelope_block", "error", PathOf(r.transform),
                                $"static renderer AABB blocks a route-derived turning envelope (vehicle half length {StreetJunctionGeometry.VehicleHalfLength:0.###} m + {StreetJunctionGeometry.Clearance:0.###} m clearance sweep)");
                            f.aabbCenter = box.center;
                            f.aabbSize = box.size;
                            result.findings.Add(f);
                        }
                    }
                }
                break; // one scenario is sufficient; extra managers would duplicate findings
            }

            // ---------------------------------------------------------------
            // P0-§3 round 2: scene geometry checks a-h, k and l. Everything
            // below is additive; the checks above keep their original shape.
            // ---------------------------------------------------------------

            // check a: render_bounds_abnormal - static render AABB size outside its category band.
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                string name = r.gameObject.name;
                if (NameContains(name, DynamicActorNames) || NameContains(name, RenderPartExemptNames)) continue;
                string category = MatchRenderCategory(name);
                if (category == null) continue;
                Vector3[] band = RenderSizeBands[category];
                Vector3 size = r.bounds.size;
                if (!SpatialAuditPredicates.IsWithinSizeBand(size, band[0], band[1], out float bandRatio))
                {
                    StreetSpatialAuditFinding f = Finding("render_bounds_abnormal", "warning", PathOf(r.transform),
                        $"[{category}] render AABB size {size:0.00} outside the category band [{band[0]:0.00} .. {band[1]:0.00}]");
                    f.value = bandRatio;
                    f.aabbCenter = r.bounds.center;
                    f.aabbSize = size;
                    FillMetadata(f, r.gameObject, result.scene);
                    result.findings.Add(f);
                }
            }

            // check b: tipped_over - vertical-dominant props lying on their side.
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                string name = r.gameObject.name;
                if (NameContains(name, DynamicActorNames)) continue;
                if (!NameContains(name, TippedCategories)) continue;
                if (NameContains(name, RenderPartExemptNames)) continue;
                Vector3 size = r.bounds.size;
                if (SpatialAuditPredicates.IsTippedOver(size, TippedUprightRatio))
                {
                    float ratio = size.y / Mathf.Max(Mathf.Max(size.x, size.z), 1e-4f);
                    StreetSpatialAuditFinding f = Finding("tipped_over", "warning", PathOf(r.transform),
                        $"vertical prop is lying down: height {size.y:0.00} below {TippedUprightRatio:0.0} x footprint {Mathf.Max(size.x, size.z):0.00}");
                    f.value = ratio;
                    f.aabbCenter = r.bounds.center;
                    f.aabbSize = size;
                    FillMetadata(f, r.gameObject, result.scene);
                    result.findings.Add(f);
                }
            }

            // checks c/d: floating / sunk - ground-landing props whose AABB bottom
            // drifted off y=0 by more than GroundGapThreshold. Overhangs exempt
            // (same rule as check 4). These stay "info" because check 4 already
            // carries the warning for the generic case.
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                string name = r.gameObject.name;
                if (NameContains(name, DynamicActorNames) || NameContains(name, OverhangNames)) continue;
                if (!NameContains(name, GroundLandingCategories) || NameContains(name, RenderPartExemptNames)) continue;
                float gap = r.bounds.min.y;
                if (SpatialAuditPredicates.IsFloating(gap, GroundGapThreshold))
                {
                    StreetSpatialAuditFinding f = Finding("floating", "info", PathOf(r.transform),
                        $"ground-landing prop floats {gap:0.000} m above y=0 (threshold {GroundGapThreshold:0.00} m)");
                    f.value = gap;
                    f.aabbCenter = r.bounds.center;
                    f.aabbSize = r.bounds.size;
                    FillMetadata(f, r.gameObject, result.scene);
                    result.findings.Add(f);
                }
                else if (SpatialAuditPredicates.IsSunk(gap, GroundGapThreshold))
                {
                    StreetSpatialAuditFinding f = Finding("sunk", "info", PathOf(r.transform),
                        $"ground-landing prop sinks {(-gap):0.000} m below y=0 (threshold {GroundGapThreshold:0.00} m)");
                    f.value = gap;
                    f.aabbCenter = r.bounds.center;
                    f.aabbSize = r.bounds.size;
                    FillMetadata(f, r.gameObject, result.scene);
                    result.findings.Add(f);
                }
            }

            // check e: category_overlap - two static props of the same category
            // intersecting. Structural/overhang pairs (beams, columns, embedded
            // window frames, canopy overhangs) are whitelisted outright; volume
            // share of the smaller box at/above OverlapShareThreshold warns,
            // anything smaller stays info evidence.
            List<Renderer> staticRenderers = new List<Renderer>();
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (NameContains(r.gameObject.name, DynamicActorNames)) continue;
                if (NameContains(r.gameObject.name, StructuralNames) || NameContains(r.gameObject.name, OverhangNames)) continue;
                if (NameContains(r.gameObject.name, RenderPartExemptNames)) continue;
                staticRenderers.Add(r);
            }
            for (int i = 0; i < staticRenderers.Count; i++)
            {
                string categoryA = MatchRenderCategory(staticRenderers[i].gameObject.name);
                if (categoryA == null) continue;
                for (int j = i + 1; j < staticRenderers.Count; j++)
                {
                    string categoryB = MatchRenderCategory(staticRenderers[j].gameObject.name);
                    if (categoryB != categoryA) continue;
                    Bounds a = staticRenderers[i].bounds, b = staticRenderers[j].bounds;
                    float shared = SpatialAuditPredicates.SharedVolume(a, b);
                    if (shared <= 0f) continue;
                    float smaller = Mathf.Min(SpatialAuditPredicates.VolumeOf(a), SpatialAuditPredicates.VolumeOf(b));
                    if (smaller <= 1e-8f) continue;
                    float share = shared / smaller;
                    StreetSpatialAuditFinding f = Finding("category_overlap", share >= OverlapShareThreshold ? "warning" : "info", PathOf(staticRenderers[i].transform),
                        $"two '{categoryA}' props intersect; volume share {share:0.00} of '{staticRenderers[j].gameObject.name}'");
                    f.other = PathOf(staticRenderers[j].transform);
                    f.value = share;
                    f.aabbCenter = (a.center + b.center) * 0.5f;
                    f.aabbSize = Vector3.Min(a.max, b.max) - Vector3.Max(a.min, b.min);
                    FillMetadata(f, staticRenderers[i].gameObject, result.scene);
                    result.findings.Add(f);
                }
            }

            // checks f/g: hierarchy grouping - "same hierarchy" means the shared
            // ancestor at most 2 levels above each component (ColliderGroupLevels).
            Dictionary<Transform, List<Collider>> hierarchyGroups = new Dictionary<Transform, List<Collider>>();
            foreach (Collider c in colliders)
            {
                if (c == null || NameContains(c.gameObject.name, DynamicActorNames)) continue;
                Transform ancestor = c.transform;
                for (int level = 0; level < ColliderGroupLevels && ancestor.parent != null; level++) ancestor = ancestor.parent;
                if (!hierarchyGroups.TryGetValue(ancestor, out List<Collider> group)) { group = new List<Collider>(); hierarchyGroups[ancestor] = group; }
                group.Add(c);
            }

            // check f: collider_offset - merged renderer AABB vs merged collider
            // AABB of the same hierarchy drifting apart or sizing apart.
            foreach (KeyValuePair<Transform, List<Collider>> pair in hierarchyGroups)
            {
                if (pair.Value.Count == 0) continue;
                Bounds colliderBounds = pair.Value[0].bounds;
                for (int i = 1; i < pair.Value.Count; i++) colliderBounds.Encapsulate(pair.Value[i].bounds);

                Bounds rendererBounds = new Bounds();
                bool hasRenderer = false;
                foreach (Renderer r in renderers)
                {
                    if (r == null || !r.transform.IsChildOf(pair.Key)) continue;
                    if (hasRenderer) rendererBounds.Encapsulate(r.bounds);
                    else { rendererBounds = r.bounds; hasRenderer = true; }
                }
                if (!hasRenderer) continue;

                if (SpatialAuditPredicates.ColliderOffsetExceeds(rendererBounds.center, rendererBounds.size,
                    colliderBounds.center, colliderBounds.size, ColliderOffsetDistance, ColliderSizeRatio,
                    out float distance, out float ratio))
                {
                    StreetSpatialAuditFinding f = Finding("collider_offset", "warning", PathOf(pair.Key),
                        $"hierarchy visuals and physics diverge: centre drift {distance:0.00} m (limit {ColliderOffsetDistance:0.00}), size ratio {ratio:0.00} (limit {ColliderSizeRatio:0.0})");
                    f.value = distance;
                    f.aabbCenter = colliderBounds.center;
                    f.aabbSize = colliderBounds.size;
                    FillMetadata(f, pair.Key.gameObject, result.scene);
                    result.findings.Add(f);
                }
            }

            // check g: duplicate_collider - two colliders of one hierarchy covering
            // nearly the same volume. Pairs named blocker/bounds are deliberate
            // facilitator boxes and get recorded as info instead of a warning.
            foreach (KeyValuePair<Transform, List<Collider>> pair in hierarchyGroups)
            {
                List<Collider> group = pair.Value;
                if (group.Count < 2) continue;
                for (int i = 0; i < group.Count; i++)
                {
                    for (int j = i + 1; j < group.Count; j++)
                    {
                        Collider a = group[i], b = group[j];
                        if (a.gameObject.name == b.gameObject.name) continue;
                        float iou = SpatialAuditPredicates.Iou(a.bounds, b.bounds);
                        if (iou <= DuplicateColliderIou) continue;
                        bool deliberate = NameContains(a.gameObject.name, DuplicateColliderNames) || NameContains(b.gameObject.name, DuplicateColliderNames);
                        StreetSpatialAuditFinding f = Finding("duplicate_collider", deliberate ? "info" : "warning", PathOf(a.transform),
                            deliberate
                                ? $"deliberate '{a.gameObject.name}'/'{b.gameObject.name}' collider pair duplicates volume (IoU {iou:0.00}, reviewed, evidence only)"
                                : $"'{a.gameObject.name}' and '{b.gameObject.name}' duplicate the same volume (IoU {iou:0.00} above {DuplicateColliderIou:0.0})");
                        f.other = PathOf(b.transform);
                        f.value = iou;
                        f.aabbCenter = (Vector3.Min(a.bounds.max, b.bounds.max) + Vector3.Max(a.bounds.min, b.bounds.min)) * 0.5f;
                        f.aabbSize = Vector3.Min(a.bounds.max, b.bounds.max) - Vector3.Max(a.bounds.min, b.bounds.min);
                        FillMetadata(f, a.gameObject, result.scene);
                        result.findings.Add(f);
                    }
                }
            }

            // checks k/l need the scenario geometry; one manager is enough.
            StreetGameManager geometryManager = null;
            foreach (StreetGameManager candidate in auditManagers)
            {
                if (candidate != null && candidate.scenario != null) { geometryManager = candidate; break; }
            }
            if (geometryManager != null)
            {
                StreetScenarioDefinition geometryScenario = geometryManager.scenario;

                // check h: corridor_blocker - building/tree/lamp/bench render AABBs
                // crowding the doorstep corridor centred on StreetMissionGeometry.CorridorZ.
                float halfBand = DoorstepSetback + DoorstepClearance;
                foreach (int side in new[] { 1, -1 })
                {
                    float corridorZ = StreetMissionGeometry.CorridorZ(side);
                    foreach (Renderer r in renderers)
                    {
                        if (r == null) continue;
                        string name = r.gameObject.name;
                        if (NameContains(name, DynamicActorNames) || NameContains(name, RenderPartExemptNames)) continue;
                        if (!NameContains(name, CorridorBlockerCategories)) continue;
                        if (SpatialAuditPredicates.BlocksDoorstepCorridor(r.bounds, corridorZ, halfBand, -CorridorBlockerHalfX, CorridorBlockerHalfX, out float depth))
                        {
                            StreetSpatialAuditFinding f = Finding("corridor_blocker", "error", PathOf(r.transform),
                                $"[{name}] render AABB intrudes {depth:0.00} m into the doorstep corridor band z={corridorZ:0.00}±{halfBand:0.00}, |x| < {CorridorBlockerHalfX:0}");
                            f.value = depth;
                            f.aabbCenter = r.bounds.center;
                            f.aabbSize = r.bounds.size;
                            FillMetadata(f, r.gameObject, result.scene);
                            result.findings.Add(f);
                        }
                    }
                }

                // check k: mission_path_block - sample every catalog mission route
                // (spawn -> crosswalk approach -> arrival -> doorstep) at 0.25 m and
                // flag samples that land inside a building/seat collider.
                List<StreetMissionDefinition> missions = new List<StreetMissionDefinition>(StreetMissionCatalog.ForDistrict(geometryScenario.scenarioId));
                List<Collider> pathBlockers = new List<Collider>();
                foreach (Collider c in colliders)
                {
                    if (c != null && !NameContains(c.gameObject.name, DynamicActorNames) && NameContains(c.gameObject.name, MissionBlockerNames))
                        pathBlockers.Add(c);
                }
                foreach (StreetMissionDefinition mission in missions)
                {
                    int segments = mission.roundTrip ? 2 : 1;
                    for (int segment = 0; segment < segments; segment++)
                    {
                        int forward = StreetMissionGeometry.ForwardSign(mission, segment);
                        Vector3 spawn = StreetMissionGeometry.Spawn(mission, segment);
                        Vector3 goal = StreetMissionGeometry.Doorstep(StreetMissionGeometry.GoalKind(mission, segment));
                        List<Vector3> route = new List<Vector3> { spawn };
                        route.AddRange(StreetMissionGeometry.Approach(spawn, forward, geometryScenario.roadHalfWidth));
                        route.AddRange(StreetMissionGeometry.Arrival(goal, forward, geometryScenario.roadHalfWidth));
                        route.Add(goal);

                        Dictionary<Collider, Vector3> blocked = new Dictionary<Collider, Vector3>();
                        for (int p = 0; p < route.Count - 1 && blocked.Count < pathBlockers.Count; p++)
                        {
                            Vector3 from = route[p], to = route[p + 1];
                            float length = Vector3.Distance(from, to);
                            int steps = Mathf.CeilToInt(length / MissionSampleStep);
                            for (int s = 0; s <= steps; s++)
                            {
                                Vector3 sample = length <= 1e-6f ? from : Vector3.Lerp(from, to, (float)s / steps);
                                foreach (Collider blocker in pathBlockers)
                                {
                                    if (blocked.ContainsKey(blocker)) continue;
                                    if (SpatialAuditPredicates.PathPointBlocked(blocker.bounds, new Vector2(sample.x, sample.z)))
                                        blocked[blocker] = sample;
                                }
                            }
                        }
                        foreach (KeyValuePair<Collider, Vector3> hit in blocked)
                        {
                            StreetSpatialAuditFinding f = Finding("mission_path_block", "error", $"{mission.missionId} segment {segment}",
                                $"route sample at ({hit.Value.x:0.00}, {hit.Value.z:0.00}) lands inside collider '{hit.Key.gameObject.name}'");
                            f.other = PathOf(hit.Key.transform);
                            f.aabbCenter = hit.Key.bounds.center;
                            f.aabbSize = hit.Key.bounds.size;
                            FillMetadata(f, hit.Key.gameObject, result.scene);
                            result.findings.Add(f);
                        }
                    }
                }

                // check l: signal_occluded - sight lines from the crosswalk waiting
                // points to each signal head (housing) blocked by a decoration part.
                // The signal's own pole/bracket/head parts are exempt (SignalSelfNames).
                List<Renderer> heads = new List<Renderer>();
                foreach (Renderer r in renderers)
                    if (r != null && r.gameObject.name.ToLowerInvariant().Contains("housing")) heads.Add(r);
                List<Renderer> occluders = new List<Renderer>();
                foreach (Renderer r in renderers)
                {
                    if (r == null || heads.Contains(r)) continue;
                    if (NameContains(r.gameObject.name, DynamicActorNames)) continue;
                    if (NameContains(r.gameObject.name, SignalSelfNames)) continue;
                    occluders.Add(r);
                }
                float width = geometryScenario.roadHalfWidth;
                foreach (Renderer head in heads)
                {
                    foreach (float waitZ in new[] { width + SignalKerbOffset, -(width + SignalKerbOffset) })
                    {
                        foreach (float waitX in new[] { geometryScenario.crosswalkHalfWidth, -geometryScenario.crosswalkHalfWidth })
                        {
                            Vector3 eye = new Vector3(waitX, SignalEyeHeight, waitZ);
                            foreach (Renderer occluder in occluders)
                            {
                                if (SpatialAuditPredicates.SegmentIntersectsAabb(eye, head.bounds.center, occluder.bounds))
                                {
                                    StreetSpatialAuditFinding f = Finding("signal_occluded", "warning", PathOf(head.transform),
                                        $"signal head sight line from waiting point ({waitX:0.00}, {SignalEyeHeight:0.00}, {waitZ:0.00}) is blocked by '{occluder.gameObject.name}'");
                                    f.other = PathOf(occluder.transform);
                                    f.aabbCenter = head.bounds.center;
                                    f.aabbSize = head.bounds.size;
                                    FillMetadata(f, occluder.gameObject, result.scene);
                                    result.findings.Add(f);
                                    break; // one occluder per sight line is enough evidence
                                }
                            }
                        }
                    }
                }
            }

            // dataPath is <repo>/Assets; the "/../../" specified by rectification
            // item 2.1 intentionally resolves above the Unity project to the
            // workspace-root BuildOutput folder shared by all deliverables.
            string folder = Path.GetFullPath(Application.dataPath + "/../../" + OutputRelativeFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "audit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(result, true));

            StreetSpatialAuditCache.LastResult = result;

            int errors = 0, warnings = 0, infos = 0;
            foreach (StreetSpatialAuditFinding f in result.findings)
            {
                if (f.severity == "error") errors++;
                else if (f.severity == "warning") warnings++;
                else infos++;
            }
            Debug.Log($"{LogPrefix}scene '{result.scene}': {result.records.Count} records, {result.proxies.Count} collider proxies, "
                + $"{result.findings.Count} findings (errors {errors}, warnings {warnings}, structural {infos}). Report: {path}");
        }

        /// <summary>Builds one record with hierarchy path, prefab info, transform data and world AABB.</summary>
        private static StreetSpatialAuditRecord BuildRecord(GameObject go, string role, string componentType, Bounds aabb)
        {
            (bool isInstance, string source) prefab = PrefabInfo(go);
            Transform t = go.transform;
            return new StreetSpatialAuditRecord
            {
                name = go.name,
                path = PathOf(t),
                role = role,
                componentType = componentType,
                isPrefabInstance = prefab.isInstance,
                prefabSourcePath = prefab.source,
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                localScale = t.localScale,
                aabbCenter = aabb.center,
                aabbSize = aabb.size
            };
        }

        private static (bool isInstance, string source) PrefabInfo(GameObject go)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null ? (true, AssetDatabase.GetAssetPath(source)) : (false, string.Empty);
        }

        /// <summary>Keeps only active-in-hierarchy components that belong to the given scene.</summary>
        private static T[] FilterScene<T>(T[] components, Scene scene) where T : Component
        {
            List<T> list = new List<T>();
            foreach (T c in components)
                if (c.gameObject.scene == scene && c.gameObject.activeInHierarchy) list.Add(c);
            return list.ToArray();
        }

        /// <summary>Axis-aligned box volume; Unity's Bounds has no volume property.</summary>
        private static float Volume(Bounds b)
        {
            return b.size.x * b.size.y * b.size.z;
        }

        /// <summary>Intersection-over-union of two world AABBs; touching boxes score 0.</summary>
        private static float BoundsIou(Bounds a, Bounds b)
        {
            Vector3 d = Vector3.Min(a.max, b.max) - Vector3.Max(a.min, b.min);
            if (d.x <= 0f || d.y <= 0f || d.z <= 0f) return 0f;
            float intersection = d.x * d.y * d.z;
            float union = Volume(a) + Volume(b) - intersection;
            return union > 1e-10f ? intersection / union : 0f;
        }

        /// <summary>check 5: overlap with the corridor strip |z| in [inner, outer] and |x| &lt; CorridorHalfX.</summary>
        private static bool IntersectsCorridor(Bounds b, float outerPos, float outerNeg, out string side)
        {
            side = null;
            if (b.min.x >= CorridorHalfX || b.max.x <= -CorridorHalfX) return false;
            if (b.max.z > CorridorInnerZ && b.min.z < outerPos) { side = "positive"; return true; }
            if (b.max.z > -outerNeg && b.min.z < -CorridorInnerZ) { side = "negative"; return true; }
            return false;
        }

        /// <summary>check 6: parent-child pairs or connector-named parts overlap structurally, not erroneously.</summary>
        private static bool IsStructural(Collider a, Collider b)
        {
            if (a.transform.IsChildOf(b.transform) || b.transform.IsChildOf(a.transform)) return true;
            return NameContains(a.gameObject.name, StructuralNames) || NameContains(b.gameObject.name, StructuralNames);
        }

        /// <summary>check 6 recheck: the physics world must report both colliders inside the intersection region.</summary>
        private static bool PhysicsConfirms(Collider a, Collider b, Vector3 center, Vector3 size)
        {
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return false;
            Collider[] hits = Physics.OverlapBox(center, size * 0.5f, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            bool foundA = false, foundB = false;
            foreach (Collider hit in hits)
            {
                if (hit == a) foundA = true;
                if (hit == b) foundB = true;
            }
            return foundA && foundB;
        }

        private static StreetSpatialAuditFinding Finding(string type, string severity, string target, string message)
            => new StreetSpatialAuditFinding { type = type, severity = severity, target = target, message = message, other = string.Empty };

        /// <summary>Full hierarchy path from the scene root, e.g. "Street/Props/LampPost_01".</summary>
        private static string PathOf(Transform t)
        {
            StringBuilder sb = new StringBuilder(t.name);
            Transform parent = t.parent;
            while (parent != null)
            {
                sb.Insert(0, parent.name + "/");
                parent = parent.parent;
            }
            return sb.ToString();
        }

        /// <summary>Case-insensitive "name contains any keyword" test used by the exemptions.</summary>
        private static bool NameContains(string objectName, string[] keywords)
        {
            string lower = objectName.ToLowerInvariant();
            foreach (string keyword in keywords)
                if (lower.Contains(keyword)) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // P0-§3 round 2 helpers (checks a-l): deliberate duplicate-collider
        // names, render category matching and the additive JSON metadata
        // writer for task 10.
        // ------------------------------------------------------------------

        /// <summary>check g: collider pairs whose names contain one of these keywords are deliberate facilitator boxes (blocker/bounds) and stay info.</summary>
        private static readonly string[] DuplicateColliderNames = { "blocker", "bounds" };

        /// <summary>
        /// check a/b/c/d/e/h: resolves the render category of an object by lowercase
        /// name substring; the first entry of RenderCategoryOrder wins so earlier
        /// (more specific) categories mask later ones. Returns null on no match.
        /// </summary>
        private static string MatchRenderCategory(string objectName)
        {
            string lower = objectName.ToLowerInvariant();
            foreach (string category in RenderCategoryOrder)
                if (lower.Contains(category)) return category;
            return null;
        }

        /// <summary>
        /// Task 10: fills the additive JSON fields (sceneId, worldRotation,
        /// worldSize, assetSource, suggestedFix) after a new finding was created.
        /// Purely additive - existing fields keep their meaning.
        /// </summary>
        private static void FillMetadata(StreetSpatialAuditFinding f, GameObject go, string scene)
        {
            f.sceneId = scene;
            f.worldRotation = go.transform.rotation;
            f.worldSize = go.transform.lossyScale;
            (bool isInstance, string source) prefab = PrefabInfo(go);
            f.assetSource = prefab.isInstance ? prefab.source : string.Empty;
            f.suggestedFix = SuggestFix(f.type);
        }

        /// <summary>Repair hint per finding type; empty string means "no suggestion" (JSON keeps the key either way).</summary>
        private static string SuggestFix(string type)
        {
            switch (type)
            {
                case "render_bounds_abnormal": return "Rescale the prop so its render AABB fits the category size band.";
                case "tipped_over": return "Restore the upright rotation of the prop (x/z rotation back to 0).";
                case "floating": return "Lower the prop until its AABB bottom touches the ground plane y=0.";
                case "sunk": return "Raise the prop until its AABB bottom sits on the ground plane y=0.";
                case "category_overlap": return "Move or shrink one of the two same-category props so their AABBs stop intersecting.";
                case "collider_offset": return "Re-align the collider with the rendered mesh (rebake the collider or reposition the child).";
                case "duplicate_collider": return "Remove one of the duplicate colliders; keep deliberate blocker/bounds pairs documented as info.";
                case "corridor_blocker": return "Move the prop out of the doorstep corridor band or tuck it flush against the facade.";
                case "mission_path_block": return "Clear the collider from the sampled mission route or add an avoidable detour.";
                case "signal_occluded": return "Shorten or move the occluder, or raise the signal head, so the waiting-point sight line is clear.";
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Read-only result browser for StreetSpatialAudit. Draws every finding of the
    /// cached result as a wire box in the scene view: red for errors, yellow for
    /// warnings, green for structural/info entries; the selected finding gets a
    /// white outline plus a label. The window never touches scene objects and never
    /// starts an audit by itself; it subscribes to SceneView.duringSceneGui instead
    /// of adding components.
    /// </summary>
    public class StreetSpatialAuditWindow : EditorWindow
    {
        private Vector2 scroll;
        private StreetSpatialAuditFinding selected;

        [MenuItem("Street Crossing/Spatial Audit/Audit Window")]
        public static void Open()
        {
            StreetSpatialAuditWindow window = GetWindow<StreetSpatialAuditWindow>("Spatial Audit");
            window.Show();
        }

        private void OnEnable() { SceneView.duringSceneGui += DrawGizmos; }
        private void OnDisable() { SceneView.duringSceneGui -= DrawGizmos; }

        private void OnGUI()
        {
            StreetSpatialAuditResult result = StreetSpatialAuditCache.LastResult;
            if (result == null)
            {
                EditorGUILayout.HelpBox("No audit results yet. Run 'Street Crossing/Spatial Audit/Run Full Audit'.", MessageType.Info);
                if (GUILayout.Button("Run Full Audit")) { selected = null; StreetSpatialAudit.RunFullAudit(); }
                return;
            }

            EditorGUILayout.LabelField($"Scene '{result.scene}' at {result.timestamp}: {result.findings.Count} finding(s), {result.proxies.Count} collider proxy(ies)");
            if (GUILayout.Button("Run Full Audit Again")) { selected = null; StreetSpatialAudit.RunFullAudit(); }
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < result.findings.Count; i++)
            {
                StreetSpatialAuditFinding f = result.findings[i];
                if (GUILayout.Button($"[{f.severity}] {f.type} - {f.target}"))
                {
                    selected = f;
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Task 11: draws every cached finding as a severity-colored wire box and
        /// highlights the selected one with a white outline and label. Read-only
        /// over StreetSpatialAuditCache; the audit only ever runs through the
        /// explicit menu item or window button.
        /// </summary>
        private void DrawGizmos(SceneView view)
        {
            StreetSpatialAuditResult result = StreetSpatialAuditCache.LastResult;
            if (result == null) return;
            for (int i = 0; i < result.findings.Count; i++)
            {
                StreetSpatialAuditFinding f = result.findings[i];
                if (f.aabbSize == Vector3.zero) continue;
                Handles.color = f.severity == "error" ? Color.red : f.severity == "warning" ? Color.yellow : Color.green;
                Handles.DrawWireCube(f.aabbCenter, f.aabbSize);
            }
            StreetSpatialAuditFinding chosen = selected;
            if (chosen == null || chosen.aabbSize == Vector3.zero) return;
            Handles.color = Color.white;
            Handles.DrawWireCube(chosen.aabbCenter, chosen.aabbSize * 1.06f);
            Handles.Label(chosen.aabbCenter + new Vector3(0f, chosen.aabbSize.y * 0.5f + 0.2f, 0f), $"{chosen.type} ({chosen.severity})");
        }
    }
}
