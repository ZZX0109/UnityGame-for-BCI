using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class StreetSceneSmokeTests
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
        [UnityTest]
        public IEnumerator S01BuildsFirstPersonStreetPausesAndResetsWithoutDuplicateCars()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            Assert.That(game, Is.Not.Null); game.SetInputSource(new QuietInput());
            Assert.That(game.State, Is.EqualTo(StreetGameState.Menu));
            Assert.That(game.Player.View.transform.localPosition.y, Is.EqualTo(1.65f).Within(0.001f));
            game.StartPractice(); yield return null;
            Assert.That(game.Traffic.Active.Count, Is.EqualTo(2));
            game.Pause(); float frozen = game.WorldTime; TrafficPhase phase = game.Intersection.Phase;
            Vector3 position = game.Traffic.Active[0].transform.position;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.WorldTime, Is.EqualTo(frozen)); Assert.That(game.Intersection.Phase, Is.EqualTo(phase));
            Assert.That(game.Traffic.Active[0].transform.position, Is.EqualTo(position));
            for (int i = 0; i < 20; i++) { game.StartPractice(); game.Pause(); }
            Assert.That(game.Traffic.Active.Count, Is.EqualTo(2));
            Assert.That(Object.FindObjectsOfType<VehicleController>().Length, Is.EqualTo(2));
            Assert.That(game.Evaluation.Attempts, Is.Zero);
            Assert.That(game.Intersection.Phase, Is.EqualTo(TrafficPhase.VehicleGreen));
            Assert.That(Vector3.Distance(game.PlayerPosition, game.scenario.playerSpawn), Is.LessThan(0.02f));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
