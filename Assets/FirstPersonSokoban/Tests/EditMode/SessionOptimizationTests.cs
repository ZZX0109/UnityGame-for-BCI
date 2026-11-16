using FirstPersonSokoban.Core;
using FirstPersonSokoban.SaveSystem;
using NUnit.Framework;
using UnityEngine;

namespace FirstPersonSokoban.Tests.EditMode
{
    public sealed class SessionOptimizationTests
    {
        [TestCase(0, 1)] [TestCase(1, 7)] [TestCase(2, 22)] [TestCase(3, 12)]
        [TestCase(4, 4)] [TestCase(5, 10)] [TestCase(6, 5)] [TestCase(7, 32)]
        public void TimeSlicedSolver_PreservesMinimumPushes(int index, int expected)
        {
            LevelEntry entry = LevelCatalog.Get(index);
            LevelDefinition level = LevelValidator.ParseAndValidate(
                Resources.Load<TextAsset>("Levels/" + entry.Resource).text, entry.Title, entry.InitialYaw);
            var session = new SolverSession(level, level.Boxes, level.PlayerStart, 100000);
            int slices = 0;
            // An effectively zero budget must still make progress and preserve
            // frontier/visited state rather than reporting an unknown solution.
            while (!session.IsDone && slices++ < 100001) session.Step(200, 0.000001);
            Assert.IsTrue(session.IsDone);
            Assert.AreEqual(SolveStatus.Solved, session.Result.Status);
            Assert.AreEqual(expected, session.Result.MinimumPushes);
        }

        [Test]
        public void StateBudgetExhaustion_RemainsUnknownWithTimeSlicing()
        {
            LevelDefinition level = LevelValidator.ParseAndValidate(
                "#######\n#.....#\n#..G..#\n#..B..#\n#..P..#\n#.....#\n#######", "budget", 180f);
            var session = new SolverSession(level, level.Boxes, level.PlayerStart, 1);
            while (!session.IsDone) session.Step(1, 2.0);
            Assert.AreEqual(SolveStatus.SearchLimitReached, session.Result.Status);
            Assert.AreEqual(DeadlockStatus.Unknown, DeadlockDetector.Classify(session.Result).Status);
        }

        [Test]
        public void CompletedCount_UsesCurrentRevisionsInsteadOfLegacyUnlocks()
        {
            ISaveStore previous = SaveManager.Store;
            try
            {
                var store = new InMemorySaveStore();
                SaveManager.Store = store;
                store.SetInt("FPSokoban.v2.unlocked", 8);
                for (int i = 1; i < 8; i++)
                    store.SetInt("FPSokoban.v2.best." + LevelCatalog.GetId(i), 1);
                SaveManager.RecordCompletion(7, 32, 60f, 8);
                Assert.AreEqual(1, SaveManager.CompletedCount(8));
                for (int i = 0; i < 7; i++) SaveManager.RecordCompletion(i, 40, 90f, 8);
                Assert.AreEqual(8, SaveManager.CompletedCount(8));
            }
            finally { SaveManager.Store = previous; }
        }
    }
}
