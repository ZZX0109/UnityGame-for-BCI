using UnityEngine;

namespace StreetCrossing.Presentation
{
    public enum StreetFacadeKind { Residence, Apartment, Convenience, Cafe, Pharmacy, Bookshop }

    /// <summary>Metre-scale original modular streetscape. Does not create traffic or training volumes.</summary>
    public static class StreetDistrictBuilder
    {
        private static readonly string[] Captions = { "梧桐里 · 住宅", "街心公寓", "邻里便利", "街角咖啡", "社区药房", "时光书店" };
        private static Transform Group(Transform parent, string name, Vector3 position, float yaw = 0)
        {
            Transform group = new GameObject(name).transform; group.SetParent(parent, false);
            group.localPosition = position; group.localRotation = Quaternion.Euler(0, yaw, 0); return group;
        }
        private static Renderer Part(StreetWorld world, Transform parent, string name, Vector3 p, Vector3 s, string colour, float radius = .025f)
            => world.VisualRounded(parent, name, p, s, colour, radius);

        public static Transform Building(Transform parent, StreetWorld world, StreetVisualProfile profile, Vector3 position, int side,
            float height, StreetFacadeKind kind, bool distant = false, bool corner = false, float width = 10.3f)
        {
            Transform block = Group(parent, (corner ? "Corner building " : "Facade ") + kind, position, side > 0 ? 0 : 180);
            string masonry = kind == StreetFacadeKind.Apartment ? "C5CBC8" : kind == StreetFacadeKind.Residence ? profile.facade : "D9CBBB";
            Renderer shell = Part(world, block, "Masonry shell", Vector3.up * (height * .5f), new Vector3(width, height, 4), masonry, .06f);
            shell.sharedMaterial = world.FacadeMaterial(masonry);
            bool arcadeCorner = corner && (kind == StreetFacadeKind.Cafe || kind == StreetFacadeKind.Apartment);
            float cornerCut = arcadeCorner ? .7f : 1.15f;
            if (corner) shell.GetComponent<MeshFilter>().sharedMesh = world.CornerMesh(new Vector3(width, height, 4), cornerCut);
            Part(world, block, "Stone plinth", new Vector3(0, .18f, -2.025f), new Vector3(width, .36f, .09f), "A9A69A");
            Part(world, block, "Roof parapet", new Vector3(0, height + .08f, 0), new Vector3(width + .16f, .22f, 4.16f), profile.secondary);
            Part(world, block, "Roof ventilation", new Vector3(width * .22f, height + .38f, .5f), new Vector3(1.0f, .6f, 1), profile.metal);
            Part(world, block, "Roof service cabinet", new Vector3(-width * .26f, height + .28f, .7f), new Vector3(1.1f, .4f, .8f), "858D87");
            int floors = Mathf.Max(1, Mathf.FloorToInt((height - 3.1f) / 2.6f));
            for (int floor = 0; floor < floors; floor++)
            for (int bay = -2; bay <= 2; bay++)
            {
                if (corner && bay == -2) continue;
                float x = bay * width * .17f, y = 4.2f + floor * 2.6f;
                Part(world, block, "Window surround", new Vector3(x, y, -2.05f), new Vector3(width * .132f, 1.5f, .14f), "EEE7D7");
                Renderer pane = Part(world, block, "Recessed upper glazing", new Vector3(x, y, -2.135f), new Vector3(width * .115f, 1.27f, .035f), "506866");
                pane.sharedMaterial = world.GlassMaterial;
                if (!distant)
                {
                    Part(world, block, "Window mullion", new Vector3(x, y, -2.16f), new Vector3(.045f, 1.28f, .045f), profile.secondary);
                    Part(world, block, "Window sill", new Vector3(x, y - .76f, -2.16f), new Vector3(width * .145f, .08f, .34f), profile.secondary);
                    if (kind == StreetFacadeKind.Residence && bay % 2 == 0)
                    {
                        Part(world, block, "Planter box", new Vector3(x, y - .56f, -2.30f), new Vector3(.75f, .24f, .25f), profile.accent);
                        Part(world, block, "Window planting", new Vector3(x, y - .36f, -2.30f), new Vector3(.7f, .2f, .22f), profile.foliage);
                    }
                }
            }
            // The side windows make corner/end elevations complete rather than presenting a blank block.
            for (int edge = -1; edge <= 1; edge += 2)
            {
                Part(world, block, "Rainwater downpipe", new Vector3(edge * (width * .5f - .12f), height * .5f, -2.13f), new Vector3(.075f, height, .075f), profile.metal);
                for (int floor = 0; floor < floors; floor++)
                    Part(world, block, "Side elevation window", new Vector3(edge * (width * .5f + .015f), 4.2f + floor * 2.6f, .1f), new Vector3(.035f, 1.3f, 1.1f), "506866");
            }
            bool residential = kind == StreetFacadeKind.Residence || kind == StreetFacadeKind.Apartment;
            if (residential)
            {
                Part(world, block, "Entrance stone surround", new Vector3(0, 1.45f, -2.10f), new Vector3(1.8f, 2.9f, .22f), profile.secondary);
                Part(world, block, "Entrance door", new Vector3(0, 1.35f, -2.23f), new Vector3(1.3f, 2.6f, .035f), profile.metal);
                Part(world, block, "Door glazing", new Vector3(0, 1.7f, -2.26f), new Vector3(.92f, 1.5f, .015f), "718782");
                for (int edge = -1; edge <= 1; edge += 2)
                    Part(world, block, "Ground floor residential window", new Vector3(edge * width * .30f, 1.65f, -2.10f), new Vector3(width * .22f, 1.7f, .08f), "667E78");
            }
            else
            {
                for (int bay = -1; bay <= 1; bay++)
                {
                    if (corner && bay == -1) continue;
                    float x = bay * width * .30f;
                    Part(world, block, "Shop window frame", new Vector3(x, 1.45f, -2.07f), new Vector3(width * .28f, 2.6f, .16f), profile.metal);
                    Part(world, block, "Shop interior shadow", new Vector3(x, 1.45f, -2.17f), new Vector3(width * .25f, 2.35f, .035f), "344441");
                    if (!distant) ShopDisplay(world, block, profile, kind, x, width * .25f);
                    Part(world, block, "Shopfront transom", new Vector3(x, 2.45f, -2.22f), new Vector3(width * .26f, .065f, .05f), profile.secondary);
                }
                string colour = kind == StreetFacadeKind.Pharmacy ? "4E7965" : kind == StreetFacadeKind.Bookshop ? "5C6A7D" : kind == StreetFacadeKind.Cafe ? "A97651" : profile.accent;
                Part(world, block, "Shop fascia", new Vector3(corner ? cornerCut * .5f : 0, 2.98f, -2.2f), new Vector3(width + .08f - (corner ? cornerCut : 0), .65f, .25f), colour);
                Part(world, block, "Fabric awning", new Vector3(corner ? cornerCut * .5f : 0, 3.36f, -2.65f), new Vector3(width - (corner ? cornerCut : 0), .13f, 1.3f), profile.secondary);
                if (kind == StreetFacadeKind.Pharmacy)
                {
                    Part(world, block, "Pharmacy cross vertical", new Vector3(width * .40f, 2.96f, -2.35f), new Vector3(.10f, .42f, .03f), "F0EFDC");
                    Part(world, block, "Pharmacy cross horizontal", new Vector3(width * .40f, 2.96f, -2.36f), new Vector3(.36f, .10f, .03f), "F0EFDC");
                }
            }
            if (!distant) world.Label(Captions[(int)kind], new Vector3(0, residential ? 2.82f : 2.98f, -2.37f), .075f,
                residential ? profile.accent : "F3EDDB", block, 0, width * .75f, .43f);
            if (corner) CornerEntrance(world, block, profile, width, cornerCut, arcadeCorner);
            world.CombineVisuals(block);
            world.AddVisualLod(block, distant ? .018f : .006f);
            return block;
        }

