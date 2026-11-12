using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;

namespace StreetCrossing.Traffic
{
    public sealed class TrafficSignalView : MonoBehaviour
    {
        private IntersectionController intersection;
        private StreetGameManager game;
        private readonly Renderer[] pedestrianLamps = new Renderer[2];
        private readonly Renderer[,] vehicleLamps = new Renderer[2, 3];
        private readonly Material[] lit = new Material[3];
        private Material off;
        private readonly TextMesh[] pedestrianLabels = new TextMesh[2];
        private readonly TextMesh[] vehicleLabels = new TextMesh[2];
        public void Initialize(StreetGameManager owner, StreetWorld world, StreetScenarioDefinition scenario)
        {
            game = owner; intersection = owner.Intersection;
            string[] colors = { "E87663", "F0C663", "83C692" };
            for (int i = 0; i < 3; i++)
            {
                lit[i] = new Material(world.Material(colors[i])); lit[i].EnableKeyword("_EMISSION"); lit[i].SetColor("_EmissionColor", lit[i].color * 0.8f);
            }
            off = world.Material("3E463E");
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? scenario.roadHalfWidth + 0.42f : -scenario.roadHalfWidth - 0.42f;
                float x = side == 0 ? 2.35f : -2.35f;
                float yaw = side == 0 ? 0 : 180;
                Transform pole = new GameObject("Pedestrian signal (same authority)").transform; pole.SetParent(transform, false); pole.localPosition = new Vector3(x, 0, z); pole.localRotation = Quaternion.Euler(0, yaw, 0);
                Part(pole, "Signal pole", new Vector3(0, 1.35f, 0), new Vector3(0.08f, 2.7f, 0.08f), world.Material("626D59"), true);
                Part(pole, "Pedestrian housing", new Vector3(0, 2.1f, 0), new Vector3(0.8f, 0.95f, 0.2f), world.Material("384B3B"), true);
                pedestrianLamps[side] = Part(pole, "Pedestrian lamp", new Vector3(0, 2.16f, -0.12f), new Vector3(0.65f, 0.68f, 0.06f), off, false);
                pedestrianLabels[side] = world.Label("", new Vector3(0, 2.16f, -0.17f), 0.08f, "17291F", pole, 0, 0.58f, 0.55f);
            }
            for (int lane = 0; lane < 2; lane++)
            {
                int direction = scenario.DirectionForLane(lane);
                Transform mast = new GameObject("Vehicle signal lane " + lane).transform; mast.SetParent(transform, false);
                // Local -Z faces the approaching driver, independent of the configured driving side.
                mast.localPosition = new Vector3(-direction * scenario.stopLineOffset, 0, lane == 0 ? -scenario.roadHalfWidth - 0.5f : scenario.roadHalfWidth + 0.5f);
                mast.localRotation = Quaternion.Euler(0, direction * 90, 0);
                Part(mast, "Vehicle pole", new Vector3(0, 2, 0), new Vector3(0.1f, 4, 0.1f), world.Material("626D59"), true);
                Part(mast, "Vehicle housing", new Vector3(0, 3.25f, 0), new Vector3(0.65f, 1.9f, 0.32f), world.Material("384B3B"), true);
                for (int lamp = 0; lamp < 3; lamp++)
                    vehicleLamps[lane, lamp] = Part(mast, "Vehicle lamp " + lamp, new Vector3(0, 3.85f - lamp * 0.6f, -0.2f), new Vector3(0.42f, 0.42f, 0.05f), off, false);
                vehicleLabels[lane] = world.Label("", new Vector3(0, 2.12f, -0.22f), 0.05f, "FAF1D9", mast, 0, 0.55f, 0.22f);
            }
            intersection.PhaseChanged += OnPhase; OnPhase(intersection.Phase);
        }
        private static Renderer Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            Renderer renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            if (!collider) { go.GetComponent<Collider>().enabled = false; Destroy(go.GetComponent<Collider>()); }
            return renderer;
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
                pedestrianLamps[side].sharedMaterial = walk || (clearing && flashOn) ? lit[2] : clearing ? off : lit[0];
                pedestrianLabels[side].text = walk ? "WALK\n通行" : clearing ? "CLEAR\n清空" : "WAIT\n等待";
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
