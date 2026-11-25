using System;
using System.IO;
using System.Linq;
using StreetCrossing.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace StreetCrossing.Editor
{
    /// <summary>
    /// Additive isolated visual-replacement test: one realistic pedestrian, two real-branch
    /// trees, one building with real openings. Does NOT modify S01-S03 or traffic logic.
    /// Entry point is invoked from batchmode; captures 1080p PNGs for a before/after gate.
    /// </summary>
    public static class VisualUpgradeTestBuilder
    {
        public const string ScenePath = "Assets/StreetCrossing/Scenes/VisualUpgradeTest_Refined.unity";
        private const string AssetRoot = "Assets/StreetCrossing/Art/External/";
        private const int W = 1920, H = 1080;

        [MenuItem("Street Crossing/Create Refined Runtime Prefabs")]
        public static void CreateRefinedRuntimePrefabs()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string[] pairs =
            {
                "Trees/StreetPlane_Oak_Refined.fbx|RefinedTreeOak",
                "Trees/StreetGinkgo_Willow_Refined.fbx|RefinedTreeWillow",
                "Trees/StreetConifer_Pine_Refined.fbx|RefinedTreePine",
                "Buildings/NearFieldResidence_Refined.fbx|RefinedResidence"
            };
            foreach (string pair in pairs)
            {
                string[] parts = pair.Split('|');
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + parts[0]);
                if (model == null) throw new InvalidOperationException("Missing model: " + parts[0]);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                string path = "Assets/StreetCrossing/Resources/StreetCrossing/Modules/" + parts[1] + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                UnityEngine.Object.DestroyImmediate(instance);
                Debug.Log("[VisualUpgrade] runtime prefab saved: " + path);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        [MenuItem("Street Crossing/Capture S01 With Refined Assets")]
        public static void CaptureGameWorld()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                EditorSceneManager.OpenScene("Assets/StreetCrossing/Scenes/StreetCrossingScene.unity", OpenSceneMode.Single);
                StreetGameManager game = UnityEngine.Object.FindObjectOfType<StreetGameManager>();
                if (game == null) throw new InvalidOperationException("StreetGameManager missing from StreetCrossingScene.");
                game.SelectScenario(0);
                int refinedTrees = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .Count(t => t.name.StartsWith("Refined street tree"));
                int refinedBuildings = UnityEngine.Object.FindObjectsOfType<Transform>()
                    .Count(t => t.name == "Facade Residence" && t.GetComponent<MeshFilter>() == null);
                Debug.Log($"[VisualUpgrade] in-game refined trees={refinedTrees} refined buildings={refinedBuildings}");
                Vector3 spawn = game.scenario.playerSpawn;
                Vector3 destination = game.scenario.destination;
                GameObject camGo = new GameObject("Refined game world capture camera");
                camGo.tag = "MainCamera";
                Camera cam = camGo.AddComponent<Camera>();
                camGo.transform.position = spawn + Vector3.up * 1.65f;
                Vector3 forward = (destination - spawn).normalized;
                camGo.transform.rotation = Quaternion.LookRotation(forward);
                cam.fieldOfView = 62f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.Skybox;
                string dir = "Results/visual-upgrade-test";
                Directory.CreateDirectory(dir);
                Capture(cam, Path.Combine(dir, "in-game-refined-S01-1080p.png"));
                camGo.transform.position = spawn + Vector3.up * 1.65f;
                camGo.transform.rotation = Quaternion.LookRotation(Vector3.right);
                Capture(cam, Path.Combine(dir, "in-game-refined-S01-sidewalk-1080p.png"));
                Debug.Log("[VisualUpgrade] in-game capture complete: " + Path.GetFullPath(dir));
            }
            catch (Exception ex)
            {
                Debug.LogError("[VisualUpgrade] GAME CAPTURE FAILED: " + ex);
                throw;
            }
        }

        [MenuItem("Street Crossing/Build Visual Upgrade Test Scene")]
        public static void BuildAndCapture()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.70f, 0.75f, 0.79f);
                RenderSettings.ambientEquatorColor = new Color(0.52f, 0.55f, 0.56f);
                RenderSettings.ambientGroundColor = new Color(0.34f, 0.34f, 0.31f);
                RenderSettings.ambientIntensity = 0.85f;
                RenderSettings.fog = false;

                GameObject sun = new GameObject("Soft morning sun");
                Light light = sun.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.965f, 0.92f);
                light.intensity = 0.95f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.65f;
                sun.transform.rotation = Quaternion.Euler(38, -32, 0);
                RenderSettings.sun = light;

                // Neutral matte ground so refined assets are judged on their own shape,
                // not on the project's default checkerboard paving texture.
                Material groundMat = new Material(Shader.Find("Standard"));
                groundMat.name = "VisualUpgrade neutral pavement";
                groundMat.color = new Color(0.72f, 0.70f, 0.66f);
                groundMat.SetFloat("_Glossiness", 0.18f);
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Test pavement (neutral)";
                ground.transform.localScale = new Vector3(6, 1, 6);
                ground.GetComponent<Renderer>().sharedMaterial = groundMat;

                // Minimal street context: asphalt, kerb and a zebra crossing.
                // This is test-scene dressing only; no traffic logic or S01-S03 is touched.
                Material asphalt = new Material(Shader.Find("Standard"));
                asphalt.name = "VisualUpgrade asphalt";
                asphalt.color = new Color(0.23f, 0.24f, 0.24f);
                asphalt.SetFloat("_Glossiness", 0.12f);
                GameObject road = GameObject.CreatePrimitive(PrimitiveType.Plane);
                road.name = "Test road";
                road.transform.localScale = new Vector3(6, 1, 0.85f);
                road.transform.position = new Vector3(0, 0.012f, -4.0f);
                road.GetComponent<Renderer>().sharedMaterial = asphalt;

                Material kerbMat = new Material(Shader.Find("Standard"));
                kerbMat.name = "VisualUpgrade kerb";
                kerbMat.color = new Color(0.62f, 0.61f, 0.58f);
                kerbMat.SetFloat("_Glossiness", 0.15f);
                GameObject kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                kerb.name = "Test kerb";
                kerb.transform.localScale = new Vector3(30f, 0.14f, 0.28f);
                kerb.transform.position = new Vector3(0, 0.07f, 0.02f);
                kerb.GetComponent<Renderer>().sharedMaterial = kerbMat;

                Material stripeMat = new Material(Shader.Find("Standard"));
                stripeMat.name = "VisualUpgrade crossing stripe";
                stripeMat.color = new Color(0.88f, 0.88f, 0.84f);
                stripeMat.SetFloat("_Glossiness", 0.12f);
                for (int i = -3; i <= 3; i++)
                {
                    GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stripe.name = "Test crossing stripe " + i;
                    stripe.transform.localScale = new Vector3(0.42f, 0.012f, 2.1f);
                    stripe.transform.position = new Vector3(i * 0.92f, 0.025f, -1.75f);
                    stripe.GetComponent<Renderer>().sharedMaterial = stripeMat;
                }

                // One realistic pedestrian.
                PlaceModel(AssetRoot + "Pedestrians/Athletic_man.fbx", new Vector3(0, 0, 2.2f), 1f, "Realistic pedestrian");

                // Two refined procedural street trees. FBX import can arrive at 0.01
                // scale, so normalise against measured height before placing.
                GameObject oak = PlaceModel(AssetRoot + "Trees/StreetPlane_Oak_Refined.fbx",
                    Vector3.zero, 1f, "Refined tree oak");
                NormalizeHeight(oak, 7.2f);
                oak.transform.position = new Vector3(-3.15f, 0, 3.6f);
                Debug.Log($"[VisualUpgrade] refined oak bounds={BoundsOf(oak).size}");

                GameObject willow = PlaceModel(AssetRoot + "Trees/StreetGinkgo_Willow_Refined.fbx",
                    Vector3.zero, 1f, "Refined tree willow");
                NormalizeHeight(willow, 6.8f);
                willow.transform.position = new Vector3(3.45f, 0, 4.3f);
                Debug.Log($"[VisualUpgrade] refined willow bounds={BoundsOf(willow).size}");

                // Refined building. Blender -Y front maps to Unity +Z after FBX import;
                // rotate 180 degrees so the framed front faces the kerb camera.
                GameObject building = PlaceModel(AssetRoot + "Buildings/NearFieldResidence_Refined.fbx",
                    Vector3.zero, 1f, "Refined near-field building");
                // Preserve the importer's Z-up correction, then turn the facade 180 degrees.
                building.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * building.transform.rotation;
                Bounds b = BoundsOf(building);
                float scale = 10.3f / Mathf.Max(0.001f, b.size.x);
                building.transform.localScale *= scale;
                b = BoundsOf(building);
                building.transform.position += new Vector3(-b.center.x, -b.min.y, 12.0f - b.center.z);
                Debug.Log($"[VisualUpgrade] refined building bounds centre={BoundsOf(building).center} " +
                          $"size={BoundsOf(building).size} renderers={building.GetComponentsInChildren<Renderer>().Length}");

                // First-person kerb-height camera looking toward the subjects.
                GameObject camGo = new GameObject("Kerb first-person camera");
                Camera cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                camGo.transform.position = new Vector3(0, 1.65f, -6.0f);
                Vector3 look = new Vector3(0, 2.45f, 5.0f);
                camGo.transform.rotation = Quaternion.LookRotation(look - camGo.transform.position);
                cam.fieldOfView = 58f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.70f, 0.77f, 0.80f);

                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("[VisualUpgrade] scene saved: " + ScenePath);

                string dir = "Results/visual-upgrade-test";
                Directory.CreateDirectory(dir);
                Capture(cam, Path.Combine(dir, "refined-assets-kerb-1080p.png"));
                CaptureSummary(dir);
                Debug.Log("[VisualUpgrade] capture complete: " + Path.GetFullPath(dir));
            }
            catch (Exception ex)
            {
                Debug.LogError("[VisualUpgrade] FAILED: " + ex);
                throw;
            }
        }

        private static Bounds BoundsOf(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
                b.Encapsulate(r.bounds);
            return b;
        }

        private static void NormalizeHeight(GameObject go, float targetHeight)
        {
            Bounds b = BoundsOf(go);
            float scale = targetHeight / Mathf.Max(0.001f, b.size.y);
            go.transform.localScale *= scale;
            b = BoundsOf(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        }

        private static GameObject PlaceModel(string assetPath, Vector3 position, float scale, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) throw new InvalidOperationException("Missing asset: " + assetPath);
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        private static void Capture(Camera cam, string file)
        {
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24);
            RenderTexture prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
            RenderTexture.active = prev;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(rt);
            Debug.Log("[VisualUpgrade] captured " + file);
        }

        private static void CaptureSummary(string dir)
        {
            string json = "{\n" +
                "  \"source\": \"Game03 VisualUpgrade refined isolated test\",\n" +
                "  \"assets\": {\n" +
                "    \"pedestrian\": \"Ara 3D Gen / CC0 MakeHuman data\",\n" +
                "    \"trees\": \"Procedural Blender refined street trees (fine leaf clusters)\",\n" +
                "    \"building\": \"Procedural Blender refined residence (frames, mullions, awning, balcony)\"\n" +
                "  },\n" +
                "  \"resolution\": \"1920x1080\",\n" +
                "  \"note\": \"Refined-asset gate test only; S01-S03 and traffic logic unchanged. Not a full-street acceptance.\"\n" +
                "}\n";
            File.WriteAllText(Path.Combine(dir, "visual-upgrade-summary.json"), json);
        }
    }
}
