using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>Real-scene coverage for expiring, refreshing, and preserving road observations.</summary>
    public sealed class CourseObservationRefreshTests
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
        private readonly List<string> gameEvents = new List<string>();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            originalScale = Time.timeScale;
            Time.timeScale = 3;
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene");
            game = Object.FindObjectOfType<StreetGameManager>();
            Assert.That(game, Is.Not.Null, "The real StreetCrossingScene must load.");
            game.AutoPauseOnFocusLoss = false;
            game.PersistMissionProgress = false;
            game.GameEvent += OnGameEvent;
            input = new RouteInput { game = game };
            game.SetInputSource(input);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (input != null) input.motion = Vector2.zero;
            if (game != null)
            {
                game.GameEvent -= OnGameEvent;
                game.ReturnToMenu();
            }
            Time.timeScale = originalScale;
            yield return null;
        }

        private void OnGameEvent(string name, string detail) { gameEvents.Add(name); }

        private int EventCount(string name) => gameEvents.Count(item => item == name);

        private IEnumerator Walk(Vector3 target, float radius = .10f)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (game.IsPlaying && Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target - game.PlayerPosition;
                delta.y = 0;
                if (delta.magnitude <= radius) break;
                input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                input.pitch = 0;
                input.motion = Vector2.up * Mathf.Min(1, delta.magnitude / .25f);
                yield return null;
            }
            input.motion = Vector2.zero;
            Vector3 remaining = target - game.PlayerPosition;
            remaining.y = 0;
            Assert.That(remaining.magnitude, Is.LessThan(.24f),
                "Native route blocked at " + game.PlayerPosition + " heading for " + target + " in " + game.Mission.missionId);
        }

        private IEnumerator WalkToKerb()
        {
            int sign = game.ForwardSign;
            float corridor = sign > 0 ? -9.95f : 10.2f;
            yield return Walk(new Vector3(game.PlayerPosition.x, 0, corridor));
            yield return Walk(new Vector3(0, 0, corridor));
            yield return Walk(new Vector3(0, 0, -sign * (game.scenario.roadHalfWidth + .13f)));
            Assert.That(game.IsAtKerb, Is.True, "Observation and waiting must occur on the actual kerb.");
        }

        private IEnumerator Observe(bool left)
        {
            string eventName = left ? "LeftLook" : "RightLook";
            int countBefore = EventCount(eventName);
            Vector3 target = (left ? game.LeftRoadTargets : game.RightRoadTargets)[0];
            Vector3 delta = target - game.Player.View.transform.position;
            input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            input.pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
            float deadline = Time.realtimeSinceStartup + 8;
            while (game.IsPlaying && EventCount(eventName) <= countBefore && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(EventCount(eventName), Is.GreaterThan(countBefore),
                game.Mission.missionId + " did not emit a new real " + (left ? "left" : "right") + " observation.");
        }

        private IEnumerator WaitForWorldTime(float seconds)
        {
            float start = game.WorldTime;
            float deadline = Time.realtimeSinceStartup + 15;
            input.motion = Vector2.zero;
            input.yaw = game.ForwardSign > 0 ? 0 : 180;
            input.pitch = 0;
            while (game.IsPlaying && game.WorldTime - start < seconds && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(game.WorldTime - start, Is.GreaterThanOrEqualTo(seconds - .02f), "The simulation must actually wait at the kerb.");
            Assert.That(game.IsAtKerb, Is.True, "The pedestrian must remain at the kerb during the wait.");
        }

        private static Dictionary<int, float> VehicleDistances(StreetGameManager manager)
        {
            return manager.Traffic.Active.ToDictionary(car => car.EncounterId, car => car.Motion.Distance);
        }

        private int VehicleDirectionForObservation(bool left)
        {
            bool worldLeft = game.ForwardSign > 0 ? left : !left;
            return worldLeft ? 1 : -1;
        }

        private void AssertTrafficChanged(Dictionary<int, float> before)
        {
            Dictionary<int, float> after = VehicleDistances(game);
            bool changed = after.Keys.Except(before.Keys).Any() || before.Keys.Except(after.Keys).Any();
            if (!changed)
                changed = after.Any(pair => before.TryGetValue(pair.Key, out float oldDistance) && Mathf.Abs(pair.Value - oldDistance) >= 2f);
            Assert.That(changed, Is.True, "At least one live vehicle must move 2 m or traffic membership must change during the observation interval.");
        }

        private IEnumerator WaitForSafeEntryWindow()
        {
            input.motion = Vector2.zero;
            input.yaw = game.ForwardSign > 0 ? 0 : 180;
            input.pitch = 0;
            float deadline = Time.realtimeSinceStartup + 70;
            while (game.IsPlaying && Time.realtimeSinceStartup < deadline &&
                   (game.Intersection.Phase != TrafficPhase.Walk ||
                    game.Intersection.Remaining < game.scenario.SafeCrossingSeconds || game.Traffic.HasUnclearedVehicles))
                yield return null;
            Assert.That(game.Intersection.Phase, Is.EqualTo(TrafficPhase.Walk), "A legal pedestrian signal is required for the real crossing.");
            Assert.That(game.Intersection.Remaining, Is.GreaterThanOrEqualTo(game.scenario.SafeCrossingSeconds));
            Assert.That(game.Traffic.HasUnclearedVehicles, Is.False);
        }

        private IEnumerator CrossAndFinish()
        {
            int sign = game.ForwardSign;
            yield return Walk(new Vector3(0, 0, sign * (game.scenario.roadHalfWidth + .8f)));
            Assert.That(game.CrossingCompleted, Is.True, "The player must physically reach the opposite sidewalk.");
            float deadline = Time.realtimeSinceStartup + 30;
            while (game.IsPlaying && !game.Evaluation.Finished && Time.realtimeSinceStartup < deadline)
            {
                Vector3 target = game.NavigationTarget;
                Vector3 delta = target - game.PlayerPosition;
                delta.y = 0;
                input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                input.pitch = 0;
                input.motion = delta.sqrMagnitude < .01f ? Vector2.zero : Vector2.up * Mathf.Min(1, delta.magnitude / .25f);
                yield return null;
            }
            input.motion = Vector2.zero;
            Assert.That(game.Evaluation.Finished, Is.True, "The actual destination trigger must finish the crossing.");
            Assert.That(game.State, Is.EqualTo(StreetGameState.Result));
        }

        [UnityTest, Category("StreetCourse")]
        public IEnumerator RiskAwareObservationRefreshIsLimitedToTheFourCrossingStages()
        {
            for (int stage = 0; stage < StreetCourse.Stages.Count; stage++)
            {
                game.StartCourse(stage);
                Assert.That(game.CourseIndex, Is.EqualTo(stage));
                Assert.That(game.CourseRiskAwareObservations, Is.EqualTo(stage >= 2), "Course stage " + (stage + 1));
                game.ReturnToMenu();
            }

            game.SelectScenario(0);
            game.SelectMission(0);
            game.StartPractice();
            Assert.That(game.CourseIndex, Is.EqualTo(-1));
            Assert.That(game.CourseRiskAwareObservations, Is.False);
            yield return null;
        }

        [UnityTest, Category("StreetCourse")]
        public IEnumerator ExpiredCourseObservationsAfterLiveTrafficChangesCannotEarnSafetyCredit()
        {
            game.StartCourse(2); // Sixth-stage curriculum's first crossing mission.
            Assert.That(game.CourseRiskAwareObservations, Is.True);
            yield return WalkToKerb();
            yield return Observe(true);
            yield return Observe(false);
            Assert.That(game.Evaluation.LeftObserved && game.Evaluation.RightObserved, Is.True);

            Dictionary<int, float> trafficBeforeWait = VehicleDistances(game);
            yield return WaitForWorldTime(11.2f);
            AssertTrafficChanged(trafficBeforeWait);
            Assert.That(game.IsPlaying, Is.True, "A long wait must leave the curriculum task available for a safe decision.");
            Assert.That(game.Evaluation.Finished, Is.False);
            Assert.That(game.Evaluation.LeftObservationInvalidated && game.Evaluation.RightObservationInvalidated, Is.True,
                "Both old observations must be withdrawn after the wait or live-road change.");
            CollectionAssert.Contains(new[] { "expired-after-10-seconds", "left-road-risk-changed" }, game.Evaluation.LeftObservationInvalidationReason);
            CollectionAssert.Contains(new[] { "expired-after-10-seconds", "right-road-risk-changed" }, game.Evaluation.RightObservationInvalidationReason);
            Assert.That(game.Evaluation.LeftObserved || game.Evaluation.RightObserved, Is.False,
                "Expired evidence cannot remain usable at entry.");
            Assert.That(game.CourseObservationPrompt, Is.Not.Empty, "The course must explain that a fresh view is needed.");

            yield return WaitForSafeEntryWindow();
            yield return CrossAndFinish();
            Assert.That(game.Evaluation.SafetySuccess, Is.False, game.Evaluation.Feedback);
            Assert.That(game.MissionSafetySuccess, Is.False);
            Assert.That(game.Stars, Is.Zero);
            Assert.That(EventCount("ObservationRequirementMissing"), Is.GreaterThan(0),
                "Entering after stale observations must be recorded as missing a current observation.");
        }

        [UnityTest, Category("StreetCourse")]
        public IEnumerator ReobservingAfterTheSameWaitRestoresARealSafeCourseCrossing()
        {
            game.StartCourse(2);
            yield return WalkToKerb();
            yield return Observe(true);
            yield return Observe(false);

            Dictionary<int, float> trafficBeforeWait = VehicleDistances(game);
            yield return WaitForWorldTime(11.2f);
            AssertTrafficChanged(trafficBeforeWait);
            Assert.That(game.IsPlaying && !game.Evaluation.Finished, Is.True,
                "Waiting for fresh conditions must not end the curriculum task.");

            // Wait for a safe phase first, then refresh both visible sides immediately before entry.
            yield return WaitForSafeEntryWindow();
            int leftCount = EventCount("LeftLook");
            int rightCount = EventCount("RightLook");
            yield return Observe(true);
            yield return Observe(false);
            Assert.That(EventCount("LeftLook"), Is.GreaterThan(leftCount));
            Assert.That(EventCount("RightLook"), Is.GreaterThan(rightCount));
            Assert.That(game.Evaluation.LeftObserved && game.Evaluation.RightObserved, Is.True);
            Assert.That(game.Evaluation.LeftObservationInvalidated || game.Evaluation.RightObservationInvalidated, Is.False,
                "A fresh real dwell must clear each side's invalidation state.");
            Assert.That(game.Evaluation.LeftObservationInvalidationReason, Is.Empty);
            Assert.That(game.Evaluation.RightObservationInvalidationReason, Is.Empty);
            Assert.That(game.CourseObservationPrompt, Is.Empty);

            yield return CrossAndFinish();
            Assert.That(game.Evaluation.SafetySuccess, Is.True, game.Evaluation.Feedback);
            Assert.That(game.MissionSafetySuccess, Is.True, game.MissionSummary);
            Assert.That(game.Stars, Is.GreaterThanOrEqualTo(1), "Waiting itself must not remove all safety credit.");
        }

        [UnityTest, Category("StreetCourse")]
        public IEnumerator ASingleRoadRiskBandChangeInvalidatesOnlyItsObservedSide()
        {
            game.StartCourse(2);
            Assert.That(game.CourseRiskAwareObservations, Is.True);
            yield return WalkToKerb();
            yield return WaitForSafeEntryWindow();
            yield return Observe(true);
            yield return Observe(false);

            int rightBefore = game.CourseObservationRiskSignature(false);
            int leftBefore = game.CourseObservationRiskSignature(true);
            VehicleController leftCar = game.Traffic.Active.FirstOrDefault(car =>
                !car.Lane.IsTurning && car.Lane.Direction == VehicleDirectionForObservation(true));
            Assert.That(leftCar, Is.Not.Null, "S01 must have a live straight vehicle in the observed left-side approach.");

            // Make one real pooled vehicle occupy the left conflict band in the loaded gameplay scene.
            // The crossing remains empty of pedestrians; this only creates a deterministic risk signature transition.
            float distance = leftCar.Lane.ConflictEntry - leftCar.Motion.BodyHalfLength + .25f;
            leftCar.Motion.Reset(distance, .1f, leftCar.Motion.BodyHalfLength);
            leftCar.ApplyPosition();
            Physics.SyncTransforms();
            yield return null;

            Assert.That(game.CourseObservationRiskSignature(true), Is.Not.EqualTo(leftBefore), "The left-road risk signature must change.");
            Assert.That(game.CourseObservationRiskSignature(false), Is.EqualTo(rightBefore), "No right-road vehicle or signal state changed.");
            Assert.That(game.Evaluation.LeftObservationInvalidated, Is.True);
            Assert.That(game.Evaluation.LeftObserved, Is.False);
            Assert.That(game.Evaluation.LeftObservationInvalidationReason, Is.EqualTo("left-road-risk-changed"));
            Assert.That(game.Evaluation.RightObserved, Is.True, "Unchanged right-side evidence must remain usable.");
            Assert.That(game.Evaluation.RightObservationInvalidated, Is.False);
            Assert.That(game.Evaluation.RightObservationInvalidationReason, Is.Empty);
        }

        [UnityTest, Category("MissionAcceptance")]
        public IEnumerator LegacyPracticeStillAllowsLongKerbWaitingWithoutAWaitingPenalty()
        {
            game.SelectScenario(0);
            game.SelectMission(0);
            game.StartPractice();
            Assert.That(game.CourseIndex, Is.EqualTo(-1), "This checks the pre-curriculum mission entry point.");
            Assert.That(game.CourseRiskAwareObservations, Is.False, "The old mission entry point keeps its existing observation behavior.");
            yield return WalkToKerb();
            yield return Observe(true);
            yield return Observe(false);

            Dictionary<int, float> trafficBeforeWait = VehicleDistances(game);
            yield return WaitForWorldTime(11.2f);
            AssertTrafficChanged(trafficBeforeWait);
            Assert.That(game.IsPlaying, Is.True);
            Assert.That(game.Evaluation.Finished, Is.False, "A reasonable wait cannot force a timeout or failure.");
            Assert.That(game.Evaluation.LeftObserved && game.Evaluation.RightObserved, Is.True,
                "Legacy missions keep their established observation state while waiting.");
            Assert.That(game.Evaluation.LeftObservationInvalidated || game.Evaluation.RightObservationInvalidated, Is.False);

            yield return WaitForSafeEntryWindow();
            yield return CrossAndFinish();
            Assert.That(game.MissionSafetySuccess, Is.True, game.MissionSummary);
            Assert.That(game.SegmentResults[0].WaitingSeconds, Is.GreaterThan(10f));
            Assert.That(game.Stars, Is.GreaterThanOrEqualTo(1), "The safe task result must remain scoreable after a long wait.");
        }
    }
}
