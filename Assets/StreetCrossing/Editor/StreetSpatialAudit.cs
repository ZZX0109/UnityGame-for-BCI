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
    }

    /// <summary>
    /// Read-only result browser for StreetSpatialAudit. Selecting a finding draws
    /// its world AABB in the scene view: red for errors, yellow for warnings,
    /// green for structural/info entries. The window never touches scene objects;
    /// it subscribes to SceneView.duringSceneGui instead of adding components.
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

        /// <summary>Draws the selected finding's world AABB; colors follow finding severity.</summary>
        private void DrawGizmos(SceneView view)
        {
            StreetSpatialAuditFinding f = selected;
            if (f == null) return;
            Handles.color = f.severity == "error" ? Color.red : f.severity == "warning" ? Color.yellow : Color.green;
            Handles.DrawWireCube(f.aabbCenter, f.aabbSize);
            Handles.Label(f.aabbCenter + new Vector3(0f, f.aabbSize.y * 0.5f + 0.2f, 0f), $"{f.type} ({f.severity})");
        }
    }
}
