using FirstPersonSokoban.Core;
using NUnit.Framework;
using UnityEngine;

namespace FirstPersonSokoban.Tests.EditMode
{
    public sealed class LevelSolvabilityTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 3)]
        public void ShippedLevels_AreSolvableWithExpectedMinimumPushes(int levelIndex, int expectedPushes)
        {
            TextAsset asset = Resources.Load<TextAsset>($"Levels/Level{levelIndex + 1:00}");
            Assert.NotNull(asset);
            LevelDefinition level = LevelValidator.ParseAndValidate(asset.text, asset.name);
            SolveResult result = LevelSolver.Solve(level, 100000);
            Assert.AreEqual(SolveStatus.Solved, result.Status);
            Assert.AreEqual(expectedPushes, result.MinimumPushes);
            Assert.Greater(result.ExploredStates, 0);
        }

        [Test]
        public void Solver_DistinguishesSearchLimitFromUnsolvable()
        {
            TextAsset asset = Resources.Load<TextAsset>("Levels/Level03");
            LevelDefinition level = LevelValidator.ParseAndValidate(asset.text, asset.name);
            SolveResult result = LevelSolver.Solve(level, 1);
            Assert.AreEqual(SolveStatus.SearchLimitReached, result.Status);
        }
    }
}
