using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Serialized original modular models/materials are shared by all scenarios and pooled vehicle instances.</summary>
    public sealed class StreetAssetLibrary
    {
        private readonly GameObject[] vehicles;
        private readonly GameObject residential;
        public Material Asphalt { get; }
        public Material Paving { get; }
        public StreetAssetLibrary()
        {
            vehicles = new[] { Load("SedanModule"), Load("HatchbackModule"), Load("VanModule") };
            residential = Load("ResidentialModule");
            Asphalt = Resources.Load<Material>("StreetCrossing/Modules/Asphalt"); Paving = Resources.Load<Material>("StreetCrossing/Modules/Paving");
            if (Asphalt == null || Paving == null) throw new System.InvalidOperationException("Street textured materials are missing.");
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
        public void CreateBuilding(Transform parent, Vector3 position, int side, float height)
        {
            GameObject go = Object.Instantiate(residential, parent); go.name = "Modular residential facade";
            go.transform.localPosition = position; go.transform.localRotation = Quaternion.Euler(0, side > 0 ? 0 : 180, 0);
            go.transform.localScale = new Vector3(1, height / 7.2f, 1);
            BoxCollider collider = go.AddComponent<BoxCollider>(); collider.center = Vector3.up * 3.6f; collider.size = new Vector3(10.3f, 7.2f, 4);
        }
    }
}