        private static void CornerEntrance(StreetWorld world, Transform block, StreetVisualProfile profile, float width, float cut, bool arcade)
        {
            // A diagonal, human-scale doorway occupies the chamfer; a second shopfront faces the side street.
            Transform doorway = Group(block, arcade ? "Arcade corner entrance" : "Chamfer shop corner entrance",
                new Vector3(-width * .5f + cut * .5f - .025f, 0, -2 + cut * .5f - .025f), 45);
            Part(world, doorway, "Corner doorway surround", new Vector3(0, 1.4f, 0), new Vector3(cut * 1.25f, 2.8f, .12f), profile.secondary);
            Part(world, doorway, "Diagonal entrance glass", new Vector3(0, 1.35f, -.075f), new Vector3(cut * 1.03f, 2.55f, .035f), "4D6962");
            Part(world, doorway, "Corner door handle", new Vector3(cut * .25f, 1.25f, -.12f), new Vector3(.035f, .34f, .04f), "D3C3A1");
            Transform side = Group(block, "Side street active frontage", new Vector3(-width * .5f - .04f, 0, .45f), 90);
            Part(world, side, "Side shop window frame", new Vector3(0, 1.5f, 0), new Vector3(2.35f, 2.5f, .12f), profile.metal);
            Part(world, side, "Side display interior", new Vector3(0, 1.5f, -.075f), new Vector3(2.12f, 2.25f, .025f), "506962");
            for (int shelf = 0; shelf < 3; shelf++)
                Part(world, side, "Side display shelf", new Vector3(0, .65f + shelf * .55f, -.10f), new Vector3(1.9f, .06f, .035f), profile.secondary);
            Part(world, side, "Side street fascia", new Vector3(0, 2.98f, -.04f), new Vector3(2.8f, .65f, .20f), profile.accent);
            if (arcade)
            {
                Part(world, side, "Arcade flat canopy", new Vector3(0, 3.38f, -.46f), new Vector3(3.1f, .18f, 1.0f), profile.secondary);
                for (int end = -1; end <= 1; end += 2)
                    Part(world, side, "Arcade square column", new Vector3(end * 1.35f, 1.6f, -.76f), new Vector3(.12f, 3.2f, .12f), profile.accent);
                Part(world, side, "Residential corner bay sill", new Vector3(0, 4.1f, -.26f), new Vector3(2.5f, .18f, .62f), profile.secondary);
                Part(world, side, "Residential projecting bay glass", new Vector3(0, 4.8f, -.30f), new Vector3(2.3f, 1.25f, .30f), "617D77");
            }
            else
            {
                Part(world, doorway, "Corner entrance hood", new Vector3(0, 3.25f, -.25f), new Vector3(cut * 1.55f, .16f, .65f), profile.accent);
                Part(world, side, "Side street fabric canopy", new Vector3(0, 3.38f, -.35f), new Vector3(2.8f, .12f, .85f), profile.secondary);
            }
        }

