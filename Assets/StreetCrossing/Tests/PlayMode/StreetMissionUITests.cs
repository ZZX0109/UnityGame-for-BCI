using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Save;
using StreetCrossing.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class StreetMissionUITests
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
        private sealed class MemoryProgress : IStreetMissionProgressStore
        {
            private readonly Dictionary<string, int> values = new Dictionary<string, int>();
            public int Writes { get; private set; }
            public int GetInt(string key, int fallback) => values.TryGetValue(key, out int value) ? value : fallback;
            public void SetInt(string key, int value) { values[key] = value; Writes++; }
            public void Save() { }
        }
        private StreetGameManager game;
        private StreetUIController ui;
        private IStreetMissionProgressStore previousStore;
        private MemoryProgress progress;
        private float previousTimeScale;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            previousTimeScale = Time.timeScale; Time.timeScale = 1;
            previousStore = StreetMissionProgress.Store;
            progress = new MemoryProgress(); StreetMissionProgress.Store = progress;
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            game = Object.FindObjectOfType<StreetGameManager>(); Assert.NotNull(game);
            game.AutoPauseOnFocusLoss = false; game.PersistMissionProgress = false; game.SetInputSource(new QuietInput());
            ui = game.GetComponent<StreetUIController>(); Assert.NotNull(ui);
            yield return RefreshUI();
        }
        [UnityTearDown]
        public IEnumerator RestoreIsolation()
        {
            if (game != null) game.ReturnToMenu();
            if (previousStore != null) StreetMissionProgress.Store = previousStore;
            Time.timeScale = previousTimeScale;
            yield return null;
        }
        private static IEnumerator RefreshUI()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }
        private Button FindButton(string name)
        {
            foreach (Button button in ui.RuntimeCanvas.GetComponentsInChildren<Button>())
                if (button.name == name) return button;
            Assert.Fail("An active semantic action is missing: " + name); return null;
        }
        private static TMP_Text CardProgress(Button card) => card.transform.Find(card.name + " description/Mission progress").GetComponent<TMP_Text>();

        [UnityTest]
        public IEnumerator ExternalSelectionsRefreshIndependentMissionCardsAndTrafficChoice()
        {
            var first = game.Missions[0]; var challenge = game.Missions[2];
            // Progress fixtures only seed the menu model. They do not claim a physical crossing.
            StreetMissionProgress.RecordSuccess(first, 0, 3);
            StreetMissionProgress.RecordSuccess(challenge, 1, 1);
            Button initial = FindButton("Mission " + first.missionId);
            Color selectedColor = initial.GetComponent<Image>().color;
            game.SelectMission(2); game.SelectVariant(2);
            yield return RefreshUI();
            Button selected = FindButton("Mission " + challenge.missionId);
            Assert.AreEqual(selectedColor, selected.GetComponent<Image>().color, "External SelectMission must move the selected card.");
            StringAssert.StartsWith("任务最佳 3 / 3 星", CardProgress(FindButton("Mission " + first.missionId)).text);
            StringAssert.StartsWith("任务最佳 1 / 3 星", CardProgress(selected).text);
            Assert.AreEqual(selectedColor, FindButton("Traffic variant 2").GetComponent<Image>().color);
            Assert.AreNotEqual(selectedColor, FindButton("Traffic variant 0").GetComponent<Image>().color);
            StringAssert.Contains("1 / 3 星", FindButton("Traffic variant 1").GetComponentInChildren<TMP_Text>().text, "Each fixed configuration must show its own saved result.");

            game.SelectScenario(1); game.SelectMission(1); game.SelectVariant(1);
            yield return RefreshUI();
            Assert.AreEqual(selectedColor, FindButton("Mission " + game.Mission.missionId).GetComponent<Image>().color);
            Assert.AreEqual(selectedColor, FindButton("Traffic variant 1").GetComponent<Image>().color);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnDemandHidesStepsUntilHelpActionAndCountsOnlyFirstOpen()
        {
            game.SelectMission(2); Assert.AreEqual(StreetAssistance.OnDemand, game.Mission.assistance);
            game.StartPractice(); yield return RefreshUI();
            Assert.IsEmpty(game.Notice, "This safe starting-sidewalk fixture must not have a live risk notice.");
            Assert.IsFalse(ui.GuidancePanel.gameObject.activeSelf);
            Assert.IsFalse(ui.DestinationMarker.gameObject.activeSelf);
            Assert.IsTrue(ui.HelpButton.gameObject.activeInHierarchy);
            Assert.AreEqual(0, game.AssistanceRequests);
            ui.HelpButton.onClick.Invoke(); yield return RefreshUI();
            Assert.IsTrue(game.HelpRequested); Assert.IsTrue(ui.GuidancePanel.gameObject.activeSelf);
            Assert.IsTrue(ui.DestinationMarker.gameObject.activeInHierarchy);
            Assert.AreEqual(1, game.AssistanceRequests); Assert.AreEqual(StreetGameState.Playing, game.State);
            ui.HelpButton.onClick.Invoke(); yield return null;
            Assert.AreEqual(1, game.AssistanceRequests); Assert.AreEqual(0, game.Evaluation.Violations);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PauseHelpAndSettingsKeepTheAlreadyPausedSimulation()
        {
            game.SelectMission(2); game.StartPractice(); game.Pause(); yield return RefreshUI();
            float pausedClock = game.WorldTime;
            FindButton("Pause request help").onClick.Invoke(); yield return RefreshUI();
            Assert.IsTrue(game.HelpRequested); Assert.AreEqual(1, game.AssistanceRequests);
            Assert.AreEqual(StreetGameState.Paused, game.State); Assert.AreEqual(pausedClock, game.WorldTime);
            ui.ShowSettings(); yield return RefreshUI();
            FindButton("Save settings").onClick.Invoke(); yield return RefreshUI();
            Assert.IsFalse(ui.SettingsVisible); Assert.AreEqual(StreetGameState.Paused, game.State);
            Assert.AreEqual(pausedClock, game.WorldTime); Assert.AreEqual(1, game.AssistanceRequests);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReturningToMenuAndOpeningSettingsPreservesTierVariantAndProgress()
        {
            game.SelectMission(1); game.SelectVariant(2);
            var mission = game.Mission;
            StreetMissionProgress.RecordSuccess(mission, 2, 2);
            int writes = progress.Writes;
            yield return RefreshUI();
            ScrollRect menuScroll = ui.ModalPanel.GetComponentInChildren<ScrollRect>();
            Assert.NotNull(menuScroll); menuScroll.verticalNormalizedPosition = 0.4f;
            game.StartPractice(); yield return RefreshUI();
            Assert.IsFalse(ui.ModalPanel.gameObject.activeInHierarchy, "The regression requires a genuinely hidden playing overlay.");
            game.ReturnToMenu(); yield return RefreshUI();
            ScrollRect restoredScroll = ui.ModalPanel.GetComponentInChildren<ScrollRect>();
            Assert.NotNull(restoredScroll);
            Assert.That(restoredScroll.verticalNormalizedPosition, Is.EqualTo(0.4f).Within(0.03f), "The rebuilt menu must retain the actual old scroll position.");
            ui.ShowSettings(); yield return RefreshUI();
            FindButton("Save settings").onClick.Invoke(); yield return RefreshUI();
            Assert.AreEqual(StreetGameState.Menu, game.State); Assert.IsFalse(ui.SettingsVisible);
            Assert.AreSame(mission, game.Mission); Assert.AreEqual(2, game.Mission.tier);
            Assert.AreEqual(2, game.SelectedVariant); Assert.AreEqual(2, StreetMissionProgress.BestStars(mission));
            Assert.AreEqual(1, StreetMissionProgress.CompletedVariantCount(mission)); Assert.AreEqual(writes, progress.Writes);
            Assert.NotNull(ui.RuntimeCanvas); Assert.NotNull(ui.RuntimeFont);
            Assert.NotNull(ui.ModalPanel); Assert.NotNull(ui.DestinationMarker);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
