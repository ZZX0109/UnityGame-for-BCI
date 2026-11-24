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
        private readonly Renderer[,] vehicleLamps = new Renderer[StreetLayout.StraightLaneCount, 3];
        private readonly Transform[] walkIcons = new Transform[4];
        private readonly Transform[] waitIcons = new Transform[4];
        private readonly Material[] lit = new Material[3];
        private Material off;
        private Mesh lensHood;
        private TrafficPhase? displayedPhase;
        private bool displayedFlash;
        private readonly TextMesh[] pedestrianLabels = new TextMesh[2];
        private readonly TextMesh[] vehicleLabels = new TextMesh[StreetLayout.StraightLaneCount];
        public bool FacingFarPedestrianSignal(Camera view, Transform player)
        {
            Transform icon = walkIcons[0];
            if (!view || !icon) return false;
            Vector3 towards = icon.position - view.transform.position;
            return Vector3.Angle(view.transform.forward, towards) <= 10f &&
                StreetCrossing.Player.RoadObservationVisibility.PointVisible(view, icon.position, player, icon.parent);
        }
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
            lensHood = CreateLensHood();
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? scenario.roadHalfWidth + .42f : -scenario.roadHalfWidth - .42f;
                float x = side == 0 ? 2.35f : -2.85f;
                Transform pole = new GameObject("Pedestrian signal (same authority)").transform; pole.SetParent(transform, false); pole.localPosition = new Vector3(x, 0, z); pole.localRotation = Quaternion.Euler(0, side == 0 ? 0 : 180, 0);
                // The original collider envelopes are retained. New trim/icons/visors have no colliders.
                Body(world, pole, "Signal pole", new Vector3(0, 1.35f, 0), new Vector3(.08f, 2.7f, .08f), "626D59", true, .035f);
                Body(world, pole, "Pedestrian housing", new Vector3(0, 2.1f, 0), new Vector3(.8f, .95f, .2f), "384B3B", true, .12f);
                Body(world, pole, "Signal service cover", new Vector3(0, 2.1f, .115f), new Vector3(.68f, .79f, .04f), "46524E", false, .07f);
                Body(world, pole, "Pole mounting channel", new Vector3(0, 2.1f, .17f), new Vector3(.095f, .69f, .08f), "66736C", false, .024f);
                Body(world, pole, "Pedestrian inset screen", new Vector3(0, 2.16f, -.12f), new Vector3(.65f, .68f, .06f), "172B2C", false, .08f);
                Body(world, pole, "Pedestrian visor", new Vector3(0, 2.57f, -.15f), new Vector3(.79f, .06f, .42f), "293C3D", false, .025f);
                walkIcons[side] = Icon(world, pole, true, lit[2]); waitIcons[side] = Icon(world, pole, false, lit[0]);
                walkIcons[side].localScale = waitIcons[side].localScale = Vector3.one * 1.24f;
                // A low nearside repeater can be read from the waiting area.
                // Its indication is driven by exactly the same phase as the far head.
                var repeater = new GameObject("Nearside pedestrian repeater").transform;
                repeater.SetParent(pole, false);
                repeater.localPosition = new Vector3(0, 1.43f, .11f);
                repeater.localRotation = Quaternion.Euler(0, 200, 0);
                Body(world, repeater, "Repeater body", Vector3.zero, new Vector3(.25f, .34f, .095f), "263539", false, .035f);
                Body(world, repeater, "Repeater visor", new Vector3(0,.165f,-.064f), new Vector3(.26f,.025f,.14f), "293C3D", false, .01f);
                int index = side + 2;
                walkIcons[index] = Icon(world, repeater, true, lit[2]);
                waitIcons[index] = Icon(world, repeater, false, lit[0]);
                walkIcons[index].localPosition = waitIcons[index].localPosition = new Vector3(0,0,-.057f);
                walkIcons[index].localScale = waitIcons[index].localScale = Vector3.one * .52f;
                pedestrianLabels[side] = world.Label("", new Vector3(0, 1.73f, -.18f), .035f, "E9E4D3", pole, 0, .64f, .16f);
                Body(world, pole, "Pedestrian collar", new Vector3(0, 1.5f, 0), new Vector3(.16f, .10f, .16f), "546766", false, .04f);
                // Blue crossing sign on a short bracket; the pole's local -z faces the carriageway on both sides.
                world.VisualRounded(pole, "Crossing sign bracket", new Vector3(0, 2.72f, 0), new Vector3(.05f, .50f, .05f), "626D59", .02f, .4f);
                world.VisualRounded(pole, "Pedestrian crossing sign", new Vector3(0, 2.95f, -.06f), new Vector3(.42f, .42f, .02f), "2456A8", .008f, .35f);
                world.VisualMesh(pole, "Crossing sign symbol", new Vector3(0, 2.95f, -.072f), "CrossingTriangle:0.3", CreateCrossingTriangleMesh, "F3F0E4", .30f);
            }
            for (int lane = 0; lane < scenario.StraightLaneCount; lane++)
            {
                int direction = scenario.DirectionForLane(lane);
                Transform mast = new GameObject("Vehicle signal lane " + lane).transform; mast.SetParent(transform, false);
                float roadside = lane < scenario.StraightLaneCount / 2 ? -scenario.roadHalfWidth - .5f : scenario.roadHalfWidth + .5f;
                mast.localPosition = new Vector3(-direction * scenario.stopLineOffset, 0, roadside);
                mast.localRotation = Quaternion.Euler(0, direction * 90, 0);
                float lateral = (roadside - scenario.LaneZ(lane)) * direction;
                if (lane == 0 || lane == scenario.StraightLaneCount - 1)
                {
                    Body(world, mast, "Vehicle pole", new Vector3(0, 2, 0), new Vector3(.1f, 4, .1f), "626D59", true, .045f);
                    float span = Mathf.Abs(roadside - scenario.LaneZ(lane == 0 ? 1 : scenario.StraightLaneCount - 2));
                    Body(world, mast, "Overhead signal arm", new Vector3(Mathf.Sign(lateral) * span * .5f, 4.14f, 0), new Vector3(span + .2f, .10f, .10f), "626D59", false, .045f);
                }
                // Four overhead heads, two roadside support poles; no support
                // is placed in a newly occupied vehicle lane.
                Body(world, mast, "Matte signal backplate", new Vector3(lateral, 3.25f, .11f), new Vector3(.89f, 2.08f, .05f), "253238", false, .085f);
                Body(world, mast, "Vehicle housing", new Vector3(lateral, 3.25f, 0), new Vector3(.65f, 1.9f, .32f), "384B3B", true, .16f);
                for (int lamp = 0; lamp < 3; lamp++)
                {
                    float y = 3.85f - lamp * .6f;
                    Body(world, mast, "Vehicle circular bezel", new Vector3(lateral, y, -.19f), new Vector3(.51f, .51f, .055f), "1E3031", false, .25f);
                    vehicleLamps[lane, lamp] = Body(world, mast, "Vehicle circular lens " + lamp, new Vector3(lateral, y, -.225f), new Vector3(.42f, .42f, .035f), "172B2C", false, .21f);
                    var hood = new GameObject("Vehicle lens hood", typeof(MeshFilter), typeof(MeshRenderer));
                    hood.transform.SetParent(mast, false);
                    hood.transform.localPosition = new Vector3(lateral, y, -.215f);
                    hood.GetComponent<MeshFilter>().sharedMesh = lensHood;
                    hood.GetComponent<MeshRenderer>().sharedMaterial = world.Material("293C3D");
                }
                vehicleLabels[lane] = world.Label("", new Vector3(lateral, 2.12f, -.22f), .05f, "FAF1D9", mast, 0, .55f, .22f);
            }
            intersection.PhaseChanged += OnPhase; Refresh();
        }
        private static Renderer Body(StreetWorld world, Transform parent, string name, Vector3 position, Vector3 size, string colour, bool collider, float radius)
        {
            Renderer renderer = world.VisualRounded(parent, name, position, size, colour, radius);
            if (collider) renderer.gameObject.AddComponent<BoxCollider>().size = size;
            return renderer;
        }
        // Rolled aluminium visor, open below. Each signal uses the same mesh;
        // the longer upper lip shades the lens without masking it at eye level.
        private static Mesh CreateLensHood()
        {
            const int segments = 12;
            var vertices = new Vector3[(segments + 1) * 4];
            var triangles = new int[segments * 24];
            int index = 0;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-10, 190, i / (float)segments) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                float lip = .10f + .16f * Mathf.Max(radial.y, 0);
                int a = i * 4;
                vertices[a] = radial * .252f;
                vertices[a + 1] = radial * .252f - Vector3.forward * lip;
                vertices[a + 2] = radial * .239f - Vector3.forward * lip;
                vertices[a + 3] = radial * .239f;
                if (i == segments) continue;
                for (int edge = 0; edge < 4; edge++)
                {
                    int from = a + edge, next = a + (edge + 1) % 4;
                    triangles[index++] = from; triangles[index++] = next; triangles[index++] = next + 4;
                    triangles[index++] = from; triangles[index++] = next + 4; triangles[index++] = from + 4;
                }
            }
            var mesh = new Mesh { name = "Shared curved signal visor", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        // Thin triangular warning prism on the crossing sign; the front face points along local -z.
        private static Mesh CreateCrossingTriangleMesh()
        {
            Vector3[] vertices =
            {
                new Vector3(-.15f, -.13f, -.006f), new Vector3(0f, .14f, -.006f), new Vector3(.15f, -.13f, -.006f),
                new Vector3(-.15f, -.13f, 0f), new Vector3(0f, .14f, 0f), new Vector3(.15f, -.13f, 0f)
            };
            int[] triangles =
            {
                0, 1, 2,
                3, 5, 4,
                0, 3, 4, 0, 4, 1,
                1, 4, 5, 1, 5, 2,
                2, 5, 3, 2, 3, 0
            };
            var mesh = new Mesh { name = "Crossing warning triangle", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
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
            world.CombineVisuals(icon);
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
            if (displayedPhase == intersection.Phase && displayedFlash == flashOn) return;
            displayedPhase = intersection.Phase;
            displayedFlash = flashOn;
            for (int side = 0; side < walkIcons.Length; side++)
            {
                walkIcons[side].gameObject.SetActive(walk || clearing && flashOn);
                waitIcons[side].gameObject.SetActive(!walk && !clearing);
                if (side < pedestrianLabels.Length) pedestrianLabels[side].text = walk ? "WALK / 通行" : clearing ? "CLEAR / 清空" : "WAIT / 等待";
            }
            int active = intersection.Phase == TrafficPhase.VehicleGreen ? 2 : intersection.Phase == TrafficPhase.VehicleAmber ? 1 : 0;
            for (int lane = 0; lane < vehicleLamps.GetLength(0); lane++)
            {
                for (int lamp = 0; lamp < 3; lamp++) vehicleLamps[lane, lamp].sharedMaterial = active == lamp ? lit[lamp] : off;
                vehicleLabels[lane].text = active == 2 ? "GO" : active == 1 ? "AMBER" : "STOP";
            }
        }
        private void OnDestroy()
        {
            if (intersection != null) intersection.PhaseChanged -= OnPhase;
            if (lensHood != null) Destroy(lensHood);
            for (int i = 0; i < lit.Length; i++) if (lit[i] != null) Destroy(lit[i]);
        }
    }
}
