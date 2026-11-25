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
            yield return null; // The avatar binds after the player camera is created.
            var eyes = game.Player.transform.Find("FirstPersonFullBody(StreetCrossing)/BodyRoot/EyeCenter");
            Assert.NotNull(eyes, "The active player must have the avatar eye anchor.");
            Assert.That(game.Player.View.transform.position.y, Is.EqualTo(eyes.position.y).Within(.005f));
            game.StartPractice(); yield return null;
            int lamps = 0;
            foreach (MeshRenderer lamp in Object.FindObjectsOfType<MeshRenderer>())
            {
                Transform mount = lamp.transform;
                while (mount && mount.name != "street_lamp_01 (imported)") mount = mount.parent;
                if (!mount) continue;
                lamps++;
                Vector3 size = lamp.bounds.size;
                Assert.That(size.y, Is.EqualTo(3.9f).Within(.05f), mount.position.ToString());
                Assert.LessOrEqual(Mathf.Max(size.x, size.z), 1.6f, "A sideways lamp spans the street.");
            }
            Assert.GreaterOrEqual(lamps, 2, "Expected the actual street lamps beside the crossing.");
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
