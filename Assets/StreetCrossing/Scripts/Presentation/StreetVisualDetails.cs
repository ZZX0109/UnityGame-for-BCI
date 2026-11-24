using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>
    /// Original street furniture and facade detailing. Every generated piece is visual-only.
    /// Existing world colliders retain sole authority over walking and road-observation occlusion.
    /// </summary>
    public static class StreetVisualDetails
    {
        public static void Build(Transform root, StreetWorld world, StreetScenarioDefinition scenario)
        {
            StreetVisualProfile p = scenario.visualProfile;
            ShopWindows(root, world, p);
            Shelter(root, world, p, new Vector3(7, 0, scenario.roadHalfWidth + 2));
            Bench(root, world, p, new Vector3(6.8f, 0, StreetLayout.SidewalkZ(-7.5f)));
            KerbDetails(root, world, scenario.roadHalfWidth);
            if (p.identity == StreetVisualIdentity.Residential) GardenEntrance(root, world, p);
            else if (p.identity == StreetVisualIdentity.Commercial) MarketArcade(root, world, p);
            else CornerLandmark(root, world, p);
        }

        private static Transform Group(Transform parent, string name, Vector3 p, float yaw = 0)
        {
            Transform t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = p; t.localRotation = Quaternion.Euler(0, yaw, 0); return t;
        }
        private static Renderer Part(Transform parent, StreetWorld w, string name, Vector3 p, Vector3 size, string color, float radius = .04f, float smoothness = .18f)
            => w.VisualRounded(parent, name, p, size, color, radius, smoothness);

        private static void ShopWindows(Transform parent, StreetWorld w, StreetVisualProfile p)
        {
            Transform shop = Group(parent, "Recessed market windows", Vector3.forward * StreetLayout.SidewalkOffset);
            BuildMarketUpperStorey(shop, w, p);
            for (int bay = -1; bay <= 1; bay++)
            {
                float x = bay * 3;
                Part(shop, w, "Recessed interior backdrop", new Vector3(x, 1.45f, 9.77f), new Vector3(2.58f, 2.2f, .025f), "354A47");
                // Transparent shop glazing sits between the recessed backdrop and the front jamb.
                Renderer glass = Part(shop, w, "Shop glazing", new Vector3(x, 1.45f, 9.60f), new Vector3(2.5f, 2.05f, .015f), "A7C1B8", .01f);
                glass.sharedMaterial = w.GlassMaterial;
                for (int edge = -1; edge <= 1; edge += 2)
                {
                    Part(shop, w, "Window jamb", new Vector3(x + edge * 1.34f, 1.45f, 9.58f), new Vector3(.10f, 2.45f, .12f), p.metal);
                    Part(shop, w, "Window lintel and sill", new Vector3(x, 1.45f + edge * 1.18f, 9.58f), new Vector3(2.78f, .10f, .12f), p.secondary);
                }
                if (bay == 0)
                {
                    Part(shop, w, "Door central mullion", new Vector3(x, 1.45f, 9.56f), new Vector3(.065f, 2.25f, .09f), p.secondary);
                    for (int side = -1; side <= 1; side += 2)
                        Part(shop, w, "Door pull", new Vector3(side * .14f, 1.25f, 9.47f), new Vector3(.035f, .38f, .045f), "DFD9C6", .015f);
                }
                else
                {
                    for (int shelf = 0; shelf < 3; shelf++)
                    {
                        float y = .65f + shelf * .53f;
                        Part(shop, w, "Interior timber shelf", new Vector3(x, y, 9.66f), new Vector3(2.34f, .055f, .12f), p.secondary);
                        for (int product = 0; product < 5; product++)
                            Part(shop, w, "Market display package", new Vector3(x - .88f + product * .44f, y + .18f, 9.64f), new Vector3(.25f, .28f + (product % 2) * .07f, .07f), product % 2 == 0 ? "C99A65" : "A5B59A", .025f);
                    }
                }
            }
            Part(shop, w, "Raised market fascia", new Vector3(0, 4.34f, 9.68f), new Vector3(10.2f, .12f, .10f), p.secondary);
            w.Label(p.shopCaption, new Vector3(0, 3.43f, 9.59f), .045f, "F3EBDD", shop, 0, 7, .16f);
        }

        private static void BuildMarketUpperStorey(Transform shop, StreetWorld world, StreetVisualProfile profile)
        {
            // The shop occupies a 10.5 m frontage. Set the upper floor back from its sign
            // and keep every projecting detail over the shop or above pedestrian headroom.
            Transform floor = Group(shop, "Upper floor over the market", new Vector3(0, 4.2f, 10.18f));
            bool residential = profile.identity == StreetVisualIdentity.Residential;
            bool corner = profile.identity == StreetVisualIdentity.Corner;
            float height = residential ? 3.05f : corner ? 3.65f : 2.85f;
            string wall = residential ? "B68770" : corner ? "BEAD96" : "BFC4B8";
            Renderer body = Part(floor, world, "Upper floor masonry", new Vector3(0, height * .5f, 1.9f),
                new Vector3(10.14f, height, 3.8f), wall);
            body.sharedMaterial = world.FacadeMaterial(wall);
            Part(floor, world, "Upper parapet coping", new Vector3(0, height + .08f, 1.9f),
                new Vector3(10.42f, .16f, 4.04f), "DCCDB4");
            int bays = residential || corner ? 3 : 4;
            float spacing = 9.6f / bays;
            for (int bay = 0; bay < bays; bay++)
            {
                float x = (bay - (bays - 1) * .5f) * spacing;
                float width = spacing - (residential ? .82f : .44f);
                float openingHeight = corner && bay == 0 ? 2.37f : 1.72f;
                Part(floor, world, "Stone window reveal", new Vector3(x, 1.49f, -.055f),
                    new Vector3(width, openingHeight, .13f), "D3C8B2");
                // Transparent glazing between the stone reveal and the dark sash.
                Renderer glass = Part(floor, world, "Upper storey glazing", new Vector3(x, 1.49f, -.085f),
                    new Vector3(width - .10f, openingHeight - .10f, .015f), "A7C1B8", .01f);
                glass.sharedMaterial = world.GlassMaterial;
                Part(floor, world, "Deep sash and transom", new Vector3(x, 1.49f, -.14f),
                    new Vector3(width - .18f, openingHeight - .18f, .07f), "496264");
                Part(floor, world, "Opening casement", new Vector3(x + width * .17f, 1.49f, -.20f),
                    new Vector3(.07f, openingHeight - .15f, .075f), profile.metal);
                Part(floor, world, "Window head rail", new Vector3(x, 1.93f, -.20f),
                    new Vector3(width - .16f, .05f, .07f), profile.metal);
                if (residential)
                {
                    Part(floor, world, "Balcony slab", new Vector3(x, .43f, -.34f), new Vector3(width + .36f, .15f, .83f), "C9BCA4");
                    Part(floor, world, "Balcony handrail", new Vector3(x, 1.42f, -.72f), new Vector3(width + .19f, .055f, .065f), profile.metal);
                    for (int bar = -3; bar <= 3; bar++)
                        Part(floor, world, "Balcony baluster", new Vector3(x + bar * width / 6, .96f, -.72f),
                            new Vector3(.032f, .92f, .035f), profile.metal, .008f);
                    for (int end = -1; end <= 1; end += 2)
                        Part(floor, world, "Balcony rail return", new Vector3(x + end * width * .5f, 1.42f, -.4f),
                            new Vector3(.055f, .055f, .66f), profile.metal);
                }
                else if (!corner)
                {
                    var hood = Part(floor, world, "Shop office solar shade", new Vector3(x, 2.45f, -.34f),
                        new Vector3(width + .12f, .09f, .83f), "A07555");
                    hood.transform.localRotation = Quaternion.Euler(-14, 0, 0);
                }
                else
                {
                    Part(floor, world, "Corner pilaster", new Vector3(x - spacing * .49f, height * .5f, -.095f),
                        new Vector3(.23f, height, .26f), "D7C8AF");
                    Part(floor, world, "Window hood molding", new Vector3(x, 1.56f + openingHeight * .5f, -.15f),
                        new Vector3(width + .21f, .16f, .39f), "D7C8AF");
                }
            }
            world.CombineVisuals(floor);
            world.AddVisualLod(floor, .009f);
        }

        private static void Shelter(Transform parent, StreetWorld w, StreetVisualProfile p, Vector3 position)
        {
            Transform shelter = Group(parent, "Detailed bus shelter", position);
            Part(shelter, w, "Shelter rounded fascia", new Vector3(0, 2.59f, -.88f), new Vector3(3.76f, .26f, .08f), p.metal, .10f);
            Part(shelter, w, "Shelter roof inset", new Vector3(0, 2.66f, 0), new Vector3(3.5f, .035f, 1.62f), p.secondary, .012f);
            for (int support = -1; support <= 1; support += 2)
            {
                // The legacy shelter's posts move here so the detailed shell is self-supporting;
                // they rise from the ground shoe to the roof inset underside.
                Part(shelter, w, "Shelter post", new Vector3(support * 1.6f, 1.33f, .65f), new Vector3(.09f, 2.66f, .09f), p.metal);
                Part(shelter, w, "Shelter post shoe", new Vector3(support * 1.6f, .11f, .65f), new Vector3(.19f, .22f, .19f), p.metal);
            }
            // Opaque framed panel deliberately agrees with the retained collision/visibility surface.
            Part(shelter, w, "Opaque information panel", new Vector3(.9f, 1.52f, .645f), new Vector3(1.10f, 1.48f, .035f), "E8E2CF");
            for (int line = 0; line < 6; line++)
                Part(shelter, w, "Timetable row", new Vector3(.9f, 1.98f - line * .16f, .62f), new Vector3(.80f - (line % 2) * .18f, .035f, .012f), p.accent, .01f);
            // Finished seat top is 0.450 m over the local paving. This shelter stands past the 1.55 m kerb
            // ease (HeightAtDepth is already 0 at its 2 m setback), so the datum is y=0 and the slat band
            // keeps the original 75 mm pitch centred on the seat depth.
            const float SeatTop = .450f, SlatThickness = .055f, SlatPitch = .075f;
            const float SlatUnder = SeatTop - SlatThickness; // .395 m: the underside every support must reach.
            const float SeatCentreZ = .25f, LegX = .95f;
            for (int plank = 0; plank < 5; plank++)
                Part(shelter, w, "Bus seat timber slat", new Vector3(0, SlatUnder + SlatThickness * .5f, SeatCentreZ - 2 * SlatPitch + plank * SlatPitch), new Vector3(2.7f, SlatThickness, .06f), p.secondary, .025f);
            // Two symmetric support groups carry the slats: stringer, twin legs and ground shoes each side,
            // tied by a low stretcher. Visual-only; the retained seat collider above owns any obstruction.
            foreach (float x in new[] { -LegX, LegX })
            {
                Part(shelter, w, "Bus seat support bracket", new Vector3(x, SlatUnder - .03f, SeatCentreZ), new Vector3(.06f, .06f, .36f), p.metal, .01f);
                foreach (float z in new[] { .16f, .34f })
                {
                    Part(shelter, w, "Bus seat support leg", new Vector3(x, SlatUnder * .5f, z), new Vector3(.05f, SlatUnder, .05f), p.metal, .008f);
                    Part(shelter, w, "Bus seat support shoe", new Vector3(x, .02f, z), new Vector3(.09f, .04f, .09f), p.metal, .008f);
                }
            }
            Part(shelter, w, "Bus seat support stretcher", new Vector3(0, .12f, SeatCentreZ), new Vector3(2 * LegX, .05f, .05f), p.metal, .008f);
            // Glazed side screens shelter the seat from crosswinds; the rear drip edge sheds roof water behind the panel.
            for (int windscreen = -1; windscreen <= 1; windscreen += 2)
            {
                Renderer sideGlass = Part(shelter, w, "Shelter side windscreen", new Vector3(windscreen * 1.61f, 1.45f, .35f), new Vector3(.04f, 1.50f, .75f), "A7C1B8", .01f);
                sideGlass.sharedMaterial = w.GlassMaterial;
            }
            Part(shelter, w, "Shelter rear drip edge", new Vector3(0, 2.63f, .83f), new Vector3(3.5f, .06f, .05f), p.metal, .04f, .5f);
            w.Label("BUS", new Vector3(-2.2f, 2.55f, -.37f), .032f, p.accent, shelter, 0, .72f, .17f);
        }

        private static void Bench(Transform parent, StreetWorld w, StreetVisualProfile p, Vector3 position)
        {
            Transform bench = Group(parent, "Slatted neighbourhood bench", position);
            for (int plank = 0; plank < 5; plank++)
            {
                Part(bench, w, "Seat slat", new Vector3(0, .48f, -.24f + plank * .12f), new Vector3(2.2f, .08f, .09f), p.secondary);
                Part(bench, w, "Backrest slat", new Vector3(0, .72f + plank * .10f, .28f), new Vector3(2.2f, .075f, .07f), p.secondary, .025f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Part(bench, w, "Bench cast leg", new Vector3(side * .82f, .25f, 0), new Vector3(.10f, .50f, .56f), p.metal);
                Part(bench, w, "Bench armrest", new Vector3(side * 1.05f, .72f, 0), new Vector3(.08f, .07f, .60f), p.metal);
            }
        }

        private static void KerbDetails(Transform parent, StreetWorld w, float roadHalfWidth)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                // Three grate sets per kerb; group y seats the rim underside on the -0.010 asphalt top.
                foreach (float drainX in new[] { 4.6f, -18f, 24f })
                {
                    Transform drain = Group(parent, "Flush kerb drain", new Vector3(drainX, -.001f, side * (roadHalfWidth - .18f)));
                    Part(drain, w, "Drain rim", Vector3.zero, new Vector3(.85f, .018f, .28f), "535B59", .008f);
                    for (int slat = 0; slat < 10; slat++)
                        Part(drain, w, "Drain opening", new Vector3(-.35f + slat * .077f, .004f, 0), new Vector3(.035f, .008f, .21f), "252F30", .005f);
                }
                for (int joint = -8; joint <= 8; joint++)
                    if (Mathf.Abs(joint) > 2)
                        Part(parent, w, "Kerb stone joint", new Vector3(joint * .9f, .105f, side * (roadHalfWidth + .08f)), new Vector3(.014f, .012f, .18f), "A6A18F", .004f);
            }
        }

        private static void DistrictFacades(Transform parent, StreetWorld w, StreetScenarioDefinition scenario)
        {
            StreetVisualProfile p = scenario.visualProfile;
            for (int side = -1; side <= 1; side += 2)
            for (int bay = -2; bay <= 2; bay++)
            {
                if ((side == 1 && bay == 0) || (scenario.turningTraffic && bay == -scenario.DirectionForLane(0))) continue;
                Transform facade = Group(parent, p.identity + " facade detail", new Vector3(bay * 13, 0, side * 9.24f), side > 0 ? 0 : 180);
                Part(facade, w, "Facade base plinth", new Vector3(0, .22f, 0), new Vector3(10.15f, .42f, .06f), p.accent);
                Part(facade, w, "District cornice", new Vector3(0, 3.10f, -.03f), new Vector3(10.3f, .17f, .20f), p.secondary);
                if (p.identity == StreetVisualIdentity.Residential)
                {
                    for (int pane = -2; pane <= 2; pane++)
                        Part(facade, w, "Residential window planter", new Vector3(pane * 1.8f, 3.75f, -.20f), new Vector3(1.0f, .23f, .38f), p.accent);
                }
                else
                {
                    for (int pane = -1; pane <= 1; pane++)
                    {
                        Part(facade, w, "Commercial recessed window", new Vector3(pane * 3.1f, 1.65f, -.04f), new Vector3(2.65f, 2.25f, .05f), "40575A");
                        Part(facade, w, "Commercial transom", new Vector3(pane * 3.1f, 2.65f, -.09f), new Vector3(2.72f, .08f, .12f), p.secondary);
                    }
                    Part(facade, w, "Shopfront canopy", new Vector3(0, 3.35f, -.55f), new Vector3(9.8f, .15f, 1.1f), p.accent);
                    w.Label(bay % 2 == 0 ? "COFFEE  /  DAILY GOODS" : "BOOKS  /  DESIGN", new Vector3(0, 3.08f, -.16f), .05f, "F2E6CE", facade, 0, 8, .35f);
                }
            }
        }

        private static void GardenEntrance(Transform parent, StreetWorld w, StreetVisualProfile p)
        {
            Transform entrance = Group(parent, "Garden walk pergola", new Vector3(7.5f, 0, StreetLayout.SidewalkZ(-8.7f)));
            for (int side = -1; side <= 1; side += 2)
                Part(entrance, w, "Pergola column", new Vector3(side * 1.8f, 1.65f, 0), new Vector3(.18f, 3.3f, .18f), p.accent);
            Part(entrance, w, "Pergola lintel", new Vector3(0, 3.3f, 0), new Vector3(4.1f, .25f, .25f), p.accent);
            for (int slat = -3; slat <= 3; slat++)
                Part(entrance, w, "Pergola roof beam", new Vector3(slat * .5f, 3.5f, 0), new Vector3(.12f, .12f, 1.2f), p.secondary);
            w.Label(p.districtCaption, new Vector3(0, 2.95f, -.16f), .055f, p.accent, entrance, 0, 3.3f, .34f);
        }

        private static void MarketArcade(Transform parent, StreetWorld w, StreetVisualProfile p)
        {
            Transform market = Group(parent, "Commercial street canopy row", new Vector3(13, 0, StreetLayout.SidewalkZ(8.6f)));
            // The reusable shop already supplies the 3.36m canopy. A second lower canopy and its old 3.45m text
            // intersected that structure; keep one canopy and mount the district sign clearly above it.
            for (int support = -1; support <= 1; support += 2)
                Part(market, w, "District sign bracket", new Vector3(support * 3.1f, 3.68f, -.12f), new Vector3(.065f, .65f, .065f), p.metal);
            Part(market, w, "Raised district sign backing", new Vector3(0, 4.0f, -.12f), new Vector3(7.9f, .55f, .075f), p.facade);
            w.Label(p.districtCaption, new Vector3(0, 4.0f, -.20f), .075f, p.accent, market, 0, 7.5f, .42f);
        }

        private static void CornerLandmark(Transform parent, StreetWorld w, StreetVisualProfile p)
        {
            Transform corner = Group(parent, "Corner square wayfinding", new Vector3(6, 0, StreetLayout.SidewalkZ(-8.9f)));
            Part(corner, w, "Wayfinding pylon", new Vector3(0, 1.6f, 0), new Vector3(1.45f, 3.2f, .28f), p.metal, .16f);
            Part(corner, w, "Wayfinding inset", new Vector3(0, 1.7f, -.16f), new Vector3(1.23f, 2.7f, .025f), p.facade);
            for (int line = 0; line < 4; line++)
            {
                Part(corner, w, "Wayfinding route band", new Vector3(0, 2.48f - line * .46f, -.185f), new Vector3(1.02f, .12f, .02f), line % 2 == 0 ? p.accent : p.secondary);
            }
            w.Label("CROSSING\nSQUARE", new Vector3(0, 1.3f, -.22f), .045f, p.metal, corner, 0, 1.04f, .72f);
            w.Label(p.districtCaption, new Vector3(0, 3.35f, -.17f), .04f, p.accent, corner, 0, 3.2f, .30f);
        }
    }
}
