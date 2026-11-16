using System.Collections;
using FirstPersonSokoban.Core;
using FirstPersonSokoban.SaveSystem;
using FirstPersonSokoban.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FirstPersonSokoban.Tests.PlayMode
{
    public sealed class SessionProtectionTests
    {
        private ISaveStore priorSave;
        private ISaveStore priorSettings;
        private bool priorFocus;
        private PuzzleGameManager game;
        private GameUIController ui;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            priorSave = SaveManager.Store;
            priorSettings = GameSettings.Store;
            priorFocus = PuzzleGameManager.AutoPauseOnFocusLoss;
            SaveManager.Store = new InMemorySaveStore();
            SaveManager.Store.SetInt("FPSokoban.v2.unlocked", 8);
            GameSettings.Store = new InMemorySaveStore();
            PuzzleGameManager.AutoPauseOnFocusLoss = false;
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync("PuzzleScene");
            yield return null;
            game = Object.FindObjectOfType<PuzzleGameManager>();
            ui = Object.FindObjectOfType<GameUIController>();
            Assert.NotNull(game);
            Assert.NotNull(ui);
        }

        [TearDown]
        public void TearDown()
        {
            if (game != null) game.ShowMenu();
            Time.timeScale = 1f;
            SaveManager.Store = priorSave;
            GameSettings.Store = priorSettings;
            PuzzleGameManager.AutoPauseOnFocusLoss = priorFocus;
        }

        private IEnumerator StartParkingAndPush()
        {
            game.StartLevel(5);
            yield return new WaitForFixedUpdate();
            game.Loader.Player.SetLookRotation(0f, 0f);
            game.TryPush();
            Assert.AreEqual(GamePhase.Animating, game.Phase);
            yield return FinishPush();
            Assert.AreEqual(GamePhase.Playing, game.Phase);
        }

        private IEnumerator FinishPush()
        {
            float end = Time.realtimeSinceStartup + 8f;
            while (game.Phase == GamePhase.Animating && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreNotEqual(GamePhase.Animating, game.Phase);
        }

        [UnityTest]
        public IEnumerator MenuContinue_PreservesBoardUndoPoseClockAndDistance()
        {
            yield return StartParkingAndPush();
            game.Loader.Player.SetLookRotation(75f, -22f);
            game.Pause();
            BoardState board = game.Board;
            Vector3 position = game.Loader.Player.transform.position;
            float elapsed = game.ElapsedSeconds;
            float distance = game.Loader.Player.DistanceTravelled;
            int history = game.UndoDepth;
            game.ShowMenu();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(elapsed, game.ElapsedSeconds);
            Assert.IsTrue(game.CanContinueSession);
            game.ContinueSession();
            Assert.AreSame(board, game.Board);
            Assert.AreEqual(position, game.Loader.Player.transform.position);
            Assert.AreEqual(75f, game.Loader.Player.LookYaw, 0.01f);
            Assert.AreEqual(-22f, game.Loader.Player.LookPitch, 0.01f);
            Assert.AreEqual(elapsed, game.ElapsedSeconds);
            Assert.AreEqual(distance, game.Loader.Player.DistanceTravelled);
            Assert.AreEqual(history, game.UndoDepth);
            Assert.IsFalse(game.Loader.Player.InputLocked);
            game.Undo();
            Assert.AreEqual(0, game.Board.EffectivePushCount);
            Assert.AreEqual(1, game.Board.UndoCount);
        }

        [UnityTest]
        public IEnumerator MenuDuringPush_RollsBackOnlyPendingTransaction()
        {
            game.StartLevel(5);
            yield return new WaitForFixedUpdate();
            var player = game.Loader.Player;
            player.SetLookRotation(0f, 0f);
            Vector2Int box = game.Board.Boxes[0];
            Vector3 position = player.transform.position;
            float elapsed = game.ElapsedSeconds;
            float distance = player.DistanceTravelled;
            game.TryPush();
            Assert.AreEqual(GamePhase.Animating, game.Phase);
            yield return null;
            yield return null;
            game.Pause();
            game.ShowMenu();
            Assert.AreEqual(box, game.Board.Boxes[0]);
            Assert.AreEqual(0, game.Board.PushCount);
            Assert.AreEqual(0, game.UndoDepth);
            Assert.Less(Vector3.Distance(position, player.transform.position), 0.001f);
            Assert.AreEqual(distance, player.DistanceTravelled, 0.001f);
            Assert.AreEqual(elapsed, game.ElapsedSeconds, 0.001f);
            Assert.IsFalse(game.HasActivePush);
            game.ContinueSession();
            player.SetLookRotation(0f, 0f);
            game.TryPush();
            yield return FinishPush();
            Assert.AreEqual(1, game.Board.EffectivePushCount);
        }

        [UnityTest]
        public IEnumerator DiscardDialogs_CancelPreservesMenuAndPausedState_ConfirmResets()
        {
            yield return StartParkingAndPush();
            BoardState board = game.Board;
            game.ShowMenu();
            game.RequestStartLevel(1);
            Assert.IsTrue(ui.ConfirmationOpen);
            ui.CancelConfirmation();
            Assert.AreEqual(GamePhase.Menu, game.Phase);
            Assert.AreSame(board, game.Board);
            game.ContinueSession();
            game.Pause();
            game.RequestMenuRestart();
            Assert.IsTrue(ui.ConfirmationOpen);
            game.HandleEscape();
            Assert.AreEqual(GamePhase.Paused, game.Phase);
            Assert.AreSame(board, game.Board);
            game.RequestExit();
            Assert.IsTrue(ui.ConfirmationOpen);
            ui.CancelConfirmation();
            Assert.AreEqual(GamePhase.Paused, game.Phase);
            game.RequestMenuRestart();
            ui.ConfirmPendingAction();
            Assert.AreNotSame(board, game.Board);
            Assert.AreEqual(0, game.Board.PushCount);
            Assert.AreEqual(0, game.UndoDepth);
            Assert.AreEqual(GamePhase.Playing, game.Phase);
        }

        [UnityTest]
        public IEnumerator FirstMechanismCard_IsSkippableUntimedAndOnlyOncePerLevel()
        {
            game.RequestStartLevel(1);
            Assert.IsTrue(ui.MechanismOpen);
            Assert.AreEqual(GamePhase.Paused, game.Phase);
            float elapsed = game.ElapsedSeconds;
            yield return null;
            yield return null;
            Assert.AreEqual(elapsed, game.ElapsedSeconds);
            game.HandleEscape();
            Assert.IsFalse(ui.MechanismOpen);
            Assert.AreEqual(GamePhase.Playing, game.Phase);
            game.ShowMenu();
            game.RequestStartLevel(1);
            Assert.IsFalse(ui.MechanismOpen);
            yield return null;
            game.RequestStartLevel(2);
            Assert.IsTrue(ui.ConfirmationOpen);
            ui.ConfirmPendingAction();
            Assert.IsTrue(ui.MechanismOpen);
            Assert.AreEqual(2, game.CurrentLevelIndex);
            ui.DismissMechanism();
        }

        [UnityTest]
        public IEnumerator CancelDiscardDuringAnimation_ResumesSamePendingPush()
        {
            game.StartLevel(5);
            yield return new WaitForFixedUpdate();
            game.Loader.Player.SetLookRotation(0f, 0f);
            game.TryPush();
            Assert.IsTrue(game.HasActivePush);
            BoardState board = game.Board;
            game.RequestMenuRestart();
            Assert.IsTrue(ui.ConfirmationOpen);
            Assert.AreEqual(GamePhase.Paused, game.Phase);
            Assert.IsTrue(game.HasActivePush);
            ui.CancelConfirmation();
            Assert.AreSame(board, game.Board);
            Assert.AreEqual(GamePhase.Animating, game.Phase);
            Assert.IsTrue(game.Loader.Player.InputLocked);
            yield return FinishPush();
            Assert.AreEqual(1, game.Board.PushCount);
            Assert.AreEqual(1, game.UndoDepth);
        }

        [UnityTest]
        public IEnumerator FirstDeadlockExplanation_CanCloseAndDoesNotRepeatAfterUndo()
        {
            game.StartLevel(6);
            yield return new WaitForFixedUpdate();
            var player = game.Loader.Player;
            player.Teleport(new Vector3(6f, 0.04f, 12f));
            player.SetLookRotation(90f, 0f);
            Physics.SyncTransforms();
            yield return null;
            game.TryPush();
            yield return FinishPush();
            Assert.AreEqual(new Vector2Int(5, 6), game.Board.Boxes[0]);
            player.Teleport(new Vector3(10f, 0.04f, 10f));
            player.SetLookRotation(0f, 0f);
            Physics.SyncTransforms();
            yield return null;
            game.TryPush();
            yield return FinishPush();
            Assert.IsTrue(game.DeadlockActive);
            Assert.IsTrue(ui.DeadlockExplanationOpen);
            game.HandleEscape();
            Assert.IsFalse(ui.DeadlockExplanationOpen);
            Assert.AreEqual(GamePhase.Playing, game.Phase);
            game.Undo();
            Assert.IsFalse(game.DeadlockActive);
            Physics.SyncTransforms();
            yield return null;
            game.TryPush();
            yield return FinishPush();
            Assert.IsTrue(game.DeadlockActive);
            Assert.IsFalse(ui.DeadlockExplanationOpen);
        }

        [UnityTest]
        public IEnumerator AssistanceCanBeDisabledWithoutChangingPushRules()
        {
            game.StartLevel(1);
            yield return new WaitForFixedUpdate();
            var player = game.Loader.Player;
            // The box at (3,4) has a wall directly north. Stand east of it:
            // pushing west is legal, so only the lateral offset may deny it.
            player.Teleport(new Vector3(8f, 0.04f, 8.65f));
            Vector3 to = new Vector3(6f, 0.9f, 8f) - player.PlayerCamera.transform.position;
            player.SetLookRotation(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
            Physics.SyncTransforms();
            yield return null;
            Assert.IsTrue(game.Loader.Interactor.EvaluateAim().HasBox);
            Assert.AreEqual(PushDenyReason.StanceOffset, PushDiagnostics.Evaluate(
                game.Board, new Vector2Int(3, 4), new Vector2Int(4, 4), false).Reason);
            Assert.IsNotEmpty(ui.CurrentMovementAssist);
            game.TryPush();
            Assert.AreEqual(0, game.Board.PushCount);
            game.SetAssistanceEnabled(false);
            Assert.IsEmpty(ui.CurrentMovementAssist);
            game.TryPush();
            Assert.AreEqual(0, game.Board.PushCount);
            player.Teleport(new Vector3(8f, 0.04f, 8f));
            player.SetLookRotation(270f, 0f);
            Physics.SyncTransforms();
            yield return null;
            game.TryPush();
            yield return FinishPush();
            Assert.AreEqual(1, game.Board.PushCount);
            Assert.IsEmpty(ui.CurrentMovementAssist);
        }

        [UnityTest]
        public IEnumerator FinalLevelMessage_CountsActualCurrentRevisionCompletions()
        {
            SaveManager.RecordCompletion(7, 32, 60f, 8);
            ui.ShowVictory(7, 8, 60f, 32, 32, 0, 100f, 2, 2);
            StringAssert.DoesNotContain("全部关卡已完成", ui.VictorySummary);
            for (int i = 0; i < 7; i++) SaveManager.RecordCompletion(i, 40, 90f, 8);
            ui.ShowVictory(7, 8, 60f, 32, 32, 0, 100f, 2, 2);
            StringAssert.Contains("全部关卡已完成", ui.VictorySummary);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GeneratedGalleryLabelsUseDepthMaterialAndCrateFacesPointOutward()
        {
            game.StartLevel(7);
            yield return null;
            yield return null;
            TextMesh[] labels = game.Loader.LevelRoot.GetComponentsInChildren<TextMesh>();
            Assert.Greater(labels.Length, 8, "The actual room must include crates and wall captions.");
            foreach (TextMesh label in labels)
                Assert.AreEqual("FirstPersonSokoban/GalleryWorldText", label.GetComponent<Renderer>().sharedMaterial.shader.name,
                    label.name + " must obey scene depth.");
            foreach (var box in game.Loader.Boxes)
            foreach (TextMesh label in box.GetComponentsInChildren<TextMesh>())
                Assert.Greater(Vector3.Dot(-label.transform.forward, label.transform.position - box.transform.position), .8f,
                    "Crate number fronts must face away from the solid crate.");
        }

        [UnityTest]
        public IEnumerator SearchGeneration_CancelledOnPauseMenuRestartAndSwitch()
        {
            game.StartLevel(7);
            game.ShowMenu();
            game.ContinueSession();
            int generation = game.DeadlockGeneration;
            Assert.IsTrue(game.DeadlockSearchPending);
            game.Pause();
            Assert.IsFalse(game.DeadlockSearchPending);
            Assert.Greater(game.DeadlockGeneration, generation);
            game.Resume();
            Assert.IsTrue(game.DeadlockSearchPending);
            game.ShowMenu();
            Assert.IsFalse(game.DeadlockSearchPending);
            game.ContinueSession();
            generation = game.DeadlockGeneration;
            game.RestartLevel();
            Assert.Greater(game.DeadlockGeneration, generation);
            Assert.IsFalse(game.DeadlockSearchPending);
            game.StartLevel(0);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(0, game.CurrentLevelIndex);
            Assert.IsFalse(game.DeadlockActive);
        }
    }
}
