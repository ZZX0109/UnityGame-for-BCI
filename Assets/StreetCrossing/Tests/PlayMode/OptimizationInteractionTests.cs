using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Player;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class OptimizationInteractionTests
    {
        [UnityTest]
        public IEnumerator MovingCollisionKeepsTerminalNoticeAfterCrossingEndEvent()
        {
            yield return SceneManager.LoadSceneAsync("Assets/StreetCrossing/Scenes/StreetCrossingScene.unity");
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            bool previous = game.AutoPauseOnFocusLoss;
            game.AutoPauseOnFocusLoss = false;
            try
            {
                game.StartPractice();
                // Event integration check. Native physical contact is covered
                // separately; this exercises Collision -> CrossingEnd ordering.
                game.Evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.VehicleGreen, false, false, .02f);
                game.RegisterContact(123, 2f);
                Assert.AreEqual(StreetGameState.Result, game.State);
                Assert.IsTrue(game.Evaluation.Finished);
                Assert.AreEqual(game.Evaluation.Feedback, game.Notice);
                StringAssert.Contains("碰撞", game.Notice);
                game.StartPractice();
                Assert.AreEqual("", game.Notice);
                game.Pause();
            }
            finally { game.AutoPauseOnFocusLoss = previous; }
        }

        [UnityTest]
        public IEnumerator InspectionCannotMaskARouteViolationAndRestartClearsMessages()
        {
            yield return SceneManager.LoadSceneAsync("Assets/StreetCrossing/Scenes/StreetCrossingScene.unity");
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            game.AutoPauseOnFocusLoss = false; game.StartPractice();
            var signObject = new GameObject("test sign");
            try
            {
                var sign = signObject.AddComponent<StreetInspectable>(); sign.title = "Sign"; sign.description = "Information";
                game.Inspect(sign);
                Assert.That(game.Notice, Does.Contain("Information"));
                game.Evaluation.Tick(CrossingRegion.RoadwayHazard, TrafficPhase.VehicleGreen, false, false, .02f);
                Assert.That(game.Notice, Does.Not.Contain("Information"));
                Assert.That(game.Notice, Does.Contain("路线"));
                game.StartPractice(); Assert.AreEqual("", game.Notice);
                game.Pause();
            }
            finally { Object.Destroy(signObject); game.AutoPauseOnFocusLoss = true; }
        }
    }
}
