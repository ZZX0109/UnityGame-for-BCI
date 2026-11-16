using System;
using System.Collections;
using BCI.RuntimeValidation;
using FirstPersonSokoban.Core;
using FirstPersonSokoban.SaveSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstPersonSokoban.Validation
{
    public sealed class SokobanRuntimeAudit : IGameRuntimeAuditAdapter
    {
        [Serializable] private sealed class Snapshot
        {
            public string levelId, phase;
            public string benchmarkSelection = "L08 has the largest grid (160 cells, 77 walls); active static puzzle view, no automated solution during frame sampling.";
            public int levelIndex, restartOperations, switchOperations;
            public bool isolatedStores, rawMovementDisabled;
        }
        private PuzzleGameManager game;
        private ISaveStore priorSaves, priorSettings;
        private bool isolated, priorFocus, priorControls;
        private float priorTimeScale;
        private int restarts, switches;

        public string GameId => "sokoban";
        public string SnapshotJson => JsonUtility.ToJson(new Snapshot
        {
            levelId = game == null ? "not_loaded" : LevelCatalog.Get(game.CurrentLevelIndex).Id,
            phase = game == null ? "not_loaded" : game.Phase.ToString(),
            levelIndex = game == null ? -1 : game.CurrentLevelIndex,
            restartOperations = restarts, switchOperations = switches, isolatedStores = isolated,
            rawMovementDisabled = game != null && game.Loader.Player != null && !game.Loader.Player.enabled
        });

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var adapter = new SokobanRuntimeAudit();
            if (GameRuntimeAudit.TryStart(adapter)) adapter.Isolate();
        }

        private void Isolate()
        {
            if (isolated) return;
            priorSaves = SaveManager.Store; priorSettings = GameSettings.Store;
            priorFocus = PuzzleGameManager.AutoPauseOnFocusLoss; priorTimeScale = Time.timeScale;
            SaveManager.Store = new InMemorySaveStore(); GameSettings.Store = new InMemorySaveStore();
            SaveManager.Store.SetInt("FPSokoban.v2.unlocked", LevelCatalog.Count);
            PuzzleGameManager.AutoPauseOnFocusLoss = false;
            isolated = true;
        }

        public IEnumerator Prepare()
        {
            Isolate();
            game = Object.FindObjectOfType<PuzzleGameManager>();
            if (game == null) throw new InvalidOperationException("PuzzleScene did not create its game manager.");
            if (switches == 0 && restarts == 0) priorControls = game.ControlsEnabled;
            game.ControlsEnabled = false;
            Time.timeScale = 1f;
            game.StartLevel(7);
            FreezeRawInput();
            yield return null;
            yield return null;
        }

        public void TickActive()
        {
            if (game == null || game.Phase != GamePhase.Playing)
                throw new InvalidOperationException("The Sokoban audit must sample the active L08 puzzle, not a menu or paused scene.");
            FreezeRawInput();
        }

        public IEnumerator RebuildAndRestart(int iteration)
        {
            game.ShowMenu();
            game.StartLevel(iteration % LevelCatalog.Count);
            switches++;
            game.RestartLevel();
            restarts++;
            FreezeRawInput();
            yield return null;
        }

        private void FreezeRawInput()
        {
            game.Loader.Player.SetInputLocked(true);
            game.Loader.Player.enabled = false;
            game.Loader.Player.SetLookPitch(18f);
            game.ControlsEnabled = false;
        }

        public void Restore()
        {
            if (!isolated) return;
            if (game != null)
            {
                game.ShowMenu(); game.ControlsEnabled = priorControls;
                if (game.Loader.Player != null) game.Loader.Player.enabled = true;
            }
            SaveManager.Store = priorSaves; GameSettings.Store = priorSettings;
            PuzzleGameManager.AutoPauseOnFocusLoss = priorFocus; Time.timeScale = priorTimeScale;
            isolated = false;
        }
    }
}
