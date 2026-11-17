using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ClawMachine.Tests.PlayMode
{
    public sealed class ClawCarouselPlayTests
    {
        private ClawGameManager game;
        private bool focusPause;
        private sealed class QuietInput : IClawInputSource { public ClawInputFrame Read() => default(ClawInputFrame); }
        [SetUp]
        public void Prepare() { focusPause = ClawGameManager.AutoPauseOnFocusLoss; ClawGameManager.AutoPauseOnFocusLoss = false; Time.timeScale = 1; }
        [TearDown]
        public void Restore() { if (game) game.ReturnToMenu(); Time.timeScale = 1; ClawGameManager.AutoPauseOnFocusLoss = focusPause; }

        [UnityTest, Category("Carousel_C04")]
        public IEnumerator Carousel_AimingActuallyMovesBodies_PauseFreezes_GrabBrakesWithoutTeleport()
        {
            const string scene = "Assets/ClawMachine/Scenes/ClawMachineScene.unity";
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("ClawMachineScene");
#endif
            game = Object.FindObjectOfType<ClawGameManager>(); Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            int index = -1;
            for (int i = 0; i < game.Challenges.Count; i++) if (game.Challenges[i].challengeId == "C04") index = i;
            Assert.GreaterOrEqual(index, 0); Assert.IsTrue(game.SelectChallenge(index)); game.StartGame();
            GraspTaskController task = game.Task; Assert.IsTrue(task.IsConfigured); Assert.IsTrue(task.Carousel.IsStaged);
            PrizePhysics target = task.Prize; Assert.AreEqual("cloud-bunny", target.PrizeId);
            Vector3 initial = target.Body.position;
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(Vector3.Distance(initial, target.Body.position), 0.005f, "The live Rigidbody must travel around the ring while aiming.");
            Assert.IsTrue(target.Body.isKinematic);
            float deck = task.Carousel.Platform.GetComponentInChildren<Collider>().bounds.max.y;
            foreach (PrizePhysics rider in task.Prizes) if (rider.gameObject.activeSelf)
                Assert.AreEqual(deck, rider.Collider.bounds.min.y, 0.002f, "Staged toys must rest on the visible platform, without floating or penetrating it.");
            game.Pause(true); Vector3 pausedPose = target.Body.position; float angle = task.Carousel.AngleDegrees;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(pausedPose, target.Body.position); Assert.AreEqual(angle, task.Carousel.AngleDegrees);
            Assert.AreEqual(0, task.Session.AttemptId);
            game.Pause(false);
            Vector3 beforeGrab = target.Body.position; Quaternion beforeRotation = target.Body.rotation;
            Assert.IsTrue(task.StartGrab());
            Assert.IsFalse(task.Carousel.IsStaged); Assert.AreEqual(1, task.Session.AttemptId);
            Assert.Less(Vector3.Distance(beforeGrab, target.Body.position), 0.0001f);
            Assert.Less(Quaternion.Angle(beforeRotation, target.Body.rotation), 0.001f);
            foreach (PrizePhysics prize in task.Prizes) if (prize.gameObject.activeSelf)
            { Assert.IsFalse(prize.Body.isKinematic); Assert.IsTrue(prize.Body.useGravity); }
            float brakedAngle = task.Carousel.AngleDegrees;
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(brakedAngle, task.Carousel.AngleDegrees);
            Assert.IsFalse(task.StartGrab(), "The turntable mode must obey the single-attempt gate.");
        }
    }
}
