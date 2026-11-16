using System.Collections;
using BCI.RuntimeValidation;
using FirstPersonSokoban.Core;
using FirstPersonSokoban.SaveSystem;
using FirstPersonSokoban.Validation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FirstPersonSokoban.Tests.PlayMode
{
    public sealed class RuntimeAuditAdapterTests
    {
        [UnityTest]
        public IEnumerator OrdinaryStartupHasNoAuditRunnerAndOptInAdapterRestoresIsolatedState()
        {
            ISaveStore originalSaves = SaveManager.Store, originalSettings = GameSettings.Store;
            bool originalFocus = PuzzleGameManager.AutoPauseOnFocusLoss;
            var save = new InMemorySaveStore(); var settings = new InMemorySaveStore();
            SaveManager.Store = save; GameSettings.Store = settings;
            PuzzleGameManager.AutoPauseOnFocusLoss = false;
            var adapter = new SokobanRuntimeAudit();
            try
            {
                yield return SceneManager.LoadSceneAsync("PuzzleScene");
                yield return null;
                Assert.IsEmpty(Object.FindObjectsOfType<GameRuntimeAudit>(), "Normal startup must not create an audit driver.");
                PuzzleGameManager game = Object.FindObjectOfType<PuzzleGameManager>();
                Assert.IsTrue(game.ControlsEnabled);
                yield return adapter.Prepare();
                Assert.AreNotSame(save, SaveManager.Store);
                Assert.AreNotSame(settings, GameSettings.Store);
                Assert.AreEqual(7, game.CurrentLevelIndex);
                Assert.AreEqual(GamePhase.Playing, game.Phase);
                Assert.IsFalse(game.ControlsEnabled);
                Assert.IsFalse(game.Loader.Player.enabled);
                BoardState previous = game.Board;
                yield return adapter.RebuildAndRestart(1);
                Assert.AreEqual(1, game.CurrentLevelIndex);
                Assert.AreNotSame(previous, game.Board);
                adapter.TickActive();
                yield return adapter.Prepare();
                Assert.AreEqual(7, game.CurrentLevelIndex);
                adapter.Restore();
                Assert.AreSame(save, SaveManager.Store);
                Assert.AreSame(settings, GameSettings.Store);
                Assert.IsTrue(game.ControlsEnabled);
                Assert.IsTrue(game.Loader.Player.enabled);
            }
            finally
            {
                adapter.Restore(); SaveManager.Store = originalSaves; GameSettings.Store = originalSettings;
                PuzzleGameManager.AutoPauseOnFocusLoss = originalFocus; Time.timeScale = 1f;
            }
        }
    }
}
