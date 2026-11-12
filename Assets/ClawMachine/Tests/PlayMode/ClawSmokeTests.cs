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
    public sealed class ClawSmokeTests
    {
        private const string ScenePath = "Assets/ClawMachine/Scenes/ClawMachineScene.unity";
        private ClawGameManager game;
        private sealed class QuietInput : IClawInputSource { public ClawInputFrame Read() => default(ClawInputFrame); }

        [TearDown]
        public void RestoreTimeAndReleaseScene()
        {
            if (game) game.ReturnToMenu();
            Time.timeScale = 1;
        }

        [UnityTest]
        public IEnumerator AlignedC01_ReallyGripsReleasesAndSettlesInChute()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            GraspTaskController task = game.Task;
            Assert.IsTrue(task.IsConfigured);
            game.SetInputSource(new QuietInput());
            game.StartGame();
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Vector3 aligned = task.Gantry.ToLocal(task.Prize.Body.worldCenterOfMass);
            aligned.y = task.Gantry.Workspace.max.y;
            task.Gantry.ResetPose(aligned);
            Physics.SyncTransforms();
            Assert.IsTrue(task.StartGrab());
            Assert.IsFalse(task.StartGrab(), "A repeated command must not start a second attempt.");
            bool sawGrip = false, sawRelease = false;
            int budget = 1400;
            while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && budget-- > 0)
            {
                if (task.Session.State == GraspState.Lifting) sawGrip |= task.ContactCount >= 2;
                if (task.Prize.Released) sawRelease = true;
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(budget, 0, "The real state machine did not finish within 28 simulated seconds.");
            Assert.AreEqual(GraspState.Result, task.Session.State, task.Diagnostic);
            Assert.IsTrue(sawGrip, "An aligned attempt must establish at least two contacts.");
            Assert.IsTrue(sawRelease, "A held prize must be released into gravity.");
            Assert.IsTrue(task.Session.Awarded, "Actual chute containment and stability must produce success; failure=" + task.Session.Failure);
            Assert.AreEqual(1, task.Session.AttemptId);
            Assert.AreEqual(0, task.Gantry.GetComponentsInChildren<FixedJoint>().Length);
            Assert.IsTrue(task.Prize.IsStable);
        }

        [UnityTest]
        public IEnumerator PauseThenResetDuringGrip_ClearsAttachmentIdentityAndMotion()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            game.StartGame();
            GraspTaskController task = game.Task;
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Vector3 aligned = task.Gantry.ToLocal(task.Prize.Body.worldCenterOfMass);
            aligned.y = task.Gantry.Workspace.max.y;
            task.Gantry.ResetPose(aligned);
            Physics.SyncTransforms();
            Assert.IsTrue(task.StartGrab());
            int budget = 700;
            while (task.Session.State != GraspState.Lifting && task.Session.State != GraspState.Error && budget-- > 0)
                yield return new WaitForFixedUpdate();
            Assert.Greater(budget, 0);
            Assert.AreEqual(GraspState.Lifting, task.Session.State);
            Assert.GreaterOrEqual(task.ContactCount, 2);
            int oldSession = task.Session.SessionId;
            game.Pause(true);
            Vector3 tool = task.Gantry.CurrentToolPose.position;
            Vector3 prize = task.Prize.Body.position;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(tool, task.Gantry.CurrentToolPose.position);
            Assert.AreEqual(prize, task.Prize.Body.position);
            Assert.IsFalse(task.StartGrab());
            game.ConfirmReset();
            Assert.AreEqual(1, Time.timeScale);
            Assert.AreEqual(GraspState.Aiming, task.Session.State);
            Assert.Greater(task.Session.SessionId, oldSession);
            Assert.AreEqual(0, task.Session.AttemptId);
            Assert.IsFalse(task.Session.Awarded);
            Assert.IsFalse(task.Prize.Released);
            Assert.AreEqual(0, task.Prize.AttemptId);
            Assert.Less(task.Prize.Body.velocity.sqrMagnitude, 0.000001f);
            yield return null; // Destroy(FixedJoint) is intentionally deferred by Unity.
            Assert.AreEqual(0, task.Gantry.GetComponentsInChildren<FixedJoint>().Length);
            game.ReturnToMenu();
            Assert.AreEqual(GraspState.Idle, task.Session.State);
            Assert.IsFalse(task.Gantry.IsBusy);
        }

        private IEnumerator LoadScene()
        {
            Time.timeScale = 1;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("ClawMachineScene", LoadSceneMode.Single);
#endif
            yield return null;
            game = Object.FindObjectOfType<ClawGameManager>();
        }
    }
}
