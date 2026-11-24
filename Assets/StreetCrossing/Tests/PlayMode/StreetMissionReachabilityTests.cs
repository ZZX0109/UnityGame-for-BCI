using System.Collections;
using System.Linq;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class StreetMissionReachabilityTests
    {
        [UnityTest]
        public IEnumerator EveryMissionRouteHasCapsuleClearanceRealEntrancesAndReusesTheBuiltStreet()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            Assert.NotNull(game);
            Font font = game.uiFont;
            var scenarios = game.Scenarios.ToArray();
            game.gameObject.SetActive(false);
            StreetWorld world = null; GameObject environment = null;
            try
            {
                foreach (StreetScenarioDefinition scenario in scenarios)
                {
                    environment = new GameObject("Isolated mission route geometry");
                    world = new StreetWorld(environment.transform, scenario.surfaceMaterial, font); world.Build(scenario);
                    yield return null; yield return null; Physics.SyncTransforms();
                    var originalObjects = environment.GetComponentsInChildren<Transform>(true);
                    foreach (StreetMissionDefinition mission in StreetMissionCatalog.ForDistrict(scenario.scenarioId))
                    for (int leg = 0; leg < (mission.roundTrip ? 2 : 1); leg++)
                    {
                        world.ApplyMission(mission, leg); Physics.SyncTransforms();
                        CollectionAssert.AreEquivalent(originalObjects, environment.GetComponentsInChildren<Transform>(true), "Applying or reversing a mission cannot rebuild the street.");
                        Assert.AreEqual(mission.reverse ^ (leg > 0) ? -1 : 1, world.MissionForwardSign);
                        Assert.AreEqual(world.MissionForwardSign > 0 ? 0 : 180, world.MissionHeading);
                        int sign = world.MissionForwardSign;
                        var near = new Vector3(0, 0, -sign * (scenario.roadHalfWidth + .45f));
                        var far = new Vector3(0, 0, sign * (scenario.roadHalfWidth + .35f));
                        Assert.IsTrue(world.Zones.Single(z => z.region == CrossingRegion.Kerb).Contains(near));
                        Assert.IsTrue(world.Zones.Single(z => z.region == CrossingRegion.Exit).Contains(far));
                        Assert.IsTrue(world.Zones.Single(z => z.region == CrossingRegion.Destination).Contains(world.MissionDestination));
                        Assert.AreEqual(0, world.Zones.Single(z => z.region == CrossingRegion.CrossingRoute).transform.position.x);
                        CheckClear(world.MissionSpawn, StreetMissionGeometry.Approach(world.MissionSpawn, sign, scenario.roadHalfWidth));
                        Vector3[] arrival = StreetMissionGeometry.Arrival(world.MissionDestination, sign, scenario.roadHalfWidth);
                        CheckClear(arrival[0], arrival);
                        string doorway = "Doorway " + StreetMissionGeometry.GoalKind(mission, leg);
                        Assert.IsTrue(environment.GetComponentsInChildren<Transform>().Any(t => t.name == doorway && PlanarDistance(t.position, world.MissionDestination) < 1.2f), "The target must have the actual matching entrance, not only a changed HUD caption.");
                        // Follow every navigation waypoint through its public interface; no diagonal shortcut to a remote shop.
                        Vector3 cursor = world.MissionSpawn;
                        Vector3 target = world.NavigationTarget(cursor, false);
                        Assert.Greater(Mathf.Abs(target.z), scenario.roadHalfWidth);
                        foreach (Vector3 point in StreetMissionGeometry.Approach(cursor, sign, scenario.roadHalfWidth)) world.NavigationTarget(point, false);
                        Assert.Less(PlanarDistance(world.NavigationTarget(near, false), arrival[0]), .01f);
                        foreach (Vector3 point in arrival)
                        {
                            target = world.NavigationTarget(point, true);
                            Assert.IsTrue(Mathf.Abs(target.x - point.x) < .3f || Mathf.Abs(target.z - point.z) < .3f);
                        }
                        Assert.Less(PlanarDistance(world.NavigationTarget(world.MissionDestination, true), world.MissionDestination), .01f);
                    }
                    world.Dispose(); world = null; Object.Destroy(environment); environment = null;
                    yield return null; yield return null;
                }
            }
            finally
            {
                world?.Dispose(); if (environment) Object.Destroy(environment);
                if (game) game.gameObject.SetActive(true);
            }
        }
        private static float PlanarDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private static void CheckClear(Vector3 start, Vector3[] points)
        {
            foreach (Vector3 end in points)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(PlanarDistance(start, end) / .15f));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(start, end, i / (float)steps); p.y = 0;
                    Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * .35f, p + Vector3.up * 1.45f, .32f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider hit in hits) Assert.Fail("Required sidewalk route intersects " + hit.name + " at " + p);
                }
                start = end;
            }
        }
    }
}
