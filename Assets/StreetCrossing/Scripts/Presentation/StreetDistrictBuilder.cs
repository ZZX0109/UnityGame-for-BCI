using UnityEngine;
using StreetCrossing.Core;

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
        private static Renderer Part(StreetWorld world, Transform parent, string name, Vector3 p, Vector3 s, string colour, float radius = .025f, float smoothness = .18f)
            => world.VisualRounded(parent, name, p, s, colour, radius, smoothness);
        /// <summary>Deterministic per-building brightness nudge: parse the RGB hex, scale each channel, clamp.</summary>
        private static string ShadeHex(string hex, float factor)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color colour);
            colour.r = Mathf.Clamp01(colour.r * factor);
            colour.g = Mathf.Clamp01(colour.g * factor);
            colour.b = Mathf.Clamp01(colour.b * factor);
            return ColorUtility.ToHtmlStringRGB(colour);
        }
        /// <summary>Rectangular frame ring through a size-keyed cached mesh, so glazing reads through a real frame.</summary>
        private static Renderer Frame(StreetWorld world, Transform parent, string name, Vector3 p, float width, float height, float depth, float frame, string colour)
        {
            string key = "RectFrame:" + width.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + height.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + depth.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                + ":" + frame.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            return world.VisualMesh(parent, name, p, key, () => StreetDetailMesh.RectFrame(width, height, depth, frame), colour);
        }

        public static Transform Building(Transform parent, StreetWorld world, StreetVisualProfile profile, Vector3 position, int side,
            float height, StreetFacadeKind kind, bool distant = false, bool corner = false, float width = 10.3f)
        {
            Transform block = Group(parent, (corner ? "Corner building " : "Facade ") + kind, position, side > 0 ? 0 : 180);
            string masonry = kind == StreetFacadeKind.Apartment ? "C5CBC8" : kind == StreetFacadeKind.Residence ? profile.facade : "D9CBBB";
            // Each block shifts its masonry brightness by a deterministic ±4% so identical profiles still read as separate plots.
            masonry = ShadeHex(masonry, 1f + (((Mathf.RoundToInt(position.x * 7 + position.z * 3) % 3) + 3) % 3 - 1) * .04f);
            Renderer shell = Part(world, block, "Masonry shell", Vector3.up * (height * .5f), new Vector3(width, height, 4), masonry, .06f);
            shell.sharedMaterial = world.FacadeMaterial(masonry);
            bool arcadeCorner = corner && (kind == StreetFacadeKind.Cafe || kind == StreetFacadeKind.Apartment);
            float cornerCut = arcadeCorner ? .7f : 1.15f;
            bool residential = kind == StreetFacadeKind.Residence || kind == StreetFacadeKind.Apartment;
            if (corner) shell.GetComponent<MeshFilter>().sharedMesh = world.CornerMesh(new Vector3(width, height, 4), cornerCut);
            Part(world, block, "Stone plinth", new Vector3(0, .18f, -2.025f), new Vector3(width, .36f, .09f), "A9A69A");
            Part(world, block, "Roof parapet", new Vector3(0, height + .08f, 0), new Vector3(width + .16f, .22f, 4.16f), profile.secondary);
            Part(world, block, "Roof ventilation", new Vector3(width * .22f, height + .38f, .5f), new Vector3(1.0f, .6f, 1), profile.metal);
            Part(world, block, "Roof service cabinet", new Vector3(-width * .26f, height + .28f, .7f), new Vector3(1.1f, .4f, .8f), "858D87");
            int floors = Mathf.Max(1, Mathf.FloorToInt((height - 3.1f) / 2.6f));
            bool shapedRetailUpper = !distant && (kind == StreetFacadeKind.Bookshop || kind == StreetFacadeKind.Pharmacy || kind == StreetFacadeKind.Cafe);
            if (shapedRetailUpper)
            {
                for (int floor = 0; floor < floors; floor++)
                {
                    float y = 4.2f + floor * 2.6f;
                    if (kind == StreetFacadeKind.Pharmacy)
                    {
                        // One shallow tripartite ribbon gives the pharmacy a horizontal upper-storey rhythm.
                        float ribbonWidth = width * .76f;
                        // Recessed backing sits just behind the masonry face; glass and frame sit in front of it.
                        Part(world, block, "Pharmacy ribbon backing", new Vector3(0, y, -2.015f),
                            new Vector3(width * .72f, .88f, .025f), "263633");
                        Frame(world, block, "Pharmacy ribbon surround", new Vector3(0, y, -2.06f),
                            ribbonWidth, 1.05f, .10f, .22f, "EEE7D7");
                        Renderer pane = Part(world, block, "Pharmacy ribbon glazing", new Vector3(0, y, -2.04f),
                            new Vector3(width * .70f, .82f, .015f), "506866");
                        pane.sharedMaterial = world.GlassMaterial;
                        for (int panel = -1; panel <= 1; panel += 2)
                            Part(world, block, "Pharmacy ribbon mullion", new Vector3(panel * width * .22f, y, -2.07f),
                                new Vector3(.045f, .84f, .045f), profile.secondary);
                        Part(world, block, "Pharmacy ribbon sill", new Vector3(0, y - .56f, -2.08f),
                            new Vector3(width * .78f, .08f, .30f), profile.secondary);
                    }
                    else
                    {
                        bool bookshop = kind == StreetFacadeKind.Bookshop;
                        float spacing = width * (bookshop ? .25f : .22f);
                        float surroundWidth = width * (bookshop ? .17f : .36f);
                        float paneWidth = width * (bookshop ? .135f : .32f);
                        for (int bay = -1; bay <= 1; bay++)
                        {
                            if (!bookshop && bay == 0) continue;
                            float x = bay * spacing;
                            // 20 mm wider than the pane so the backing fully covers the frame-ring opening below.
                            Part(world, block, bookshop ? "Bookshop narrow upper backing" : "Cafe paired upper backing",
                                new Vector3(x, y, -2.015f), new Vector3(paneWidth + .02f, 1.30f, .025f), "263633");
                            Frame(world, block, bookshop ? "Bookshop narrow upper surround" : "Cafe paired upper surround",
                                new Vector3(x, y, -2.06f), surroundWidth, 1.5f, .10f, bookshop ? .19f : .21f, "EEE7D7");
                            Renderer pane = Part(world, block, bookshop ? "Bookshop narrow upper glazing" : "Cafe paired upper glazing",
                                new Vector3(x, y, -2.04f), new Vector3(paneWidth, 1.27f, .015f), "506866");
                            pane.sharedMaterial = world.GlassMaterial;
                            Part(world, block, bookshop ? "Bookshop sash mullion" : "Cafe upper window mullion",
                                new Vector3(x, y, -2.07f), new Vector3(.045f, 1.28f, .045f), profile.secondary);
                            Part(world, block, bookshop ? "Bookshop projecting upper sill" : "Cafe projecting upper sill",
                                new Vector3(x, y - .76f, -2.08f), new Vector3(surroundWidth + .04f, .08f, .30f), profile.secondary);
                            if (!bookshop)
                                Part(world, block, "Cafe upper transom", new Vector3(x, y + .22f, -2.07f),
                                    new Vector3(paneWidth, .045f, .045f), profile.secondary);
                        }
                    }
                }
            }
            else
            {
                for (int floor = 0; floor < floors; floor++)
                for (int bay = -2; bay <= 2; bay++)
                {
                    if (corner && bay == -2) continue;
                    float x = bay * width * .17f, y = 4.2f + floor * 2.6f;
                    // Dark recessed backing gives the window a real pocket behind the transparent glass.
                    Part(world, block, "Window backing", new Vector3(x, y, -2.015f), new Vector3(width * .12f, 1.30f, .025f), "263633");
                    Frame(world, block, "Window surround", new Vector3(x, y, -2.06f), width * .132f, 1.5f, .10f, .115f, "EEE7D7");
                    Renderer pane = Part(world, block, "Recessed upper glazing", new Vector3(x, y, -2.04f), new Vector3(width * .115f, 1.27f, .015f), "506866");
                    pane.sharedMaterial = world.GlassMaterialVariant(((bay + 2) + floor * 2 + (side > 0 ? 0 : 1)) % 3);
                    if (!distant)
                    {
                        Part(world, block, "Window mullion", new Vector3(x, y, -2.07f), new Vector3(.045f, 1.28f, .045f), profile.secondary);
                        Part(world, block, "Window sill", new Vector3(x, y - .76f, -2.08f), new Vector3(width * .145f, .08f, .30f), profile.secondary);
                        if (kind == StreetFacadeKind.Residence && bay % 2 == 0)
                        {
                            Part(world, block, "Planter box", new Vector3(x, y - .56f, -2.30f), new Vector3(.75f, .24f, .25f), profile.accent);
                            Part(world, block, "Window planting", new Vector3(x, y - .36f, -2.30f), new Vector3(.7f, .2f, .22f), profile.foliage);
                        }
                        if (residential && (bay + floor) % 2 == 0)
                            Part(world, block, "Window curtain", new Vector3(x, y - .25f, -2.035f),
                                new Vector3(width * .10f, .80f, .012f), (bay + floor) % 4 == 0 ? "E3D9C2" : "C7BFAE");
                    }
                    // The top-floor centre bay of a mid-block apartment steps out onto a small balcony.
                    if (kind == StreetFacadeKind.Apartment && !distant && !corner && floor == floors - 1 && bay == 0)
                    {
                        Part(world, block, "Balcony slab", new Vector3(0, y - .83f, -2.32f), new Vector3(1.5f, .09f, .62f), "C9BCA4");
                        Part(world, block, "Balcony top rail", new Vector3(0, y + .02f, -2.60f), new Vector3(1.5f, .06f, .06f), profile.metal);
                        for (int baluster = -1; baluster <= 1; baluster += 2)
                        {
                            Part(world, block, "Balcony baluster", new Vector3(baluster * .25f, y - .40f, -2.60f), new Vector3(.035f, .85f, .035f), profile.metal);
                            Part(world, block, "Balcony baluster", new Vector3(baluster * .65f, y - .40f, -2.60f), new Vector3(.035f, .85f, .035f), profile.metal);
                        }
                        for (int rail = -1; rail <= 1; rail += 2)
                            Part(world, block, "Balcony side rail", new Vector3(rail * .72f, y - .40f, -2.32f), new Vector3(.05f, .85f, .60f), profile.metal);
                    }
                }
            }
            // The side windows make corner/end elevations complete rather than presenting a blank block.
            // 2026-11-21 proof gate: the side=-1 bay=0 (world x=0) residence gets real gable hierarchy on
            // its west end (local +x) in place of the old flat pane; every other building keeps the original.
            bool westGableTarget = kind == StreetFacadeKind.Residence && !corner && !distant
                && side < 0 && Mathf.Approximately(position.x, 0f);
            for (int edge = -1; edge <= 1; edge += 2)
            {
                Part(world, block, "Rainwater downpipe", new Vector3(edge * (width * .5f - .12f), height * .5f, -2.13f), new Vector3(.075f, height, .075f), profile.metal);
                if (westGableTarget && edge == 1) continue; // replaced by EndWallDetail below
                bool detailedSide = !distant && !corner;
                for (int floor = 0; floor < floors; floor++)
                {
                    float y = 4.2f + floor * 2.6f;
                    if (!detailedSide)
                    {
                        Renderer flatPane = Part(world, block, "Side elevation window",
                            new Vector3(edge * (width * .5f + .015f), y, .1f),
                            new Vector3(.035f, 1.3f, 1.1f), "506866");
                        flatPane.sharedMaterial = world.GlassMaterial;
                        continue;
                    }
                    // Near mid-block gables get the full backing/glass/frame/sill window assembly.
                    Renderer sidePane = Part(world, block, "Side elevation window",
                        new Vector3(edge * (width * .5f + .035f), y, .1f),
                        new Vector3(.018f, 1.30f, 1.10f), "506866");
                    sidePane.sharedMaterial = world.GlassMaterialVariant((floor + (edge > 0 ? 1 : 2)) % 3);
                    Part(world, block, "Side window backing", new Vector3(edge * (width * .5f + .012f), y, .1f),
                        new Vector3(.025f, 1.34f, 1.16f), "263633");
                    Renderer sideFrame = Frame(world, block, "Side window surround",
                        new Vector3(edge * (width * .5f + .055f), y, .1f), 1.32f, 1.50f, .10f, .11f, "EEE7D7");
                    sideFrame.transform.localRotation = Quaternion.Euler(0, edge * 90, 0);
                    Part(world, block, "Side window sill", new Vector3(edge * (width * .5f + .075f), y - .76f, .1f),
                        new Vector3(.16f, .09f, 1.34f), profile.secondary);
                }
            }
            if (!distant)
            {
                Part(world, block, "Eaves cornice", new Vector3(0, height - .20f, -2.03f), new Vector3(width + .10f, .16f, .16f), profile.secondary);
                if (residential)
                    Part(world, block, "Storey string course", new Vector3(0, 3.02f, -2.045f), new Vector3(width, .14f, .12f), "A9A69A");
            }
            if (residential)
            {
                Part(world, block, "Entrance stone surround", new Vector3(0, 1.45f, -2.10f), new Vector3(1.8f, 2.9f, .22f), profile.secondary);
                Part(world, block, "Entrance door", new Vector3(0, 1.35f, -2.23f), new Vector3(1.3f, 2.6f, .035f), profile.metal);
                Renderer doorGlass = Part(world, block, "Door glazing", new Vector3(0, 1.7f, -2.26f), new Vector3(.92f, 1.5f, .015f), "718782");
                doorGlass.sharedMaterial = world.GlassMaterial;
                Transform doorway = new GameObject("Doorway " + kind).transform; doorway.SetParent(block, false); doorway.localPosition = new Vector3(0, 0, -2.32f);
                for (int edge = -1; edge <= 1; edge += 2)
                {
                    float wx = edge * width * .30f;
                    Part(world, block, "Ground floor residential window backing", new Vector3(wx, 1.65f, -2.015f), new Vector3(width * .20f, 1.55f, .025f), "263633");
                    Renderer pane = Part(world, block, "Ground floor residential window", new Vector3(wx, 1.65f, -2.04f), new Vector3(width * .22f, 1.7f, .015f), "506866");
                    pane.sharedMaterial = world.GlassMaterialVariant(edge > 0 ? 2 : 1);
                    if (!distant)
                    {
                        // Closed roller shutter surface-mounted over each side window: the imported
                        // housing reads up close, a flat slatted panel stands in when the model is absent.
                        Transform shutter = world.Modules.SpawnImported(block, "rollershutter_window_01", new Vector3(wx, .6f, -2.16f), 1.9f);
                        if (shutter != null) world.AddVisualLod(shutter, .012f);
                        else Part(world, block, "Residential shutter panel", new Vector3(wx, 1.68f, -2.07f), new Vector3(width * .20f, 1.62f, .04f), "8E959B");
                    }
                }
            }
            else
            {
                float baySpacing = kind == StreetFacadeKind.Bookshop ? .31f : kind == StreetFacadeKind.Pharmacy ? .36f : .30f;
                float sideWindowWidth = kind == StreetFacadeKind.Bookshop ? width * .30f : kind == StreetFacadeKind.Pharmacy ? width * .20f : width * .28f;
                float sideFrameWidth = kind == StreetFacadeKind.Bookshop ? width * .33f : kind == StreetFacadeKind.Pharmacy ? width * .22f : width * .28f;
                float entryWidth = kind == StreetFacadeKind.Pharmacy ? Mathf.Min(1.28f, width * .34f) : Mathf.Min(1.15f, width * .25f);
                for (int bay = -1; bay <= 1; bay++)
                {
                    if (corner && bay == -1) continue;
                    float x = bay * width * baySpacing;
                    if (kind == StreetFacadeKind.Cafe && bay != 0)
                    {
                        // Give the cafe side windows an actual recessed reveal instead of a full-size slab.
                        float opening = width * .28f;
                        const float frameZ = -2.07f, frameWidth = .085f;
                        for (int edge = -1; edge <= 1; edge += 2)
                            Part(world, block, "Cafe window stone jamb", new Vector3(x + edge * (opening * .5f - frameWidth * .5f), 1.45f, frameZ),
                                new Vector3(frameWidth, 2.6f, .18f), profile.metal);
                        Part(world, block, "Cafe window stone lintel", new Vector3(x, 2.72f, frameZ),
                            new Vector3(opening - frameWidth, .12f, .18f), profile.secondary);
                        Part(world, block, "Cafe projecting stone sill", new Vector3(x, .19f, -2.09f),
                            new Vector3(opening, .12f, .24f), profile.secondary);
                        Part(world, block, "Cafe sill apron", new Vector3(x, .075f, -2.045f),
                            new Vector3(opening * .88f, .10f, .13f), profile.metal);
                        Part(world, block, "Cafe recessed window backing", new Vector3(x, 1.45f, -2.015f),
                            new Vector3(width * .25f, 2.35f, .025f), "263633");
                        Renderer cafeGlass = Part(world, block, "Cafe side glazing", new Vector3(x, 1.45f, -2.04f),
                            new Vector3(opening * .92f, 2.30f, .015f), "506866");
                        cafeGlass.sharedMaterial = world.GlassMaterial;
                    }
                    else
                    {
                        float panelFrameWidth = bay == 0
                            ? (kind == StreetFacadeKind.Pharmacy ? entryWidth + .14f : width * .28f)
                            : sideFrameWidth;
                        float panelBackingWidth = bay == 0
                            ? (kind == StreetFacadeKind.Pharmacy ? entryWidth : width * .25f)
                            : sideWindowWidth;
                        Part(world, block, "Shop window frame", new Vector3(x, 1.45f, -2.07f), new Vector3(panelFrameWidth, 2.6f, .16f), profile.metal);
                        Part(world, block, "Shop interior shadow", new Vector3(x, 1.45f, -2.17f), new Vector3(panelBackingWidth, 2.35f, .035f), "344441");
                        Renderer shopGlass = Part(world, block, "Shop side glazing", new Vector3(x, 1.45f, -2.10f),
                            new Vector3(panelBackingWidth, 2.35f, .015f), "506866");
                        shopGlass.sharedMaterial = world.GlassMaterial;
                    }
                    if (bay == 0)
                    {
                        Renderer entryGlass = Part(world, block, "Shop entrance glazing", new Vector3(0, 1.35f, -2.22f), new Vector3(entryWidth, 2.3f, .025f), "506866");
                        entryGlass.sharedMaterial = world.GlassMaterial;
                        Part(world, block, "Shop entrance handle", new Vector3(Mathf.Min(.35f, width * .08f), 1.2f, -2.27f), new Vector3(.035f, .32f, .045f), "D3C3A1");
                        if (!distant && kind == StreetFacadeKind.Pharmacy)
                        {
                            for (int edge = -1; edge <= 1; edge += 2)
                                Part(world, block, "Pharmacy clear-width entry jamb", new Vector3(edge * (entryWidth * .5f + .045f), 1.30f, -2.24f),
                                    new Vector3(.09f, 2.60f, .08f), "E2DED2");
                            Part(world, block, "Pharmacy entry head", new Vector3(0, 2.60f, -2.24f),
                                new Vector3(entryWidth + .18f, .10f, .08f), "E2DED2");
                        }
                        Transform threshold = new GameObject("Doorway " + kind).transform; threshold.SetParent(block, false); threshold.localPosition = new Vector3(0, 0, -2.32f);
                    }
                    else if (!distant)
                    {
                        // Convenience side bays read as closed service bays: an imported roller shutter
                        // over the glazing, or a slatted flat panel when the model is unavailable.
                        if (kind == StreetFacadeKind.Convenience)
                        {
                            Transform serviceShutter = world.Modules.SpawnImported(block, "rollershutter_door", new Vector3(x, .05f, -2.15f), 2.3f);
                            if (serviceShutter != null) world.AddVisualLod(serviceShutter, .012f);
                            else Part(world, block, "Convenience service shutter", new Vector3(x, 1.25f, -2.09f), new Vector3(1.05f, 2.2f, .05f), "8E959B");
                        }
                        else ShopDisplay(world, block, profile, kind, x, sideWindowWidth);
                    }
                    float transomWidth = bay == 0 ? entryWidth + .10f : sideFrameWidth;
                    Part(world, block, "Shopfront transom", new Vector3(x, 2.45f, -2.22f), new Vector3(transomWidth, .065f, .05f), profile.secondary);
                }
                string colour = kind == StreetFacadeKind.Pharmacy ? "4E7965" : kind == StreetFacadeKind.Bookshop ? "5C6A7D" : kind == StreetFacadeKind.Cafe ? "A97651" : profile.accent;
                Part(world, block, "Shop fascia", new Vector3(corner ? cornerCut * .5f : 0, 2.98f, -2.2f), new Vector3(width + .08f - (corner ? cornerCut : 0), .65f, .25f), colour);
                if (!distant && !residential && !corner)
                {
                    // A perpendicular hanging sign repeats the fascia colour so the shopfront reads along the pavement.
                    // Its bottom edge stays above the 1.8 m player capsule brushing the facade collider.
                    Part(world, block, "Vertical shop sign", new Vector3(entryWidth * .5f + .42f, 2.85f, -2.38f), new Vector3(.34f, 1.7f, .06f), colour);
                    for (int arm = -1; arm <= 1; arm += 2)
                        Part(world, block, "Sign bracket arm", new Vector3(entryWidth * .5f + .42f, 2.85f + arm * .70f, -2.20f), new Vector3(.05f, .05f, .36f), profile.metal);
                    Part(world, block, "Sign face inset", new Vector3(entryWidth * .5f + .42f, 2.85f, -2.415f), new Vector3(.28f, 1.56f, .015f), "F3EDDB");
                }
                float canopyWidth = width - (corner ? cornerCut : 0);
                float centre = corner ? cornerCut * .5f : 0;
                if (!distant && kind == StreetFacadeKind.Cafe)
                {
                    var canvas = Part(world, block, "Cafe canvas hood", new Vector3(centre, 3.49f, -2.73f),
                        new Vector3(canopyWidth, .10f, 1.38f), "9F745A");
                    canvas.transform.localRotation = Quaternion.Euler(-13, 0, 0);
                    Part(world, block, "Cafe stitched valance", new Vector3(centre, 3.25f, -3.37f),
                        new Vector3(canopyWidth, .22f, .055f), "855C47");
                }
                else if (!distant && kind == StreetFacadeKind.Bookshop)
                {
                    Part(world, block, "Bookshop timber canopy", new Vector3(centre, 3.4f, -2.47f),
                        new Vector3(canopyWidth, .14f, .85f), "957456");
                    for (int end = -1; end <= 1; end += 2)
                    {
                        var brace = Part(world, block, "Canopy timber bracket",
                            new Vector3(centre + end * canopyWidth * .44f, 3.2f, -2.46f),
                            new Vector3(.085f, .085f, .62f), "614E44");
                        brace.transform.localRotation = Quaternion.Euler(36, 0, 0);
                    }
                }
                else if (!distant && kind == StreetFacadeKind.Pharmacy)
                {
                    Part(world, block, "Pharmacy flat stone canopy", new Vector3(centre, 3.4f, -2.57f),
                        new Vector3(canopyWidth * .92f, .14f, .88f), profile.secondary);
                    for (int end = -1; end <= 1; end += 2)
                        Part(world, block, "Pharmacy canopy bracket", new Vector3(centre + end * canopyWidth * .32f, 3.22f, -2.50f),
                            new Vector3(.12f, .27f, .42f), profile.metal);
                }
                else
                    Part(world, block, "Fabric awning", new Vector3(centre, 3.36f, -2.65f), new Vector3(canopyWidth, .13f, 1.3f), profile.secondary);
                if (kind == StreetFacadeKind.Pharmacy)
                {
                    Part(world, block, "Pharmacy cross vertical", new Vector3(width * .40f, 2.96f, -2.35f), new Vector3(.10f, .42f, .03f), "F0EFDC");
                    Part(world, block, "Pharmacy cross horizontal", new Vector3(width * .40f, 2.96f, -2.36f), new Vector3(.36f, .10f, .03f), "F0EFDC");
                }
            }
            if (residential && !distant)
            {
                // Wall-mounted condenser on its own pair of brackets; visual only, no collider.
                // 2026-11-25: the CC0 air-conditioner FBX replaces the old blocky Part unit; the
                // procedural version stays as fallback when the imported model is unavailable.
                Transform condenser = world.Modules.SpawnImported(block, "exterior_aircon_unit",
                    new Vector3(width * .17f, 2.96f, -2.30f), .55f);
                if (condenser != null)
                {
                    DisableRustedVariant(condenser);
                    world.AddVisualLod(condenser, .012f);
                }
                else
                {
                    Part(world, block, "AC outdoor unit", new Vector3(width * .17f, 3.20f, -2.30f), new Vector3(.66f, .48f, .26f), "C9CFC9", .025f, .35f);
                    Part(world, block, "AC fan grille", new Vector3(width * .17f, 3.20f, -2.435f), new Vector3(.32f, .32f, .02f), "6E7672", .025f, .30f);
                }
                for (int bracket = -1; bracket <= 1; bracket += 2)
                    Part(world, block, "AC bracket", new Vector3(width * .17f + bracket * .25f, 2.92f, -2.28f), new Vector3(.05f, .08f, .24f), profile.metal);
                // Entrance porch: canopy on brackets, lamp and number plaque beside the door, threshold stone at the step.
                Part(world, block, "Entrance porch canopy", new Vector3(0, 3.08f, -2.42f), new Vector3(2.0f, .08f, .64f), profile.secondary);
                for (int edge = -1; edge <= 1; edge += 2)
                    Part(world, block, "Porch canopy bracket", new Vector3(edge * .85f, 2.96f, -2.18f), new Vector3(.07f, .20f, .16f), profile.metal);
                Part(world, block, "Entrance wall lamp", new Vector3(1.15f, 2.20f, -2.13f), new Vector3(.10f, .18f, .10f), "D3C3A1");
                Part(world, block, "Entrance lamp glow", new Vector3(1.15f, 2.20f, -2.185f), new Vector3(.08f, .12f, .02f), "F5E7BE", .025f, .50f);
                Part(world, block, "Door number plaque", new Vector3(-1.15f, 2.05f, -2.115f), new Vector3(.30f, .15f, .03f), profile.accent);
                Part(world, block, "Entrance threshold stone", new Vector3(0, .02f, -2.40f), new Vector3(1.6f, .04f, .50f), "A9A69A");
            }
            if (!distant && kind == StreetFacadeKind.Residence)
            {
                Part(world, block, "Chimney stack", new Vector3(width * .30f, height + .55f, 1.2f), new Vector3(.45f, 1.1f, .45f), "B0A090");
                Part(world, block, "Chimney pot", new Vector3(width * .30f, height + 1.20f, 1.2f), new Vector3(.16f, .25f, .16f), "8A5F4B");
            }
            if (!distant && kind == StreetFacadeKind.Apartment)
            {
                Part(world, block, "Roof water tank", new Vector3(width * .10f, height + .85f, 1.4f), new Vector3(1.3f, 1.3f, 1.3f), "B9BDB5", .20f);
                for (int legX = -1; legX <= 1; legX += 2)
                for (int legZ = -1; legZ <= 1; legZ += 2)
                    Part(world, block, "Tank leg", new Vector3(width * .10f + legX * .5f, height + .20f, 1.4f + legZ * .5f), new Vector3(.06f, .40f, .06f), profile.metal);
                if (!corner)
                {
                    // Modular CC0 fire escape climbing the facade from pavement level, offset left of
                    // the central entrance and the bay-0 balcony; visual only, no collider. Its front
                    // tip stops at the same ~3.4 m envelope as the shopfront valances so the walk
                    // bands keep their clearance.
                    Transform fireEscape = world.Modules.SpawnImported(block, "modular_fire_escape",
                        new Vector3(-width * .28f, 0f, -2.68f), 6.4f);
                    if (fireEscape != null) world.AddVisualLod(fireEscape, .012f);
                    else Part(world, block, "Fire escape stub", new Vector3(-width * .28f, 3.2f, -2.55f), new Vector3(2.4f, 6.4f, .08f), "6E7672", .02f, .4f);
                }
            }
            if (!distant) world.Label(Captions[(int)kind], new Vector3(0, residential ? 2.82f : 2.98f, -2.37f), .075f,
                residential ? profile.accent : "F3EDDB", block, 0, width * .75f, .43f);
            if (corner) CornerEntrance(world, block, profile, width, height, cornerCut, arcadeCorner);
            if (corner && side > 0)
            {
                // The north block sits west of its side street: its active side must face east, not penetrate the west neighbour.
                foreach (Transform part in block.GetComponentsInChildren<Transform>())
                {
                    if (part == block) continue;
                    Vector3 p = part.localPosition; Quaternion r = part.localRotation;
                    part.localPosition = new Vector3(-p.x, p.y, p.z); part.localRotation = new Quaternion(r.x, -r.y, -r.z, r.w);
                }
                shell.GetComponent<MeshFilter>().sharedMesh = world.CornerMesh(new Vector3(width, height, 4), cornerCut, true);
            }
            if (westGableTarget) EndWallDetail(world, block, profile, width, height, floors);
            world.CombineVisuals(block);
            world.AddVisualLod(block, distant ? .018f : .006f);
            return block;
        }

        /// <summary>
        /// Poly Haven's exterior_aircon_unit FBX carries a pristine and a rusted twin stacked on the
        /// same spot (one texture set per state). Whole-file instantiation would z-fight the pair, so
        /// the rusted subtree is switched off — renderers too, because CombineVisuals walks inactive
        /// children as well and would otherwise bake the duplicate shell into the combined chunk.
        /// </summary>
        private static void DisableRustedVariant(Transform imported)
        {
            Transform[] nodes = imported.GetComponentsInChildren<Transform>(true);
            foreach (Transform node in nodes)
            {
                if (node == imported || !node.name.ToLowerInvariant().Contains("rusted")) continue;
                node.gameObject.SetActive(false);
                foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            }
        }

        /// <summary>
        /// West gable of the side=-1 bay=0 (world x=0) residence only. Replaces the old flat pane with
        /// two framed upper windows whose frames and sill stand proud of the masonry (local x > +5.15),
        /// a dark recessed backing in front of the shell, and a continuous plinth course. Surface
        /// hierarchy only — no boolean opening in the shell. Visual-only: no collider is created.
        /// </summary>
        private static void EndWallDetail(StreetWorld world, Transform block, StreetVisualProfile profile, float width, float height, int floors)
        {
            float face = width * .5f;          // local +x gable plane (5.15)
            // Backing and glass sit just proud of the masonry face so they are not clipped by the shell;
            // frames stand 80 mm proud of the wall.
            float backing = face + .005f;
            float pane = face + .015f;
            float frame = face + .08f;
            for (int floor = 0; floor < 1; floor++)
            {
                float y = 4.2f;
                for (int bay = -1; bay <= 1; bay += 2)
                {
                    float z = bay * .72f;      // centre spacing 1.44 m
                    Part(world, block, "Gable window backing", new Vector3(backing, y, z),
                        new Vector3(.025f, 1.35f, .78f), "263633");
                    Renderer gableFrame = Frame(world, block, "Gable window surround", new Vector3(frame - .02f, y, z),
                        .82f, 1.42f, .10f, .04f, "EEE7D7");
                    gableFrame.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    Renderer paneR = Part(world, block, "Gable recessed glazing", new Vector3(pane, y, z),
                        new Vector3(.05f, 1.35f, .75f), "506866");
                    paneR.sharedMaterial = world.GlassMaterial;
                    Part(world, block, "Gable window mullion", new Vector3(frame + .02f, y, z),
                        new Vector3(.05f, 1.36f, .05f), profile.secondary);
                    Part(world, block, "Gable projecting sill", new Vector3(frame + .045f, y - .76f, z),
                        new Vector3(.17f, .09f, .96f), profile.secondary);
                }
            }
            // Continuous plinth course wraps this gable at the street plinth height.
            Part(world, block, "Gable plinth course", new Vector3(face + .045f, .18f, 0),
                new Vector3(.11f, .36f, 4.0f), "A9A69A");
        }

        public static void CafeSeating(Transform frontage)
        {
            const string folder = "StreetCrossing/Modules/KayKitRestaurant/";
            GameObject table = Resources.Load<GameObject>(folder + "CafeTable");
            GameObject chair = Resources.Load<GameObject>(folder + "CafeChair");
            Material palette = Resources.Load<Material>(folder + "CafeCeramics");
            if (!table || !chair || !palette) throw new System.InvalidOperationException("Missing KayKit cafe furniture or its palette.");
            Transform terrace = Group(frontage, "Cafe pavement seating", new Vector3(5.3f, 0, -2.86f), 0);
            CafePiece(terrace, table, Vector3.zero, 0, .74f, palette);
            CafePiece(terrace, chair, Vector3.left * .83f, 90, .90f, palette);
            CafePiece(terrace, chair, Vector3.right * .83f, -90, .90f, palette);
            var lod = terrace.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.011f, terrace.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }

        private static void CafePiece(Transform parent, GameObject asset, Vector3 position, float yaw, float scale, Material palette)
        {
            var piece = Object.Instantiate(asset, parent, false);
            piece.name = asset.name;
            piece.transform.localPosition = position;
            piece.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            piece.transform.localScale = Vector3.one * scale;
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = palette;
            // Simplified physical bounds so players cannot walk through the furniture.
            if (asset.name == "CafeTable")
            {
                BoxCollider top = piece.gameObject.AddComponent<BoxCollider>();
                top.center = new Vector3(0, .73f, 0); top.size = new Vector3(.7f, .06f, .7f);
            }
            else if (asset.name == "CafeChair")
            {
                BoxCollider seat = piece.gameObject.AddComponent<BoxCollider>();
                seat.center = new Vector3(0, .45f, 0); seat.size = new Vector3(.45f, .06f, .45f);
                BoxCollider back = piece.gameObject.AddComponent<BoxCollider>();
                back.center = new Vector3(0, .78f, -.18f); back.size = new Vector3(.45f, .50f, .06f);
            }
        }

        private static void CornerEntrance(StreetWorld world, Transform block, StreetVisualProfile profile, float width, float height, float cut, bool arcade)
        {
            // A diagonal, human-scale doorway occupies the chamfer; a second shopfront faces the side street.
            Transform doorway = Group(block, arcade ? "Arcade corner entrance" : "Chamfer shop corner entrance",
                new Vector3(-width * .5f + cut * .5f - .025f, 0, -2 + cut * .5f - .025f), 45);
            Part(world, doorway, "Corner doorway surround", new Vector3(0, 1.4f, 0), new Vector3(cut * 1.25f, 2.8f, .12f), profile.secondary);
            Renderer diagGlass = Part(world, doorway, "Diagonal entrance glass", new Vector3(0, 1.35f, -.075f), new Vector3(cut * 1.03f, 2.55f, .035f), "4D6962");
            diagGlass.sharedMaterial = world.GlassMaterial;
            Part(world, doorway, "Corner door handle", new Vector3(cut * .25f, 1.25f, -.12f), new Vector3(.035f, .34f, .04f), "D3C3A1");
            Transform side = Group(block, "Side street active frontage", new Vector3(-width * .5f - .04f, 0, .45f), 90);
            Part(world, side, "Side shop window frame", new Vector3(0, 1.5f, 0), new Vector3(2.35f, 2.5f, .12f), profile.metal);
            Part(world, side, "Side display interior", new Vector3(0, 1.5f, -.075f), new Vector3(2.12f, 2.25f, .025f), "506962");
            Renderer sideGlass = Part(world, side, "Side display glazing", new Vector3(0, 1.5f, -.045f), new Vector3(2.0f, 2.1f, .015f), "506866");
            sideGlass.sharedMaterial = world.GlassMaterial;
            for (int shelf = 0; shelf < 3; shelf++)
                Part(world, side, "Side display shelf", new Vector3(0, .65f + shelf * .55f, -.10f), new Vector3(1.9f, .06f, .035f), profile.secondary);
            Part(world, side, "Side street fascia", new Vector3(0, 2.98f, -.04f), new Vector3(2.8f, .65f, .20f), profile.accent);
            if (arcade)
            {
                Part(world, side, "Arcade flat canopy", new Vector3(0, 3.38f, -.46f), new Vector3(3.1f, .18f, 1.0f), profile.secondary);
                for (int end = -1; end <= 1; end += 2)
                    Part(world, side, "Arcade square column", new Vector3(end * 1.35f, 1.6f, -.76f), new Vector3(.12f, 3.2f, .12f), profile.accent);
                Part(world, side, "Residential corner bay sill", new Vector3(0, 4.1f, -.26f), new Vector3(2.5f, .18f, .62f), profile.secondary);
                Renderer bayGlass = Part(world, side, "Residential projecting bay glass", new Vector3(0, 4.8f, -.30f), new Vector3(2.3f, 1.25f, .30f), "617D77");
                bayGlass.sharedMaterial = world.GlassMaterial;
            }
            else
            {
                Part(world, doorway, "Corner entrance hood", new Vector3(0, 3.25f, -.25f), new Vector3(cut * 1.55f, .16f, .65f), profile.accent);
                Part(world, side, "Side street fabric canopy", new Vector3(0, 3.38f, -.35f), new Vector3(2.8f, .12f, .85f), profile.secondary);
                // Storey count is recomputed here from the height (same formula as Building) instead of adding a floors parameter.
                int floors = Mathf.Max(1, Mathf.FloorToInt((height - 3.1f) / 2.6f));
                // The doorway group already carries the 45 degree chamfer yaw, so these upper windows land on the diagonal face.
                for (int floor = 0; floor < floors; floor++)
                {
                    float y = 4.2f + floor * 2.6f;
                    Part(world, doorway, "Chamfer window backing", new Vector3(0, y, -.02f), new Vector3(cut * .9f, 1.25f, .025f), "263633");
                    Renderer chamferGlass = Part(world, doorway, "Chamfer glazing", new Vector3(0, y, -.05f), new Vector3(cut * .85f, 1.22f, .015f), "506866");
                    chamferGlass.sharedMaterial = world.GlassMaterial;
                    Frame(world, doorway, "Chamfer window surround", new Vector3(0, y, -.07f), cut * 1.0f, 1.45f, .10f, .10f, "EEE7D7");
                }
            }
        }

        private static void ShopDisplay(StreetWorld world, Transform block, StreetVisualProfile profile, StreetFacadeKind kind, float x, float width)
        {
            if (kind == StreetFacadeKind.Cafe)
            {
                CafeWindowDisplay(world, block, x, width);
            }
            else
                for (int shelf = 0; shelf < 3; shelf++)
                {
                    float y = .55f + shelf * .52f;
                    Part(world, block, "Display shelf", new Vector3(x, y, -2.22f), new Vector3(width * .85f, .055f, .06f), profile.secondary);
                    for (int item = -2; item <= 2; item++)
                    {
                        bool books = kind == StreetFacadeKind.Bookshop;
                        float pitch = width * .16f;
                        float itemWidth = Mathf.Min(books ? .13f : .22f, pitch * .80f);
                        float itemHeight = .28f + (item & 1) * .07f;
                        Part(world, block, books ? "Book spine" : "Product carton", new Vector3(x + item * pitch, y + .0275f + itemHeight * .5f, -2.25f),
                            new Vector3(itemWidth, itemHeight, .025f), (item & 1) == 0 ? profile.accent : profile.secondary);
                    }
                }
        }

        private static void CafeWindowDisplay(StreetWorld world, Transform block, float x, float windowWidth)
        {
            float displayWidth = windowWidth * .88f;
            Part(world, block, "Cafe oak counter cabinet", new Vector3(x, .91f, -2.08f),
                new Vector3(displayWidth * .96f, .62f, .23f), "76553D", .045f);
            Part(world, block, "Cafe recessed counter toe kick", new Vector3(x, .59f, -2.105f),
                new Vector3(displayWidth * .88f, .08f, .12f), "453B35", .018f);
            Part(world, block, "Cafe counter front inset", new Vector3(x, .91f, -2.187f),
                new Vector3(displayWidth * .76f, .38f, .025f), "604736", .018f);
            Part(world, block, "Cafe counter brass rail", new Vector3(x, .70f, -2.208f),
                new Vector3(displayWidth * .72f, .018f, .018f), "B79864", .008f);
            Part(world, block, "Cafe stone countertop", new Vector3(x, 1.245f, -2.08f),
                new Vector3(displayWidth * 1.02f, .08f, .26f), "D7C8AD", .025f);
            Part(world, block, "Cafe counter front stone edge", new Vector3(x, 1.238f, -2.212f),
                new Vector3(displayWidth * .98f, .045f, .025f), "B6A588", .012f);

            // The rear shelf sits behind the cups so the narrow window reads as a small, layered cafe display.
            Part(world, block, "Cafe rear floating display shelf", new Vector3(x, 1.78f, -2.04f),
                new Vector3(displayWidth * .92f, .055f, .13f), "775B43", .018f);
            for (int end = -1; end <= 1; end += 2)
                Part(world, block, "Cafe shelf bracket", new Vector3(x + end * displayWidth * .33f, 1.67f, -2.07f),
                    new Vector3(.035f, .18f, .045f), "57483B", .01f);

            Part(world, block, "Cafe coffee bean pouch", new Vector3(x - displayWidth * .27f, 1.90f, -2.075f),
                new Vector3(.14f, .18f, .075f), "AA7950", .024f);
            Part(world, block, "Cafe pouch paper label", new Vector3(x - displayWidth * .27f, 1.90f, -2.118f),
                new Vector3(.084f, .075f, .009f), "E8DCC4", .008f);
            Part(world, block, "Cafe pouch roast mark", new Vector3(x - displayWidth * .27f, 1.895f, -2.125f),
                new Vector3(.036f, .009f, .004f), "76533D", .002f);
            world.VisualMesh(block, "Cafe bean tin", new Vector3(x + displayWidth * .27f, 1.815f, -2.075f),
                "CafeBeanTin", CreateBeanTinMesh, "78867B", .28f);
            Part(world, block, "Cafe bean tin lid", new Vector3(x + displayWidth * .27f, 1.966f, -2.075f),
                new Vector3(.09f, .016f, .09f), "B79662", .018f);
            Part(world, block, "Cafe bean tin label", new Vector3(x + displayWidth * .27f, 1.86f, -2.123f),
                new Vector3(.066f, .05f, .008f), "D6C4A0", .008f);

            for (int cup = -1; cup <= 1; cup++)
                CoffeeCup(world, block, x + cup * displayWidth * .31f, 1.287f, -2.10f);
        }

        private static void CoffeeCup(StreetWorld world, Transform parent, float x, float baseY, float z)
        {
            world.VisualMesh(parent, "Cafe ceramic saucer", new Vector3(x, 1.285f, z),
                "CafeSaucer", CreateSaucerMesh, "F1EBDD", .32f);
            world.VisualMesh(parent, "Cafe tapered ceramic cup", new Vector3(x, baseY, z),
                "CafeCupBody", CreateCupBodyMesh, "F4EFE4", .32f);
            world.VisualMesh(parent, "Cafe cup handle", new Vector3(x, baseY, z - .052f),
                "CafeCupHandle", CreateCupHandleMesh, "F4EFE4", .32f);
            world.VisualMesh(parent, "Cafe coffee surface", new Vector3(x, baseY + .163f, z),
                "CafeCoffeeSurface", CreateCoffeeSurfaceMesh, "4A281D", .16f);
            world.VisualMesh(parent, "Cafe crema center", new Vector3(x - .005f, baseY + .1645f, z),
                "CafeCrema", CreateCremaMesh, "C69A66", .18f);
        }

        private static Mesh CreateCupBodyMesh()
        {
            // The profile turns over a thick lip and descends into the cup, leaving a real opening above the coffee.
            Vector2[] profile =
            {
                new Vector2(0f, 0f), new Vector2(.039f, 0f), new Vector2(.044f, .004f), new Vector2(.047f, .013f),
                new Vector2(.055f, .13f), new Vector2(.058f, .175f), new Vector2(.047f, .175f),
                new Vector2(.044f, .164f), new Vector2(.040f, .035f), new Vector2(0f, .035f)
            };
            return LatheMesh(profile, 24, "Tapered cafe cup");
        }

        private static Mesh CreateSaucerMesh()
        {
            Vector2[] profile =
            {
                new Vector2(0f, .002f), new Vector2(.045f, 0f), new Vector2(.071f, .003f),
                new Vector2(.092f, .011f), new Vector2(.098f, .018f), new Vector2(.088f, .016f),
                new Vector2(.068f, .008f), new Vector2(0f, .008f)
            };
            return LatheMesh(profile, 24, "Shallow cafe cup saucer");
        }

        private static Mesh CreateBeanTinMesh()
        {
            Vector2[] profile =
            {
                new Vector2(0f, 0f), new Vector2(.034f, 0f), new Vector2(.041f, .008f), new Vector2(.041f, .124f),
                new Vector2(.035f, .15f), new Vector2(.030f, .15f), new Vector2(0f, .15f)
            };
            return LatheMesh(profile, 20, "Cafe bean tin");
        }

        private static Mesh CreateCoffeeSurfaceMesh() => DiskMesh(.0405f, 24, "Coffee surface");
        private static Mesh CreateCremaMesh() => DiskMesh(.018f, 20, "Crema center");

        private static Mesh LatheMesh(Vector2[] profile, int segments, string name)
        {
            var vertices = new Vector3[profile.Length * segments];
            var uv = new Vector2[vertices.Length];
            for (int ring = 0; ring < profile.Length; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = 2f * Mathf.PI * segment / segments;
                int index = ring * segments + segment;
                vertices[index] = new Vector3(profile[ring].x * Mathf.Cos(angle), profile[ring].y,
                    profile[ring].x * Mathf.Sin(angle));
                uv[index] = new Vector2(segment / (float)segments, ring / (float)(profile.Length - 1));
            }
            var triangles = new int[(profile.Length - 1) * segments * 6];
            int cursor = 0;
            for (int ring = 0; ring < profile.Length - 1; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                int a = ring * segments + segment;
                int b = ring * segments + next;
                int c = (ring + 1) * segments + segment;
                int d = (ring + 1) * segments + next;
                triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh DiskMesh(float radius, int segments, string name)
        {
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            vertices[0] = Vector3.zero; uv[0] = new Vector2(.5f, .5f);
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = 2f * Mathf.PI * segment / segments;
                vertices[segment + 1] = new Vector3(radius * Mathf.Cos(angle), 0, radius * Mathf.Sin(angle));
                uv[segment + 1] = new Vector2(.5f + .5f * Mathf.Cos(angle), .5f + .5f * Mathf.Sin(angle));
            }
            var triangles = new int[segments * 3];
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles[segment * 3] = 0; triangles[segment * 3 + 1] = next + 1; triangles[segment * 3 + 2] = segment + 1;
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCupHandleMesh()
        {
            const int pathSegments = 24, tubeSegments = 8;
            const float centerX = .078f, centerY = .09f, pathRadius = .05f, tubeRadius = .009f;
            var vertices = new Vector3[(pathSegments + 1) * tubeSegments];
            var uv = new Vector2[vertices.Length];
            for (int path = 0; path <= pathSegments; path++)
            {
                float theta = Mathf.Lerp(135f, -135f, path / (float)pathSegments) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0);
                Vector3 centre = new Vector3(centerX, centerY, 0) + radial * pathRadius;
                for (int tube = 0; tube < tubeSegments; tube++)
                {
                    float phi = 2f * Mathf.PI * tube / tubeSegments;
                    int index = path * tubeSegments + tube;
                    vertices[index] = centre + radial * (tubeRadius * Mathf.Cos(phi))
                        + Vector3.forward * (tubeRadius * Mathf.Sin(phi));
                    uv[index] = new Vector2(path / (float)pathSegments, tube / (float)tubeSegments);
                }
            }
            var triangles = new int[pathSegments * tubeSegments * 6];
            int cursor = 0;
            for (int path = 0; path < pathSegments; path++)
            for (int tube = 0; tube < tubeSegments; tube++)
            {
                int nextTube = (tube + 1) % tubeSegments;
                int a = path * tubeSegments + tube;
                int b = path * tubeSegments + nextTube;
                int c = (path + 1) * tubeSegments + tube;
                int d = (path + 1) * tubeSegments + nextTube;
                triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = c;
                triangles[cursor++] = b; triangles[cursor++] = d; triangles[cursor++] = c;
            }
            var mesh = new Mesh { name = "C shaped cafe cup handle", vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public static void BuildExtensions(Transform parent, StreetWorld world, StreetScenarioDefinition scenario, float roadHalfWidth)
        {
            StreetVisualProfile profile = scenario.visualProfile;
            Transform streets = Group(parent, "Continuous 160 metre streetscape", Vector3.zero);
            for (int end = -1; end <= 1; end += 2)
            {
                Renderer asphalt = Part(world, streets, "Distant road continuation", new Vector3(end * 62, -.15f, 0), new Vector3(36, .28f, roadHalfWidth * 2), "53534D");
                asphalt.sharedMaterial = world.AsphaltMaterial;
                world.SetAsphaltMetreScaleUv(asphalt);
                for (int side = -1; side <= 1; side += 2)
                {
                    Renderer pavement = Part(world, streets, "Distant sidewalk continuation", new Vector3(end * 62, -.06f, side * (roadHalfWidth + 3.2f)), new Vector3(36, .12f, 6.4f), "D6CBAD");
                    pavement.sharedMaterial = world.PavingMaterial;
                    for (int bay = 0; bay < 3; bay++)
                        Building(streets, world, profile, new Vector3(end * (46 + bay * 12), 0, StreetLayout.SidewalkZ(side * 11.3f)), side,
                            7.4f + (bay % 2) * 2.6f, profile.FacadeAt(bay + (end > 0 ? 3 : 0)), true, false, 10.8f);
                    for (int bay = 0; bay < 4; bay++)
                    {
                        float x = end * (43 + bay * 10);
                        world.CreateLodgedTree(streets,
                            new Vector3(x, 0, StreetLayout.SidewalkZ(side * 5.4f)),
                            bay + (side > 0 ? 1 : 0));
                    }
                }
                // Distant markings sit flush on the -0.01 m asphalt top.
                for (int dash = 0; dash < 12; dash++)
                {
                    for (int side = -1; side <= 1; side += 2)
                        Part(world, streets, "Distant same-direction lane dash", new Vector3(end * (45 + dash * 3), -.007f, side * StreetLayout.LaneWidth), new Vector3(1.6f, .006f, .1f), "ECE9DA", .002f);
                }
                for (int side = -1; side <= 1; side += 2)
                    Part(world, streets, "Distant double centre line", new Vector3(end * 62, -.007f, side * .08f), new Vector3(36, .006f, .055f), "DBBC76", .002f);
            }
            // Low-contrast second row supplies real depth behind the street-facing blocks.
            for (int side = -1; side <= 1; side += 2)
            for (int bay = -3; bay <= 3; bay++)
            {
                Transform skyline = Group(streets, "Background urban block", new Vector3(bay * 19 + 5, 0, StreetLayout.SidewalkZ(side * 29)));
                int floors = 3 + (bay + 3 + (side > 0 ? 1 : 0)) % 3;
                float height = floors * 2.8f + .65f;
                float faceZ = -side * 4.52f;
                Part(world, skyline, "Apartment masonry", Vector3.up * (height * .5f), new Vector3(15, height, 9),
                    (bay & 1) == 0 ? "B5BAAF" : "B0AFAB");
                Part(world, skyline, "Parapet coping", Vector3.up * height, new Vector3(15.24f, .16f, 9.24f), "989D97");
                Part(world, skyline, "Stairwell roof", new Vector3((bay & 1) == 0 ? -3 : 3, height + .69f, .35f),
                    new Vector3(3.5f, 1.38f, 3.2f), "A4A69E");
                for (int row = 0; row < floors; row++)
                {
                    float y = 1.65f + row * 2.8f;
                    for (int window = -2; window <= 2; window++)
                    {
                        float x = window * 2.68f;
                        Part(world, skyline, "Recessed apartment window", new Vector3(x, y, faceZ),
                            new Vector3(1.62f, 1.28f, .075f), (window + row + bay) % 3 == 0 ? "879594" : "748589");
                        if (window == -2 || window == 2)
                            Part(world, skyline, "Balcony spandrel", new Vector3(x, y - .65f, faceZ - side * .23f),
                                new Vector3(2.08f, .62f, .5f), "ADB2AC");
                    }
                    for (int end = -1; end <= 1; end += 2)
                        Part(world, skyline, "Gable stair window", new Vector3(end * 7.52f, y, .85f),
                            new Vector3(.075f, 1.32f, 1.02f), "829090");
                }
                world.CombineVisuals(skyline); world.AddVisualLod(skyline, .025f);
            }
            // Low-contrast horizon silhouettes close the view past both street ends; visual parts only, no colliders.
            for (int end = -1; end <= 1; end += 2)
            {
                Part(world, streets, "Horizon ground apron", new Vector3(end * 95, -.06f, 0), new Vector3(30, .12f, 120), "98A19A");
                for (int b = 0; b < 3; b++)
                {
                    float bx = end * (86 + b * 7);
                    Part(world, streets, "Horizon block", new Vector3(bx, 4.5f + b * 1.5f, (b - 1) * 13),
                        new Vector3(11, 9 + b * 3, 10), b % 2 == 0 ? "98A3A6" : "A3ACA9");
                }
                for (int pier = -1; pier <= 1; pier += 2)
                    Part(world, streets, "Footbridge pier", new Vector3(end * 85, 2.6f, pier * 7.4f), new Vector3(.5f, 5.2f, .6f), "98A3A6");
                Part(world, streets, "Footbridge deck", new Vector3(end * 85, 5.05f, 0), new Vector3(1.2f, .9f, 16.5f), "A3ACA9");
                if (scenario.turningTraffic)
                {
                    float jx = -9 * scenario.DirectionForLane(0);
                    Part(world, streets, "Side street end block", new Vector3(jx, 5.5f, end * 47), new Vector3(12, 11, 8), "9AA4A7");
                }
            }
        }
    }
}
