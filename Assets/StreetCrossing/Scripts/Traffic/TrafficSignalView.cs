using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Traffic
{
    /// <summary>Rounded original signal geometry; all icons and lenses use the existing phase authority.</summary>
    public sealed class TrafficSignalView : MonoBehaviour
    {
        private IntersectionController intersection;
        private StreetGameManager game;
        private readonly Renderer[,] vehicleLamps = new Renderer[2, 3];
        private readonly Transform[] walkIcons = new Transform[2];
        private readonly Transform[] waitIcons = new Transform[2];
        private readonly Material[] lit = new Material[3];
        private Material off;
        private readonly TextMesh[] pedestrianLabels = new TextMesh[2];
        private readonly TextMesh[] vehicleLabels = new TextMesh[2];
        public void Initialize(StreetGameManager owner, StreetWorld world, StreetScenarioDefinition scenario)
        {
            game = owner; intersection = owner.Intersection;
            string[] colors = { "ED7667", "F1C561", "8FD5A0" };
            for (int i = 0; i < 3; i++)
            {
                // Keep colour/shape legible in daylight; no bloom or post-processing is required.
                lit[i] = new Material(world.Material(colors[i])); lit[i].EnableKeyword("_EMISSION"); lit[i].SetColor("_EmissionColor", lit[i].color * .35f);
            }
            off = world.Material("172B2C");
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? scenario.roadHalfWidth + .42f : -scenario.roadHalfWidth - .42f;
                float x = side == 0 ? 2.35f : -2.35f;
                Transform pole = new GameObject("Pedestrian signal (same authority)").transform; pole.SetParent(transform, false); pole.localPosition = new Vector3(x, 0, z); pole.localRotation = Quaternion.Euler(0, side == 0 ? 0 : 180, 0);
                // The original collider envelopes are retained. New trim/icons/visors have no colliders.
                Body(world, pole, "Signal pole", new Vector3(0, 1.35f, 0), new Vector3(.08f, 2.7f, .08f), "626D59", true, .035f);
                Body(world, pole, "Pedestrian housing", new Vector3(0, 2.1f, 0), new Vector3(.8f, .95f, .2f), "384B3B", true, .12f);
                Body(world, pole, "Pedestrian inset screen", new Vector3(0, 2.16f, -.12f), new Vector3(.65f, .68f, .06f), "172B2C", false, .08f);
                Body(world, pole, "Pedestrian visor", new Vector3(0, 2.57f, -.15f), new Vector3(.79f, .06f, .42f), "293C3D", false, .025f);
                walkIcons[side] = Icon(world, pole, true, lit[2]); waitIcons[side] = Icon(world, pole, false, lit[0]);
                pedestrianLabels[side] = world.Label("", new Vector3(0, 1.73f, -.18f), .035f, "E9E4D3", pole, 0, .64f, .16f);
                Body(world, pole, "Pedestrian collar", new Vector3(0, 1.5f, 0), new Vector3(.16f, .10f, .16f), "546766", false, .04f);
            }
            for (int lane = 0; lane < 2; lane++)
            {
                int direction = scenario.DirectionForLane(lane);
                Transform mast = new GameObject("Vehicle signal lane " + lane).transform; mast.SetParent(transform, false);
                mast.localPosition = new Vector3(-direction * scenario.stopLineOffset, 0, lane == 0 ? -scenario.roadHalfWidth - .5f : scenario.roadHalfWidth + .5f);
                mast.localRotation = Quaternion.Euler(0, direction * 90, 0);
                Body(world, mast, "Vehicle pole", new Vector3(0, 2, 0), new Vector3(.1f, 4, .1f), "626D59", true, .045f);
                Body(world, mast, "Vehicle housing", new Vector3(0, 3.25f, 0), new Vector3(.65f, 1.9f, .32f), "384B3B", true, .16f);
                for (int lamp = 0; lamp < 3; lamp++)
                {
                    float y = 3.85f - lamp * .6f;
                    Body(world, mast, "Vehicle circular bezel", new Vector3(0, y, -.19f), new Vector3(.51f, .51f, .055f), "1E3031", false, .25f);
                    vehicleLamps[lane, lamp] = Body(world, mast, "Vehicle circular lens " + lamp, new Vector3(0, y, -.225f), new Vector3(.42f, .42f, .035f), "172B2C", false, .21f);
                    Body(world, mast, "Vehicle lens hood", new Vector3(0, y + .27f, -.24f), new Vector3(.55f, .055f, .30f), "293C3D", false, .025f);
                }
                vehicleLabels[lane] = world.Label("", new Vector3(0, 2.12f, -.22f), .05f, "FAF1D9", mast, 0, .55f, .22f);
            }
            intersection.PhaseChanged += OnPhase; Refresh();
        }
        private static Renderer Body(StreetWorld world, Transform parent, string name, Vector3 position, Vector3 size, string colour, bool collider, float radius)
        {
            Renderer renderer = world.VisualRounded(parent, name, position, size, colour, radius);
            if (collider) renderer.gameObject.AddComponent<BoxCollider>().size = size;
            return renderer;
        }
        private static Transform Icon(StreetWorld world, Transform parent, bool walking, Material material)
        {
            Transform icon = new GameObject(walking ? "Walking pedestrian icon" : "Standing pedestrian icon").transform;
            icon.SetParent(parent, false); icon.localPosition = new Vector3(0, 2.20f, -.18f);
            Stroke(world, icon, new Vector2(0, .13f), new Vector2(walking ? -.025f : 0, -.07f), .065f, material);
            Stroke(world, icon, new Vector2(0, .075f), new Vector2(walking ? -.17f : -.10f, walking ? -.025f : -.10f), .046f, material);
            Stroke(world, icon, new Vector2(0, .075f), new Vector2(walking ? .16f : .10f, walking ? .12f : -.10f), .046f, material);
            Stroke(world, icon, new Vector2(walking ? -.025f : 0, -.06f), new Vector2(walking ? -.15f : -.07f, -.26f), .055f, material);
            Stroke(world, icon, new Vector2(walking ? -.025f : 0, -.06f), new Vector2(walking ? .15f : .07f, -.26f), .055f, material);
            Renderer head = world.VisualRounded(icon, "Pedestrian head", new Vector3(0, .215f, 0), new Vector3(.10f, .10f, .025f), "E9E4D3", .05f); head.sharedMaterial = material;
            return icon;
        }
        private static void Stroke(StreetWorld world, Transform parent, Vector2 a, Vector2 b, float width, Material material)
        {
            Vector2 d = b - a, centre = (a + b) * .5f;
            Renderer renderer = world.VisualRounded(parent, "Pedestrian limb", new Vector3(centre.x, centre.y, 0), new Vector3(width, d.magnitude, .025f), "E9E4D3", width * .49f);
            renderer.transform.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg); renderer.sharedMaterial = material;
        }
        private void OnPhase(TrafficPhase phase) { Refresh(); }
        private void Update() { if (intersection != null) Refresh(); }
        private void Refresh()
        {
            bool walk = intersection.Phase == TrafficPhase.Walk;
            bool clearing = intersection.Phase == TrafficPhase.WalkFlashing;
            bool flashOn = !clearing || ((int)(game.WorldTime * 2) % 2 == 0);
            for (int side = 0; side < 2; side++)
            {
                walkIcons[side].gameObject.SetActive(walk || clearing && flashOn);
                waitIcons[side].gameObject.SetActive(!walk && !clearing);
                pedestrianLabels[side].text = walk ? "WALK / 通行" : clearing ? "CLEAR / 清空" : "WAIT / 等待";
            }
            int active = intersection.Phase == TrafficPhase.VehicleGreen ? 2 : intersection.Phase == TrafficPhase.VehicleAmber ? 1 : 0;
            for (int lane = 0; lane < 2; lane++)
            {
                for (int lamp = 0; lamp < 3; lamp++) vehicleLamps[lane, lamp].sharedMaterial = active == lamp ? lit[lamp] : off;
                vehicleLabels[lane].text = active == 2 ? "GO" : active == 1 ? "AMBER" : "STOP";
            }
        }
        private void OnDestroy()
        {
            if (intersection != null) intersection.PhaseChanged -= OnPhase;
            for (int i = 0; i < lit.Length; i++) if (lit[i] != null) Destroy(lit[i]);
        }
    }
}
