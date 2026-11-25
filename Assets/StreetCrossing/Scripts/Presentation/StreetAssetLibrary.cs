using System.Collections.Generic;
using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Serialized original modular models/materials are shared by all scenarios and pooled vehicle instances.</summary>
    public sealed class StreetAssetLibrary
    {
        private readonly GameObject[] vehicles;
        private readonly GameObject[] refinedTrees;
        private readonly GameObject refinedBuilding;
        private readonly GameObject streetPedestrian;
        private readonly Mesh[] crownMeshes;
        public Material Asphalt { get; }
        public Material Paving { get; }
        public Material Plaster { get; }
        public StreetAssetLibrary()
        {
            vehicles = new[] { Load("SedanModule"), Load("HatchbackModule"), Load("VanModule") };
            refinedTrees = new[]
            {
                Load("RefinedTreeOak"),
                Load("RefinedTreeWillow"),
                Load("RefinedTreePine")
            };
            refinedBuilding = Load("RefinedResidence");
            streetPedestrian = Load("StreetPedestrian");
            crownMeshes = new Mesh[3];
            string[] parts = { "CrownBranches", "CrownFoliage", "CrownLeafTips" };
            for (int i = 0; i < parts.Length; i++) crownMeshes[i] = Load(parts[i]).GetComponentInChildren<MeshFilter>().sharedMesh;
            Asphalt = Resources.Load<Material>("StreetCrossing/Modules/Asphalt");
            Paving = Resources.Load<Material>("StreetCrossing/Modules/PavingRefined");
            Plaster = Resources.Load<Material>("StreetCrossing/Modules/PlasterRefined");
            if (Asphalt == null || Paving == null || Plaster == null) throw new System.InvalidOperationException("Street textured materials are missing.");
        }
        private static GameObject Load(string name)
        {
            GameObject asset = Resources.Load<GameObject>("StreetCrossing/Modules/" + name);
            if (asset == null) throw new System.InvalidOperationException("Missing original module: " + name); return asset;
        }
        public Transform CreateVehicle(Transform parent, int variant)
        {
            GameObject go = Object.Instantiate(vehicles[Mathf.Clamp(variant, 0, 2)], parent); go.name = "Reusable vehicle model"; return go.transform;
        }
        public void CreateTreeCrown(Transform parent, Vector3 position, Material bark, Material foliage, Material tips)
        {
            Transform crown = new GameObject("Street tree crown").transform;
            crown.SetParent(parent, false);
            crown.localPosition = position;
            crown.localRotation = Quaternion.Euler(0, Mathf.Repeat(position.x * 31 + position.z * 19, 360), 0);
            Material[] materials = { bark, foliage, tips };
            for (int i = 0; i < crownMeshes.Length; i++)
            {
                var part = new GameObject(crownMeshes[i].name, typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(crown, false);
                part.GetComponent<MeshFilter>().sharedMesh = crownMeshes[i];
                part.GetComponent<MeshRenderer>().sharedMaterial = materials[i];
            }
        }
        private static Bounds BoundsOf(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return b;
        }
        /// <summary>Renderer AABB measured in <paramref name="space"/>'s local space. Ground
        /// alignment must use this instead of world bounds, otherwise a street root that is not
        /// at the world origin shifts every corrected model by the root offset.</summary>
        private static Bounds BoundsInSpace(GameObject go, Transform space)
        {
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                Bounds wb = r.bounds; Vector3 c = wb.center, e = wb.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = space.InverseTransformPoint(c + new Vector3(
                        (corner & 1) == 0 ? -e.x : e.x, (corner & 2) == 0 ? -e.y : e.y, (corner & 4) == 0 ? -e.z : e.z));
                    if (!any) { min = max = p; any = true; }
                    else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                }
            }
            return any ? new Bounds((min + max) * .5f, max - min) : new Bounds(Vector3.zero, Vector3.zero);
        }

        /// <summary>Instantiate a refined procedural tree and scale it to the original street tree height.</summary>
        public Transform CreateRefinedTree(Transform parent, Vector3 position, int variant)
        {
            GameObject prefab = refinedTrees[Mathf.Abs(variant) % refinedTrees.Length];
            GameObject go = Object.Instantiate(prefab, parent);
            go.name = "Refined street tree " + prefab.name;
            go.transform.localPosition = Vector3.zero;
            // Yaw first, then measure. Rotating after the ground alignment swung the trunk off
            // its pit whenever the model origin is not the footprint centre (the reported
            // floating-tree symptom).
            go.transform.rotation = Quaternion.Euler(0, (position.x * 37f + position.z * 23f) % 360f, 0) * go.transform.rotation;
            Bounds b = BoundsInSpace(go, parent);
            float target = variant % 3 == 2 ? 7.6f : variant % 3 == 1 ? 6.8f : 7.2f;
            float scale = target / Mathf.Max(0.001f, b.size.y);
            go.transform.localScale *= scale;
            b = BoundsInSpace(go, parent);
            // Footprint centred on the pit with the trunk base resting on the pit surface.
            go.transform.localPosition = position + new Vector3(-b.center.x, -b.min.y, -b.center.z);
            return go.transform;
        }

        private const float Lod0ScreenHeight = 0.15f;
        private const float Lod1ScreenHeight = 0.006f;

        /// <summary>Create the refined tree and give it a two-level LOD chain in one step. Level 0
        /// keeps the refined FBX renderers; level 1 swaps them for a procedural trunk plus the
        /// revived low-poly crown parts so far trees stop paying full FBX vertex cost. The proxy
        /// container cancels the wrapper's -90° FBX axis fix and uniform scale, so the authored
        /// crown meshes stay upright and metre-sized, scaled so the canopy tops out at the height
        /// the refined model was measured to. Exactly one SetLODs call defines both levels; below
        /// the last level the group culls the tree entirely. LOD children carry no colliders —
        /// the tree-pit box stays the only collision surface.</summary>
        public Transform CreateLodgedTree(Transform parent, Vector3 position, int variant)
        {
            Transform tree = CreateRefinedTree(parent, position, variant);
            tree = ApplyLodChain(tree, position);
            // §4.2: neither LOD level carries a collider (StreetTreeLodTests keeps it that way),
            // so the player could walk straight through the stem. Add a world-true slim box as a
            // sibling of the (possibly scaled/yawed) tree root; the flat tree-pit box stays the
            // step-over planter surface below it.
            GameObject trunkBounds = new GameObject("Tree trunk bounds");
            trunkBounds.transform.SetParent(parent, false);
            trunkBounds.transform.localPosition = position + new Vector3(0, 1.3f, 0);
            trunkBounds.layer = StreetPhysicsLayers.PlayerObstacle;
            trunkBounds.AddComponent<BoxCollider>().size = new Vector3(.24f, 2.6f, .24f);
            return tree;
        }

        /// <summary>Apply the two-level LOD chain to an already created refined tree. Safe to
        /// call more than once: a wrapper that already carries a LODGroup is returned unchanged,
        /// so a double application can never stack groups, proxy containers or renderer
        /// sets.</summary>
        public Transform ApplyLodChain(Transform tree, Vector3 position)
        {
            if (tree == null || tree.GetComponentInChildren<LODGroup>() != null) return tree;
            Transform parent = tree.parent;

            // Collect level 0 before any proxy child exists under the wrapper.
            MeshRenderer[] lod0 = tree.GetComponentsInChildren<MeshRenderer>();

            // Authored crown extents (mesh units above the trunk base) size the proxy.
            float crownTop = 1f;
            foreach (Mesh mesh in crownMeshes)
                if (mesh != null) crownTop = Mathf.Max(crownTop, mesh.bounds.max.y);
            float branchStart = crownMeshes[0] != null ? crownMeshes[0].bounds.min.y : crownTop * .4f;

            Bounds treeBounds = BoundsInSpace(tree.gameObject, parent);
            float treeHeight = Mathf.Max(.5f, treeBounds.size.y);
            float worldScale = treeHeight / crownTop;

            Transform proxy = new GameObject("Distance proxy").transform;
            proxy.SetParent(tree, false);
            proxy.localPosition = tree.InverseTransformPoint(parent.TransformPoint(position));
            // Reuse the tree's own yaw so the proxy silhouette matches level 0 at the swap.
            float yaw = (position.x * 37f + position.z * 23f) % 360f;
            proxy.localRotation = Quaternion.Inverse(tree.rotation) * Quaternion.Euler(0f, yaw, 0f);
            float localScale = worldScale / Mathf.Max(1e-3f, tree.lossyScale.x);
            proxy.localScale = new Vector3(localScale, localScale, localScale);

            CollectTreeMaterials(lod0, out Material bark, out Material[] leaves);
            Material[] partMaterials = leaves.Length > 0
                ? new[] { bark, leaves[0], leaves[Mathf.Min(1, leaves.Length - 1)] }
                : new Material[] { bark, bark, bark };
            for (int i = 0; i < crownMeshes.Length; i++)
            {
                if (crownMeshes[i] == null) continue;
                GameObject part = new GameObject(crownMeshes[i].name, typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(proxy, false);
                part.GetComponent<MeshFilter>().sharedMesh = crownMeshes[i];
                if (partMaterials[i] != null) part.GetComponent<MeshRenderer>().sharedMaterial = partMaterials[i];
            }

            // Code trunk from just under the pit surface up into the branch mass.
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            trunk.name = "Proxy trunk";
            trunk.transform.SetParent(proxy, false);
            float trunkBottom = -.05f, trunkTop = branchStart + .35f;
            trunk.transform.localPosition = new Vector3(0f, (trunkBottom + trunkTop) * .5f, 0f);
            trunk.transform.localScale = new Vector3(.18f, (trunkTop - trunkBottom) * .5f, .18f);
            if (bark != null) trunk.GetComponent<MeshRenderer>().sharedMaterial = bark;

            LOD[] levels =
            {
                new LOD(Lod0ScreenHeight, lod0),
                new LOD(Lod1ScreenHeight, proxy.GetComponentsInChildren<MeshRenderer>())
            };
            LODGroup group = tree.gameObject.AddComponent<LODGroup>();
            group.SetLODs(levels);
            // Bake the cull volume now that both levels exist; Unity would otherwise derive it
            // lazily and the first frame could pop the whole tree out of view.
            group.RecalculateBounds();
            return tree;
        }

        /// <summary>Split the refined tree's material slots into bark and foliage groups so the
        /// LOD1 proxy reuses the FBX materials instead of introducing new assets.</summary>
        private static void CollectTreeMaterials(Renderer[] renderers, out Material bark, out Material[] leaves)
        {
            bark = null;
            List<Material> found = new List<Material>();
            List<Material> foliage = new List<Material>();
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || found.Contains(material)) continue;
                    found.Add(material);
                    string name = material.name;
                    if (name.IndexOf("bark", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { if (bark == null) bark = material; }
                    else if (name.IndexOf("leaf", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("foliage", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        foliage.Add(material);
                }
            }
            if (bark == null && found.Count > 0) bark = found[0];
            if (foliage.Count == 0) foliage.AddRange(found);
            if (foliage.Count == 0 && bark != null) foliage.Add(bark);
            leaves = foliage.ToArray();
        }

        /// <summary>Instantiate the refined near-field residence with real frames, awning and balcony.</summary>
        public Transform CreateRefinedBuilding(Transform parent, Vector3 position, int side, float height)
        {
            GameObject go = Object.Instantiate(refinedBuilding, parent);
            go.name = "Facade " + StreetFacadeKind.Residence;
            go.transform.localPosition = Vector3.zero;
            // FBX import maps Blender -Y to Unity +Z; keep the importer's Z-up fix, then face the street.
            go.transform.rotation = Quaternion.Euler(0, side > 0 ? 180f : 0f, 0f) * go.transform.rotation;
            Bounds b = BoundsInSpace(go, parent);
            float widthScale = 10.3f / Mathf.Max(0.001f, b.size.x);
            go.transform.localScale *= widthScale;
            b = BoundsInSpace(go, parent);
            float heightScale = height / Mathf.Max(0.001f, b.size.y);
            go.transform.localScale *= heightScale;
            b = BoundsInSpace(go, parent);
            go.transform.localPosition = position + new Vector3(-b.center.x, -b.min.y, -b.center.z);
            // Isolate the display transform from the physics proxy: the facade is scaled to fit
            // its parcel, so a BoxCollider on `go` would get its world size multiplied by
            // lossyScale and drift away from collider.size. This unscaled child counter-scales
            // the parent's lossyScale so it sits at world scale 1 and collider.size/center
            // below stay world-true (10.3 x height x 4 footprint centred at half height).
            Vector3 displayScale = go.transform.lossyScale;
            GameObject proxy = new GameObject("Collision proxy");
            proxy.transform.SetParent(go.transform, false);
            proxy.layer = StreetPhysicsLayers.PlayerObstacle;
            proxy.transform.localScale = new Vector3(
                1f / Mathf.Max(.0001f, displayScale.x),
                1f / Mathf.Max(.0001f, displayScale.y),
                1f / Mathf.Max(.0001f, displayScale.z));
            BoxCollider collider = proxy.AddComponent<BoxCollider>();
            collider.size = new Vector3(10.3f, height, 4f);
            collider.center = Vector3.up * (height * .5f);
            return go.transform;
        }

        private static readonly Dictionary<string, Material> PedestrianPalette = new Dictionary<string, Material>();
        private const int PedestrianSeed = 0x3F1B293;
        private static readonly Color[] OutfitColors =
        {
            new Color(.86f, .30f, .26f), new Color(.24f, .42f, .62f), new Color(.93f, .78f, .34f),
            new Color(.36f, .55f, .45f), new Color(.55f, .33f, .24f), new Color(.30f, .30f, .34f),
            new Color(.78f, .74f, .68f), new Color(.62f, .38f, .58f)
        };
        private static readonly Color[] SkinColors =
        {
            new Color(.98f, .84f, .72f), new Color(.92f, .74f, .58f), new Color(.80f, .60f, .44f), new Color(.55f, .38f, .26f)
        };
        private static readonly Color[] HairColors =
        {
            new Color(.16f, .12f, .10f), new Color(.36f, .22f, .12f), new Color(.78f, .62f, .34f),
            new Color(.55f, .28f, .16f), new Color(.72f, .72f, .72f)
        };
        private static readonly Color[] ShoeColors =
        {
            new Color(.12f, .12f, .13f), new Color(.42f, .28f, .16f), new Color(.85f, .83f, .80f)
        };

        /// <summary>Silhouette knobs for one body type. Axes were measured in Blender on the
        /// trimmed Mixamo rig (BuildOutput/fbx_axes.py): spine localY runs up the back, localX
        /// spans width, localZ is depth; shoulder localY runs along the bone. Hips localY and
        /// the leg bones are never scaled, so leg length — and therefore the planted feet —
        /// cannot drift. The Animator clips only author position/rotation curves, so bone
        /// scale set once at spawn survives every state transition on the Generic rig.</summary>
        public struct Silhouette
        {
            public float hipsX;
            public float spineX;
            public float spineZ;
            public float spineY;
            public float shoulderY;
            public float headScale;
        }

        private static readonly Silhouette[] Silhouettes =
        {
            new Silhouette { hipsX = 1.00f, spineX = 1.00f, spineZ = 1.00f, spineY = 1.00f, shoulderY = 1.00f, headScale = 1.00f },
            new Silhouette { hipsX = 1.12f, spineX = 1.10f, spineZ = 1.08f, spineY = 0.98f, shoulderY = 1.06f, headScale = 1.03f },
            new Silhouette { hipsX = 0.92f, spineX = 0.90f, spineZ = 0.94f, spineY = 1.01f, shoulderY = 0.94f, headScale = 0.97f },
            new Silhouette { hipsX = 0.96f, spineX = 0.95f, spineZ = 0.92f, spineY = 1.02f, shoulderY = 1.05f, headScale = 0.95f }
        };

        /// <summary>Number of distinct body-type profiles the crowd draws from.</summary>
        public static int SilhouetteCount => Silhouettes.Length;

        /// <summary>Deterministic body type plus seeded per-variant jitter. Self-contained
        /// (own Random on the shared PedestrianSeed) so tests can assert stability without
        /// instantiating the prefab.</summary>
        public static Silhouette SilhouetteFor(int variant)
        {
            Silhouette s = Silhouettes[Mathf.Abs(variant) % Silhouettes.Length];
            System.Random random = new System.Random(unchecked(variant ^ PedestrianSeed));
            s.hipsX *= .96f + (float)random.NextDouble() * .08f;
            s.spineX *= .96f + (float)random.NextDouble() * .08f;
            s.spineZ = Mathf.Clamp(s.spineZ * (.96f + (float)random.NextDouble() * .08f), .90f, 1.13f);
            s.spineY *= .99f + (float)random.NextDouble() * .02f;
            s.shoulderY *= .97f + (float)random.NextDouble() * .06f;
            s.headScale *= .97f + (float)random.NextDouble() * .06f;
            return s;
        }

        /// <summary>Scale rig bones so variants read as different body types, not just
        /// recolours. Root scale and feet planting stay untouched: hips/leg length axes are
        /// never scaled, so bind-pose bounds keep matching the planted feet.</summary>
        private static void ApplySilhouette(GameObject go, int variant)
        {
            Silhouette s = SilhouetteFor(variant);
            foreach (Transform bone in go.GetComponentsInChildren<Transform>())
            {
                string n = bone.name;
                // EndsWith keeps matching whether or not the importer prefixes "mixamorig:".
                if (n.EndsWith("Hips"))
                {
                    bone.localScale = new Vector3(s.hipsX, 1f, s.hipsX);
                }
                else if (n.EndsWith("Spine1") || n.EndsWith("Spine2") || n.EndsWith("Spine"))
                {
                    bone.localScale = new Vector3(s.spineX, s.spineY, s.spineZ);
                }
                else if (n.EndsWith("LeftShoulder") || n.EndsWith("RightShoulder"))
                {
                    bone.localScale = new Vector3(1f, s.shoulderY, 1f);
                }
                else if (n.EndsWith("Head"))
                {
                    bone.localScale = Vector3.one * s.headScale;
                }
            }
        }

        /// <summary>Instantiate the animated pedestrian with its feet on the actor origin.
        /// Variant drives seeded height, body-type silhouette and palette variation so
        /// sidewalk crowds are not clones.</summary>
        public (Transform root, Animator animator) CreatePedestrian(Transform parent, int variant)
        {
            GameObject go = Object.Instantiate(streetPedestrian, parent);
            go.name = "Animated pedestrian model";
            go.transform.localPosition = Vector3.zero;
            System.Random random = new System.Random(unchecked(variant ^ PedestrianSeed));
            float scale = .94f + (float)random.NextDouble() * .12f;
            go.transform.localScale *= scale; // scale first: bounds must be measured post-scale or feet drift by h*(1-scale)
            ApplySilhouette(go, variant); // bone scale after root scale, before bounds: never touches length axes, so planting stays exact
            Bounds b = BoundsInSpace(go, parent);
            // Feet on the actor origin, measured in the actor's own space so the yaw applied by
            // the route follower keeps them planted.
            go.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            ApplyVariation(go, random);
            Animator animator = go.GetComponentInChildren<Animator>();
            return (go.transform, animator);
        }

        /// <summary>Recolour shared materials by name heuristic (outfit/skin/hair/shoes/eyes).
        /// Tinted clones are cached per source+tint so equal variants keep static batching;
        /// unmatched material names stay untouched, keeping arbitrary rigs safe.</summary>
        private static void ApplyVariation(GameObject go, System.Random random)
        {
            Color outfit = OutfitColors[random.Next(OutfitColors.Length)];
            Color skin = SkinColors[random.Next(SkinColors.Length)];
            Color hair = HairColors[random.Next(HairColors.Length)];
            Color shoes = ShoeColors[random.Next(ShoeColors.Length)];
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                Material[] shared = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    Color? tint = TintFor(shared[i].name.ToLowerInvariant(), outfit, skin, hair, shoes);
                    if (tint == null) continue;
                    shared[i] = TintedMaterial(shared[i], tint.Value);
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = shared;
            }
        }

        private static Color? TintFor(string name, Color outfit, Color skin, Color hair, Color shoes)
        {
            // Order matters: "eyes_brownlight" must not hit "brow", "outfit_shoes01" must hit shoe first.
            if (name.Contains("eyebrow")) return hair;
            if (name.Contains("eyes")) return new Color(.30f, .22f, .15f);
            if (name.Contains("eyelash")) return null;
            if (name.Contains("shoe") || name.Contains("boot")) return shoes;
            if (name.Contains("skin") || name.Contains("face")) return skin;
            if (name.Contains("hair")) return hair;
            if (name.Contains("outfit") || name.Contains("suit") || name.Contains("shirt") ||
                name.Contains("jacket") || name.Contains("pant") || name.Contains("clothes")) return outfit;
            return null;
        }

        private static Material TintedMaterial(Material source, Color tint)
        {
            string html = ColorUtility.ToHtmlStringRGB(tint);
            string key = source.name + "|" + html;
            Material cached;
            if (!PedestrianPalette.TryGetValue(key, out cached) || cached == null)
            {
                cached = new Material(source);
                cached.name = source.name + "_" + html;
                cached.color = Color.Lerp(Color.white, tint, .8f);
                PedestrianPalette[key] = cached;
            }
            return cached;
        }

        // Imported CC0 glTF models live under Resources/StreetCrossing/Imported/Models and load lazily.
        private GameObject importedRollershutterDoor;
        private GameObject importedWaterManholeCover;
        private GameObject importedOldTyre;
        private GameObject importedUtilityBox01;
        private GameObject importedUtilityBox02;
        private GameObject importedMetalTrashCan;
        private GameObject importedSecurityLight;
        private GameObject importedRollershutterWindow01;
        private GameObject importedFireHydrant;
        private GameObject importedStreetLamp01;
        private GameObject importedModularFireEscape;
        private GameObject importedExteriorAirconUnit;
        private Dictionary<string, Material> importedMaterials;
        private bool importedAssetLoaded;
        /// <summary>True once at least one imported CC0 model was loaded successfully.</summary>
        public bool HasImportedAssets => importedAssetLoaded;
        /// <summary>
        /// Instantiate an imported CC0 model under <paramref name="parent"/>, normalise it to
        /// <paramref name="targetHeight"/> and seat its footprint on <paramref name="localPosition"/>
        /// (base at that height, centred in X/Z). Returns null when the asset is unavailable so every
        /// caller keeps its procedural fallback.
        /// </summary>
        public Transform SpawnImported(Transform parent, string assetName, Vector3 localPosition, float targetHeight)
        {
            GameObject loaded = GetImportedModel(assetName);
            if (loaded == null)
            {
                loaded = Resources.Load<GameObject>("StreetCrossing/Imported/Models/" + assetName);
                if (loaded == null)
                {
                    Debug.LogWarning(string.Format("imported model '{0}' unavailable, using fallback", assetName));
                    return null;
                }
                SetImportedModel(assetName, loaded);
            }
            // Measure in the installation frame before parenting to a rotated facade.
            Transform mount = new GameObject(assetName + " (imported)").transform;
            GameObject visual = Object.Instantiate(loaded, mount, false);
            visual.transform.localPosition = Vector3.zero;
            // Some CC0 files contain clean and weathered alternatives side by side.
            foreach (Transform part in visual.GetComponentsInChildren<Transform>())
                if (part.name.StartsWith(assetName + "_rust") || part.name.StartsWith(assetName + "_graffiti"))
                    part.gameObject.SetActive(false);
            Bounds bounds = BoundsInSpace(visual, mount);
            if (bounds.size.y <= .00001f || targetHeight <= 0f)
                return RejectImported(mount);
            Vector3 scale = Vector3.one * (targetHeight / bounds.size.y);
            if (assetName == "water_manhole_cover")
            {
                // Keep the authored lid diameter, only recess the casting to road height.
                scale.x = scale.z = 1f;
            }
            visual.transform.localScale = Vector3.Scale(visual.transform.localScale, scale);
            bounds = BoundsInSpace(visual, mount);
            Vector3 foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            if (assetName == "street_lamp_01" && !TryLampBase(visual, bounds, out foot))
                return RejectImported(mount);
            visual.transform.localPosition -= foot;
            ApplyImportedMaterial(visual, assetName);
            mount.SetParent(parent, false);
            mount.localPosition = localPosition;
            importedAssetLoaded = true;
            return mount;
        }

        private static Transform RejectImported(Transform mount)
        {
            mount.gameObject.SetActive(false);
            if (Application.isPlaying) Object.Destroy(mount.gameObject);
            else Object.DestroyImmediate(mount.gameObject);
            return null;
        }

        private static bool TryLampBase(GameObject visual, Bounds bounds, out Vector3 foot)
        {
            foot = Vector3.zero;
            // Reject a sideways or oversized import before it can span the crossing.
            if (!StreetImportedAssetProfile.TryGet("street_lamp_01", out var profile) ||
                Mathf.Max(bounds.size.x, bounds.size.z) > profile.MaxFootprintMaxEdge) return false;
            MeshFilter filter = visual.GetComponentInChildren<MeshFilter>();
            if (filter == null || !filter.sharedMesh || !filter.sharedMesh.isReadable) return false;
            Mesh mesh = filter.sharedMesh;
            Material[] slots = filter.GetComponent<Renderer>().sharedMaterials;
            int bulb = System.Array.FindIndex(slots, slot => slot != null && slot.name.EndsWith("_bulb"));
            if (bulb < 0 || bulb >= mesh.subMeshCount) return false;
            Vector3 bulbCentre = filter.transform.TransformPoint(mesh.GetSubMesh(bulb).bounds.center);
            if (bulbCentre.y < bounds.min.y + bounds.size.y * .7f) return false;
            Bounds baseRing = default;
            bool found = false;
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 point = filter.transform.TransformPoint(vertex);
                if (point.y > bounds.min.y + .025f) continue;
                if (!found) baseRing = new Bounds(point, Vector3.zero);
                else baseRing.Encapsulate(point);
                found = true;
            }
            foot = new Vector3(baseRing.center.x, bounds.min.y, baseRing.center.z);
            return found;
        }
        /// <summary>
        /// Replace the FBX's untextured materials with Standard PBR materials built from the CC0
        /// 1k albedo/normal/roughness maps shipped under Imported/Textures. Falls back to leaving the
        /// imported material untouched when the maps are absent, so the model still renders.
        /// Multi-slot assets (Poly Haven FBX ships one material per part, e.g.
        /// <c>modular_fire_escape_01</c>/<c>_02</c>) get one material per original slot, looked up
        /// from slot-prefixed textures; single-slot assets keep the plain behaviour.
        /// </summary>
        private void ApplyImportedMaterial(GameObject instance, string assetName)
        {
            if (importedMaterials == null) importedMaterials = new Dictionary<string, Material>();
            Material material;
            if (!importedMaterials.TryGetValue(assetName, out material))
            {
                material = BuildTexturedMaterial(assetName, null);
                if (material != null) importedMaterials.Add(assetName, material);
            }
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Material[] shared = renderer.sharedMaterials;
                if (shared.Length <= 1)
                {
                    // Single-slot asset (or no base maps): the plain behaviour covers it, and the
                    // FBX's own material is kept when no maps exist at all.
                    if (material != null) renderer.sharedMaterials = new Material[] { material };
                    continue;
                }
                // Preserve the original slot count: one slot-prefix material per slot, falling back
                // to the base material, then to the FBX's own material when nothing resolves.
                var materials = new Material[shared.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    if (assetName == "street_lamp_01" && shared[i] != null &&
                        (shared[i].name.EndsWith("_glass") || shared[i].name.EndsWith("_bulb")))
                    {
                        materials[i] = shared[i];
                        continue;
                    }
                    int slotIndex = ParseSlotIndex(assetName, shared[i] != null ? shared[i].name : string.Empty);
                    string slotKey = assetName + "_" + slotIndex.ToString("00");
                    Material slotMaterial;
                    if (!importedMaterials.TryGetValue(slotKey, out slotMaterial))
                    {
                        slotMaterial = BuildTexturedMaterial(assetName, slotIndex);
                        if (slotMaterial != null) importedMaterials.Add(slotKey, slotMaterial);
                    }
                    materials[i] = slotMaterial != null ? slotMaterial : (material != null ? material : shared[i]);
                }
                renderer.sharedMaterials = materials;
            }
        }
        /// <summary>Extract the numeric slot suffix (<c>modular_fire_escape_02</c> → 2) from an
        /// imported material name; 0 when the name carries no recognisable suffix.</summary>
        private static int ParseSlotIndex(string assetName, string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return 0;
            string name = materialName;
            int paren = name.IndexOf("(instance)", System.StringComparison.Ordinal);
            if (paren > 0) name = name.Substring(0, paren).TrimEnd();
            if (!name.StartsWith(assetName + "_", System.StringComparison.Ordinal)) return 0;
            string suffix = name.Substring(assetName.Length + 1);
            int parsed;
            return int.TryParse(suffix, out parsed) ? Mathf.Max(0, parsed) : 0;
        }
        /// <summary>Build one Standard material from the slot-prefixed texture triplet
        /// (<c>{asset}_{slot:00}_diff/_nor/_rough</c>); <paramref name="slotIndex"/> of null builds
        /// the plain unprefixed base material. Returns null when no albedo map exists.</summary>
        private Material BuildTexturedMaterial(string assetName, int? slotIndex)
        {
            string prefix = slotIndex.HasValue ? assetName + "_" + slotIndex.Value.ToString("00") : assetName;
            Texture2D albedo = Resources.Load<Texture2D>("StreetCrossing/Imported/Textures/" + prefix + "_diff");
            if (albedo == null) return null;
            Texture2D normal = Resources.Load<Texture2D>("StreetCrossing/Imported/Textures/" + prefix + "_nor");
            Texture2D roughness = Resources.Load<Texture2D>("StreetCrossing/Imported/Textures/" + prefix + "_rough");
            Shader shader = Shader.Find("Standard");
            if (shader == null && Plaster != null) shader = Plaster.shader;
            if (shader == null) return null;
            string materialName = slotIndex.HasValue
                ? "Imported " + assetName + " slot " + slotIndex.Value.ToString("00")
                : "Imported " + assetName;
            Material material = new Material(shader) { name = materialName };
            material.mainTexture = albedo;
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", .85f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (roughness != null)
            {
                // Roughness map is greyscale (R = roughness) while Standard expects
                // smoothness in _MetallicGlossMap.a, so the map cannot be bound
                // directly without a custom shader. Tuning the uniform gloss to a
                // balanced midpoint and scale is the best Standard-only compromise;
                // per-pixel roughness variation is a known limitation.
                material.SetFloat("_Glossiness", .38f);
                material.SetFloat("_GlossMapScale", .85f);
                material.SetFloat("_Metallic", .08f);
            }
            else
            {
                material.SetFloat("_Glossiness", .30f);
                material.SetFloat("_Metallic", .12f);
            }
            return material;
        }
        /// <summary>The primary material captured when the named model was first instantiated, or null.</summary>
        public Material GetCachedMaterialFor(string assetName)
        {
            Material material;
            return importedMaterials != null && importedMaterials.TryGetValue(assetName, out material) ? material : null;
        }
        private GameObject GetImportedModel(string assetName)
        {
            switch (assetName)
            {
                case "rollershutter_door": return importedRollershutterDoor;
                case "water_manhole_cover": return importedWaterManholeCover;
                case "old_tyre": return importedOldTyre;
                case "utility_box_01": return importedUtilityBox01;
                case "utility_box_02": return importedUtilityBox02;
                case "metal_trash_can": return importedMetalTrashCan;
                case "security_light": return importedSecurityLight;
                case "rollershutter_window_01": return importedRollershutterWindow01;
                case "fire_hydrant": return importedFireHydrant;
                case "street_lamp_01": return importedStreetLamp01;
                case "modular_fire_escape": return importedModularFireEscape;
                case "exterior_aircon_unit": return importedExteriorAirconUnit;
                default: return null;
            }
        }
        private void SetImportedModel(string assetName, GameObject asset)
        {
            switch (assetName)
            {
                case "rollershutter_door": importedRollershutterDoor = asset; break;
                case "water_manhole_cover": importedWaterManholeCover = asset; break;
                case "old_tyre": importedOldTyre = asset; break;
                case "utility_box_01": importedUtilityBox01 = asset; break;
                case "utility_box_02": importedUtilityBox02 = asset; break;
                case "metal_trash_can": importedMetalTrashCan = asset; break;
                case "security_light": importedSecurityLight = asset; break;
                case "rollershutter_window_01": importedRollershutterWindow01 = asset; break;
                case "fire_hydrant": importedFireHydrant = asset; break;
                case "street_lamp_01": importedStreetLamp01 = asset; break;
                case "modular_fire_escape": importedModularFireEscape = asset; break;
                case "exterior_aircon_unit": importedExteriorAirconUnit = asset; break;
            }
        }
        /// <summary>Destroy every cached pedestrian tint material and clear the session-lifetime
        /// palette; call on scene unload so repeated Play sessions (domain reload disabled) do not
        /// accumulate runtime materials.</summary>
        public static void DisposePedestrianPalette()
        {
            foreach (Material material in PedestrianPalette.Values) Object.Destroy(material);
            PedestrianPalette.Clear();
        }
        /// <summary>Destroy the runtime material copies built for imported models and the shared
        /// pedestrian palette; safe to call when none loaded.</summary>
        public void DisposeImported()
        {
            DisposePedestrianPalette();
            if (importedMaterials == null) return;
            foreach (Material material in importedMaterials.Values) Object.Destroy(material);
            importedMaterials.Clear();
        }
    }
}
