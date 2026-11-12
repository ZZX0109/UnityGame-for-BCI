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
        private readonly Transform root;
        private readonly Material source;
        private readonly Font font;
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<CrossingZone> zones = new List<CrossingZone>();
        public IReadOnlyList<CrossingZone> Zones => zones;
        public Transform TrafficRoot { get; private set; }
        public StreetWorld(Transform parent, Material referencedSurface, Font uiFont) { root = parent; source = referencedSurface; font = uiFont; }
        public Material Material(string hex, float smoothness = 0.18f)
        {
            if (materials.TryGetValue(hex, out Material existing)) return existing;
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            Material material = new Material(source) { name = "Street " + hex, color = color };
            material.SetFloat("_Glossiness", smoothness); materials.Add(hex, material); return material;
        }
        public void Dispose() { foreach (Material material in materials.Values) Object.Destroy(material); materials.Clear(); }
        private GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 p, Vector3 scale, string color, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = p; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = Material(color);
            if (!collider) { Collider c = go.GetComponent<Collider>(); c.enabled = false; Object.Destroy(c); }
            return go;
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
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            WorldLabelFit fit = go.AddComponent<WorldLabelFit>(); fit.maximumWidth = maxWidth; fit.maximumHeight = maxHeight; return mesh;
        }
        public void Build(StreetScenarioDefinition s)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.82f, 0.83f);
            RenderSettings.ambientEquatorColor = new Color(0.64f, 0.61f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.36f, 0.31f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.77f, 0.84f, 0.85f); RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 65; RenderSettings.fogEndDistance = 135;
            GameObject sun = new GameObject("Soft morning sunlight"); sun.transform.SetParent(root, false); sun.transform.rotation = Quaternion.Euler(43, -32, 0);
            Light light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.color = new Color(1, 0.92f, 0.79f); light.intensity = 1.05f; light.shadows = LightShadows.Soft; light.shadowStrength = 0.55f;
            float width = s.roadHalfWidth;
            Box("Road asphalt", new Vector3(0, -0.16f, 0), new Vector3(s.roadHalfLength * 2 + 4, 0.3f, width * 2), "53534D");
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Paved sidewalk", new Vector3(0, -0.06f, side * (width + 3.2f)), new Vector3(88, 0.12f, 6.4f), "D6CBAD");
                // A continuous flush opening is the curb ramp at the crossing; other curbs are 12 cm high.
                for (int end = -1; end <= 1; end += 2)
                    Box("Kerb beyond ramp", new Vector3(end * 23, 0.02f, side * (width + 0.08f)), new Vector3(41.8f, 0.16f, 0.18f), "E6DDC8");
                Box("Tactile waiting paving", new Vector3(0, 0.008f, side * (width + 0.55f)), new Vector3(s.crosswalkHalfWidth * 2, 0.012f, 0.65f), "CFAD62", false);
                for (int p = -4; p <= 4; p++)
                    Box("Tactile ridge", new Vector3(p * 0.38f, 0.025f, side * (width + 0.55f)), new Vector3(0.07f, 0.02f, 0.48f), "B18B42", false);
                for (int tile = -20; tile <= 20; tile++)
                    Box("Paving joint", new Vector3(tile * 2, 0.005f, side * (width + 3)), new Vector3(0.018f, 0.005f, 5.8f), "BFB498", false);
                for (int b = -2; b <= 2; b++)
                {
                    if (side == 1 && b == 0) continue;
                    Building(new Vector3(b * 13, 0, side * 11.3f), side, 6.8f + ((b + 3) % 3) * 0.9f, (b + 3) % 2 == 0 ? "D9CBB7" : "D5BA9A");
                }
                for (int x = -26; x <= 26; x += 13) if (Mathf.Abs(x) > 4) { Tree(new Vector3(x, 0, side * 5.5f)); Lamp(new Vector3(x + 3.5f, 0, side * 4.7f)); }
                Box("Street end pedestrian barrier", new Vector3(39, 0.65f, side * 6.3f), new Vector3(0.3f, 1.3f, 6), "807D6D");
                Box("Street end pedestrian barrier", new Vector3(-39, 0.65f, side * 6.3f), new Vector3(0.3f, 1.3f, 6), "807D6D");
            }
            for (int n = -4; n <= 4; n++) Box("Crosswalk stripe", new Vector3(0, 0.002f, n * 0.7f), new Vector3(s.crosswalkHalfWidth * 2, 0.016f, 0.35f), "ECE9DA", false);
            for (int n = -13; n <= 13; n++) if (Mathf.Abs(n) > 1) Box("Centre line dash", new Vector3(n * 3, 0.004f, 0), new Vector3(1.6f, 0.018f, 0.1f), "E6C16C", false);
            for (int lane = 0; lane < 2; lane++)
            {
                int direction = s.DirectionForLane(lane); float z = lane == 0 ? -s.laneOffset : s.laneOffset;
                Box("Vehicle stop line", new Vector3(-direction * s.stopLineOffset, 0.005f, z), new Vector3(0.22f, 0.02f, 3.05f), "F0ECDC", false);
                Box("Lane direction shaft", new Vector3(-direction * 11, 0.005f, z), new Vector3(1.4f, 0.02f, 0.15f), "D7D4C6", false);
                for (int edge = -1; edge <= 1; edge += 2)
                {
                    GameObject tip = Box("Lane direction chevron", new Vector3(-direction * 10.3f, 0.005f, z + edge * 0.2f), new Vector3(0.65f, 0.02f, 0.13f), "D7D4C6", false);
                    tip.transform.rotation = Quaternion.Euler(0, edge * direction * 38, 0);
                }
            }
            // Deterministic shallow road flecks; visual only, no collision noise.
            for (int n = 0; n < 70; n++)
            {
                float x = Mathf.Repeat(n * 17.33f, 78) - 39, z = Mathf.Repeat(n * 3.17f, width * 2 - 0.4f) - width + 0.2f;
                if (Mathf.Abs(x) > 2.2f) Box("Asphalt wear", new Vector3(x, 0.005f, z), new Vector3(0.08f + n % 3 * 0.09f, 0.005f, 0.025f), n % 2 == 0 ? "64635B" : "484A45", false);
            }
            Shop(); BusStop(new Vector3(7, 0, width + 2));
            Box("Start notice", new Vector3(-2.8f, 1.2f, -7.7f), new Vector3(1.3f, 1.3f, 0.12f), "E9DFCA");
            Label("街角便利店\n在马路对面", new Vector3(-2.8f, 1.25f, -7.79f), 0.055f, "365A46", null, 0, 1.1f, 1.0f);
            Label("目的地", new Vector3(0, 2.7f, 9.48f), 0.07f, "2A6D50");
            Box("Destination welcome mat", s.destination + new Vector3(0, 0.008f, 0), new Vector3(2.7f, 0.02f, 1.2f), "80946F", false);
            TrafficRoot = new GameObject("Vehicle routes and pool").transform; TrafficRoot.SetParent(root, false);
            Zone(CrossingRegion.Approach, new Vector3(0, 0.9f, -6.9f), new Vector3(6, 3, 5.5f));
            Zone(CrossingRegion.Kerb, new Vector3(0, 0.9f, -width - 0.45f), new Vector3(4.1f, 3, 1.1f));
            // Entry includes the near half-metre of roadway, sampled before conflict in the evaluator.
            Zone(CrossingRegion.Entry, new Vector3(0, 0.9f, -width), new Vector3(s.crosswalkHalfWidth * 2, 3, 0.9f));
            Zone(CrossingRegion.Conflict, new Vector3(0, 0.9f, 0), new Vector3(86, 3, width * 2));
            Zone(CrossingRegion.Exit, new Vector3(0, 0.9f, width + 0.35f), new Vector3(s.crosswalkHalfWidth * 2, 3, 0.9f));
            Zone(CrossingRegion.Destination, s.destination + Vector3.up * 0.9f, new Vector3(2.8f, 3, 2));
        }
        private void Zone(CrossingRegion type, Vector3 centre, Vector3 size)
        {
            GameObject go = new GameObject(type + "Zone"); go.transform.SetParent(root, false); go.transform.localPosition = centre;
            CrossingZone zone = go.AddComponent<CrossingZone>(); zone.region = type; zone.size = size; zones.Add(zone);
        }
        private void Building(Vector3 p, int side, float height, string color)
        {
            Box("Residential building", p + Vector3.up * height * 0.5f, new Vector3(10.3f, height, 4), color);
            Box("Roof cornice", p + Vector3.up * (height - 0.1f), new Vector3(10.6f, 0.3f, 4.2f), "B9A78D");
            float face = p.z - side * 2.04f;
            for (int row = 0; row < 3; row++) for (int col = -1; col <= 1; col++)
            {
                Vector3 w = new Vector3(p.x + col * 2.9f, 1.8f + row * 2.05f, face);
                Box("Recessed window frame", w, new Vector3(1.65f, 1.35f, 0.13f), "ECE3CF", false);
                Box("Glazed window", w + new Vector3(0, 0, -side * 0.08f), new Vector3(1.4f, 1.13f, 0.06f), row % 2 == 0 ? "70817A" : "88938B", false);
                Box("Window mullion", w + new Vector3(0, 0, -side * 0.12f), new Vector3(0.07f, 1.15f, 0.05f), "DFD5BF", false);
                Box("Window sill", w + new Vector3(0, -0.68f, -side * 0.14f), new Vector3(1.85f, 0.12f, 0.32f), "BDAE93", false);
            }
            Box("Entrance door", new Vector3(p.x + 4, 1, face - side * 0.03f), new Vector3(1, 2, 0.15f), "625F4F");
        }
        private void Shop()
        {
            Box("Convenience shop", new Vector3(0, 2.1f, 12), new Vector3(10.5f, 4.2f, 4.3f), "E8D9B8");
            Box("Shop sign backing", new Vector3(0, 3.25f, 9.79f), new Vector3(10.7f, 1, 0.2f), "3E6A50");
            Label("街 角 便 利 店", new Vector3(0, 3.25f, 9.65f), 0.16f, "FAF1D9", null, 0, 9.5f, 0.68f);
            for (int n = -1; n <= 1; n++)
            {
                Box("Shopfront frame", new Vector3(n * 3, 1.45f, 9.78f), new Vector3(2.8f, 2.45f, 0.14f), "D0B995");
                Box("Shop glazing", new Vector3(n * 3, 1.45f, 9.68f), new Vector3(2.55f, 2.2f, 0.06f), "85988A");
                Box("Shop transom", new Vector3(n * 3, 1.65f, 9.6f), new Vector3(2.6f, 0.06f, 0.08f), "E5DBC6", false);
            }
            Box("Striped awning", new Vector3(0, 2.6f, 9.25f), new Vector3(10.5f, 0.16f, 1.3f), "D6BE80");
            for (int n = -5; n <= 5; n++) Box("Awning stripe", new Vector3(n * 0.9f, 2.69f, 9.25f), new Vector3(0.4f, 0.015f, 1.3f), "7A8B61", false);
            Box("Planter", new Vector3(4.25f, 0.35f, 8.8f), new Vector3(1.2f, 0.7f, 0.8f), "A87857");
            Shape(PrimitiveType.Sphere, "Planter greenery", root, new Vector3(4.25f, 0.9f, 8.8f), new Vector3(1.1f, 0.65f, 0.8f), "667C49", false);
        }
        private void BusStop(Vector3 p)
        {
            Box("Bus shelter roof", p + new Vector3(0, 2.6f, 0), new Vector3(3.6f, 0.18f, 1.7f), "71816C");
            for (int x = -1; x <= 1; x += 2) Box("Bus shelter post", p + new Vector3(x * 1.6f, 1.3f, 0.65f), new Vector3(0.09f, 2.6f, 0.09f), "6D7466");
            Box("Shelter back panel", p + new Vector3(0, 1.5f, 0.7f), new Vector3(3.2f, 1.7f, 0.07f), "B5C4B6");
            Box("Bus seat", p + new Vector3(0, 0.55f, 0.3f), new Vector3(2.8f, 0.16f, 0.5f), "8E7656");
            Box("Bus sign pole", p + new Vector3(-2.2f, 1.4f, -0.3f), new Vector3(0.07f, 2.8f, 0.07f), "717B6A");
            Box("Bus stop board", p + new Vector3(-2.2f, 2.25f, -0.3f), new Vector3(0.95f, 1, 0.1f), "F4EBD6");
            Label("公交\n街心站", p + new Vector3(-2.2f, 2.25f, -0.37f), 0.06f, "32483D", null, 0, 0.8f, 0.8f);
        }
        private void Tree(Vector3 p)
        {
            Box("Tree pit", p + new Vector3(0, 0.07f, 0), new Vector3(1.4f, 0.14f, 1.4f), "9D967C");
            Shape(PrimitiveType.Cylinder, "Tree trunk", root, p + Vector3.up * 1.35f, new Vector3(0.25f, 1.35f, 0.25f), "8C7150");
            Shape(PrimitiveType.Sphere, "Tree crown", root, p + Vector3.up * 3.25f, new Vector3(2.8f, 2.8f, 2.5f), "7D925B", false);
        }
        private void Lamp(Vector3 p)
        {
            Shape(PrimitiveType.Cylinder, "Streetlight pole", root, p + Vector3.up * 2.2f, new Vector3(0.09f, 2.2f, 0.09f), "686D60");
            Box("Streetlight arm", p + new Vector3(0, 4.35f, -0.3f), new Vector3(0.12f, 0.12f, 0.8f), "686D60", false);
            Box("Streetlight head", p + new Vector3(0, 4.3f, -0.65f), new Vector3(0.4f, 0.16f, 0.55f), "C7C4A8", false);
        }
        public void DetailVehicle(GameObject vehicle)
        {
            Transform t = vehicle.transform;
            Shape(PrimitiveType.Cube, "Painted body", t, new Vector3(0, 0.65f, 0), new Vector3(1.75f, 0.7f, 4.2f), "A55F4B", false);
            Shape(PrimitiveType.Cube, "Cabin", t, new Vector3(0, 1.18f, -0.15f), new Vector3(1.58f, 0.65f, 2.15f), "A55F4B", false);
            Shape(PrimitiveType.Cube, "Windscreen", t, new Vector3(0, 1.19f, 0.94f), new Vector3(1.37f, 0.48f, 0.06f), "6F807C", false);
            Shape(PrimitiveType.Cube, "Rear glass", t, new Vector3(0, 1.19f, -1.25f), new Vector3(1.37f, 0.48f, 0.06f), "6F807C", false);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape(PrimitiveType.Cube, "Side window", t, new Vector3(side * 0.8f, 1.19f, -0.15f), new Vector3(0.05f, 0.45f, 1.75f), "718580", false);
                Shape(PrimitiveType.Cube, "Door pillar", t, new Vector3(side * 0.83f, 1.15f, -0.2f), new Vector3(0.05f, 0.58f, 0.09f), "44483F", false);
                Shape(PrimitiveType.Cube, "Mirror", t, new Vector3(side * 0.95f, 1.05f, 0.8f), new Vector3(0.2f, 0.15f, 0.22f), "5E6256", false);
                Shape(PrimitiveType.Cube, "Headlamp", t, new Vector3(side * 0.56f, 0.67f, 2.12f), new Vector3(0.4f, 0.2f, 0.04f), "F5EBC6", false);
                Shape(PrimitiveType.Cube, "Tail lamp", t, new Vector3(side * 0.61f, 0.67f, -2.12f), new Vector3(0.32f, 0.18f, 0.04f), "A74435", false);
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    GameObject wheel = Shape(PrimitiveType.Cylinder, "Tyre", t, new Vector3(side * 0.86f, 0.37f, axle * 1.25f), new Vector3(0.68f, 0.12f, 0.68f), "343A33", false);
                    wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    GameObject hub = Shape(PrimitiveType.Cylinder, "Wheel hub", t, new Vector3(side * 1, 0.37f, axle * 1.25f), new Vector3(0.39f, 0.015f, 0.39f), "A8AEA4", false);
                    hub.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
            }
            Shape(PrimitiveType.Cube, "Front grille", t, new Vector3(0, 0.51f, 2.12f), new Vector3(0.7f, 0.18f, 0.04f), "444A40", false);
            Shape(PrimitiveType.Cube, "Rear bumper", t, new Vector3(0, 0.39f, -2.11f), new Vector3(1.6f, 0.08f, 0.06f), "C0C1AF", false);
        }
        public void ColorVehicle(GameObject car, int index)
        {
            string[] colors = { "A55F4B", "C3B18E", "687D70", "C3C3B4" };
            foreach (Renderer renderer in car.GetComponentsInChildren<Renderer>())
                if (renderer.gameObject.name == "Painted body" || renderer.gameObject.name == "Cabin") renderer.sharedMaterial = Material(colors[index % colors.Length], 0.45f);
        }
    }
}
