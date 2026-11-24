using System.Collections.Generic;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetCrossing.Presentation
{
    /// <summary>Original lightweight procedural street art. All dimensions use scenario road coordinates.</summary>
    public sealed class StreetWorld
    {
        public const float AsphaltTileSizeMetres = 3f;
        private readonly Transform root;
        private readonly Material source;
        private readonly Font font;
        private Material worldTextMaterial;
        private readonly StreetAssetLibrary modules = new StreetAssetLibrary();
        public StreetAssetLibrary Modules => modules;
        private readonly Dictionary<string, Mesh> detailMeshes = new Dictionary<string, Mesh>();
        private StreetVisualProfile profile;
        public Transform VisualDetailsRoot { get; private set; }
        private StreetScenarioDefinition definition;
        private Transform destinationMat, startNotice;
        private TextMesh startLabel;
        private Vector3[] approachPath, arrivalPath;
        private int approachStep, arrivalStep;
        public Vector3 MissionSpawn { get; private set; }
        public float MissionHeading { get; private set; }
        public Vector3 MissionDestination { get; private set; }
        public string MissionDestinationName { get; private set; }
        public int MissionForwardSign { get; private set; } = 1;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<Mesh> combinedMeshes = new List<Mesh>();
        private List<Vector2[]> turningAprons;
        private readonly List<CrossingZone> zones = new List<CrossingZone>();
        public IReadOnlyList<CrossingZone> Zones => zones;
        public Vector3[] LeftRoadTargets { get; private set; }
        public Vector3[] RightRoadTargets { get; private set; }
        private readonly Vector3[][] laneRoadTargets = new Vector3[StreetLayout.StraightLaneCount][];
        private Collider stopPanel;
        public bool StopPanelHidesNearLane(Vector3 eye)
        {
            if (!stopPanel || MissionForwardSign < 0 || definition.DirectionForLane(0) != 1) return false;
            foreach (Vector3 target in laneRoadTargets[0])
            {
                Vector3 delta = target - eye;
                if (!stopPanel.Raycast(new Ray(eye, delta.normalized), out _, delta.magnitude)) return false;
            }
            return true;
        }
        public Vector3[] RoadTargetsForLane(int lane) => laneRoadTargets[lane];
        public Transform TrafficRoot { get; private set; }
        public Material AsphaltMaterial => modules.Asphalt;
        public Material PavingMaterial => modules.Paving;
        public Transform CreateRefinedTree(Transform parent, Vector3 position, int variant) => modules.CreateRefinedTree(parent, position, variant);
        public (Transform root, Animator animator) CreatePedestrian(Transform parent, int variant) => modules.CreatePedestrian(parent, variant);
        /// <summary>Real transparent glass for windows; alpha tinted so recessed backing reads through.</summary>
        public Material GlassMaterial
        {
            get
            {
                string key = "Glass:Transparent";
                if (materials.TryGetValue(key, out Material existing)) return existing;
                ColorUtility.TryParseHtmlString("#667F7A", out Color color);
                color.a = .35f;
                Material material = new Material(source)
                {
                    name = "Street transparent glass",
                    color = color,
                    renderQueue = 3000
                };
                material.SetFloat("_Mode", 3f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Glossiness", .78f);
                material.SetFloat("_Metallic", .22f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                materials.Add(key, material);
                return material;
            }
        }
        /// <summary>Deterministic glass tint variants so a whole street of windows no longer shares one pane.</summary>
        public Material GlassMaterialVariant(int variant)
        {
            variant = Mathf.Clamp(variant, 0, 2);
            if (variant == 0) return GlassMaterial;
            string key = "Glass:Transparent:" + variant;
            if (materials.TryGetValue(key, out Material existing)) return existing;
            ColorUtility.TryParseHtmlString(variant == 1 ? "#5E7570" : "#71878A", out Color color);
            color.a = variant == 1 ? .45f : .30f;
            Material material = new Material(source)
            {
                name = "Street transparent glass " + variant,
                color = color,
                renderQueue = 3000
            };
            material.SetFloat("_Mode", 3f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Glossiness", variant == 1 ? .60f : .85f);
            material.SetFloat("_Metallic", .22f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            materials.Add(key, material);
            return material;
        }
        public StreetWorld(Transform parent, Material referencedSurface, Font uiFont)
        {
            root = parent; source = referencedSurface; font = uiFont;
            Font.textureRebuilt += RefreshWorldTextAtlas;
        }
        private void RefreshWorldTextAtlas(Font rebuilt)
        {
            if (rebuilt == font && worldTextMaterial && font.material)
                worldTextMaterial.mainTexture = font.material.mainTexture;
        }
        private Material WorldTextMaterial()
        {
            if (worldTextMaterial) return worldTextMaterial;
            Shader shader = Resources.Load<Shader>("StreetCrossing/WorldText");
            if (!shader) throw new System.InvalidOperationException("The depth-tested street text shader is missing.");
            worldTextMaterial = new Material(shader) { name = "Street world text (depth tested)", mainTexture = font.material.mainTexture };
            return worldTextMaterial;
        }
        public Material Material(string hex, float smoothness = 0.18f)
        {
            string key = hex + ":" + smoothness.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (materials.TryGetValue(key, out Material existing)) return existing;
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            Material material = new Material(source) { name = "Street " + hex, color = color };
            material.SetFloat("_Glossiness", smoothness); material.SetFloat("_Metallic", smoothness > .4f ? .35f : .03f); materials.Add(key, material); return material;
        }
        public void Dispose()
        {
            Font.textureRebuilt -= RefreshWorldTextAtlas;
            if (worldTextMaterial) Object.Destroy(worldTextMaterial);
            worldTextMaterial = null;
            foreach (Material material in materials.Values) Object.Destroy(material); materials.Clear();
            foreach (Mesh mesh in detailMeshes.Values) Object.Destroy(mesh); detailMeshes.Clear();
            foreach (Mesh mesh in combinedMeshes) Object.Destroy(mesh); combinedMeshes.Clear();
            // Imported CC0 model materials are owned by the shared library.
            modules.DisposeImported();
        }
        public Material FacadeMaterial(string colour)
        {
            string key = "Facade:" + colour;
            if (materials.TryGetValue(key, out Material existing)) return existing;
            Material material = new Material(modules.Plaster) { name = "Street masonry " + colour };
            ColorUtility.TryParseHtmlString("#" + colour, out Color tint); material.color = tint;
            material.mainTextureScale = new Vector2(3, 2); material.SetFloat("_Glossiness", .12f);
            materials.Add(key, material); return material;
        }
        /// <summary>Combine each original static detail chunk by material, retaining text as its own renderer.</summary>
        public void CombineVisuals(Transform group)
        {
            var buckets = new Dictionary<Material, List<CombineInstance>>();
            var originals = new List<GameObject>();
            foreach (MeshFilter filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                if (filter.transform == group || filter.GetComponentInParent<LODGroup>() != null) continue;
                Material material = renderer.sharedMaterial; if (material == null) continue;
                if (!buckets.TryGetValue(material, out List<CombineInstance> instances))
                { instances = new List<CombineInstance>(); buckets.Add(material, instances); }
                instances.Add(new CombineInstance { mesh = filter.sharedMesh, transform = group.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                originals.Add(filter.gameObject);
            }
            foreach (KeyValuePair<Material, List<CombineInstance>> entry in buckets)
            {
                Mesh mesh = new Mesh { name = group.name + " / " + entry.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(entry.Value.ToArray(), true, true); mesh.RecalculateBounds(); combinedMeshes.Add(mesh);
                GameObject chunk = new GameObject("Combined " + entry.Key.name, typeof(MeshFilter), typeof(MeshRenderer)); chunk.transform.SetParent(group, false);
                chunk.GetComponent<MeshFilter>().sharedMesh = mesh; chunk.GetComponent<MeshRenderer>().sharedMaterial = entry.Key;
            }
            foreach (GameObject original in originals) { original.SetActive(false); Object.Destroy(original); }
        }
        public void AddVisualLod(Transform group, float cullHeight)
        {
            // A second LODGroup on the same object is rejected by Unity, so keep the first one.
            if (group.gameObject.GetComponent<LODGroup>() != null) return;
            LODGroup lod = group.gameObject.AddComponent<LODGroup>();
            // Single-level distance culling. Cross-fading needs real lower-detail levels; forcing it
            // on a one-level group only ghosts objects, which is uncomfortable in VR.
            lod.SetLODs(new[] { new LOD(cullHeight, group.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }
        public Renderer VisualRounded(Transform parent, string name, Vector3 position, Vector3 size, string color, float radius = .07f, float smoothness = .18f)
        {
            string key = size.ToString("F4") + ":" + radius.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            if (!detailMeshes.TryGetValue(key, out Mesh mesh))
            { mesh = StreetDetailMesh.RoundedBox(size, radius); detailMeshes.Add(key, mesh); }
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; Renderer renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = Material(color, smoothness); return renderer;
        }
        /// <summary>Attach a cached, generated mesh for original static presentation geometry.</summary>
        public Renderer VisualMesh(Transform parent, string name, Vector3 position, string meshKey,
            System.Func<Mesh> createMesh, string color, float smoothness = .18f)
        {
            string key = "Procedural:" + meshKey;
            if (!detailMeshes.TryGetValue(key, out Mesh mesh))
            {
                mesh = createMesh();
                if (mesh == null) throw new System.InvalidOperationException("A procedural visual mesh could not be created: " + meshKey);
                mesh.name = "Street " + meshKey;
                detailMeshes.Add(key, mesh);
            }
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = Material(color, smoothness); return renderer;
        }
        public Mesh CornerMesh(Vector3 size, float cut, bool mirrorX = false)
        {
            string key = "Corner:" + size.ToString("F3") + ":" + cut.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ":" + mirrorX;
            if (!detailMeshes.TryGetValue(key, out Mesh mesh)) { mesh = StreetDetailMesh.ChamferedCorner(size, cut, mirrorX); detailMeshes.Add(key, mesh); }
            return mesh;
        }
        private GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 p, Vector3 scale, string color, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = p; go.transform.localScale = scale;
            bool asphalt = name == "Road asphalt" || name == "S03 intersecting side street" || name == "Rear service yard";
            go.GetComponent<Renderer>().sharedMaterial = asphalt ? modules.Asphalt : name == "Paved sidewalk" ? modules.Paving : Material(color);
            if (asphalt)
            {
                SetAsphaltMetreScaleUv(go.GetComponent<Renderer>());
            }
            if (!collider) { Collider c = go.GetComponent<Collider>(); c.enabled = false; Object.Destroy(c); }
            return go;
        }
        /// <summary>Give asphalt surfaces one 3 metre repeat in root-local street coordinates.</summary>
        public void SetAsphaltMetreScaleUv(Renderer renderer)
        {
            if (!renderer) return;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            Mesh mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            mesh.name = "Asphalt UVs (3 metre repeat)";
            Vector3[] vertices = mesh.vertices; Vector3[] normals = mesh.normals; Vector2[] uv = mesh.uv;
            Matrix4x4 rootFromMesh = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < uv.Length; i++)
            {
                if (normals[i].y < .9f) continue;
                Vector3 point = rootFromMesh.MultiplyPoint3x4(vertices[i]);
                uv[i] = new Vector2(point.x, point.z) / AsphaltTileSizeMetres;
            }
            mesh.uv = uv; mesh.RecalculateTangents(); filter.sharedMesh = mesh; combinedMeshes.Add(mesh);
        }
        private GameObject Box(string name, Vector3 p, Vector3 scale, string color, bool collider = true, Transform parent = null)
            => Shape(PrimitiveType.Cube, name, parent == null ? root : parent, p, scale, color, collider);
        public TextMesh Label(string text, Vector3 p, float size, string color = "32483D", Transform parent = null, float yaw = 0, float maxWidth = 3.3f, float maxHeight = 0.75f)
        {
            GameObject go = new GameObject(text); go.transform.SetParent(parent == null ? root : parent, false);
            go.transform.localPosition = p; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            TextMesh mesh = go.AddComponent<TextMesh>(); mesh.font = font; mesh.fontSize = 80; mesh.characterSize = size;
            mesh.text = text; mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center;
            ColorUtility.TryParseHtmlString("#" + color, out Color c); mesh.color = c;
            // Dynamic Font's default GUI material draws through walls. World signs must obey scene depth.
            go.GetComponent<MeshRenderer>().sharedMaterial = WorldTextMaterial();
            WorldLabelFit fit = go.AddComponent<WorldLabelFit>(); fit.maximumWidth = maxWidth; fit.maximumHeight = maxHeight; return mesh;
        }
        public void Build(StreetScenarioDefinition s)
        {
            definition = s;
            MissionSpawn = s.playerSpawn; MissionHeading = 0; MissionDestination = s.destination;
            MissionDestinationName = s.destinationName; MissionForwardSign = 1;
            profile = s.visualProfile; profile.Validate(s.scenarioId);
            turningAprons = StreetJunctionGeometry.TurnAprons(s);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.82f, 0.83f);
            RenderSettings.ambientEquatorColor = new Color(0.57f, 0.60f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.36f, 0.31f);
            // Fog now begins inside the built street so the distant rows and the horizon silhouettes layer into haze.
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.77f, 0.84f, 0.85f); RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 55; RenderSettings.fogEndDistance = 120;
            GameObject sun = new GameObject("Soft morning sunlight"); sun.transform.SetParent(root, false); sun.transform.rotation = Quaternion.Euler(43, -32, 0);
            Light light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.color = new Color(1, 0.965f, 0.915f); light.intensity = 1.06f; light.shadows = LightShadows.Soft; light.shadowStrength = 0.60f;
            RenderSettings.sun = light;
            // The skybox material asset references the built-in Skybox/Procedural shader, so builds include it.
            // If the reference ever breaks (shader rename), keep the camera's solid fallback instead of a magenta sky.
            Material sky = Resources.Load<Material>("StreetCrossing/Modules/SkyMorning");
            if (sky && sky.shader && sky.shader.name == "Skybox/Procedural") RenderSettings.skybox = sky;
            float width = s.roadHalfWidth;
            float leftApproachZ = s.DirectionForLane(0) > 0 ? s.LaneZ(0) : s.LaneZ(s.StraightLaneCount - 1);
            LeftRoadTargets = new[] { new Vector3(-14, 1.1f, leftApproachZ), new Vector3(-20, 1.1f, leftApproachZ), new Vector3(-28, 1.1f, leftApproachZ) };
            RightRoadTargets = new[] { new Vector3(14, 1.1f, -leftApproachZ), new Vector3(20, 1.1f, -leftApproachZ), new Vector3(28, 1.1f, -leftApproachZ) };
            for (int lane = 0; lane < s.StraightLaneCount; lane++)
            {
                int direction = s.DirectionForLane(lane);
                float z = s.LaneZ(lane);
                laneRoadTargets[lane] = new[] { new Vector3(-14 * direction, 1.1f, z), new Vector3(-20 * direction, 1.1f, z), new Vector3(-28 * direction, 1.1f, z) };
            }
            Box("Road asphalt", new Vector3(0, -0.16f, 0), new Vector3(s.roadHalfLength * 2 + 4, 0.3f, width * 2), "53534D");
            for (int side = -1; side <= 1; side += 2)
            {
                BuildFootway(s, side);
                // The spaces between shop buildings open onto paved yards behind the footway.
                Box("Rear service yard", new Vector3(0, -.12f, side * (width + 23f)),
                    new Vector3(160f, .12f, 40f), "53534D", false);
                for (int b = -2; b <= 2; b++)
                {
                    if ((side == 1 && b == 0) || (s.turningTraffic && b == -s.DirectionForLane(0))) continue;
                    Building(new Vector3(b * 13, 0, StreetLayout.SidewalkZ(side * 11.3f)), side, 6.8f + ((b + 3) % 3) * 0.9f, (b + 3) % 2 == 0 ? "D9CBB7" : "D5BA9A");
                }
                for (int x = -26; x <= 26; x += 13) if (Mathf.Abs(x) > 4 && (!s.turningTraffic || Mathf.Abs(x + 9 * s.DirectionForLane(0)) > 6)) { Tree(new Vector3(x, 0, StreetLayout.SidewalkZ(side * 5.5f))); Lamp(new Vector3(x + 3.5f, 0, StreetLayout.SidewalkZ(side * 4.7f))); }
                // Physical street-end boundary is now supplied by the visible maintenance gate
                // built in StreetFurnitureBuilder; no invisible collider walls remain here.
            }
            // Crosswalk stripes sit flush on the asphalt top (-0.01 m); 3 mm thick paint.
            for (float z = -width + .5f; z < width - .3f; z += .7f) Box("Crosswalk stripe", new Vector3(0, -.0085f, z), new Vector3(s.crosswalkHalfWidth * 2, .003f, .35f), "ECE9DA", false);
            BuildLaneMarkings(s);
            BuildRoadWear(s);
            for (int lane = 0; lane < s.StraightLaneCount; lane++)
            {
                int direction = s.DirectionForLane(lane); float z = s.LaneZ(lane);
                // Markings are 2 cm thermoplastic on the -0.01 m asphalt top.
                Box("Vehicle stop line", new Vector3(-direction * s.stopLineOffset, 0f, z), new Vector3(0.22f, 0.02f, StreetLayout.LaneWidth - .25f), "F0ECDC", false);
                Box("Lane direction shaft", new Vector3(-direction * 11, 0f, z), new Vector3(1.4f, 0.02f, 0.15f), "D7D4C6", false);
                for (int edge = -1; edge <= 1; edge += 2)
                {
                    GameObject tip = Box("Lane direction chevron", new Vector3(-direction * 10.3f, 0f, z + edge * 0.2f), new Vector3(0.65f, 0.02f, 0.13f), "D7D4C6", false);
                    tip.transform.rotation = Quaternion.Euler(0, edge * direction * 38, 0);
                }
            }
            // Deterministic shallow road flecks; visual only, no collision noise. 5 mm thick, flush on asphalt top.
            for (int n = 0; n < 70; n++)
            {
                float x = Mathf.Repeat(n * 17.33f, 78) - 39, z = Mathf.Repeat(n * 3.17f, width * 2 - 0.4f) - width + 0.2f;
                if (Mathf.Abs(x) > 2.2f) Box("Asphalt wear", new Vector3(x, -.0075f, z), new Vector3(0.08f + n % 3 * 0.09f, 0.005f, 0.025f), n % 2 == 0 ? "64635B" : "484A45", false);
            }
            if (s.turningTraffic) BuildSideJunction(s);
            if (s.kerbOcclusion) BuildCornerStop(width);
            Shop(); BusStop(new Vector3(7, 0, width + 2));
            VisualDetailsRoot = new GameObject("Visual details - " + profile.identity).transform; VisualDetailsRoot.SetParent(root, false);
            StreetVisualDetails.Build(VisualDetailsRoot, this, s);
            StreetDistrictBuilder.BuildExtensions(VisualDetailsRoot, this, s, width);
            StreetFurnitureBuilder.Build(VisualDetailsRoot, this, s);
            BuildFurnitureBounds(s);
            StreetBackgroundPopulation population = new GameObject("Background population").AddComponent<StreetBackgroundPopulation>();
            population.transform.SetParent(VisualDetailsRoot, false); population.Initialize(root.GetComponentInParent<Core.StreetGameManager>(), this, s);
            startNotice = Box("Start notice", new Vector3(-2.8f, 1.2f, StreetLayout.SidewalkZ(-7.7f)), new Vector3(1.3f, 1.3f, 0.12f), "E9DFCA").transform;
            Inspectable(startNotice.gameObject, "过街练习", "沿人行道到路沿，观察左右来车和行人信号。确认车流停稳、行人绿灯后，从斑马线前往对面便利店。");
            startLabel = Label("街角便利店\n在马路对面", new Vector3(-2.8f, 1.25f, StreetLayout.SidewalkZ(-7.79f)), 0.055f, "365A46", null, 0, 1.1f, 1.0f);
            destinationMat = Box("Destination welcome mat", s.destination + new Vector3(0, 0.008f, 0), new Vector3(2.7f, 0.02f, 1.2f), "80946F", false).transform;
            TrafficRoot = new GameObject("Vehicle routes and pool").transform; TrafficRoot.SetParent(root, false);
            Zone(CrossingRegion.Approach, new Vector3(0, 0.9f, StreetLayout.SidewalkZ(-6.9f)), new Vector3(6, 3, 5.5f));
            Zone(CrossingRegion.Kerb, new Vector3(0, 0.9f, -width - 0.45f), new Vector3(4.1f, 3, 1.1f));
            // Entry includes the near half-metre of roadway, sampled before conflict in the evaluator.
            Zone(CrossingRegion.Entry, new Vector3(0, 0.9f, -width), new Vector3(s.crosswalkHalfWidth * 2, 3, 0.9f));
            Zone(CrossingRegion.RoadwayHazard, new Vector3(0, 0.9f, 0), new Vector3(86, 3, width * 2));
            Zone(CrossingRegion.ProtectedCrosswalk, new Vector3(0, 0.9f, 0), new Vector3(s.conflictHalfWidth * 2, 3, width * 2 + 0.1f));
            Zone(CrossingRegion.CrossingRoute, new Vector3(0, 0.9f, 0), new Vector3(s.crosswalkHalfWidth * 2, 3, width * 2 + 1.8f));
            Zone(CrossingRegion.Exit, new Vector3(0, 0.9f, width + 0.35f), new Vector3(s.crosswalkHalfWidth * 2, 3, 0.9f));
            Zone(CrossingRegion.Destination, s.destination + Vector3.up * 0.9f, new Vector3(2.8f, 3, 2));
        }
        public void ApplyMission(StreetMissionDefinition mission, int segmentIndex)
        {
            if (definition == null) throw new System.InvalidOperationException("Build the street before applying a mission.");
            if (mission == null || mission.districtId != definition.scenarioId) throw new System.ArgumentException("Mission must match the built district.", nameof(mission));
            MissionForwardSign = StreetMissionGeometry.ForwardSign(mission, segmentIndex);
            MissionSpawn = StreetMissionGeometry.Spawn(mission, segmentIndex);
            MissionHeading = MissionForwardSign > 0 ? 0 : 180;
            StreetDestinationKind kind = StreetMissionGeometry.GoalKind(mission, segmentIndex);
            MissionDestination = StreetMissionGeometry.Doorstep(kind);
            MissionDestinationName = StreetMissionGeometry.Name(kind);
            if (Mathf.Sign(MissionDestination.z) != MissionForwardSign) throw new System.ArgumentException("The goal must be on the opposite sidewalk.", nameof(mission));
            float width = definition.roadHalfWidth;
            foreach (CrossingZone zone in zones)
            {
                Vector3 centre = zone.transform.localPosition;
                switch (zone.region)
                {
                    case CrossingRegion.Approach: centre.z = -MissionForwardSign * Mathf.Abs(StreetLayout.SidewalkZ(-6.9f)); break;
                    case CrossingRegion.Kerb: centre.z = -MissionForwardSign * (width + .45f); break;
                    case CrossingRegion.Entry: centre.z = -MissionForwardSign * width; break;
                    case CrossingRegion.Exit: centre.z = MissionForwardSign * (width + .35f); break;
                    case CrossingRegion.Destination:
                        centre.x = MissionDestination.x; centre.z = MissionDestination.z;
                        zone.size = new Vector3(1.2f, 3, 1f); break;
                }
                zone.transform.localPosition = centre;
            }
            destinationMat.localPosition = MissionDestination + Vector3.up * .008f;
            destinationMat.localScale = new Vector3(1.1f, .02f, .85f);
            startNotice.localPosition = new Vector3(-2.8f, 1.2f, -MissionForwardSign * 11f);
            startNotice.localRotation = Quaternion.Euler(0, MissionHeading, 0);
            startLabel.transform.localPosition = new Vector3(-2.8f, 1.25f, -MissionForwardSign * 11.09f);
            startLabel.transform.localRotation = Quaternion.Euler(0, MissionHeading, 0);
            startLabel.text = MissionDestinationName + "\n先走指定横道";
            bool useStartSign = !stopPanel || MissionForwardSign < 0;
            startNotice.gameObject.SetActive(useStartSign);
            startLabel.gameObject.SetActive(useStartSign);
            Player.StreetInspectable notice = startNotice.GetComponent<Player.StreetInspectable>();
            notice.title = mission.title;
            notice.description = "目标：" + MissionDestinationName + "。先沿人行道到横道入口，确认信号和左右道路，再沿横道过街；出横道后沿人行道到门前绿色地垫。";
            approachPath = StreetMissionGeometry.Approach(MissionSpawn, MissionForwardSign, width);
            arrivalPath = StreetMissionGeometry.Arrival(MissionDestination, MissionForwardSign, width);
            approachStep = arrivalStep = 0;
        }
        public Vector3 NavigationTarget(Vector3 playerPosition, bool crossingComplete)
        {
            if (approachPath == null || arrivalPath == null) return MissionDestination;
            if (!crossingComplete)
            {
                // A late HUD query at the kerb must not send an already-aligned pedestrian backwards.
                if (Mathf.Abs(playerPosition.x) < .3f && MissionForwardSign * playerPosition.z >= -Mathf.Abs(StreetMissionGeometry.CorridorZ(-MissionForwardSign)) - .25f)
                    approachStep = Mathf.Max(approachStep, 2);
                while (approachStep < approachPath.Length && PlanarDistance(playerPosition, approachPath[approachStep]) <= .25f) approachStep++;
                return approachStep < approachPath.Length ? approachPath[approachStep] : arrivalPath[0];
            }
            if (MissionForwardSign * playerPosition.z >= definition.roadHalfWidth + .7f) arrivalStep = Mathf.Max(arrivalStep, 1);
            while (arrivalStep < arrivalPath.Length - 1 && PlanarDistance(playerPosition, arrivalPath[arrivalStep]) <= .25f) arrivalStep++;
            return arrivalPath[arrivalStep];
        }
        private static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private void BuildFootway(StreetScenarioDefinition scenario, int side)
        {
            Vector2 junction = scenario.turningTraffic
                ? StreetJunctionGeometry.CurbOpening(scenario, side, turningAprons) : Vector2.zero;
            Mesh paving = StreetFootwayMesh.Surface(scenario.roadHalfWidth, side, scenario.crosswalkHalfWidth + .3f, junction);
            combinedMeshes.Add(paving);
            var footway = new GameObject("Paved sidewalk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            footway.transform.SetParent(root, false);
            footway.GetComponent<MeshFilter>().sharedMesh = paving;
            footway.GetComponent<MeshRenderer>().sharedMaterials = new[] { modules.Paving, Material("B7B2A5") };
            footway.GetComponent<MeshCollider>().sharedMesh = paving;

            Vector3 centre = new Vector3(0, .008f, side * (scenario.roadHalfWidth + .55f));
            Box("Tactile waiting paving", centre, new Vector3(scenario.crosswalkHalfWidth * 2, .012f, .65f), "BFA163", false);
            var studs = new GameObject("Warning paving domes", typeof(MeshFilter), typeof(MeshRenderer));
            studs.transform.SetParent(root, false);
            studs.transform.localPosition = centre + Vector3.up * .006f;
            const string key = "Warning domes:";
            string sizeKey = key + scenario.crosswalkHalfWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (!detailMeshes.TryGetValue(sizeKey, out Mesh domes))
            {
                domes = StreetFootwayMesh.WarningStuds(scenario.crosswalkHalfWidth);
                detailMeshes.Add(sizeKey, domes);
            }
            studs.GetComponent<MeshFilter>().sharedMesh = domes;
            studs.GetComponent<MeshRenderer>().sharedMaterial = Material("D1B47A");
        }
        private void BuildLaneMarkings(StreetScenarioDefinition s)
        {
            for (int boundary = 1; boundary < s.StraightLaneCount; boundary++)
            {
                float z = -s.roadHalfWidth + boundary * StreetLayout.LaneWidth;
                if (boundary == s.StraightLaneCount / 2)
                {
                    foreach (Vector2 segment in new[] { new Vector2(-s.roadHalfLength, -2.4f), new Vector2(2.4f, s.roadHalfLength) })
                    {
                        float junction = -9 * s.DirectionForLane(0);
                        if (s.turningTraffic && junction > segment.x && junction < segment.y)
                        { CentreLine(segment.x, junction - StreetLayout.SideRoadHalfWidth - .1f); CentreLine(junction + StreetLayout.SideRoadHalfWidth + .1f, segment.y); }
                        else CentreLine(segment.x, segment.y);
                    }
                }
                else for (int n = -13; n <= 13; n++)
                    if (Mathf.Abs(n) > 1 && (!s.turningTraffic || Mathf.Abs(n * 3 + 9 * s.DirectionForLane(0)) > 4.3f))
                        // 8 mm dashed lane marking flush on asphalt top.
                        Box("Same-direction lane dash", new Vector3(n * 3, -.006f, z), new Vector3(1.6f, .008f, .10f), "ECE9DA", false);
            }
            BuildEdgeLinesAndWheelTracks(s);
        }
        /// <summary>Kerb edge lines and the two polished tyre tracks per lane; both skip the crosswalk and the S03 mouth.</summary>
        private void BuildEdgeLinesAndWheelTracks(StreetScenarioDefinition s)
        {
            var ranges = new List<Vector2> { new Vector2(-s.roadHalfLength, -2.7f), new Vector2(2.7f, s.roadHalfLength) };
            if (s.turningTraffic)
            {
                float junction = -9 * s.DirectionForLane(0);
                float mouthA = junction - 4.8f, mouthB = junction + 4.8f;
                var cut = new List<Vector2>();
                foreach (Vector2 range in ranges)
                {
                    if (mouthB <= range.x || mouthA >= range.y) { cut.Add(range); continue; }
                    if (range.x < mouthA) cut.Add(new Vector2(range.x, mouthA));
                    if (range.y > mouthB) cut.Add(new Vector2(mouthB, range.y));
                }
                ranges = cut;
            }
            foreach (Vector2 range in ranges)
            {
                float centre = (range.x + range.y) * .5f, length = range.y - range.x;
                for (int side = -1; side <= 1; side += 2)
                    // 8 mm kerb edge line on the same paint level as the lane dashes.
                    Box("Kerb edge line", new Vector3(centre, -.006f, side * (s.roadHalfWidth - .15f)), new Vector3(length, .008f, .10f), "ECE9DA", false);
                for (int lane = 0; lane < s.StraightLaneCount; lane++)
                {
                    float z = s.LaneZ(lane);
                    foreach (float offset in new[] { -.75f, .75f })
                    {
                        // Tyre-polished bands: 4 mm proud of the asphalt so they sit below every painted marking.
                        GameObject band = Box("Polished wheel track", new Vector3(centre, -.008f, z + offset), new Vector3(length, .004f, .40f), "4B4D47", false);
                        band.GetComponent<Renderer>().sharedMaterial = Material("4B4D47", .30f);
                    }
                }
            }
        }
        private void CentreLine(float a, float b)
        {
            if (b <= a) return;
            for (int side = -1; side <= 1; side += 2)
                // 8 mm double centre line flush on asphalt top.
                Box("Double centre line", new Vector3((a + b) * .5f, -.006f, side * .08f), new Vector3(b - a, .008f, .055f), "D7B569", false);
        }
        /// <summary>Manholes, one repatched trench and stop-line oil stains; deterministic, flush and collider-free.</summary>
        private void BuildRoadWear(StreetScenarioDefinition s)
        {
            foreach (Vector2 at in new[] { new Vector2(-24, 4.95f), new Vector2(17, -4.95f), new Vector2(26, -1.65f), new Vector2(-19, 1.65f) })
            {
                if (s.turningTraffic && Mathf.Abs(at.x + 9 * s.DirectionForLane(0)) < 5.2f) continue;
                Vector3 manholePos = new Vector3(at.x, -.008f, at.y);
                // Prefer the imported CC0 manhole model; fall back to the procedural cylinder pair.
                Transform manhole = modules.SpawnImported(root, "water_manhole_cover", manholePos, .02f);
                if (manhole != null) { AddVisualLod(manhole, .004f); continue; }
                // Rim sits 1 mm below the cover so the lid reads seated in its frame.
                GameObject rim = Shape(PrimitiveType.Cylinder, "Manhole rim", root, new Vector3(at.x, -.0084f, at.y), new Vector3(.84f, .0016f, .84f), "3B3D39", false);
                rim.GetComponent<Renderer>().sharedMaterial = Material("3B3D39", .30f);
                GameObject cover = Shape(PrimitiveType.Cylinder, "Manhole cover", root, new Vector3(at.x, -.008f, at.y), new Vector3(.72f, .002f, .72f), "4A4C46", false);
                cover.GetComponent<Renderer>().sharedMaterial = Material("4A4C46", .35f);
            }
            // A repatched trench across the crown; the double centre line is repainted over it.
            if (!s.turningTraffic || Mathf.Abs(18 + 9 * s.DirectionForLane(0)) > 5.2f)
            {
                GameObject patch = Box("Asphalt patch", new Vector3(18, -.0075f, 0), new Vector3(3.5f, .005f, 1.2f), "3F413B", false);
                patch.GetComponent<Renderer>().sharedMaterial = Material("3F413B", .20f);
            }
            for (int lane = 0; lane < s.StraightLaneCount; lane++)
            {
                int direction = s.DirectionForLane(lane);
                float x = -direction * (s.stopLineOffset + 1.6f + (lane & 1) * .8f);
                if (s.turningTraffic && Mathf.Abs(x + 9 * direction) < 5.2f) continue;
                // Oil drippings collect where engines idle at the stop line.
                GameObject stain = Shape(PrimitiveType.Cylinder, "Stop line oil stain", root,
                    new Vector3(x, -.0084f, s.LaneZ(lane) + (lane & 1) * .3f - .15f), new Vector3(.95f + lane * .07f, .0016f, .68f + (lane % 3) * .06f), "2E302C", false);
                stain.GetComponent<Renderer>().sharedMaterial = Material("2E302C", .40f);
            }
        }
        private void BuildSideJunction(StreetScenarioDefinition s)
        {
            float x = -9 * s.DirectionForLane(0);
            Box("S03 intersecting side street", new Vector3(x, 0.008f, 0), new Vector3(StreetLayout.SideRoadHalfWidth * 2, 0.025f, 78 + StreetLayout.SidewalkOffset * 2), "53534D");
            BuildTurnAprons(s);
            // Side street top is +0.0205 m; 10 mm centre markings sit flush on it.
            for (int n = -12; n <= 12; n++) if (Mathf.Abs(n) > 2) Box("Side street centre markings", new Vector3(x, 0.0255f, n * 3), new Vector3(0.12f, 0.01f, 1.6f), "E6C16C", false);
            for (int side = -1; side <= 1; side += 2)
            {
                // 20 mm yield thermoplastic flush on the side-street top.
                Box("Turn approach yield line", new Vector3(x + side * 1.65f, 0.0305f, StreetLayout.SidewalkZ(-side * 9)), new Vector3(3, 0.02f, 0.22f), "F0ECDC", false);
                Zone(CrossingRegion.RoadwayHazard, new Vector3(x, 0.9f, side * (s.roadHalfWidth + 17.7f)), new Vector3(6.6f, 3, 36));
            }
        }
        private void BuildCornerStop(float width)
        {
            Transform stop = new GameObject("Corner bus stop").transform;
            stop.SetParent(root, false);
            stop.localPosition = new Vector3(-2.15f, 0, -width - 1.48f);
            GameObject panel = Box("Opaque route board", new Vector3(0, 1.19f, 0), new Vector3(.15f, 2.08f, 2.28f), "285D65", true, stop);
            stopPanel = panel.GetComponent<Collider>();
            Inspectable(panel, "街角公交站", "站牌挡住左侧近车道。在人行道上靠近斑马线，绕过牌边再看；不用踏进车道。");
            // Keep the tested obstacle envelopes; the small construction details are visual only.
            GameObject roof = Box("Stop canopy", new Vector3(-1.18f, 2.31f, 0), new Vector3(2.85f, .16f, 2.45f), "32717A", true, stop);
            roof.GetComponent<Renderer>().enabled = false;
            Box("Stop post", new Vector3(-2.38f, 1.12f, -.91f), new Vector3(.10f, 2.24f, .10f), "344D50", true, stop);
            GameObject seat = Box("Waiting bench", new Vector3(-1.27f, .46f, -.7f), new Vector3(1.82f, .13f, .46f), "A97850", true, stop);
            seat.GetComponent<Renderer>().enabled = false;
            Transform detail = new GameObject("Corner shelter construction").transform;
            detail.SetParent(stop, false);
            VisualRounded(detail, "Folded roof sheet", new Vector3(-1.18f, 2.365f, 0), new Vector3(2.85f, .05f, 2.45f), "778B89", .012f, .28f);
            foreach (float z in new[] { -1.19f, 1.19f })
                VisualRounded(detail, "Roof drip edge", new Vector3(-1.18f, 2.30f, z), new Vector3(2.85f, .12f, .07f), "344D50", .008f);
            foreach (float x in new[] { -2.565f, .205f })
                VisualRounded(detail, "Roof end frame", new Vector3(x, 2.30f, 0), new Vector3(.08f, .12f, 2.31f), "344D50", .008f);
            foreach (float z in new[] { -.91f, .91f })
                // Beams run frame-to-frame and bite into the canopy underside, the roof sheet and the board top rail,
                // so both bearers carry the sheet down onto the post cap and the route board instead of floating.
                VisualRounded(detail, "Roof cross bearer", new Vector3(-1.18f, 2.28f, z), new Vector3(2.69f, .13f, .07f), "344D50", .006f);
            VisualRounded(detail, "Column cap plate", new Vector3(-2.38f, 2.235f, -.91f), new Vector3(.21f, .025f, .21f), "778B89", .003f);
            VisualRounded(detail, "Column base plate", new Vector3(-2.38f, .022f, -.91f), new Vector3(.22f, .044f, .22f), "778B89", .004f);
            foreach (float dx in new[] { -.075f, .075f })
            foreach (float dz in new[] { -.075f, .075f })
                VisualRounded(detail, "Base anchor nut", new Vector3(-2.38f + dx, .048f, -.91f + dz), new Vector3(.024f, .016f, .024f), "344D50", .003f);
            // The front ends of both cross bearers and the roof frame rest on their own post; one back post cannot carry them.
            Box("Shelter front post", new Vector3(-2.38f, 1.118f, .91f), new Vector3(.09f, 2.245f, .09f), "344D50", true, stop);
            VisualRounded(detail, "Front post cap plate", new Vector3(-2.38f, 2.235f, .91f), new Vector3(.19f, .025f, .19f), "778B89", .003f);
            VisualRounded(detail, "Front post base plate", new Vector3(-2.38f, .0225f, .91f), new Vector3(.2f, .045f, .2f), "778B89", .004f);
            foreach (float dx in new[] { -.07f, .07f })
            foreach (float dz in new[] { -.07f, .07f })
                VisualRounded(detail, "Front post anchor nut", new Vector3(-2.38f + dx, .047f, .91f + dz), new Vector3(.022f, .015f, .022f), "344D50", .003f);
            foreach (float x in new[] { -.60f, -1.94f })
            {
                VisualRounded(detail, "Bench leg", new Vector3(x, .215f, -.70f), new Vector3(.07f, .42f, .07f), "344D50", .005f);
                VisualRounded(detail, "Bench ground shoe", new Vector3(x, .024f, -.70f), new Vector3(.15f, .048f, .36f), "344D50", .005f);
                VisualRounded(detail, "Bench seat bracket", new Vector3(x, .455f, -.70f), new Vector3(.09f, .07f, .43f), "344D50", .005f);
            }
            VisualRounded(detail, "Bench stretcher", new Vector3(-1.27f, .30f, -.70f), new Vector3(1.41f, .065f, .065f), "344D50", .005f);
            for (int slat = 0; slat < 4; slat++)
                VisualRounded(detail, "Timber seat slat", new Vector3(-1.27f, .505f, -.8725f + slat * .115f), new Vector3(1.82f, .04f, .105f), slat % 2 == 0 ? "9A7959" : "A88763", .008f);
            VisualRounded(detail, "Route board inset", new Vector3(.083f, 1.25f, 0), new Vector3(.016f, 1.73f, 2.03f), "E9ECE5", .005f);
            foreach (float z in new[] { -1.09f, 1.09f })
                VisualRounded(detail, "Route board edge", new Vector3(.086f, 1.19f, z), new Vector3(.025f, 2.08f, .045f), "778B89", .005f);
            foreach (float y in new[] { .17f, 2.21f })
                VisualRounded(detail, "Route board rail", new Vector3(.086f, y, 0), new Vector3(.025f, .04f, 2.20f), "778B89", .005f);
            // The board frame is posts, not a floating sign: legs land on paving under the bottom rail, feet spread the load.
            foreach (float z in new[] { -.91f, .91f })
            {
                VisualRounded(detail, "Route board leg", new Vector3(.086f, 1.11f, z), new Vector3(.05f, 2.22f, .05f), "344D50", .005f);
                VisualRounded(detail, "Route board foot", new Vector3(.086f, .024f, z), new Vector3(.13f, .048f, .15f), "344D50", .005f);
                VisualRounded(detail, "Route board gusset", new Vector3(.028f, .32f, z), new Vector3(.11f, .29f, .04f), "344D50", .005f);
            }
            VisualRounded(detail, "Route colour band", new Vector3(.097f, 1.91f, 0), new Vector3(.016f, .27f, 1.99f), "285D65", .003f);
            Label("梧桐里站", new Vector3(.112f, 1.91f, 0), .068f, "FFFFFF", stop, -90, 1.78f, .22f);
            Label("03路", new Vector3(.112f, 1.40f, 0), .105f, "224D53", stop, -90, 1.60f, .38f);
            Label("开往街心路", new Vector3(.112f, .93f, 0), .058f, "224D53", stop, -90, 1.78f, .24f);
            VisualRounded(detail, "Direction rule", new Vector3(.102f, .66f, 0), new Vector3(.012f, .025f, 1.55f), "C2603E", .003f);
            Label("梧桐里站  03路", new Vector3(-1.18f, 2.30f, -1.231f), .045f, "FFFFFF", stop, 0, 2.15f, .105f);
            CombineVisuals(detail);
        }
        private void BuildTurnAprons(StreetScenarioDefinition s)
        {
            Transform geometry = new GameObject("Route-aligned turn shoulders").transform; geometry.SetParent(root, false);
            float junction = -9 * s.DirectionForLane(0);
            foreach (Vector2[] polygon in turningAprons)
            {
                // Corner-matched height field: flush with the side-street top at the covered corner,
                // flush with the main road beyond the 0.953 m ramp, XZ footprint and UVs unchanged.
                Mesh mesh = StreetJunctionGeometry.CornerRamp(polygon, junction, s.roadHalfWidth, out Vector2 corner, out Vector2 axis)
                    ? StreetJunctionGeometry.Surface(polygon, corner, axis)
                    : StreetJunctionGeometry.Surface(polygon, .021f);
                combinedMeshes.Add(mesh);
                GameObject surface = new GameObject("Turn shoulder asphalt", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider)); surface.transform.SetParent(geometry, false);
                surface.GetComponent<MeshFilter>().sharedMesh = mesh; surface.GetComponent<MeshRenderer>().sharedMaterial = modules.Asphalt; surface.GetComponent<MeshCollider>().sharedMesh = mesh;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                    // The two shared seams join existing road; the other polygon edges receive real visible curbstones.
                    if (Mathf.Abs(Mathf.Abs(a.y) - s.roadHalfWidth) < .001f && Mathf.Abs(a.y - b.y) < .001f) continue;
                    if (Mathf.Abs(Mathf.Abs(a.x - junction) - StreetLayout.SideRoadHalfWidth) < .001f && Mathf.Abs(a.x - b.x) < .001f) continue;
                    Vector2 delta = b - a, outward = new Vector2(delta.y, -delta.x).normalized;
                    Vector2 centre = (a + b) * .5f + outward * .10f;
                    GameObject kerb = Box("Kerb around turn shoulder", new Vector3(centre.x, .02f, centre.y), new Vector3(delta.magnitude, .16f, .18f), "E6DDC8", true, geometry);
                    kerb.transform.localRotation = Quaternion.Euler(0, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, 0);
                }
            }
        }
        private void Zone(CrossingRegion type, Vector3 centre, Vector3 size)
        {
            GameObject go = new GameObject(type + "Zone"); go.transform.SetParent(root, false); go.transform.localPosition = centre;
            CrossingZone zone = go.AddComponent<CrossingZone>(); zone.region = type; zone.size = size; zones.Add(zone);
        }
        private void Building(Vector3 p, int side, float height, string color)
        {
            int bay = Mathf.RoundToInt(p.x / 13);
            int type = (bay + 2 + (side > 0 ? 0 : 3)) % 6;
            Transform block;
            if (side > 0 && bay == 1)
            {
                block = new GameObject("Mission shop frontage block").transform; block.SetParent(root, false); block.localPosition = p;
                StreetDistrictBuilder.Building(block, this, profile, new Vector3(-3.4f, 0, 0), side, height, StreetFacadeKind.Bookshop, false, false, 3.24f);
                StreetDistrictBuilder.Building(block, this, profile, Vector3.zero, side, height, StreetFacadeKind.Pharmacy, false, false, 3.24f);
                StreetDistrictBuilder.Building(block, this, profile, new Vector3(3.4f, 0, 0), side, height, StreetFacadeKind.Cafe, false, false, 3.24f);
                StreetDistrictBuilder.CafeSeating(block);
            }
            else if (side < 0 && bay == 0)
                block = modules.CreateRefinedBuilding(root, p, side, height);
            else block = StreetDistrictBuilder.Building(root, this, profile, p, side, height, profile.FacadeAt(type));
            // Refined facades carry their own unscaled "Collision proxy" child; every other facade
            // still needs the shared envelope collider on the block root.
            BoxCollider collider = block.gameObject.GetComponent<BoxCollider>();
            if (collider == null && block.Find("Collision proxy") == null)
            {
                collider = block.gameObject.AddComponent<BoxCollider>();
                collider.center = Vector3.up * (height * .5f); collider.size = new Vector3(10.3f, height, 4);
            }
        }
        private static void Inspectable(GameObject target, string title, string description)
        { Player.StreetInspectable sign = target.AddComponent<Player.StreetInspectable>(); sign.title = title; sign.description = description; }
        private void PhysicalBounds(string name, Vector3 centre, Vector3 size)
        {
            Transform geometry = root.Find("Additional world geometry");
            if (geometry == null) { geometry = new GameObject("Additional world geometry").transform; geometry.SetParent(root, false); }
            GameObject obstacle = new GameObject(name); obstacle.transform.SetParent(geometry, false); obstacle.transform.localPosition = centre;
            obstacle.AddComponent<BoxCollider>().size = size;
        }
        private void BuildFurnitureBounds(StreetScenarioDefinition s)
        {
            PhysicalBounds("Neighbourhood bench bounds", new Vector3(6.8f, .5f, StreetLayout.SidewalkZ(-7.5f)), new Vector3(2.25f, 1, .65f));
            // Pergola columns (StreetVisualDetails.GardenEntrance) are visual-only; these blockers stop
            // the player from clipping through them on the way to the residence doorstep. The pergola
            // visual only exists in the Residential district, so the blockers must follow the same gate.
            if (profile.identity == StreetVisualIdentity.Residential)
                for (int side = -1; side <= 1; side += 2)
                    PhysicalBounds("Pergola column bounds", new Vector3(7.5f + side * 1.8f, 1.65f, StreetLayout.SidewalkZ(-8.7f)), new Vector3(.18f, 3.3f, .18f));
            // The kerbside hydrants (StreetFurnitureBuilder) are visual-only, so the player can walk
            // straight through them. These twins mirror the builder's positions exactly: the x offset
            // follows hydrantSide (not side), and |z| = 6.9 sits just past the kerb line on purpose.
            for (int hydrantSide = -1; hydrantSide <= 1; hydrantSide += 2)
                PhysicalBounds("Kerbside fire hydrant bounds",
                    new Vector3(hydrantSide > 0 ? -16.5f : 14.5f, .31f, StreetLayout.SidewalkZ(hydrantSide * 3.6f)),
                    new Vector3(.30f, .62f, .30f));
            for (int side = -1; side <= 1; side += 2)
            {
                PhysicalBounds("Cycle stand bounds", new Vector3((side > 0 ? 19 : 17) + .8f, .44f, StreetLayout.SidewalkZ(side * 8.4f)), new Vector3(2.3f, .88f, .72f));
                PhysicalBounds("Litter bin bounds", new Vector3(25, .52f, StreetLayout.SidewalkZ(side * 7.8f)), new Vector3(.62f, 1.04f, .62f));
                // The imported utility cabinets are visual-only (StreetVisualContractTests forbids
                // colliders under VisualDetailsRoot), so their blockers live here instead and stop
                // the player from walking through the box.
                PhysicalBounds("Utility cabinet bounds", new Vector3(side * 4f, .56f, StreetLayout.SidewalkZ(side * 8.2f)), new Vector3(.56f, 1.12f, .46f));
                PhysicalBounds("Secondary utility cabinet bounds", new Vector3(side * 22.5f, .43f, StreetLayout.SidewalkZ(side * 8.2f)), new Vector3(.72f, .86f, .36f));
                // The discarded tyre by each maintenance gate is a knee-high solid object; give it a
                // blocker so the player cannot walk through it. Half width .31 keeps the far face at
                // |x| = 38.79, still clear of the gate envelope collider that starts at |x| = 38.85.
                for (int end = -1; end <= 1; end += 2)
                    PhysicalBounds("Old tyre bounds", new Vector3(end * 38.48f, .30f, StreetLayout.SidewalkZ(side * 7.8f)), new Vector3(.62f, .58f, .62f));
                for (int slot = -2; slot <= 2; slot++)
                {
                    float x = slot * profile.furnishingSpacing;
                    if (slot != 0 && Mathf.Abs(x) < 37 && (!s.turningTraffic || Mathf.Abs(x + 9 * s.DirectionForLane(0)) > 6))
                        PhysicalBounds("Kerb bollard bounds", new Vector3(x, .46f, StreetLayout.SidewalkZ(side * 3.85f)), new Vector3(.13f, .92f, .13f));
                }
                float height = s.turningTraffic ? (side > 0 ? 10.2f : 8.8f) : 9.8f;
                Vector3 corner = new Vector3(s.turningTraffic ? (-9 - 8.4f) * s.DirectionForLane(0) : 36, height * .5f, StreetLayout.SidewalkZ(side * (s.turningTraffic ? 12 : 11.3f)));
                Transform geometry = root.Find("Additional world geometry");
                GameObject obstacle = new GameObject("Corner building bounds"); obstacle.transform.SetParent(geometry, false); obstacle.transform.localPosition = corner;
                obstacle.transform.localRotation = Quaternion.Euler(0, side > 0 ? 0 : 180, 0);
                // Match the visible cut corner rather than leaving an invisible rectangular obstruction outside its doorway.
                obstacle.AddComponent<MeshCollider>().sharedMesh = CornerMesh(new Vector3(s.turningTraffic ? 6.6f : 7.1f, height, 4), side > 0 ? 1.15f : .7f, side > 0);
            }
        }
        private void Shop()
        {
            Transform doorway = new GameObject("Doorway Convenience").transform; doorway.SetParent(root, false); doorway.localPosition = new Vector3(0, 0, StreetLayout.SidewalkZ(9.5f));
            Box("Convenience shop", new Vector3(0, 2.1f, StreetLayout.SidewalkZ(12)), new Vector3(10.5f, 4.2f, 4.3f), profile.facade);
            Inspectable(Box("Shop sign backing", new Vector3(0, 3.8f, StreetLayout.SidewalkZ(9.79f)), new Vector3(10.7f, 1, 0.2f), profile.accent), "街角便利店", "本次目的地是便利店门前的绿色地垫。安全走完斑马线后，继续沿人行道前往门口。");
            // Both scripts sit above the projecting canopy even from the destination eye height, with normal scene depth.
            Label("街 角 便 利 店", new Vector3(0, 3.78f, StreetLayout.SidewalkZ(9.65f)), 0.16f, "FAF1D9", null, 0, 9.5f, 0.4f);
            for (int n = -1; n <= 1; n++)
            {
                Box("Shopfront frame", new Vector3(n * 3, 1.45f, StreetLayout.SidewalkZ(9.78f)), new Vector3(2.8f, 2.45f, 0.14f), "D0B995").GetComponent<Renderer>().enabled = false;
                Box("Shop glazing", new Vector3(n * 3, 1.45f, StreetLayout.SidewalkZ(9.68f)), new Vector3(2.55f, 2.2f, 0.06f), "85988A").GetComponent<Renderer>().enabled = false;
                Box("Shop transom", new Vector3(n * 3, 1.65f, StreetLayout.SidewalkZ(9.6f)), new Vector3(2.6f, 0.06f, 0.08f), "E5DBC6", false);
            }
            Box("Striped awning", new Vector3(0, 2.6f, StreetLayout.SidewalkZ(9.25f)), new Vector3(10.5f, 0.16f, .95f), profile.secondary);
            for (int n = -5; n <= 5; n++) Box("Awning stripe", new Vector3(n * 0.9f, 2.69f, StreetLayout.SidewalkZ(9.25f)), new Vector3(0.4f, 0.015f, .95f), profile.accent, false);
            Box("Planter", new Vector3(4.25f, 0.35f, StreetLayout.SidewalkZ(8.8f)), new Vector3(1.2f, 0.7f, 0.8f), "A87857");
            Shape(PrimitiveType.Sphere, "Planter greenery", root, new Vector3(4.25f, 0.9f, StreetLayout.SidewalkZ(8.8f)), new Vector3(1.1f, 0.65f, 0.8f), "667C49", false);
        }
        // The shelter shell (roof, posts, back panel, glazing, slatted seat) is the detailed build in
        // StreetVisualDetails.Shelter; only the collision and interaction fixtures that PlayMode tests
        // resolve by name stay here, as direct children of the environment root.
        private void BusStop(Vector3 p)
        {
            // The visible seat is the slatted build in StreetVisualDetails (finished top 0.450 m). This
            // block keeps only its collider, sized onto the slat envelope so the retained blocker is
            // the seat surface itself instead of a floating invisible shelf above it.
            Box("Bus seat", p + new Vector3(0, 0.4225f, 0.25f), new Vector3(2.7f, 0.055f, 0.36f), "8E7656").GetComponent<Renderer>().enabled = false;
            Box("Bus sign pole", p + new Vector3(-2.2f, 1.4f, -0.3f), new Vector3(0.07f, 2.8f, 0.07f), "717B6A");
            Inspectable(Box("Bus stop board", p + new Vector3(-2.2f, 2.25f, -0.3f), new Vector3(0.95f, 1, 0.1f), "F4EBD6"), "街心公交站", "候车区位于人行道内。请保留斑马线入口和盲道通道，过街时以行人信号和实际车流为准。");
        }
        private void Tree(Vector3 p)
        {
            // Keep the original planter collider used by clearance tests; replace only the
            // primitive trunk and low-poly crown with the refined procedural tree model.
            Box("Tree pit", p + new Vector3(0, 0.07f, 0), new Vector3(1.4f, 0.14f, 1.4f), "9D967C");
            int variant = Mathf.Abs(Mathf.RoundToInt(p.x * 13f + p.z * 7f)) % 3;
            Transform tree = modules.CreateRefinedTree(root, p, variant);
            AddVisualLod(tree, .006f);
        }
        private void Lamp(Vector3 p)
        {
            // Try the imported CC0 cast-iron street lamp first; fall back to the procedural assembly.
            Transform imported = modules.SpawnImported(root, "street_lamp_01", p, 3.9f);
            if (imported != null)
            {
                AddVisualLod(imported, .012f);
                if (Mathf.Abs(p.x) <= 17f)
                {
                    float face = -Mathf.Sign(p.z);
                    Box("Street name blade", p + new Vector3(0, 3.02f, face * .10f), new Vector3(.55f, .14f, .025f), "3D5A4C", false);
                    Label("梧桐里街", p + new Vector3(0, 3.02f, face * .115f), .05f, "F3EBDD", null, face > 0 ? 0 : 180, .5f, .12f);
                }
                return;
            }
            Shape(PrimitiveType.Cylinder, "Streetlight pole", root, p + Vector3.up * 2.2f, new Vector3(0.09f, 2.2f, 0.09f), "686D60");
            Box("Streetlight arm", p + new Vector3(0, 4.35f, -0.3f), new Vector3(0.12f, 0.12f, 0.8f), "686D60", false);
            Box("Streetlight head", p + new Vector3(0, 4.3f, -0.65f), new Vector3(0.4f, 0.16f, 0.55f), "C7C4A8", false);
            // Base flange, diagonal brace and an opal diffuser so the head stops reading as a bare block.
            Box("Streetlight base flange", p + new Vector3(0, .03f, 0), new Vector3(.22f, .06f, .22f), "5E635A", false);
            GameObject brace = Box("Streetlight arm brace", p + new Vector3(0, 4.12f, -.28f), new Vector3(.045f, .045f, .72f), "686D60", false);
            brace.transform.rotation = Quaternion.Euler(-34, 0, 0);
            GameObject diffuser = Box("Streetlight diffuser", p + new Vector3(0, 4.21f, -.65f), new Vector3(.34f, .02f, .47f), "E9E4CE", false);
            diffuser.GetComponent<Renderer>().sharedMaterial = Material("E9E4CE", .55f);
            if (Mathf.Abs(p.x) <= 17f)
            {
                // Street name blades face the carriageway on the two lamps nearest the crossing.
                float face = -Mathf.Sign(p.z);
                Box("Street name blade", p + new Vector3(0, 3.02f, face * .10f), new Vector3(.55f, .14f, .025f), "3D5A4C", false);
                Label("梧桐里街", p + new Vector3(0, 3.02f, face * .115f), .05f, "F3EBDD", null, face > 0 ? 0 : 180, .5f, .12f);
            }
        }
        public void DetailVehicle(GameObject vehicle, int variant = 0)
        {
            variant = Mathf.Clamp(variant, 0, 2);
            StreetVehicleVisualCache cache = vehicle.GetComponent<StreetVehicleVisualCache>();
            if (cache == null) cache = vehicle.AddComponent<StreetVehicleVisualCache>();
            if (cache.Get(variant) != null) { cache.Select(variant); return; }
            Transform model = modules.CreateVehicle(vehicle.transform, variant);
            model.name = "Reusable vehicle model " + variant;
            // Variant dimensions: sedan 4.2 m, hatchback 3.7 m, van 4.8 m.
            float length = variant == 2 ? 4.8f : variant == 1 ? 3.7f : 4.2f;
            float halfWidth = variant == 2 ? 1.02f : 0.94f;
            // Measured module tyre centres (prefab local z): sedan 1.26, hatchback 1.11, van 1.44.
            float wheelZ = variant == 2 ? 1.44f : variant == 1 ? 1.11f : 1.26f;
            // Wheel arches give the low-poly body a believable envelope around the tyres.
            for (int side = -1; side <= 1; side += 2)
            for (int end = -1; end <= 1; end += 2)
            {
                Transform arch = Shape(PrimitiveType.Sphere, "Wheel arch", model,
                    new Vector3(side * (halfWidth + .01f), .62f, end * wheelZ),
                    new Vector3(.14f, .22f, .38f), "1A1A1A", false).transform;
                arch.localRotation = Quaternion.Euler(0, 0, side * 12);
            }
            // Thin door panel lines break up the flat side slabs.
            for (int side = -1; side <= 1; side += 2)
            {
                VisualRounded(model, "Door panel line", new Vector3(side * (halfWidth + .005f), .88f, 0),
                    new Vector3(.015f, .06f, length * .55f), "111111", .003f, .12f);
                VisualRounded(model, "Side rub strip", new Vector3(side * (halfWidth + .005f), .52f, 0),
                    new Vector3(.012f, .04f, length * .58f), "1F1F1F", .002f, .25f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                // Headlamp with a slightly recessed bezel instead of a bare cube.
                VisualRounded(model, "Headlamp bezel", new Vector3(side * 0.55f, 0.66f, length * 0.5f - .015f),
                    new Vector3(0.34f, 0.19f, 0.04f), "2A2A2A", .02f, .35f);
                Shape(PrimitiveType.Cube, "Headlamp", model, new Vector3(side * 0.55f, 0.66f, length * 0.5f + .005f),
                    new Vector3(0.30f, 0.15f, 0.04f), "F5EBC6", false);
                // Tail lamp with housing.
                VisualRounded(model, "Tail lamp housing", new Vector3(side * 0.55f, 0.66f, -length * 0.5f - .015f),
                    new Vector3(0.31f, 0.19f, 0.04f), "2A2A2A", .02f, .35f);
                Shape(PrimitiveType.Cube, "Tail lamp", model, new Vector3(side * 0.55f, 0.66f, -length * 0.5f - .002f),
                    new Vector3(0.27f, 0.15f, 0.04f), "A74435", false);
                // Mirror on a stalk plus a rounded housing.
                Transform stalk = Shape(PrimitiveType.Cylinder, "Mirror stalk", model,
                    new Vector3(side * (halfWidth + .04f), 1.0f, .8f), new Vector3(.04f, .18f, .04f), "1F1F1F", false).transform;
                stalk.localRotation = Quaternion.Euler(70, side * 20, 0);
                VisualRounded(model, "Mirror", new Vector3(side * (halfWidth + .10f), 1.03f, .82f),
                    new Vector3(.06f, .12f, .18f), "5E6256", .015f, .45f);
                for (int end = -1; end <= 1; end += 2)
                    Shape(PrimitiveType.Cube, side < 0 ? "Turn indicator left" : "Turn indicator right", model,
                        new Vector3(side * .82f, .66f, end * (length * .5f + .015f)), new Vector3(.12f, .11f, .04f), "AC702D", false);
            }
            // Front and rear bumpers, grille and plates complete the approach/departure read of the low-poly shells.
            VisualRounded(model, "Front bumper", new Vector3(0, .40f, length * .5f + .02f), new Vector3(halfWidth * 2 - .06f, .17f, .12f), "2B2B2B", .03f, .25f);
            VisualRounded(model, "Rear bumper", new Vector3(0, .40f, -length * .5f - .02f), new Vector3(halfWidth * 2 - .06f, .17f, .12f), "2B2B2B", .03f, .25f);
            VisualRounded(model, "Front grille", new Vector3(0, .64f, length * .5f + .01f), new Vector3(.62f, .13f, .05f), "161616", .015f, .35f);
            VisualRounded(model, "Front number plate", new Vector3(0, .40f, length * .5f + .085f), new Vector3(.44f, .14f, .02f), "2456A8", .008f, .40f);
            VisualRounded(model, "Rear number plate", new Vector3(0, .40f, -length * .5f - .085f), new Vector3(.44f, .14f, .02f), "2456A8", .008f, .40f);
            // Plate text sticks to glyphs the street font actually covers (梧桐· + latin digits).
            // Per-variant plate number so every vehicle reads as distinct.
            string plate = string.Format("梧桐·{0:D4}", Mathf.Abs(variant * 127 + 801) % 10000);
            Label(plate, new Vector3(0, .40f, length * .5f + .096f), .085f, "FFFFFF", model, 0, .40f, .12f);
            Label(plate, new Vector3(0, .40f, -length * .5f - .096f), .085f, "FFFFFF", model, 180, .40f, .12f);
            GameObject exhaust = Shape(PrimitiveType.Cylinder, "Exhaust pipe", model,
                new Vector3(halfWidth * .5f, .26f, -length * .5f - .04f), new Vector3(.07f, .06f, .07f), "55554F", false);
            exhaust.transform.localRotation = Quaternion.Euler(90, 0, 0);
            exhaust.GetComponent<Renderer>().sharedMaterial = Material("55554F", .50f);
            if (variant != 2)
                VisualRounded(model, "Roof antenna", new Vector3(0, 1.50f, -length * .26f), new Vector3(.02f, .22f, .02f), "1A1A1A", .004f, .30f);
            // A thin dark slab under the floor fakes contact AO so the body stops floating over the lane.
            VisualRounded(model, "Vehicle ground shade", new Vector3(0, .022f, 0), new Vector3(halfWidth * 2 - .12f, .004f, length - .35f), "101110", .002f, 0f);
            // Property blocks animate emission without creating a material per lamp or per vehicle.
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                if (renderer.name == "Tail lamp" || renderer.name.StartsWith("Turn indicator") || renderer.name == "Headlamp")
                {
                    string key = renderer.name == "Tail lamp" ? "Vehicle brake lens" : renderer.name == "Headlamp" ? "Vehicle headlamp lens" : "Vehicle amber lens";
                    if (!materials.TryGetValue(key, out Material lens))
                    {
                        lens = new Material(source) { name = key };
                        if (renderer.name == "Headlamp")
                        {
                            ColorUtility.TryParseHtmlString("#F5EBC6", out Color headlamp);
                            lens.color = headlamp; lens.SetColor("_EmissionColor", headlamp * .55f);
                        }
                        lens.EnableKeyword("_EMISSION"); materials.Add(key, lens);
                    }
                    renderer.sharedMaterial = lens;
                }
            model.gameObject.AddComponent<StreetVehiclePresentation>().ResetVisuals();
            AddVisualLod(model, .008f); cache.Store(variant, model); cache.Select(variant);
        }
        public void ColorVehicle(GameObject car, int index)
        {
            string[] colors = { "A55F4B", "C3B18E", "687D70", "C3C3B4" };
            ColorUtility.TryParseHtmlString("#" + colors[index % colors.Length], out Color colour);
            car.GetComponent<StreetVehicleVisualCache>().SetPaint(colour);
        }
    }
}
