using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class FourLaneTrafficTests
    {
        [UnityTest]
        public IEnumerator EveryStraightLaneHasPhysicalVehiclesAndResetReproducesItsSeededLayout()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene");
            var game = Object.FindObjectOfType<StreetGameManager>();
            game.AutoPauseOnFocusLoss = false;
            try
            {
                for (int scenario = 0; scenario < 3; scenario++)
                {
                    game.SelectScenario(scenario); game.StartPractice(); game.Pause();
                    var layout = new Dictionary<int, Vector2>();
                    int[] perLane = new int[game.scenario.StraightLaneCount];
                    foreach (VehicleController car in game.Traffic.Active)
                    {
                        layout.Add(car.EncounterId, new Vector2(car.Motion.Distance, car.Motion.Speed));
                        Assert.IsTrue(car.Body.enabled);
                        if (!car.Lane.IsTurning)
                        {
                            perLane[car.Lane.Index]++;
                            Assert.AreEqual(game.scenario.LaneZ(car.Lane.Index), car.transform.position.z, .001f);
                        }
                    }
                    foreach (int count in perLane) Assert.AreEqual(game.scenario.initialVehiclesPerStraightLane, count);
                    Assert.AreEqual(4 + (game.scenario.turningTraffic ? 2 : 0), game.Traffic.Routes.Count);
                    for (int repeat = 0; repeat < 5; repeat++)
                    {
                        game.StartPractice(); game.Pause();
                        Assert.AreEqual(layout.Count, game.Traffic.Active.Count);
                        foreach (VehicleController car in game.Traffic.Active)
                            Assert.AreEqual(layout[car.EncounterId], new Vector2(car.Motion.Distance, car.Motion.Speed));
                    }
                    // The former far curb is now within real traffic space.
                    CrossingRegion oldEdge = CrossingRegion.None;
                    foreach (CrossingZone zone in game.Zones)
                        if (zone.Contains(new Vector3(0, .02f, 3.5f))) oldEdge |= zone.region;
                    Assert.IsTrue((oldEdge & CrossingRegion.RoadwayHazard) != 0);
                    Assert.IsTrue((oldEdge & (CrossingRegion.Exit | CrossingRegion.Destination)) == 0);
                }
            }
            finally { game.Pause(); game.AutoPauseOnFocusLoss = true; }
        }

        [UnityTest]
        public IEnumerator AllFourLanesStopBeforeTheCrosswalkDuringPedestrianGreen()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene");
            var game = Object.FindObjectOfType<StreetGameManager>();
            game.AutoPauseOnFocusLoss = false;
            try
            {
                game.SelectScenario(1); game.StartPractice();
                float toWalk = 0;
                for (int phase = 0; phase < 3; phase++) toWalk += game.scenario.phaseTable.phases[phase].duration;
                game.Intersection.Tick(toWalk + .01f, false, false);
                Assert.AreEqual(TrafficPhase.Walk, game.Intersection.Phase);
                // Exercise the real traffic manager and native contact queries
                // with a held pedestrian phase, rather than moving only models.
                for (int tick = 0; tick < 1500; tick++) game.Traffic.Tick(.02f, tick * .02f, false);
                int[] stopped = new int[game.scenario.StraightLaneCount];
                foreach (VehicleController car in game.Traffic.Active)
                {
                    Assert.IsFalse(car.Lane.IsTurning);
                    Assert.Less(car.Motion.Front, car.Lane.StopDistance + .001f);
                    Assert.Less(car.Motion.Speed, .04f);
                    stopped[car.Lane.Index]++;
                }
                foreach (int count in stopped) Assert.Greater(count, 0);
                Assert.IsFalse(game.Traffic.HasUnclearedVehicles);
            }
            finally { game.Pause(); game.AutoPauseOnFocusLoss = true; }
        }
    }
}
