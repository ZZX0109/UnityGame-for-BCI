using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Player;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using StreetCrossing.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>Authored engine acceptance: real scene/input/CharacterController/traffic/visibility/outcome. Never writes success flags.</summary>
    public sealed class StreetAcceptanceTests
    {
        private sealed class ScriptedInput : IStreetInput
        {
            public StreetGameManager game;
            public float yaw, pitch;
            public Vector2 movement;
            public Vector2 Movement => movement;
            public Vector2 Look => new Vector2(Mathf.DeltaAngle(game.Player.Heading, yaw), game.Player.Pitch - pitch) / StreetPreferences.Sensitivity;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }
        private StreetGameManager game;
        private ScriptedInput input;
        [UnitySetUp]
        public IEnumerator LoadStreet()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            game = Object.FindObjectOfType<StreetGameManager>(); Assert.That(game, Is.Not.Null); game.AutoPauseOnFocusLoss = false;
            input = new ScriptedInput { game = game }; game.SetInputSource(input);
        }
        private IEnumerator WalkTo(float z, float maximumSimulationSeconds)
        {
            float start = game.WorldTime, realtime = Time.realtimeSinceStartup; input.yaw = input.pitch = 0; input.movement = Vector2.up;
            while (game.IsPlaying && game.PlayerPosition.z < z && game.WorldTime - start < maximumSimulationSeconds && Time.realtimeSinceStartup - realtime < maximumSimulationSeconds * 2 + 5) yield return null;
            input.movement = Vector2.zero;
            Assert.That(game.PlayerPosition.z, Is.GreaterThanOrEqualTo(z - 0.1f), "CharacterController could not reach the required route point.");
        }
        private IEnumerator Observe(bool left)
        {
            Vector3 point = left ? game.LeftRoadTargets[0] : game.RightRoadTargets[0];
            Vector3 delta = point - game.Player.View.transform.position;
            input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            input.pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
            float start = game.WorldTime, realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && !(left ? game.Evaluation.LeftObserved : game.Evaluation.RightObserved) && game.WorldTime - start < 3 && Time.realtimeSinceStartup - realtime < 10) yield return null;
            Assert.That(left ? game.Evaluation.LeftObserved : game.Evaluation.RightObserved, Is.True, "Visible road/vehicle evidence at kerb was not recorded.");
        }
        private IEnumerator Complete(int scenarioIndex)
        {
            game.SelectScenario(scenarioIndex); game.SetInputSource(input); game.StartPractice();
            Assert.That(game.scenario.scenarioId, Is.EqualTo("S0" + (scenarioIndex + 1)));
            Assert.That(game.Player.View.transform.localPosition.y, Is.EqualTo(1.65f).Within(0.001f));
            int hazard = 0, protectedCount = 0, route = 0;
            foreach (CrossingZone zone in game.Zones)
            {
                if (zone.region == CrossingRegion.RoadwayHazard) hazard++;
                if (zone.region == CrossingRegion.ProtectedCrosswalk) { protectedCount++; Assert.That(zone.size.x, Is.LessThan(8)); }
                if (zone.region == CrossingRegion.CrossingRoute) route++;
            }
            Assert.That(hazard, Is.GreaterThanOrEqualTo(1)); Assert.That(protectedCount, Is.EqualTo(1)); Assert.That(route, Is.EqualTo(1));
            CrossingRegion remote = CrossingRegion.None;
            foreach (CrossingZone zone in game.Zones) if (zone.Contains(new Vector3(16, 0, 0))) remote |= zone.region;
            Assert.That((remote & CrossingRegion.RoadwayHazard) != 0, Is.True);
            Assert.That((remote & (CrossingRegion.ProtectedCrosswalk | CrossingRegion.CrossingRoute)) == 0, Is.True);
            // Forward adjustment on the sidewalk clears S03's occlusion without entering roadway.
            yield return WalkTo(-game.scenario.roadHalfWidth - .12f, 10);
            yield return Observe(true); yield return Observe(false);
            input.yaw = input.pitch = 0;
            float start = game.WorldTime, realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && (game.Intersection.Phase != TrafficPhase.Walk || game.Intersection.Remaining < game.scenario.SafeCrossingSeconds) && game.WorldTime - start < 100 && Time.realtimeSinceStartup - realtime < 160) yield return null;
            Assert.That(game.Intersection.Phase, Is.EqualTo(TrafficPhase.Walk)); Assert.That(game.Traffic.HasUnclearedVehicles, Is.False);
            input.movement = Vector2.up; start = game.WorldTime; realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && !game.Evaluation.Finished && game.WorldTime - start < 25 && Time.realtimeSinceStartup - realtime < 50) yield return null;
            input.movement = Vector2.zero;
            Assert.That(game.Evaluation.Finished, Is.True); Assert.That(game.Evaluation.SafetySuccess, Is.True, game.Evaluation.Feedback);
            Assert.That(game.Evaluation.ObservationRequirementsSatisfied, Is.True); Assert.That(game.Evaluation.MovingCollisions, Is.Zero);
            Assert.That(game.Evaluation.Violations, Is.Zero); Assert.That(game.State, Is.EqualTo(StreetGameState.Result));
            Assert.That(game.PlayerPosition.z, Is.GreaterThan(game.scenario.roadHalfWidth));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTearDown]
        public IEnumerator RestoreFocusPolicy() { if (game != null) { game.AutoPauseOnFocusLoss = true; game.Pause(); } yield return null; }
        [UnityTest, Category("Acceptance_S01")]
        public IEnumerator S01SafeCrossingWithActualVisiblePreEntryObservation() { yield return Complete(0); }
        [UnityTest, Category("Acceptance_S02")]
        public IEnumerator S02DenseMixedVehiclesSafeCrossing() { yield return Complete(1); }
        [UnityTest, Category("Acceptance_S03")]
        public IEnumerator S03RealTurnsOcclusionAdjustmentAndSafeCrossing() { yield return Complete(2); }
        [UnityTest]
        public IEnumerator S03OpaqueKerbShelterRequiresSafeForwardRepositioning()
        {
            game.SelectScenario(2); game.SetInputSource(input); game.StartPractice();
            yield return WalkTo(-game.scenario.roadHalfWidth - .75f, 10);
            Vector3 delta = game.LeftRoadTargets[0] - game.Player.View.transform.position;
            input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg; input.pitch = 2;
            float start = game.WorldTime, realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && game.WorldTime - start < .6f && Time.realtimeSinceStartup - realtime < 4) yield return null;
            Assert.That(game.IsAtKerb, Is.True); Assert.That(game.Evaluation.LeftObserved, Is.False, "Opaque shelter must prevent fabricated left-road observation.");
            yield return WalkTo(-game.scenario.roadHalfWidth - .12f, 3);
            yield return Observe(true);
            Assert.That(game.IsPlayerInRoadwayHazard, Is.False); Assert.That(game.Evaluation.Attempts, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator S03ProtectedPedestrianHoldsAllRedAndCanFinishAfterFlashing()
        {
            game.SelectScenario(2); game.SetInputSource(input); game.StartPractice();
            yield return WalkTo(-game.scenario.roadHalfWidth - .12f, 10);
            yield return Observe(true); yield return Observe(false); input.yaw = input.pitch = 0;
            float start = game.WorldTime, realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && game.Intersection.Phase != TrafficPhase.Walk && game.WorldTime - start < 100 && Time.realtimeSinceStartup - realtime < 160) yield return null;
            Assert.That(game.Intersection.Phase, Is.EqualTo(TrafficPhase.Walk));
            yield return WalkTo(0, 6); input.movement = Vector2.zero;
            start = game.WorldTime; realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && !(game.Intersection.Phase == TrafficPhase.AllRedAfterWalk && game.Intersection.IsClearanceHeld) && game.WorldTime - start < 40 && Time.realtimeSinceStartup - realtime < 70) yield return null;
            Assert.That(game.IsPlayerInProtectedCrosswalk, Is.True); Assert.That(game.Intersection.IsClearanceHeld, Is.True);
            Assert.That(game.Intersection.CanEnterVehicles, Is.False); Assert.That(game.Traffic.HasUnclearedVehicles, Is.False);
            Assert.That(game.Evaluation.MovingCollisions, Is.Zero);
            input.movement = Vector2.up; start = game.WorldTime; realtime = Time.realtimeSinceStartup;
            while (game.IsPlaying && !game.Evaluation.Finished && game.WorldTime - start < 15 && Time.realtimeSinceStartup - realtime < 30) yield return null;
            input.movement = Vector2.zero;
            Assert.That(game.Evaluation.SafetySuccess, Is.True, game.Evaluation.Feedback); Assert.That(game.Evaluation.SignalAtEntry, Is.EqualTo(TrafficPhase.Walk));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest]
        public IEnumerator CanvasSurvivesScenarioChangesAndResponsiveSettingsModal()
        {
            StreetUIController ui = game.GetComponent<StreetUIController>();
            Assert.That(ui.RuntimeCanvas, Is.Not.Null); Assert.That(ui.RuntimeFont, Is.Not.Null);
            foreach (var resolution in new[] { new Vector2Int(1280,720), new Vector2Int(1366,768), new Vector2Int(1920,1080), new Vector2Int(1280,1024) })
            {
                Screen.SetResolution(resolution.x,resolution.y,false); yield return null;
                game.SelectScenario(2); ui.ShowSettings(); Canvas.ForceUpdateCanvases(); yield return null;
                Assert.That(ui.SettingsVisible, Is.True); Assert.That(ui.ModalPanel.gameObject.activeInHierarchy, Is.True);
                ui.CloseModal();
            }
            game.StartPractice(); yield return null; game.Pause(); float clock = game.WorldTime;
            ui.ShowRestartConfirmation(); yield return new WaitForSecondsRealtime(.1f);
            Assert.That(game.WorldTime, Is.EqualTo(clock)); ui.CloseModal();
            for (int i=0;i<20;i++) { game.StartPractice(); game.Pause(); }
            Assert.That(game.Traffic.Active.Count, Is.LessThanOrEqualTo(game.scenario.maximumVehicles));
            Assert.That(game.Evaluation.LeftObserved || game.Evaluation.RightObserved, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
