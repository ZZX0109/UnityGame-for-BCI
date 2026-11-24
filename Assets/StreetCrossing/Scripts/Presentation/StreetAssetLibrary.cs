using System.Collections.Generic;
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

        /// <summary>Instantiate the animated pedestrian with its feet on the actor origin.
        /// Variant drives seeded height and palette variation so sidewalk crowds are not clones.</summary>
        public (Transform root, Animator animator) CreatePedestrian(Transform parent, int variant)
        {
            GameObject go = Object.Instantiate(streetPedestrian, parent);
            go.name = "Animated pedestrian model";
            go.transform.localPosition = Vector3.zero;
            System.Random random = new System.Random(unchecked(variant ^ PedestrianSeed));
            float scale = .94f + (float)random.NextDouble() * .12f;
            go.transform.localScale *= scale; // scale first: bounds must be measured post-scale or feet drift by h*(1-scale)
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
            importedAssetLoaded = true;
            GameObject instance = Object.Instantiate(loaded, parent, false);
            instance.name = assetName + " (imported)";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            ApplyImportedMaterial(instance, assetName);
            float height = BoundsOf(instance).size.y;
            if (height > .001f) instance.transform.localScale = Vector3.one * (targetHeight / height);
            // Seat the scaled model: footprint centre on localPosition, base resting at localPosition.y.
            Bounds world = BoundsOf(instance);
            Vector3 centre = parent.InverseTransformPoint(world.center);
            Vector3 basePoint = parent.InverseTransformPoint(new Vector3(world.center.x, world.min.y, world.center.z));
            instance.transform.localPosition = new Vector3(
                localPosition.x - centre.x, localPosition.y - basePoint.y, localPosition.z - centre.z);
            return instance.transform;
        }
        /// <summary>
        /// Replace the FBX's untextured materials with one Standard PBR material built from the CC0
        /// 1k albedo/normal/roughness maps shipped under Imported/Textures. Falls back to leaving the
        /// imported material untouched when the maps are absent, so the model still renders.
        /// </summary>
        private void ApplyImportedMaterial(GameObject instance, string assetName)
        {
            if (importedMaterials == null) importedMaterials = new Dictionary<string, Material>();
            Material material;
            if (!importedMaterials.TryGetValue(assetName, out material))
            {
                Texture2D albedo = Resources.Load<Texture2D>("StreetCrossing/Imported/Textures/" + assetName + "_diff");
                Texture2D normal = Resources.Load<Texture2D>("StreetCrossing/Imported/Textures/" + assetName + "_nor");
                Texture2D roughness = Resources.Load<Texture2D>("StreetCrossing/Imported/Textures/" + assetName + "_rough");
                if (albedo != null)
                {
                    Shader shader = Shader.Find("Standard");
                    if (shader == null && Plaster != null) shader = Plaster.shader;
                    if (shader != null)
                    {
                        material = new Material(shader) { name = "Imported " + assetName };
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
                        // One shared material per asset keeps every instance in the same batch and gives
                        // DisposeImported a single owner to release.
                        importedMaterials.Add(assetName, material);
                    }
                }
                // No usable maps: keep the FBX's own material rather than blanking the model out.
                if (!importedMaterials.TryGetValue(assetName, out material)) return;
            }
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
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
