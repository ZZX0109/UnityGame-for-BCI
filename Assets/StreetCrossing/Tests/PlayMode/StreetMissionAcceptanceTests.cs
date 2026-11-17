using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Player;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>Real player inputs, native controller, visibility and live traffic for all authored configurations.</summary>
    public sealed class StreetMissionAcceptanceTests
    {
        private sealed class RouteInput : IStreetInput
        {
            public StreetGameManager game;
            public Vector2 motion;
            public float yaw, pitch;
            public Vector2 Movement => motion;
            public Vector2 Look => new Vector2(Mathf.DeltaAngle(game.Player.Heading, yaw), game.Player.Pitch - pitch) / StreetPreferences.Sensitivity;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }
        private StreetGameManager game;
        private RouteInput input;
        private float originalScale;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            originalScale = Time.timeScale; Time.timeScale = 3;
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene");
            game = Object.FindObjectOfType<StreetGameManager>(); game.AutoPauseOnFocusLoss = false; game.PersistMissionProgress = false;
            input = new RouteInput { game = game }; game.SetInputSource(input);
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            input.motion = Vector2.zero; game.ReturnToMenu(); Time.timeScale = originalScale;
            yield return null;
        }
        private IEnumerator Walk(Vector3 target, float radius = .10f)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (game.IsPlaying && Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target - game.PlayerPosition; delta.y = 0;
                if (delta.magnitude <= radius) break;
                input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg; input.pitch = 0;
                input.motion = Vector2.up * Mathf.Min(1, delta.magnitude / .25f);
                yield return null;
            }
            input.motion = Vector2.zero;
            Vector3 remaining = target - game.PlayerPosition; remaining.y = 0;
            Assert.That(remaining.magnitude, Is.LessThan(.24f), "Native route blocked at " + game.PlayerPosition + " heading for " + target + " in " + game.Mission.missionId);
        }
        private IEnumerator Observe(bool left)
        {
            Vector3[] targets = left ? game.LeftRoadTargets : game.RightRoadTargets;
            Vector3 delta = targets[0] - game.Player.View.transform.position;
            input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            input.pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
            float deadline = Time.realtimeSinceStartup + 6;
            while (game.IsPlaying && !(left ? game.Evaluation.LeftObserved : game.Evaluation.RightObserved) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(left ? game.Evaluation.LeftObserved : game.Evaluation.RightObserved, game.Mission.missionId + " could not actually observe " + (left ? "left" : "right") + " from " + game.PlayerPosition);
        }
        private IEnumerator CompleteLeg()
        {
            int sign = game.ForwardSign;
            // Stay on the starting sidewalk while approaching the fixed crosswalk.
            float corridor = sign > 0 ? -9.95f : 10.2f;
            yield return Walk(new Vector3(game.PlayerPosition.x, 0, corridor));
            yield return Walk(new Vector3(0, 0, corridor));
            yield return Walk(new Vector3(0, 0, -sign * (game.scenario.roadHalfWidth + .13f)));
            yield return Observe(true); yield return Observe(false);
            input.yaw = sign > 0 ? 0 : 180; input.pitch = 0;
            float deadline = Time.realtimeSinceStartup + 65;
            while (game.IsPlaying && (game.Intersection.Phase != TrafficPhase.Walk || game.Intersection.Remaining < game.scenario.SafeCrossingSeconds) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(TrafficPhase.Walk, game.Intersection.Phase, "No complete safe crossing window.");
            Assert.IsFalse(game.Traffic.HasUnclearedVehicles);
            yield return Walk(new Vector3(0, 0, sign * (game.scenario.roadHalfWidth + .8f)));
            Assert.IsTrue(game.CrossingCompleted, "Opposite sidewalk exit must be traversed.");
            deadline = Time.realtimeSinceStartup + 25;
            while (game.IsPlaying && !game.Evaluation.Finished && Time.realtimeSinceStartup < deadline)
            {
                Vector3 target = game.NavigationTarget;
                Vector3 delta = target - game.PlayerPosition; delta.y = 0;
                input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg; input.pitch = 0;
                input.motion = Vector2.up * Mathf.Min(1, delta.magnitude / .25f);
                yield return null;
            }
            input.motion = Vector2.zero;
            Assert.IsTrue(game.Evaluation.SafetySuccess, game.Mission.missionId + ": " + game.Evaluation.Feedback + " at " + game.PlayerPosition + " goal " + game.Destination);
            Assert.AreEqual(0, game.Evaluation.MovingCollisions);
            Assert.AreEqual(0, game.Evaluation.Violations);
        }
        private IEnumerator CompleteMission(int district, int tier)
        {
            for (int variant = 0; variant < 3; variant++)
            {
                game.ReturnToMenu(); game.SelectScenario(district); game.SelectMission(tier); game.SelectVariant(variant); game.StartPractice();
                Assert.AreEqual("S0" + (district + 1) + "-T" + (tier + 1), game.Mission.missionId);
                for (int leg = 0; leg < game.SegmentCount; leg++)
                {
                    yield return CompleteLeg();
                    if (leg + 1 < game.SegmentCount)
                    {
                        Assert.AreEqual(StreetGameState.SegmentComplete, game.State);
                        Vector3 position = game.PlayerPosition; TrafficPhase phase = game.Intersection.Phase; float clock = game.WorldTime;
                        game.ContinueMission();
                        Assert.AreEqual(position, game.PlayerPosition, "Continuing cannot teleport the pedestrian.");
                        Assert.AreEqual(phase, game.Intersection.Phase, "Returning cannot reset the signal.");
                        Assert.AreEqual(clock, game.WorldTime);
                        Assert.IsFalse(game.Evaluation.LeftObserved || game.Evaluation.RightObserved, "Return observations must be fresh.");
                    }
                }
                Assert.IsTrue(game.MissionSafetySuccess, game.MissionSummary);
                Assert.AreEqual(game.SegmentCount, game.SegmentResults.Count);
                string evidence = "MISSION_ACCEPTED " + game.Mission.missionId + " variant=" + variant + " seed=" + game.Mission.SeedForVariant(game.scenario.seed, variant);
                LogAssert.Expect(LogType.Log, evidence); Debug.Log(evidence);
                LogAssert.NoUnexpectedReceived();
            }
        }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S01T1AllThreeTrafficConfigurations() { yield return CompleteMission(0, 0); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S01T2AllThreeTrafficConfigurations() { yield return CompleteMission(0, 1); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S01T3AllThreeReverseConfigurations() { yield return CompleteMission(0, 2); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S02T1AllThreeBookshopConfigurations() { yield return CompleteMission(1, 0); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S02T2AllThreeMixedTrafficConfigurations() { yield return CompleteMission(1, 1); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S02T3AllThreePharmacyConfigurations() { yield return CompleteMission(1, 2); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S03T1AllThreeCafeConfigurations() { yield return CompleteMission(2, 0); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S03T2AllThreeObservationConfigurations() { yield return CompleteMission(2, 1); }
        [UnityTest, Category("MissionAcceptance")] public IEnumerator S03T3AllThreeRoundTripConfigurations() { yield return CompleteMission(2, 2); }

        [UnityTest]
        public IEnumerator UnsafeFirstLegRemainsInWholeMissionAndReturnRequiresFreshObservation()
        {
            game.SelectScenario(2); game.SelectMission(2); game.StartPractice();
            DriveSemanticLeg(false); yield return null;
            Assert.AreEqual(StreetGameState.SegmentComplete, game.State);
            Assert.IsFalse(game.SegmentResults[0].SafetySuccess);
            game.ContinueMission();
            Assert.IsFalse(game.Evaluation.LeftObserved || game.Evaluation.RightObserved);
            DriveSemanticLeg(true); yield return null;
            Assert.AreEqual(StreetGameState.Result, game.State);
            Assert.IsTrue(game.SegmentResults[1].SafetySuccess);
            Assert.IsFalse(game.MissionSafetySuccess); Assert.AreEqual(0, game.Stars);
        }
        private void DriveSemanticLeg(bool safe)
        {
            // Event-integration negative case only; real native walking is covered by the nine tests above.
            var e = game.Evaluation;
            e.Tick(CrossingRegion.Approach | CrossingRegion.Kerb | CrossingRegion.Entry, TrafficPhase.VehicleGreen, true, false, .02f);
            e.Observe(true); e.Observe(false);
            e.Tick(CrossingRegion.RoadwayHazard | CrossingRegion.CrossingRoute, safe ? TrafficPhase.Walk : TrafficPhase.VehicleGreen, true, false, .02f);
            e.Tick(CrossingRegion.Exit, TrafficPhase.Walk, true, false, .02f);
            e.Tick(CrossingRegion.Destination, TrafficPhase.Walk, true, false, .02f);
        }
    }
}
