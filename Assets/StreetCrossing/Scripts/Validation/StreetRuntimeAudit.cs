using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BCI.RuntimeValidation;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
using StreetCrossing.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StreetCrossing.Validation
{
    /// <summary>Opt-in standalone audit adapter. It exercises normal public game APIs and never edits training settings.</summary>
    public sealed class StreetRuntimeAudit : IGameRuntimeAuditAdapter
    {
        private sealed class QuietInput : IStreetInput
        {
            public Vector2 Movement => Vector2.zero;
            public Vector2 Look => Vector2.zero;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public string scenarioId, missionId, gameState, trafficPhase, eventLogPath;
            public int missionRevision, variant, segment, longWaitReminders;
            public int activeVehicles, maximumVehicles, backgroundPeople, timeoutRestarts, completedRebuilds, prepareCalls;
            public float worldTime, timeoutSeconds, timeScale;
            public double observedSimulationSeconds;
            public Vector3 playerPosition;
            public bool isPlaying, hudVisible;
        }

        private readonly QuietInput quietInput = new QuietInput();
        private StreetGameManager game;
        private bool restoreFocus, previousFocusPolicy, subscribed, timeoutObserved;
        private int timeoutRestarts, completedRebuilds, prepareCalls;
        private int longWaitReminders;
        private bool galleryCaptured;
        private float previousWorldTime, lastSimulationAdvance;
        private double observedSimulationSeconds;

        public string GameId => "street";
        public string SnapshotJson
        {
            get
            {
                if (!game) return "{\"unavailable\":true}";
                var population = game.GetComponentInChildren<StreetBackgroundPopulation>();
                var ui = game.GetComponent<StreetUIController>();
                return JsonUtility.ToJson(new Snapshot {
                    scenarioId = game.scenario.scenarioId,
                    missionId = game.Mission.missionId, missionRevision = game.Mission.revision,
                    variant = game.SelectedVariant, segment = game.CurrentSegmentIndex, longWaitReminders = longWaitReminders,
                    gameState = game.State.ToString(),
                    trafficPhase = game.Intersection.Phase.ToString(),
                    eventLogPath = game.LogPath,
                    activeVehicles = game.Traffic.Active.Count,
                    maximumVehicles = game.scenario.maximumVehicles,
                    backgroundPeople = population ? population.Count : 0,
                    timeoutRestarts = timeoutRestarts,
                    completedRebuilds = completedRebuilds,
                    prepareCalls = prepareCalls,
                    worldTime = game.WorldTime,
                    timeoutSeconds = game.scenario.timeoutSeconds,
                    timeScale = Time.timeScale,
                    observedSimulationSeconds = observedSimulationSeconds,
                    playerPosition = game.PlayerPosition,
                    isPlaying = game.IsPlaying,
                    hudVisible = ui && ui.RuntimeCanvas && ui.RuntimeCanvas.enabled && ui.RuntimeCanvas.gameObject.activeInHierarchy
                });
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeWhenRequested()
        {
            // TryStart is inert unless the explicit standalone audit command-line option is present.
            GameRuntimeAudit.TryStart(new StreetRuntimeAudit());
        }

        public IEnumerator Prepare()
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!(game = Object.FindObjectOfType<StreetGameManager>()) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!game || !game.enabled || game.Scenarios.Count != 3)
                throw new InvalidOperationException("Street audit requires the enabled three-scenario street scene.");
            if (!restoreFocus)
            {
                previousFocusPolicy = game.AutoPauseOnFocusLoss;
                restoreFocus = true;
            }
            game.AutoPauseOnFocusLoss = false;
            game.SetInputSource(quietInput);
            if (!subscribed) { game.GameEvent += OnGameEvent; subscribed = true; }
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (!galleryCaptured && arguments[i] == "--bci-mission-gallery")
                {
                    yield return CaptureMissionGallery(arguments[i + 1]); galleryCaptured = true;
                }
            game.ReturnToMenu();
            game.SelectScenario(1);
            yield return null;
            if (game.scenario.scenarioId != "S02")
                throw new InvalidOperationException("Street performance audit must use the actual S02 configuration.");
            game.SetInputSource(quietInput);
            timeoutObserved = false;
            game.StartPractice();
            yield return null;
            if (!game.IsPlaying) throw new InvalidOperationException("S02 did not start through StartPractice.");
            prepareCalls++;
            timeoutRestarts = 0;
            longWaitReminders = 0;
            observedSimulationSeconds = 0;
            previousWorldTime = game.WorldTime;
            lastSimulationAdvance = Time.realtimeSinceStartup;
        }

        [Serializable]
        private sealed class GalleryShot
        {
            public string file, missionId, state, assistance, destination;
            public int width, height, variant, segment;
            public Vector3 playerPosition, goal, navigationTarget;
        }
        [Serializable]
        private sealed class GalleryManifest
        {
            public string renderer, evidence = "Actual standalone backbuffer captures: nine task HUDs and three district menus. Human layout review is separate.";
            public List<GalleryShot> screenshots = new List<GalleryShot>();
        }
        private IEnumerator CaptureMissionGallery(string directory)
        {
            if (!Path.IsPathRooted(directory)) throw new ArgumentException("Gallery requires an absolute output path.");
            Directory.CreateDirectory(directory);
            var manifest = new GalleryManifest { renderer = SystemInfo.graphicsDeviceName };
            for (int district = 0; district < game.Scenarios.Count; district++)
            {
                game.ReturnToMenu(); game.SelectScenario(district);
                yield return CaptureGalleryShot(directory, "S0" + (district + 1) + "-menu.png", manifest);
                for (int tier = 0; tier < game.Missions.Count; tier++)
                {
                    game.ReturnToMenu(); game.SelectMission(tier); game.SelectVariant(0); game.StartPractice();
                    yield return CaptureGalleryShot(directory, game.Mission.missionId + "-hud.png", manifest);
                }
            }
            File.WriteAllText(Path.Combine(directory, "gallery-manifest.json"), JsonUtility.ToJson(manifest, true) + "\n", new System.Text.UTF8Encoding(false));
        }
        private IEnumerator CaptureGalleryShot(string directory, string file, GalleryManifest manifest)
        {
            // Real rendered frames allow deferred UI destruction and shader uploads to settle.
            for (int frame = 0; frame < 8; frame++) { yield return null; yield return new WaitForEndOfFrame(); }
            Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (!capture) throw new InvalidOperationException("No actual gallery backbuffer.");
            try
            {
                File.WriteAllBytes(Path.Combine(directory, file), capture.EncodeToPNG());
                manifest.screenshots.Add(new GalleryShot {
                    file = file, missionId = game.Mission.missionId, state = game.State.ToString(), assistance = game.Mission.assistance.ToString(),
                    destination = game.DestinationName, width = capture.width, height = capture.height, variant = game.SelectedVariant,
                    segment = game.CurrentSegmentIndex, playerPosition = game.PlayerPosition, goal = game.Destination,
                    navigationTarget = game.NavigationTarget
                });
            }
            finally { Object.Destroy(capture); }
        }

        public void TickActive()
        {
            if (!game || !game.enabled) throw new InvalidOperationException("Street game disappeared during the audit.");
            if (!Mathf.Approximately(Time.timeScale, 1f))
                throw new InvalidOperationException("Street activity sampling requires normal timeScale=1.");
            float current = game.WorldTime;
            if (current > previousWorldTime)
            {
                observedSimulationSeconds += current - previousWorldTime;
                lastSimulationAdvance = Time.realtimeSinceStartup;
            }
            previousWorldTime = current;
            if (game.State == StreetGameState.Result)
            {
                if (!timeoutObserved || game.Evaluation.SafetySuccess || game.Evaluation.MovingCollisions != 0)
                    throw new InvalidOperationException("The quiet street audit ended for a reason other than its original practice timeout.");
                timeoutObserved = false;
                timeoutRestarts++;
                game.StartPractice();
                previousWorldTime = game.WorldTime;
                lastSimulationAdvance = Time.realtimeSinceStartup;
            }
            if (!game.IsPlaying) throw new InvalidOperationException("Street activity stopped during continuous frame sampling.");
            if (Time.realtimeSinceStartup - lastSimulationAdvance > 5)
                throw new InvalidOperationException("Street simulation did not advance for five seconds during activity sampling.");
        }

        public IEnumerator RebuildAndRestart(int iteration)
        {
            if (!game) throw new InvalidOperationException("Prepare must run before rebuilding street scenarios.");
            game.ReturnToMenu();
            int index = iteration % game.Scenarios.Count;
            game.SelectScenario(index);
            game.SetInputSource(quietInput);
            timeoutObserved = false;
            yield return null;
            game.StartPractice();
            yield return null;
            if (game.SelectedScenarioIndex != index || !game.IsPlaying || game.Evaluation.Finished)
                throw new InvalidOperationException("Street scenario switching/restart did not produce a new playing attempt.");
            completedRebuilds++;
            previousWorldTime = game.WorldTime;
            lastSimulationAdvance = Time.realtimeSinceStartup;
        }

        private void OnGameEvent(string name, string detail)
        {
            if (name == "Timeout") timeoutObserved = true;
            if (name == "PracticeReminder") longWaitReminders++;
        }

        public void Restore()
        {
            if (!game) return;
            if (subscribed) game.GameEvent -= OnGameEvent;
            subscribed = false;
            game.ReturnToMenu();
            game.SetInputSource(null);
            if (restoreFocus) game.AutoPauseOnFocusLoss = previousFocusPolicy;
        }
    }
}