        private static void ShopDisplay(StreetWorld world, Transform block, StreetVisualProfile profile, StreetFacadeKind kind, float x, float width)
        {
            if (kind == StreetFacadeKind.Cafe)
            {
                Part(world, block, "Cafe counter", new Vector3(x, .9f, -2.20f), new Vector3(width * .8f, .75f, .06f), "9C7956");
                for (int cup = -1; cup <= 1; cup++)
                    Part(world, block, "Coffee cup display", new Vector3(x + cup * .45f, 1.35f, -2.25f), new Vector3(.16f, .23f, .04f), "EEE5D1");
            }
            else
                for (int shelf = 0; shelf < 3; shelf++)
                {
                    float y = .55f + shelf * .52f;
                    Part(world, block, "Display shelf", new Vector3(x, y, -2.22f), new Vector3(width * .85f, .055f, .06f), profile.secondary);
                    for (int item = -2; item <= 2; item++)
                    {
                        bool books = kind == StreetFacadeKind.Bookshop;
                        Part(world, block, books ? "Book spine" : "Product carton", new Vector3(x + item * width * .15f, y + .18f, -2.25f),
                            new Vector3(books ? .13f : .22f, .28f + (item & 1) * .07f, .025f), (item & 1) == 0 ? profile.accent : profile.secondary);
                    }
                }
        }

        public static void BuildExtensions(Transform parent, StreetWorld world, StreetVisualProfile profile, float roadHalfWidth)
        {
            Transform streets = Group(parent, "Continuous 160 metre streetscape", Vector3.zero);
            for (int end = -1; end <= 1; end += 2)
            {
                Renderer asphalt = Part(world, streets, "Distant road continuation", new Vector3(end * 62, -.15f, 0), new Vector3(36, .28f, roadHalfWidth * 2), "53534D");
                asphalt.sharedMaterial = world.AsphaltMaterial;
                for (int side = -1; side <= 1; side += 2)
                {
                    Renderer pavement = Part(world, streets, "Distant sidewalk continuation", new Vector3(end * 62, -.06f, side * (roadHalfWidth + 3.2f)), new Vector3(36, .12f, 6.4f), "D6CBAD");
                    pavement.sharedMaterial = world.PavingMaterial;
                    for (int bay = 0; bay < 3; bay++)
                        Building(streets, world, profile, new Vector3(end * (46 + bay * 12), 0, side * 11.3f), side,
                            7.4f + (bay % 2) * 2.6f, profile.FacadeAt(bay + (end > 0 ? 3 : 0)), true, false, 10.8f);
                    for (int bay = 0; bay < 4; bay++)
                    {
                        float x = end * (43 + bay * 10);
                        Part(world, streets, "Distant tree trunk", new Vector3(x, 1.3f, side * 5.4f), new Vector3(.22f, 2.6f, .22f), "82715D");
                        Part(world, streets, "Distant tree crown", new Vector3(x, 3.6f, side * 5.4f), new Vector3(2.3f, 2.8f, 2.1f), profile.foliage, .9f);
                    }
                }
                for (int dash = 0; dash < 12; dash++)
                    Part(world, streets, "Distant centre dash", new Vector3(end * (45 + dash * 3), .003f, 0), new Vector3(1.6f, .006f, .1f), "DBBC76", .002f);
            }
            // Low-contrast second row supplies real depth behind the street-facing blocks.
            for (int side = -1; side <= 1; side += 2)
            for (int bay = -3; bay <= 3; bay++)
            {
                Transform skyline = Group(streets, "Background urban block", new Vector3(bay * 19 + 5, 0, side * 29));
                Part(world, skyline, "Background masonry", new Vector3(0, 6 + (bay & 1), 0), new Vector3(15, 12 + (bay & 1) * 2, 9), "C5C7BF");
                for (int row = 0; row < 3; row++)
                    Part(world, skyline, "Distant window band", new Vector3(0, 3 + row * 3, side > 0 ? -4.53f : 4.53f), new Vector3(12, .85f, .03f), "71817D");
                world.CombineVisuals(skyline); world.AddVisualLod(skyline, .025f);
            }
        }
    }
}
