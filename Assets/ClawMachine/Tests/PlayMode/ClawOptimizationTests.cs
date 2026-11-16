using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ClawMachine.Tests.PlayMode
{
    public sealed class ClawOptimizationTests
    {
        private const string ScenePath = "Assets/ClawMachine/Scenes/ClawMachineScene.unity";
        private ClawGameManager game;
        private ClawGameUI ui;
        private string priorPreferences;
        private bool hadPreferences, priorFocusPause;
        private sealed class QuietInput : IClawInputSource { public ClawInputFrame Read() => default(ClawInputFrame); }

        [SetUp]
        public void IsolatePreferencesAndFocus()
        {
            hadPreferences = PlayerPrefs.HasKey(ClawPreferences.Key); priorPreferences = PlayerPrefs.GetString(ClawPreferences.Key, "");
            PlayerPrefs.DeleteKey(ClawPreferences.Key);
            priorFocusPause = ClawGameManager.AutoPauseOnFocusLoss; ClawGameManager.AutoPauseOnFocusLoss = false; Time.timeScale = 1;
        }
        [TearDown]
        public void RestorePreferencesAndTime()
        {
            if (game) { game.ReturnToMenu(); game.FlushSettings(); }
            if (hadPreferences) PlayerPrefs.SetString(ClawPreferences.Key, priorPreferences); else PlayerPrefs.DeleteKey(ClawPreferences.Key);
            PlayerPrefs.Save(); Time.timeScale = 1; ClawGameManager.AutoPauseOnFocusLoss = priorFocusPause;
        }

        [UnityTest]
        public IEnumerator Wheel_OnlyChangesMainObservationAndNeverPanelOrPip()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("PIP pointer exclusion needs a graphics device.");
            yield return Load();
            RectTransform panel = Field<RectTransform>(ui, "panel");
            int distance = game.Cameras.ObservationDistance;
            Assert.IsFalse(game.ZoomObservation(1, ScreenCenter(panel)));
            Assert.AreEqual(distance, game.Cameras.ObservationDistance);
            game.Cameras.SetPipEnabled(true); yield return null;
            Assert.IsTrue(game.Cameras.PipEnabled);
            Assert.IsFalse(game.ZoomObservation(1, ScreenCenter(Field<RectTransform>(ui, "pipFrame"))));
            Assert.AreEqual(distance, game.Cameras.ObservationDistance);
            Assert.IsTrue(game.ZoomObservation(1, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)));
            Assert.AreEqual(distance - 1, game.Cameras.ObservationDistance);
            Assert.IsFalse(game.ZoomObservation(-1, new Vector2(-10, -10)));
        }

        [UnityTest]
        public IEnumerator Settings_RestorePriorPauseAndPersistAllUserOptions()
        {
            yield return Load(); game.StartGame();
            game.OpenSettings(); Assert.IsTrue(game.IsPaused); game.CloseSettings(); Assert.IsFalse(game.IsPaused);
            game.Pause(true); game.OpenSettings(); game.CloseSettings(); Assert.IsTrue(game.IsPaused);
            game.Pause(false); game.RequestReset(); game.DeclineReset(); Assert.IsFalse(game.IsPaused);
            game.SetVolume(0.63f); game.SetTextScale(1.17f); game.SetPositionAssist(false); game.SetCenterOfMassAssist(false);
            game.SetCameraSmooth(false); game.SetPipQuality(false);
            // Saving uses unscaled time and must work while settings has paused physics.
            game.OpenSettings(); yield return new WaitForSecondsRealtime(0.6f);
            Assert.IsTrue(PlayerPrefs.HasKey(ClawPreferences.Key));
            game.CloseSettings();
            yield return Load();
            Assert.AreEqual(0.63f, game.Volume, 0.0001f); Assert.AreEqual(1.17f, game.TextScale, 0.0001f);
            Assert.IsFalse(game.PositionAssist); Assert.IsFalse(game.CenterOfMassAssist);
            Assert.IsFalse(game.Cameras.Smooth); Assert.IsFalse(game.Cameras.PipLowQuality);
            game.StartGame(); game.OpenSettings();
            ClawGameManager.AutoPauseOnFocusLoss = true;
            typeof(ClawGameManager).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { false });
            game.CloseSettings(); Assert.IsTrue(game.IsPaused, "Closing settings cannot undo a subsequent focus-loss pause.");
        }

        [UnityTest]
        public IEnumerator FailureFeedback_AppearsBeforeResultAndWithinPointThreeSeconds()
        {
            yield return Load(); game.StartGame();
            Assert.IsTrue(game.Task.Session.BeginAttempt());
            game.Task.Session.RecordFailure(GraspFailure.NoContact);
            float started = Time.realtimeSinceStartup;
            yield return null; yield return null;
            Assert.Less(Time.realtimeSinceStartup - started, 0.3f);
            StringAssert.Contains("接触不足", ui.FeedbackText);
            Assert.AreNotEqual(GraspState.Result, game.Task.Session.State);
            game.ConfirmReset(); Assert.IsTrue(game.Task.Session.BeginAttempt());
            game.Task.Session.RecordFailure(GraspFailure.Slipped);
            yield return null; yield return null;
            StringAssert.Contains("滑落", ui.FeedbackText);
            Assert.AreEqual(1, game.Task.Session.AttemptId);
            Assert.IsFalse(game.Task.Session.Awarded);
        }

        [UnityTest]
        public IEnumerator EmptyPhysicalGrab_VisitsSafeHomeBeforeResultAndCanBeResetWhileReturning()
        {
            yield return Load(); game.StartGame();
            GraspTaskController task = game.Task;
            Vector3 miss = task.TaskSpace.LocalBounds.min; miss.y = task.TaskSpace.LocalBounds.max.y;
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(miss), task.TaskSpace.Orientation)));
            Physics.SyncTransforms(); Assert.IsTrue(task.StartGrab());
            bool sawReturning = false, sawTransport = false;
            float deadline = Time.realtimeSinceStartup + 35;
            while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
            {
                sawReturning |= task.Session.State == GraspState.Returning;
                sawTransport |= task.Session.State == GraspState.Transporting || task.Session.State == GraspState.Releasing || task.Session.State == GraspState.Settling;
                Assert.AreEqual(1, task.Session.AttemptId); Assert.IsFalse(task.StartGrab());
                yield return null;
            }
            Assert.AreEqual(GraspState.Result, task.Session.State, task.Diagnostic);
            Assert.IsTrue(sawReturning); Assert.IsFalse(sawTransport);
            Assert.Less(Vector3.Distance(task.TaskSpace.HomePose.position, task.Driver.CurrentToolPose.position), 0.0081f);
            Assert.IsFalse(task.Session.Released); Assert.IsFalse(task.Session.Awarded);
            task.Retry(); Assert.AreEqual(1, task.Session.AttemptId);
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(miss), task.TaskSpace.Orientation)));
            Physics.SyncTransforms(); Assert.IsTrue(task.StartGrab());
            deadline = Time.realtimeSinceStartup + 35;
            while (task.Session.State != GraspState.Returning && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(GraspState.Returning, task.Session.State, task.Diagnostic);
            game.Pause(true); Vector3 frozen = task.Driver.CurrentToolPose.position;
            yield return new WaitForSecondsRealtime(0.1f); Assert.AreEqual(frozen, task.Driver.CurrentToolPose.position);
            int oldSession = task.Session.SessionId; game.ConfirmReset(); yield return null;
            Assert.Greater(task.Session.SessionId, oldSession); Assert.AreEqual(GraspState.Aiming, task.Session.State);
            Assert.AreEqual(0, task.Session.AttemptId); Assert.IsFalse(task.Session.Awarded);
            Assert.AreEqual(0, task.Gripper.GetComponents<FixedJoint>().Length);
        }

        [UnityTest]
        public IEnumerator Helpers_AreChallengeSpecificOptionalAndDoNotMoveTheExecutor()
        {
            yield return Load(); game.StartGame(); yield return null; yield return null;
            Pose before = game.Task.Driver.CurrentToolPose;
            Assert.IsTrue(ui.PositionMarkersVisible); Assert.IsFalse(ui.MassMarkerVisible);
            game.SetPositionAssist(false); yield return null; yield return null;
            Assert.IsFalse(ui.PositionMarkersVisible); Assert.AreEqual(before.position, game.Task.Driver.CurrentToolPose.position);
            Assert.AreEqual(0, game.Task.Session.AttemptId);
            game.ReturnToMenu(); Assert.IsTrue(game.SelectChallenge(1)); game.StartGame(); yield return null; yield return null;
            Assert.IsFalse(ui.PositionMarkersVisible); Assert.IsFalse(ui.MassMarkerVisible);
            game.ReturnToMenu(); Assert.IsTrue(game.SelectChallenge(2)); game.StartGame(); yield return null; yield return null;
            Assert.IsFalse(ui.PositionMarkersVisible); Assert.IsTrue(ui.MassMarkerVisible);
            game.SetCenterOfMassAssist(false); yield return null; yield return null;
            Assert.IsFalse(ui.MassMarkerVisible); Assert.AreEqual(0, game.Task.Session.AttemptId);
        }

        [UnityTest]
        public IEnumerator ReboundActionNodes_DoNotTransferKeyboardFocusBetweenGrabHelpAndRetry()
        {
            yield return Load(); game.StartGame(); yield return null; yield return null;
            // Freeze native stepping while exercising legal session transitions;
            // UI continues to update using unscaled time.
            Time.timeScale = 0;
            ScrollRect scroll = Field<ScrollRect>(ui, "panelScroll");
            Button grab = scroll.content.GetComponentInChildren<Button>();
            StringAssert.Contains("开始抓取", grab.name);
            EventSystem.current.SetSelectedGameObject(grab.gameObject);
            Assert.IsTrue(game.Task.Session.BeginAttempt());
            yield return null; yield return null;
            Button helpAction = scroll.content.GetComponentInChildren<Button>();
            Assert.AreSame(grab, helpAction, "The node is reused, but its action has changed.");
            StringAssert.Contains("抓持提示", helpAction.name);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject, "Grab focus cannot become Help focus and intercept the next Enter.");

            EventSystem.current.SetSelectedGameObject(helpAction.gameObject);
            foreach (GraspState next in new[] { GraspState.ContactDescent, GraspState.Closing, GraspState.Returning, GraspState.Result })
                Assert.IsTrue(game.Task.Session.Advance(next));
            yield return null; yield return null;
            Button retry = scroll.content.GetComponentInChildren<Button>();
            Assert.AreSame(helpAction, retry); StringAssert.Contains("再试", retry.name);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject, "Help focus cannot become Retry focus solely because the node survived.");

            EventSystem.current.SetSelectedGameObject(retry.gameObject);
            retry.onClick.Invoke(); yield return null; yield return null;
            Assert.AreEqual(GraspState.Aiming, game.Task.Session.State);
            Assert.AreEqual(1, game.Task.Session.AttemptId);
            StringAssert.Contains("开始抓取", scroll.content.GetComponentInChildren<Button>().name);
            Assert.IsNull(EventSystem.current.currentSelectedGameObject, "Retry focus cannot silently become another Grab action.");
        }

        [UnityTest]
        public IEnumerator SettingsToggle_ChangesCaptionButKeepsTheSameActionFocus()
        {
            yield return Load(); game.OpenSettings(); yield return null; yield return null;
            Button toggle = null;
            foreach (Button button in Field<ScrollRect>(ui, "panelScroll").content.GetComponentsInChildren<Button>())
                if (button.name.StartsWith("镜头平滑")) { toggle = button; break; }
            Assert.NotNull(toggle);
            string before = toggle.name;
            EventSystem.current.SetSelectedGameObject(toggle.gameObject);
            toggle.onClick.Invoke(); yield return null; yield return null;
            Assert.AreNotEqual(before, toggle.name);
            Assert.AreEqual(toggle.gameObject, EventSystem.current.currentSelectedGameObject,
                "A persistent semantic action should keep focus when only its value label changes.");
        }

        [UnityTest]
        public IEnumerator ReusedPanels_KeepScrollFocusAndBoundNodeCountAcrossThirtyTransitions()
        {
            yield return Load();
            game.OpenSettings(); yield return null; yield return null;
            ScrollRect scroll = Field<ScrollRect>(ui, "panelScroll");
            scroll.verticalNormalizedPosition = 0.35f;
            Button selected = scroll.content.GetComponentInChildren<Button>(); Assert.NotNull(selected);
            EventSystem.current.SetSelectedGameObject(selected.gameObject);
            game.SetPositionAssist(false); yield return null; yield return null;
            Assert.AreEqual(0.35f, scroll.verticalNormalizedPosition, 0.02f);
            Assert.AreEqual(selected.gameObject, EventSystem.current.currentSelectedGameObject);
            game.CloseSettings(); game.StartGame(); yield return null;
            ui.SetDetailsExpanded(true); yield return null; game.Pause(true); yield return null;
            game.OpenSettings(); yield return null; game.CloseSettings(); game.Pause(false); game.RequestReset(); yield return null;
            game.DeclineReset(); yield return null; ui.SetDetailsExpanded(false); yield return null;
            int warmed = ui.PooledNodeCount;
            for (int i = 0; i < 30; i++)
            {
                ui.SetDetailsExpanded(true); yield return null;
                game.Pause(true); yield return null;
                game.OpenSettings(); yield return null;
                game.CloseSettings(); game.Pause(false); game.RequestReset(); yield return null;
                game.DeclineReset(); ui.SetDetailsExpanded(false); yield return null;
            }
            Assert.AreEqual(warmed, ui.PooledNodeCount, "Phase/menu transitions reuse their existing nodes.");
            Assert.AreEqual(1, ui.GetComponentsInChildren<Canvas>(true).Length);
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator Load()
        {
            if (game) { game.ReturnToMenu(); game.FlushSettings(); }
            Time.timeScale = 1;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("ClawMachineScene", LoadSceneMode.Single);
#endif
            yield return null;
            game = Object.FindObjectOfType<ClawGameManager>(); ui = Object.FindObjectOfType<ClawGameUI>();
            Assert.NotNull(game); Assert.NotNull(ui); Assert.IsTrue(ui.IsReady, ui.BootstrapError);
            game.SetInputSource(new QuietInput());
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }
        private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static Vector2 ScreenCenter(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }
}
