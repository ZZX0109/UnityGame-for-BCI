using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Original street furniture, pavement markings and corner detail. Visuals contain no colliders.</summary>
    public static class StreetFurnitureBuilder
    {
        private static Renderer Part(StreetWorld world, Transform parent, string name, Vector3 position, Vector3 size, string colour, float radius = .02f, float smoothness = .18f)
            => world.VisualRounded(parent, name, position, size, colour, radius, smoothness);
        private static float Z(int side, float referenceDistance) => StreetLayout.SidewalkZ(side * referenceDistance);
        public static void Build(Transform parent, StreetWorld world, StreetScenarioDefinition scenario)
        {
            StreetVisualProfile profile = scenario.visualProfile;
            Transform furniture = new GameObject("Continuous sidewalk furnishings").transform; furniture.SetParent(parent, false);
            for (int side = -1; side <= 1; side += 2)
            {
                float z = Z(side, 6.7f);
                // Directional tactile paving joins the existing near-kerb warning surface without narrowing it.
                for (int segment = -3; segment <= 3; segment++)
                {
                    float x = segment * 10;
                    if (scenario.turningTraffic && segment == -1)
                    {
                        // S03 runtime baseline measured a 10.30 m rendered guide gap across the flush side-street
                        // mouth (west end -15.15, east start -4.85), with the mouth's warning strip 0.8 m off the
                        // guide line and tactily unconnected. Bridge the walking line in spans that follow the
                        // measured surface steps (-12.3/-5.7, raised over the side-street asphalt like the warning
                        // strip) and tie the warning strip into the guided route at the mouth centre.
                        float junctionX = -9 * scenario.DirectionForLane(0);
                        float raised = .04f;
                        Part(world, furniture, "Directional tactile paving", new Vector3(-13.725f, .013f, z), new Vector3(2.85f, .012f, .34f), "B9A16B", .008f);
                        Part(world, furniture, "Directional tactile paving", new Vector3(junctionX, raised, z), new Vector3(6.6f, .015f, .34f), "B9A16B", .008f);
                        Part(world, furniture, "Directional tactile paving", new Vector3(-5.275f, .013f, z), new Vector3(.85f, .012f, .34f), "B9A16B", .008f);
                        for (int ridge = -1; ridge <= 1; ridge++)
                        {
                            Part(world, furniture, "Tactile guide ridge", new Vector3(-13.725f, .023f, z + ridge * .09f), new Vector3(2.75f, .006f, .025f), "C5AE7B", .004f);
                            Part(world, furniture, "Tactile guide ridge", new Vector3(junctionX, raised + .01f, z + ridge * .09f), new Vector3(6.5f, .006f, .025f), "C5AE7B", .004f);
                            Part(world, furniture, "Tactile guide ridge", new Vector3(-5.275f, .023f, z + ridge * .09f), new Vector3(.75f, .006f, .025f), "C5AE7B", .004f);
                        }
                        Part(world, furniture, "Corner warning connection", new Vector3(junctionX, raised, z + side * .36f), new Vector3(.34f, .015f, .55f), "B9A16B", .008f);
                        Part(world, furniture, "Corner warning connection ridge", new Vector3(junctionX, raised + .01f, z + side * .36f), new Vector3(.025f, .006f, .45f), "C5AE7B", .004f);
                        continue;
                    }
                    Part(world, furniture, "Directional tactile paving", new Vector3(x, .013f, z), new Vector3(9.7f, .012f, .34f), "B9A16B", .008f);
                    for (int ridge = -1; ridge <= 1; ridge++)
                        Part(world, furniture, "Tactile guide ridge", new Vector3(x, .023f, z + ridge * .09f), new Vector3(9.6f, .006f, .025f), "C5AE7B", .004f);
                }
                Part(world, furniture, "Tactile crossing connection", new Vector3(0, .014f, Z(side, 5.3f)), new Vector3(.34f, .014f, 2.4f), "B9A16B", .008f);
                for (int ridge = -1; ridge <= 1; ridge++)
                    Part(world, furniture, "Crossing connection ridge", new Vector3(ridge * .09f, .024f, Z(side, 5.3f)), new Vector3(.025f, .006f, 2.35f), "C5AE7B", .004f);
                for (int bay = -2; bay <= 2; bay++)
                {
                    if (bay == 0 || (scenario.turningTraffic && bay == -1)) continue;
                    float x = bay * 13;
                    Transform grate = new GameObject("Raised tree grating").transform; grate.SetParent(furniture, false); grate.localPosition = new Vector3(x, 0, Z(side, 5.5f));
                    for (int edge = -1; edge <= 1; edge += 2)
                    {
                        // The original planter is 14 cm high. Keep every grate face above it, not buried at pavement height.
                        Part(world, grate, "Tree grate side", new Vector3(edge * .60f, .153f, 0), new Vector3(.35f, .025f, 1.4f), profile.metal, .02f, .50f);
                        Part(world, grate, "Tree grate end", new Vector3(0, .153f, edge * .6f), new Vector3(.9f, .025f, .25f), profile.metal, .02f, .50f);
                        Part(world, furniture, "Tree pit paving edge", new Vector3(x + edge * .775f, .07f, Z(side, 5.5f)), new Vector3(.15f, .14f, 1.7f), "A19E8E");
                        Part(world, furniture, "Tree pit paving edge", new Vector3(x, .07f, Z(side, 5.5f) + edge * .775f), new Vector3(1.4f, .14f, .15f), "A19E8E");
                    }
                    world.CombineVisuals(grate); world.AddVisualLod(grate, .008f);
                    // Young trees keep twin raking stakes and a tie strap; mulch covers the pit soil under the grate.
                    for (int stake = -1; stake <= 1; stake += 2)
                    {
                        Renderer treeStake = Part(world, furniture, "Tree support stake", new Vector3(x + stake * .48f, .62f, Z(side, 5.5f) + stake * .15f), new Vector3(.06f, 1.30f, .06f), "7A6647");
                        treeStake.transform.localRotation = Quaternion.Euler(0, 0, stake * -20f);
                    }
                    Part(world, furniture, "Tree tie strap", new Vector3(x, 1.10f, Z(side, 5.5f)), new Vector3(.34f, .07f, .34f), "5A4A3A");
                    Part(world, furniture, "Tree pit mulch", new Vector3(x, .145f, Z(side, 5.5f)), new Vector3(1.10f, .02f, 1.10f), "5C4A38");
                }
                float rackX = side > 0 ? 19 : 17;
                for (int slot = 0; slot < 3; slot++)
                {
                    float x = rackX + slot * .8f;
                    Part(world, furniture, "Cycle stand upright", new Vector3(x, .42f, Z(side, 8.1f)), new Vector3(.065f, .84f, .065f), profile.metal, .02f, .50f);
                    Part(world, furniture, "Cycle stand crossbar", new Vector3(x, .84f, Z(side, 8.4f)), new Vector3(.065f, .065f, .66f), profile.metal, .02f, .50f);
                    Part(world, furniture, "Cycle stand return", new Vector3(x, .42f, Z(side, 8.7f)), new Vector3(.065f, .84f, .065f), profile.metal, .02f, .50f);
                }
                // A bicycle stands locked to the middle stand of each rack, inside the retained cycle-stand
                // bounds. Zero roll: any lean pivots one wheel off the paving and lifts the bar over the bounds.
                Transform bike = new GameObject("Parked bicycle").transform;
                bike.SetParent(furniture, false);
                bike.localPosition = new Vector3(rackX + .8f, 0, Z(side, 8.4f));
                bike.localRotation = Quaternion.Euler(0, side > 0 ? 8f : -8f, 0f);
                for (int wheel = -1; wheel <= 1; wheel += 2)
                {
                    world.VisualMesh(bike, "Bicycle wheel", new Vector3(wheel * .52f, .31f, 0),
                        "BicycleWheel:0.29:0.022:14:6", BicycleWheelMesh, "1A1C1A", .16f);
                    for (int spoke = 0; spoke < 3; spoke++)
                    {
                        Renderer spokes = Part(world, bike, "Wheel spokes", new Vector3(wheel * .52f, .31f, 0), new Vector3(.54f, .03f, .018f), "8A8F8A", .008f, .5f);
                        spokes.transform.localRotation = Quaternion.Euler(0, 0, spoke * 60);
                    }
                }
                Part(world, bike, "Frame down tube", new Vector3(-.12f, .48f, 0), new Vector3(.045f, .045f, .78f), "2E4A5E", .02f, .5f)
                    .transform.localRotation = Quaternion.Euler(0, 0, -24);
                Part(world, bike, "Frame top tube", new Vector3(-.05f, .62f, 0), new Vector3(.70f, .045f, .045f), "2E4A5E", .02f, .5f);
                Part(world, bike, "Frame seat stay", new Vector3(-.33f, .55f, 0), new Vector3(.045f, .50f, .045f), "2E4A5E", .02f, .5f)
                    .transform.localRotation = Quaternion.Euler(0, 0, 28);
                Part(world, bike, "Seat post", new Vector3(-.16f, .76f, 0), new Vector3(.04f, .22f, .04f), "2E4A5E", .02f, .5f);
                Part(world, bike, "Front fork", new Vector3(.47f, .55f, 0), new Vector3(.04f, .55f, .04f), "2E4A5E", .02f, .5f)
                    .transform.localRotation = Quaternion.Euler(0, 0, -18);
                Part(world, bike, "Handlebar stem", new Vector3(.52f, .80f, 0), new Vector3(.04f, .18f, .04f), "2E4A5E", .02f, .5f);
                Part(world, bike, "Handlebar", new Vector3(.52f, .86f, 0), new Vector3(.04f, .04f, .42f), "2E4A5E", .02f, .5f);
                Part(world, bike, "Saddle", new Vector3(-.16f, .84f, 0), new Vector3(.26f, .06f, .11f), "1F1B18");
                for (int pedal = -1; pedal <= 1; pedal += 2)
                    Part(world, bike, "Pedal", new Vector3(.02f, .30f, pedal * .13f), new Vector3(.10f, .03f, .14f), "1A1A1A", .012f);
                world.AddVisualLod(bike, .012f);
                // Prefer an imported CC0 glTF model; scale to match the procedural envelope (~1.04 m tall × 0.62 m wide).
                // The imported model replaces only the bin body, lid, and opening; ribs, base flange, and ashtray are kept.
                Transform importedBin = world.Modules.SpawnImported(furniture, "metal_trash_can",
                    new Vector3(25, 0f, Z(side, 7.8f)), 1.04f);
                if (importedBin != null)
                {
                    world.AddVisualLod(importedBin, .012f);
                }
                else
                {
                    Part(world, furniture, "Litter bin", new Vector3(25, .48f, Z(side, 7.8f)), new Vector3(.55f, .96f, .55f), profile.metal, .10f, .35f);
                    Part(world, furniture, "Litter bin lid", new Vector3(25, .99f, Z(side, 7.8f)), new Vector3(.62f, .10f, .62f), profile.secondary, .08f, .30f);
                    Part(world, furniture, "Litter opening", new Vector3(25, .80f, Z(side, 7.8f) - .29f), new Vector3(.34f, .14f, .015f), "263A35");
                }
                // Pressed ribs, a base flange and a top ashtray detail the retained bin inside its bounds.
                for (int rib = -1; rib <= 1; rib += 2)
                {
                    Part(world, furniture, "Bin vertical rib", new Vector3(25 + rib * .285f, .48f, Z(side, 7.8f)), new Vector3(.02f, .80f, .03f), profile.metal, .02f, .5f);
                    Part(world, furniture, "Bin vertical rib", new Vector3(25, .48f, Z(side, 7.8f) + rib * .285f), new Vector3(.03f, .80f, .02f), profile.metal, .02f, .5f);
                }
                Part(world, furniture, "Bin base flange", new Vector3(25, .025f, Z(side, 7.8f)), new Vector3(.62f, .05f, .62f), profile.metal, .02f, .5f);
                Part(world, furniture, "Bin ashtray insert", new Vector3(25, 1.05f, Z(side, 7.8f)), new Vector3(.18f, .02f, .18f), "8A8D84", .02f, .4f);
                if (side < 0)
                {
                    // Mark the service cover on the furniture strip, clear of the tactile route.
                    var cone = Object.Instantiate(Resources.Load<GameObject>("StreetCrossing/Modules/StreetMaintenanceCone"), parent);
                    cone.name = "Utility maintenance cone";
                    cone.transform.localPosition = new Vector3(11.48f, .035f, Z(side, 6.05f));
                    foreach (Renderer renderer in cone.GetComponentsInChildren<Renderer>())
                        renderer.sharedMaterial = Resources.Load<Material>("StreetCrossing/Modules/StreetConeReflector");
                    world.AddVisualLod(cone.transform, .012f);
                }
                Part(world, furniture, "Utility service cover", new Vector3(11, .013f, Z(side, 6.1f)), new Vector3(.65f, .018f, .9f), "8E9185", .02f, .45f);
                for (int slot = 0; slot < 5; slot++)
                    Part(world, furniture, "Utility cover grip", new Vector3(10.78f + slot * .11f, .026f, Z(side, 6.1f)), new Vector3(.025f, .006f, .65f), "686F67", .003f, .45f);
                for (int slot = -2; slot <= 2; slot++)
                {
                    float x = slot * profile.furnishingSpacing;
                    if (slot != 0 && Mathf.Abs(x) < 37 && (!scenario.turningTraffic || Mathf.Abs(x + 9 * scenario.DirectionForLane(0)) > 6))
                    {
                        Part(world, furniture, "Kerb bollard", new Vector3(x, .46f, Z(side, 3.85f)), new Vector3(.13f, .92f, .13f), profile.metal, .06f, .50f);
                        Part(world, furniture, "Bollard reflector", new Vector3(x, .79f, Z(side, 3.85f)), new Vector3(.14f, .06f, .14f), "D5C7A7", .06f, .65f);
                    }
                }
                for (int end = -1; end <= 1; end += 2)
                {
                    // Replace the old invisible air wall with a visible street-maintenance gate that has real colliders.
                    float x = end * 39;
                    Transform gate = new GameObject("Street end maintenance gate").transform; gate.SetParent(furniture, false);
                    for (int post = -1; post <= 1; post++)
                    {
                        Renderer r = Part(world, gate, "Maintenance gate upright", new Vector3(x, .65f, Z(side, 6.3f) + post * 2.85f), new Vector3(.13f, 1.3f, .13f), profile.metal, .02f, .50f);
                        BoxCollider uc = r.gameObject.AddComponent<BoxCollider>();
                        uc.size = new Vector3(.13f, 1.3f, .13f);
                    }
                    for (int rail = 0; rail < 2; rail++)
                    {
                        Renderer r = Part(world, gate, "Maintenance gate rail", new Vector3(x, .55f + rail * .60f, Z(side, 6.3f)), new Vector3(.12f, .08f, 5.8f), profile.metal, .02f, .50f);
                        BoxCollider rc = r.gameObject.AddComponent<BoxCollider>();
                        rc.size = new Vector3(.12f, .08f, 5.8f);
                    }
                    for (int stripe = -3; stripe <= 3; stripe++)
                        Part(world, gate, "Maintenance reflective board", new Vector3(x, .86f, Z(side, 6.3f) + stripe * .72f), new Vector3(.16f, .27f, .70f), (stripe & 1) == 0 ? "D7A15B" : "E8DFBE", .02f, .65f);
                    // One continuous collider matches the full gate envelope for reliable player contact.
                    BoxCollider envelope = gate.gameObject.AddComponent<BoxCollider>();
                    envelope.center = new Vector3(x, .65f, Z(side, 6.3f));
                    envelope.size = new Vector3(.30f, 1.35f, 6.0f);
                }
            }
            // One cast-iron hydrant per kerb, outside the walking band and clear of the S03 mouth.
            for (int hydrantSide = -1; hydrantSide <= 1; hydrantSide += 2)
            {
                Transform hydrant = new GameObject("Kerbside fire hydrant").transform;
                hydrant.SetParent(furniture, false);
                hydrant.localPosition = new Vector3(hydrantSide > 0 ? -16.5f : 14.5f, 0, Z(hydrantSide, 3.6f));
                // Prefer an imported CC0 glTF model when available; scale to match the procedural envelope (~0.62 m tall).
                Transform importedHydrant = world.Modules.SpawnImported(hydrant, "fire_hydrant", Vector3.zero, .62f);
                if (importedHydrant != null)
                {
                    world.AddVisualLod(importedHydrant, .012f);
                    continue;
                }
                world.VisualMesh(hydrant, "Hydrant body", Vector3.zero, "HydrantBody:0.15:0.54:14", HydrantBodyMesh, "A83A2E", .35f);
                for (int nozzle = -1; nozzle <= 1; nozzle += 2)
                    Part(world, hydrant, "Hydrant nozzle", new Vector3(nozzle * .16f, .34f, 0), new Vector3(.07f, .07f, .09f), "C7BFA9", .02f, .5f);
                Part(world, hydrant, "Hydrant top nut", new Vector3(0, .56f, 0), new Vector3(.06f, .06f, .06f), "C7BFA9", .02f, .5f);
                Part(world, hydrant, "Hydrant base flange", new Vector3(0, .02f, 0), new Vector3(.26f, .04f, .26f), "8E3529");
            }
            // Street-level utility cabinets stand against the facade line at |z| ~ 11.5 m, the same
            // depth the bins and cycle racks use. They must stay clear of the z ~ +-10 m walking
            // corridor (StreetMissionGeometry.CorridorZ) and of every doorstep approach at |z| = 11.9.
            for (int boxSide = -1; boxSide <= 1; boxSide += 2)
            {
                Vector3 boxPos = new Vector3(boxSide * 4f, 0, Z(boxSide, 8.2f));
                Transform box01 = world.Modules.SpawnImported(furniture, "utility_box_01", boxPos, 1.1f);
                // Imported props carry their own LODGroup: CombineVisuals(furniture) skips renderers
                // held by a LODGroup, which keeps FBX sub-meshes out of the merged furniture batch.
                if (box01 != null) world.AddVisualLod(box01, .012f);
                else
                {
                    Part(world, furniture, "Utility box", boxPos + Vector3.up * .25f, new Vector3(.45f, .50f, .30f), "5E6B5A", .04f, .22f);
                    Part(world, furniture, "Utility box door", boxPos + new Vector3(0, .35f, .16f), new Vector3(.32f, .25f, .015f), "4A5447", .015f, .30f);
                    Part(world, furniture, "Utility box lock", boxPos + new Vector3(.10f, .35f, .155f), new Vector3(.02f, .04f, .02f), "C1BAA7", .005f, .45f);
                }
                Vector3 box2Pos = new Vector3(boxSide * 22.5f, 0, Z(boxSide, 8.2f));
                Transform box02 = world.Modules.SpawnImported(furniture, "utility_box_02", box2Pos, .85f);
                if (box02 != null) world.AddVisualLod(box02, .012f);
                else Part(world, furniture, "Secondary utility box", box2Pos + Vector3.up * .20f, new Vector3(.32f, .40f, .22f), "616F5E", .03f, .22f);
            }
            // A discarded tyre and a security light beside each maintenance gate, on both footways.
            for (int side = -1; side <= 1; side += 2)
            for (int end = -1; end <= 1; end += 2)
            {
                // Both props sit just clear of the 0.30 m wide gate envelope collider, which starts
                // at |x| = 38.85, so nothing overlaps an invisible blocker.
                float x = end * 38.48f;
                Transform tyre = world.Modules.SpawnImported(furniture, "old_tyre", new Vector3(x, .03f, Z(side, 7.8f)), .58f);
                if (tyre != null) world.AddVisualLod(tyre, .012f);
                else Part(world, furniture, "Old tyre", new Vector3(x, .30f, Z(side, 7.8f)), new Vector3(.56f, .58f, .56f), "282A28", .08f, .15f);
                // Security light bracketed onto the gate's upper rail; the rail face is at
                // |x| = 38.94 and its underside at y = 1.11, so the body touches both.
                Transform lightParent = new GameObject("Gate security light").transform;
                lightParent.SetParent(furniture, false);
                lightParent.localPosition = new Vector3(end * 38.88f, .95f, Z(side, 6.3f) + end * 2.1f);
                Transform securityLight = world.Modules.SpawnImported(lightParent, "security_light", Vector3.zero, .22f);
                if (securityLight != null) world.AddVisualLod(securityLight, .012f);
                else
                {
                    Part(world, lightParent, "Security light body", new Vector3(0, .08f, .04f), new Vector3(.12f, .16f, .14f), "3B4145", .03f, .18f);
                    Part(world, lightParent, "Security light lens", new Vector3(0, .08f, -.04f), new Vector3(.08f, .10f, .02f), "D8D4C4", .005f, .50f);
                }
            }
            if (scenario.turningTraffic)
            {
                float junctionX = -9 * scenario.DirectionForLane(0);
                for (int side = -1; side <= 1; side += 2)
                {
                    // The extra two corners stand behind the old sidewalk, outside the swept turn approaches.
                    StreetDistrictBuilder.Building(furniture, world, profile, new Vector3(junctionX - 8.4f * scenario.DirectionForLane(0), 0, Z(side, 12)), side,
                        side > 0 ? 10.2f : 8.8f, side > 0 ? StreetFacadeKind.Pharmacy : StreetFacadeKind.Cafe, false, true, 6.6f);
                    Part(world, furniture, "Side street tactile warning", new Vector3(junctionX, .04f, Z(side, 7.5f)), new Vector3(5.8f, .015f, .50f), "C1A56C");
                }
            }
            else
                for (int side = -1; side <= 1; side += 2)
                    StreetDistrictBuilder.Building(furniture, world, profile, new Vector3(36, 0, Z(side, 11.3f)), side, 9.8f,
                        side > 0 ? StreetFacadeKind.Bookshop : StreetFacadeKind.Apartment, false, true, 7.1f);
            world.CombineVisuals(furniture);
        }
        // Bicycle tyre: a slim torus in the XY plane so the tread faces the kerb (+-z).
        private static Mesh BicycleWheelMesh()
        {
            const int pathSegments = 14, tubeSegments = 6;
            const float mainRadius = .29f, tubeRadius = .022f;
            var vertices = new Vector3[pathSegments * tubeSegments];
            var uv = new Vector2[vertices.Length];
            for (int path = 0; path < pathSegments; path++)
            {
                float pathAngle = 2f * Mathf.PI * path / pathSegments;
                Vector3 radial = new Vector3(Mathf.Cos(pathAngle), Mathf.Sin(pathAngle), 0);
                Vector3 centre = radial * mainRadius;
                for (int tube = 0; tube < tubeSegments; tube++)
                {
                    float tubeAngle = 2f * Mathf.PI * tube / tubeSegments;
                    int index = path * tubeSegments + tube;
                    vertices[index] = centre + (radial * Mathf.Cos(tubeAngle) + Vector3.forward * Mathf.Sin(tubeAngle)) * tubeRadius;
                    uv[index] = new Vector2(path / (float)pathSegments, tube / (float)tubeSegments);
                }
            }
            var triangles = new int[pathSegments * tubeSegments * 6];
            int cursor = 0;
            for (int path = 0; path < pathSegments; path++)
            {
                int nextPath = (path + 1) % pathSegments;
                for (int tube = 0; tube < tubeSegments; tube++)
                {
                    int nextTube = (tube + 1) % tubeSegments;
                    int a = path * tubeSegments + tube;
                    int b = path * tubeSegments + nextTube;
                    int c = nextPath * tubeSegments + tube;
                    int d = nextPath * tubeSegments + nextTube;
                    triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = b;
                    triangles[cursor++] = b; triangles[cursor++] = c; triangles[cursor++] = d;
                }
            }
            var mesh = new Mesh { name = "Bicycle wheel torus", vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        // Turned hydrant barrel: flanged foot, full barrel, shoulder and domed cap.
        private static Mesh HydrantBodyMesh()
        {
            Vector2[] profile =
            {
                new Vector2(0f, 0f), new Vector2(.095f, 0f), new Vector2(.13f, .03f), new Vector2(.15f, .08f),
                new Vector2(.15f, .34f), new Vector2(.13f, .42f), new Vector2(.09f, .48f),
                new Vector2(.065f, .50f), new Vector2(0f, .54f)
            };
            const int segments = 14;
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
            var mesh = new Mesh { name = "Kerbside hydrant body", vertices = vertices, triangles = triangles, uv = uv };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
