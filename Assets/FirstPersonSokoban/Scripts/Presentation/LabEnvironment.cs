using System.Collections.Generic;
using FirstPersonSokoban.Core;
using FirstPersonSokoban.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstPersonSokoban.Presentation
{
    /// <summary>
    /// Room identity layered over the existing puzzle: geometry never participates
    /// in collisions, ray selection or grid rules. All generated objects are owned
    /// by the level root, so restarting cannot leave fixtures in the next room.
    /// </summary>
    public static class LabEnvironment
    {
        // Lighting/probe volume only; the room deliberately has no overhead mesh.
        public const float LightingHeight = 3.2f;
        public const int MaxShadowLights = 1;
        public const int MaxFillLights = 3;
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };

        private sealed class WallFace
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Transform Frame;
        }

        public static void StyleFloor(GameObject instance, LabMaterials palette)
        {
            palette.Initialize();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = palette.Floor;
        }

        public static void StyleWall(GameObject instance, LabMaterials palette)
        {
            palette.Initialize();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = renderer.name == "Wall Body" ? palette.Wall : palette.Trim;
                // The old single-face inset reads as a prototype grid line. New
                // panels are placed on the actual inward-facing wall surfaces.
                if (renderer.name.Contains("Inlay")) renderer.enabled = false;
            }
        }

        public static void Build(LevelDefinition definition, GridManager grid, Transform root, LabMaterials palette)
        {
            if (definition == null || grid == null || root == null || palette == null) return;
            if (root.Find("Lab Architecture") != null) return;
            palette.Initialize();
            Transform architecture = new GameObject("Lab Architecture").transform;
            architecture.SetParent(root, false);
            HashSet<Vector2Int> reachable = ReachableFloor(definition);
            var faces = new List<WallFace>();
            float cell = grid.CellSize;

            for (int y = 0; y < definition.Height; y++)
            for (int x = 0; x < definition.Width; x++)
            {
                var tile = new Vector2Int(x, y);
                if (definition.TileAt(tile) != TileType.Wall) continue;
                Vector3 world = grid.GridToWorld(tile);
                foreach (Vector2Int direction in Directions)
                {
                    if (!reachable.Contains(tile + direction)) continue;
                    Vector3 normal = new Vector3(direction.x, 0f, direction.y);
                    Transform surface = new GameObject("Interior Wall Surface").transform;
                    surface.SetParent(architecture, false);
                    surface.position = world + normal * cell * 0.5f;
                    surface.rotation = Quaternion.LookRotation(normal, Vector3.up);
                    faces.Add(new WallFace { Position = surface.position, Normal = normal, Frame = surface });
                    WallPanel(surface, cell, palette);
                }
            }

            Vector3 first = grid.GridToWorld(Vector2Int.zero);
            Vector3 last = grid.GridToWorld(new Vector2Int(definition.Width - 1, definition.Height - 1));
            Vector3 center = (first + last) * 0.5f;
            Vector3 span = new Vector3(definition.Width * cell, LightingHeight,
                definition.Height * cell);
            AddFeatureWalls(faces, definition, grid, palette);
            ConfigureLighting(architecture, center, span, definition, grid, reachable);
            var probeCells = new HashSet<Vector2Int>(reachable);
            probeCells.ExceptWith(definition.Boxes);
            AddProbe(architecture, center, span, grid, probeCells);
        }

        private static void WallPanel(Transform surface, float cell, LabMaterials palette)
        {
            // Every face stays at/barely in front of the original wall plane. In
            // particular, no proud shelving intrudes into the narrow box corridors.
            LocalPart(surface, "Lower Service Panel", new Vector3(0, 0.58f, -0.010f),
                new Vector3(cell - 0.06f, 0.88f, 0.028f), palette.Trim);
            LocalPart(surface, "Skirting", new Vector3(0, 0.075f, -0.018f),
                new Vector3(cell - 0.02f, 0.15f, 0.04f), palette.Dark);
            LocalPart(surface, "Panel Joint", new Vector3(-cell * 0.5f + 0.025f, 1.80f, -0.009f),
                new Vector3(0.022f, 1.44f, 0.02f), palette.Dark);
            LocalPart(surface, "Upper Service Rail", new Vector3(0, 2.69f, -0.012f),
                new Vector3(cell - 0.02f, 0.045f, 0.028f), palette.Trim);
        }

        private static void AddFeatureWalls(List<WallFace> faces, LevelDefinition definition,
            GridManager grid, LabMaterials palette)
        {
            if (faces.Count == 0) return;
            Vector3 start = grid.GridToWorld(definition.PlayerStart);
            Vector3 forward = Quaternion.Euler(0, definition.InitialYaw, 0) * Vector3.forward;
            int windowIndex = 0;
            float best = float.NegativeInfinity;
            for (int i = 0; i < faces.Count; i++)
            {
                Vector3 to = faces[i].Position - start;
                float score = Vector3.Dot(to.normalized, forward) * 4f - to.magnitude * 0.08f;
                if (score <= best) continue;
                best = score;
                windowIndex = i;
            }
            ObservationWindow(faces[windowIndex].Frame, palette);
            if (faces.Count > 4)
            {
                int doorIndex = (windowIndex + faces.Count / 2) % faces.Count;
                DoorFrame(faces[doorIndex].Frame, palette);
            }
            for (int i = 0; i < faces.Count; i++)
            {
                if (i == windowIndex || i == (windowIndex + faces.Count / 2) % faces.Count) continue;
                if (i % 9 == 4) Vent(faces[i].Frame, palette);
                if (i % 13 == 2) Display(faces[i].Frame, palette);
            }
        }

        private static void ObservationWindow(Transform wall, LabMaterials palette)
        {
            LocalPart(wall, "Observation Window Frame", new Vector3(0, 1.87f, -0.012f),
                new Vector3(1.74f, 1.08f, 0.028f), palette.Dark);
            LocalPart(wall, "Observation Window Glass", new Vector3(0, 1.87f, 0.004f),
                new Vector3(1.58f, 0.93f, 0.008f), palette.Glass);
            LocalPart(wall, "Window Mullion", new Vector3(0.26f, 1.87f, 0.012f),
                new Vector3(0.025f, 0.93f, 0.012f), palette.Metal);
            LocalPart(wall, "Window Status", new Vector3(-0.72f, 2.36f, 0.012f),
                new Vector3(0.13f, 0.018f, 0.012f), palette.GoalActive);
            Label(wall, "Observation Label", "OBSERVATION / 01", new Vector3(0, 1.25f, 0.014f),
                0.030f, LabMaterials.Hex("31444C"));
        }

        private static void DoorFrame(Transform wall, LabMaterials palette)
        {
            LocalPart(wall, "Service Door Frame", new Vector3(0, 1.19f, -0.011f),
                new Vector3(1.44f, 2.38f, 0.026f), palette.Dark);
            LocalPart(wall, "Closed Service Door", new Vector3(0, 1.16f, 0.005f),
                new Vector3(1.25f, 2.27f, 0.008f), palette.Wall);
            LocalPart(wall, "Door Center Joint", new Vector3(0, 1.15f, 0.012f),
                new Vector3(0.018f, 2.23f, 0.008f), palette.Trim);
            LocalPart(wall, "Door Access Panel", new Vector3(0.83f, 1.34f, 0.005f),
                new Vector3(0.15f, 0.27f, 0.012f), palette.Dark);
            LocalPart(wall, "Door Access Indicator", new Vector3(0.83f, 1.42f, 0.015f),
                new Vector3(0.075f, 0.022f, 0.006f), palette.Warning);
            Label(wall, "Room Number", "TRAINING BAY 01", new Vector3(0, 2.52f, 0.014f),
                0.028f, LabMaterials.Hex("31444C"));
        }

        private static void Vent(Transform wall, LabMaterials palette)
        {
            LocalPart(wall, "Vent Housing", new Vector3(0.2f, 2.33f, 0.003f),
                new Vector3(0.68f, 0.28f, 0.012f), palette.Dark);
            for (int row = 0; row < 4; row++)
                LocalPart(wall, "Vent Louver", new Vector3(0.2f, 2.24f + row * 0.055f, 0.012f),
                    new Vector3(0.60f, 0.024f, 0.012f), palette.Metal);
        }

        private static void Display(Transform wall, LabMaterials palette)
        {
            LocalPart(wall, "Wall Display Frame", new Vector3(0.16f, 1.87f, -0.003f),
                new Vector3(0.87f, 0.61f, 0.02f), palette.Dark);
            LocalPart(wall, "Wall Display Screen", new Vector3(0.16f, 1.87f, 0.012f),
                new Vector3(0.77f, 0.51f, 0.01f), palette.Glass);
            LocalPart(wall, "Display Status Line", new Vector3(0.14f, 1.74f, 0.020f),
                new Vector3(0.60f, 0.014f, 0.008f), palette.GoalActive);
            Label(wall, "Display Caption", "SPATIAL\nREADY", new Vector3(0.16f, 1.94f, 0.026f),
                0.023f, LabMaterials.AccentColor);
        }

        private static void ConfigureLighting(Transform parent, Vector3 center, Vector3 span,
            LevelDefinition definition, GridManager grid, HashSet<Vector2Int> reachable)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = LabMaterials.Hex("71818A");
            RenderSettings.ambientEquatorColor = LabMaterials.Hex("4E5B5E");
            RenderSettings.ambientGroundColor = LabMaterials.Hex("293A3D");
            RenderSettings.ambientIntensity = 0.65f;
            RenderSettings.fog = false;

            var cells = new List<Vector2Int>(reachable);
            cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            Vector3 keyPosition = center;
            float best = float.MaxValue;
            foreach (Vector2Int tile in cells)
            {
                Vector3 candidate = grid.GridToWorld(tile);
                float distance = (candidate - center).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                keyPosition = candidate;
            }
            Light key = MakeLight(parent, "Lab Main Softbox", keyPosition + Vector3.up * 3.02f,
                Color.white, 1.1f, Mathf.Max(span.x, span.z) * 1.5f);
            key.type = LightType.Spot;
            key.spotAngle = 145f;
            key.transform.rotation = Quaternion.Euler(90, 0, 0);
            key.useColorTemperature = true;
            key.colorTemperature = 6100;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.65f;
            key.shadowBias = 0.035f;
            key.shadowNormalBias = 0.22f;
            key.shadowNearPlane = 0.15f;

            for (int i = 0; i < MaxFillLights && cells.Count > 0; i++)
            {
                int index = i * (cells.Count - 1) / Mathf.Max(1, MaxFillLights - 1);
                Vector3 location = grid.GridToWorld(cells[index], 2.52f);
                MakeLight(parent, i == 0 ? "Warm Observation Fill" : "Lab Soft Fill",
                    location, LabMaterials.Hex(i == 0 ? "FFE8CC" : "DDEEEB"),
                    i == 0 ? 0.28f : 0.34f, Mathf.Max(span.x, span.z) * 0.85f);
            }
        }

        private static Light MakeLight(Transform parent, string name, Vector3 position,
            Color color, float intensity, float range)
        {
            Transform lamp = new GameObject(name).transform;
            lamp.SetParent(parent, false);
            lamp.position = position;
            Light light = lamp.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        private static void AddProbe(Transform parent, Vector3 center, Vector3 span, GridManager grid,
            HashSet<Vector2Int> reachable)
        {
            Transform anchor = new GameObject("Room Reflection Probe").transform;
            anchor.SetParent(parent, false);
            Vector3 probePosition = center;
            float nearest = float.MaxValue;
            foreach (Vector2Int cell in reachable)
            {
                Vector3 candidate = grid.GridToWorld(cell);
                float distance = (candidate - center).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                probePosition = candidate;
            }
            anchor.position = probePosition + Vector3.up * 1.55f;
            ReflectionProbe probe = anchor.gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.boxProjection = true;
            probe.center = center - probePosition;
            probe.size = span;
            probe.resolution = 128;
            probe.intensity = 0.5f;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = LabMaterials.Hex("68797C");
            probe.nearClipPlane = 0.08f;
            probe.farClipPlane = Mathf.Max(span.x, span.z) * 1.8f;
            if (Application.isPlaying && !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                probe.RenderProbe();
        }

        private static HashSet<Vector2Int> ReachableFloor(LevelDefinition definition)
        {
            var reached = new HashSet<Vector2Int> { definition.PlayerStart };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(definition.PlayerStart);
            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current + direction;
                    if (!definition.IsInside(next) || definition.TileAt(next) == TileType.Wall || !reached.Add(next)) continue;
                    queue.Enqueue(next);
                }
            }
            return reached;
        }

        private static GameObject WorldPart(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject result = LocalPart(parent, name, Vector3.zero, scale, material);
            result.transform.position = position;
            return result;
        }

        private static GameObject LocalPart(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = true;
            return part;
        }

        private static void Label(Transform parent, string name, string content, Vector3 position,
            float characterSize, Color color)
        {
            Transform label = new GameObject(name).transform;
            label.SetParent(parent, false);
            label.localPosition = position;
            label.localRotation = Quaternion.Euler(0, 180, 0);
            TextMesh text = label.gameObject.AddComponent<TextMesh>();
            text.text = content;
            text.font = UIFactory.Font;
            text.fontSize = 40;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.richText = false;
            if (text.font != null)
            {
                text.font.RequestCharactersInTexture(content, text.fontSize, FontStyle.Normal);
                Renderer renderer = text.GetComponent<Renderer>();
                renderer.sharedMaterial = text.font.material;
                // TextMesh glyph scale depends on the imported font. Fit the label
                // into its physical plaque rather than assuming a pixel/world ratio.
                Bounds bounds = renderer.localBounds;
                float width = content.Contains("\n") ? 0.60f : 1.42f;
                float height = content.Contains("\n") ? 0.32f : 0.16f;
                if (bounds.size.x > 0.0001f && bounds.size.y > 0.0001f)
                    label.localScale = Vector3.one * Mathf.Min(width / bounds.size.x, height / bounds.size.y);
            }
        }
    }
}
