using StreetCrossing.Core;
using UnityEngine;

namespace StreetCrossing.Presentation
{
    /// <summary>Original street furniture, pavement markings and corner detail. Visuals contain no colliders.</summary>
    public static class StreetFurnitureBuilder
    {
        private static Renderer Part(StreetWorld world, Transform parent, string name, Vector3 position, Vector3 size, string colour, float radius = .02f)
            => world.VisualRounded(parent, name, position, size, colour, radius);
        public static void Build(Transform parent, StreetWorld world, StreetScenarioDefinition scenario)
        {
            StreetVisualProfile profile = scenario.visualProfile;
            Transform furniture = new GameObject("Continuous sidewalk furnishings").transform; furniture.SetParent(parent, false);
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * 6.7f;
                // Directional tactile paving joins the existing near-kerb warning surface without narrowing it.
                for (int segment = -3; segment <= 3; segment++)
                {
                    float x = segment * 10;
                    if (scenario.turningTraffic && segment == -1) continue;
                    Part(world, furniture, "Directional tactile paving", new Vector3(x, .013f, z), new Vector3(9.7f, .012f, .34f), "B9A16B", .008f);
                    for (int ridge = -1; ridge <= 1; ridge++)
                        Part(world, furniture, "Tactile guide ridge", new Vector3(x, .023f, z + ridge * .09f), new Vector3(9.6f, .006f, .025f), "C5AE7B", .004f);
                }
                Part(world, furniture, "Tactile crossing connection", new Vector3(0, .014f, side * 5.3f), new Vector3(.34f, .014f, 2.4f), "B9A16B", .008f);
                for (int ridge = -1; ridge <= 1; ridge++)
                    Part(world, furniture, "Crossing connection ridge", new Vector3(ridge * .09f, .024f, side * 5.3f), new Vector3(.025f, .006f, 2.35f), "C5AE7B", .004f);
                for (int bay = -2; bay <= 2; bay++)
                {
                    if (bay == 0 || (scenario.turningTraffic && bay == -1)) continue;
                    float x = bay * 13;
                    Part(world, furniture, "Tree pit paving edge", new Vector3(x, .016f, side * 5.5f), new Vector3(1.7f, .025f, 1.7f), "A19E8E");
                    for (int edge = -1; edge <= 1; edge += 2)
                    {
                        Part(world, furniture, "Tree grate side", new Vector3(x + edge * .60f, .037f, side * 5.5f), new Vector3(.35f, .025f, 1.4f), profile.metal);
                        Part(world, furniture, "Tree grate end", new Vector3(x, .037f, side * 5.5f + edge * .6f), new Vector3(.9f, .025f, .25f), profile.metal);
                    }
                }
                float rackX = side > 0 ? 19 : 17;
                for (int slot = 0; slot < 3; slot++)
                {
                    float x = rackX + slot * .8f;
                    Part(world, furniture, "Cycle stand upright", new Vector3(x, .42f, side * 8.1f), new Vector3(.065f, .84f, .065f), profile.metal);
                    Part(world, furniture, "Cycle stand crossbar", new Vector3(x, .84f, side * 8.4f), new Vector3(.065f, .065f, .66f), profile.metal);
                    Part(world, furniture, "Cycle stand return", new Vector3(x, .42f, side * 8.7f), new Vector3(.065f, .84f, .065f), profile.metal);
                }
                Part(world, furniture, "Litter bin", new Vector3(25, .48f, side * 7.8f), new Vector3(.55f, .96f, .55f), profile.metal, .10f);
                Part(world, furniture, "Litter bin lid", new Vector3(25, .99f, side * 7.8f), new Vector3(.62f, .10f, .62f), profile.secondary, .08f);
                Part(world, furniture, "Litter opening", new Vector3(25, .80f, side * 7.8f - .29f), new Vector3(.34f, .14f, .015f), "263A35");
                Part(world, furniture, "Utility service cover", new Vector3(11, .013f, side * 6.1f), new Vector3(.65f, .018f, .9f), "8E9185");
                for (int slot = 0; slot < 5; slot++)
                    Part(world, furniture, "Utility cover grip", new Vector3(10.78f + slot * .11f, .026f, side * 6.1f), new Vector3(.025f, .006f, .65f), "686F67", .003f);
                for (int slot = -2; slot <= 2; slot++)
                {
                    float x = slot * profile.furnishingSpacing;
                    if (slot != 0 && Mathf.Abs(x) < 37 && (!scenario.turningTraffic || Mathf.Abs(x + 9 * scenario.DirectionForLane(0)) > 6))
                    {
                        Part(world, furniture, "Kerb bollard", new Vector3(x, .46f, side * 3.85f), new Vector3(.13f, .92f, .13f), profile.metal, .06f);
                        Part(world, furniture, "Bollard reflector", new Vector3(x, .79f, side * 3.85f), new Vector3(.14f, .06f, .14f), "D5C7A7", .06f);
                    }
                }
                for (int end = -1; end <= 1; end += 2)
                {
                    // Retain the old walking boundary but explain it as a street-maintenance gate, not a solid wall at the horizon.
                    float x = end * 39;
                    for (int post = -1; post <= 1; post++)
                        Part(world, furniture, "Maintenance gate upright", new Vector3(x, .65f, side * 6.3f + post * 2.85f), new Vector3(.13f, 1.3f, .13f), profile.metal);
                    for (int rail = 0; rail < 2; rail++)
                        Part(world, furniture, "Maintenance gate rail", new Vector3(x, .55f + rail * .60f, side * 6.3f), new Vector3(.12f, .08f, 5.8f), profile.metal);
                    for (int stripe = -3; stripe <= 3; stripe++)
                        Part(world, furniture, "Maintenance reflective board", new Vector3(x, .86f, side * 6.3f + stripe * .72f), new Vector3(.16f, .27f, .70f), (stripe & 1) == 0 ? "D7A15B" : "E8DFBE");
                }
            }
            if (scenario.turningTraffic)
            {
                float junctionX = -9 * scenario.DirectionForLane(0);
                for (int side = -1; side <= 1; side += 2)
                {
                    // The extra two corners stand behind the old sidewalk, outside the swept turn approaches.
                    StreetDistrictBuilder.Building(furniture, world, profile, new Vector3(junctionX - 8.4f * scenario.DirectionForLane(0), 0, side * 12), side,
                        side > 0 ? 10.2f : 8.8f, side > 0 ? StreetFacadeKind.Pharmacy : StreetFacadeKind.Cafe, false, true, 6.6f);
                    Part(world, furniture, "Side street tactile warning", new Vector3(junctionX, .04f, side * 7.5f), new Vector3(5.8f, .015f, .50f), "C1A56C");
                }
            }
            else
                for (int side = -1; side <= 1; side += 2)
                    StreetDistrictBuilder.Building(furniture, world, profile, new Vector3(36, 0, side * 11.3f), side, 9.8f,
                        side > 0 ? StreetFacadeKind.Bookshop : StreetFacadeKind.Apartment, false, true, 7.1f);
            world.CombineVisuals(furniture);
        }
    }
}
