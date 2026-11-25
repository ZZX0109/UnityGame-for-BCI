using System.Collections;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Presentation;
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
            // 全身接线链每次场景加载会确定性输出 7 条一次性 info 日志（良性诊断），
            // 在加载前显式登记为预期，保持 NoUnexpectedReceived 的真实纪律。
            FullBodyWiringLogs.ExpectWiringSequence();
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            Assert.That(game, Is.Not.Null); game.AutoPauseOnFocusLoss = false; game.SetInputSource(new QuietInput());
            Assert.That(game.State, Is.EqualTo(StreetGameState.Menu));
            Assert.That(Object.FindObjectsOfType<BCI.RuntimeValidation.GameRuntimeAudit>().Length, Is.Zero,
                "Normal scene loading must not create the opt-in Player auditor.");
            int quality = QualitySettings.GetQualityLevel(), vSync = QualitySettings.vSyncCount, target = Application.targetFrameRate;
            Assert.IsFalse(BCI.RuntimeValidation.GameRuntimeAudit.TryStart(new StreetCrossing.Validation.StreetRuntimeAudit()),
                "Without --bci-audit-output the shared bootstrap must stay inert.");
            Assert.That(Object.FindObjectsOfType<BCI.RuntimeValidation.GameRuntimeAudit>().Length, Is.Zero);
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(quality));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(vSync));
            Assert.That(Application.targetFrameRate, Is.EqualTo(target));
            Assert.That(game.Player.View.transform.localPosition.y, Is.EqualTo(1.65f).Within(0.001f));
            game.StartPractice(); yield return null;
            Assert.That(game.Traffic.Active.Count, Is.EqualTo(4));
            game.Pause(); float frozen = game.WorldTime; TrafficPhase phase = game.Intersection.Phase;
            Vector3 position = game.Traffic.Active[0].transform.position;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(game.WorldTime, Is.EqualTo(frozen)); Assert.That(game.Intersection.Phase, Is.EqualTo(phase));
            Assert.That(game.Traffic.Active[0].transform.position, Is.EqualTo(position));
            for (int i = 0; i < 20; i++) { game.StartPractice(); game.Pause(); }
            Assert.That(game.Traffic.Active.Count, Is.EqualTo(4));
            Assert.That(Object.FindObjectsOfType<VehicleController>().Length, Is.EqualTo(4));
            Assert.That(game.Evaluation.Attempts, Is.Zero);
            Assert.That(game.Intersection.Phase, Is.EqualTo(TrafficPhase.VehicleGreen));
            Assert.That(Vector3.Distance(game.PlayerPosition, StreetMissionGeometry.Spawn(game.Mission, 0)), Is.LessThan(0.02f));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
