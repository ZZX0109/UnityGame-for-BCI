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
        private bool savedFocusPause;
        private sealed class QuietInput : IClawInputSource { public ClawInputFrame Read() => default(ClawInputFrame); }

        [SetUp]
        public void IsolateFocusPause()
        {
            savedFocusPause = ClawGameManager.AutoPauseOnFocusLoss;
            ClawGameManager.AutoPauseOnFocusLoss = false;
            Time.timeScale = 1;
        }

        [TearDown]
        public void RestoreTimeAndReleaseScene()
        {
            try { if (game) game.ReturnToMenu(); }
            finally { Time.timeScale = 1; ClawGameManager.AutoPauseOnFocusLoss = savedFocusPause; }
        }

        [UnityTest, Category("Acceptance_C01")]
        public IEnumerator AlignedC01_ReallyGripsReleasesAndSettlesInChute()
        {
            yield return RunAcceptance(0, "C01");
        }

        [UnityTest, Category("Acceptance_C02")]
        public IEnumerator AlignedC02_PlushTargetReallyGripsReleasesAndSettlesInChute()
        {
            yield return RunAcceptance(1, "C02");
        }

        [UnityTest, Category("Acceptance_C03")]
        public IEnumerator AlignedC03_OffsetMassTargetReallyGripsReleasesAndSettlesInChute()
        {
            yield return RunAcceptance(2, "C03");
        }

        private IEnumerator RunAcceptance(int challengeIndex, string challengeId)
        {
            yield return LoadScene();
            Assert.NotNull(game);
            GraspTaskController task = game.Task;
            Assert.IsTrue(task.IsConfigured);
            game.SetInputSource(new QuietInput());
            Assert.IsTrue(game.SelectChallenge(challengeIndex));
            game.StartGame();
            Assert.AreEqual(challengeId, task.Challenge.challengeId);
            yield return WaitForPhysics(0.2f);
            PrizePhysics target = task.Prize;
            Assert.NotNull(target);
            Vector3 aligned = task.TaskSpace.ToLocal(target.Body.worldCenterOfMass);
            aligned.y = task.TaskSpace.LocalBounds.max.y;
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
            Physics.SyncTransforms();
            Assert.IsTrue(task.StartGrab());
            Assert.IsFalse(task.StartGrab(), "A repeated command must not start a second attempt.");
            bool sawGrip = false, sawRelease = false, sawContactDescent = false;
            float deadline = Time.realtimeSinceStartup + 75;
            while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
            {
                if (task.Session.State == GraspState.ContactDescent) sawContactDescent = true;
                if (task.Session.State == GraspState.Lifting) sawGrip |= task.ContactCount >= 2 && task.Gripper.IsHolding && task.Gripper.HeldPrize == target;
                if (task.Session.Released && target.Released) sawRelease = true;
                yield return null;
            }
            Assert.AreEqual(GraspState.Result, task.Session.State, "Real-time watchdog; timescale=" + Time.timeScale + "; " + task.Diagnostic);
            Assert.IsTrue(sawContactDescent, "The safe approach must be followed by a distinct slow contact phase.");
            Assert.IsTrue(sawGrip, "An aligned attempt must establish at least two contacts.");
            Assert.IsTrue(sawRelease, "A held prize must be released into gravity.");
            Assert.IsTrue(task.Session.Awarded, "Actual chute containment and stability must produce success; failure=" + task.Session.Failure);
            Assert.AreEqual(1, task.Session.AttemptId);
            Assert.AreEqual(0, task.Gripper.GetComponents<FixedJoint>().Length);
            Assert.IsTrue(target.IsStable);
            PrizeChuteDetector chute = Object.FindObjectOfType<PrizeChuteDetector>();
            Assert.NotNull(chute);
            Assert.IsTrue(chute.Contains(target.AllBounds), "The complete physical target must be inside the chute, including compound features.");
        }

        [UnityTest]
        public IEnumerator PauseThenResetDuringGrip_ClearsAttachmentIdentityAndMotion()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            game.StartGame();
            GraspTaskController task = game.Task;
            yield return WaitForPhysics(0.2f);
            Vector3 aligned = task.TaskSpace.ToLocal(task.Prize.Body.worldCenterOfMass);
            aligned.y = task.TaskSpace.LocalBounds.max.y;
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
            Physics.SyncTransforms();
            Assert.IsTrue(task.StartGrab());
            float deadline = Time.realtimeSinceStartup + 45;
            while (task.Session.State != GraspState.Lifting && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(GraspState.Lifting, task.Session.State, "Real-time watchdog; timescale=" + Time.timeScale + "; " + task.Diagnostic);
            Assert.GreaterOrEqual(task.ContactCount, 2);
            Assert.IsTrue(task.Gripper.IsHolding);
            int oldSession = task.Session.SessionId;
            game.Pause(true);
            Vector3 tool = task.Driver.CurrentToolPose.position;
            Vector3 prize = task.Prize.Body.position;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(tool, task.Driver.CurrentToolPose.position);
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
            Assert.AreEqual(0, task.Gripper.GetComponents<FixedJoint>().Length);
            game.ReturnToMenu();
            Assert.AreEqual(GraspState.Idle, task.Session.State);
            Assert.IsFalse(task.Driver.IsBusy);
        }

        [UnityTest, Category("Lifecycle")]
        public IEnumerator ResetAtEveryAutomaticPhase_PreventsStaleMotionAndHangingPrizes()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            GraspState[] phases = { GraspState.Approaching, GraspState.ContactDescent, GraspState.Closing,
                GraspState.Lifting, GraspState.Transporting, GraspState.Releasing, GraspState.Settling };
            foreach (GraspState phase in phases)
            {
                game.StartGame();
                GraspTaskController task = game.Task;
                yield return WaitForPhysics(0.2f);
                Vector3 aligned = task.TaskSpace.ToLocal(task.Prize.Body.worldCenterOfMass);
                aligned.y = task.TaskSpace.LocalBounds.max.y;
                Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
                Physics.SyncTransforms();
                Assert.IsTrue(task.StartGrab());
                float deadline = Time.realtimeSinceStartup + 75;
                while (task.Session.State != phase && task.Session.State != GraspState.Error && task.Session.State != GraspState.Result && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.AreEqual(phase, task.Session.State, "Real-time watchdog at " + phase + "; timescale=" + Time.timeScale + "; " + task.Diagnostic);
                int oldSession = task.Session.SessionId;
                game.Pause(true);
                Vector3 frozen = task.Driver.CurrentToolPose.position;
                yield return new WaitForSecondsRealtime(0.04f);
                Assert.AreEqual(frozen, task.Driver.CurrentToolPose.position);
                game.ConfirmReset();
                Assert.AreEqual(GraspState.Aiming, task.Session.State);
                Assert.Greater(task.Session.SessionId, oldSession);
                Assert.AreEqual(0, task.Session.AttemptId);
                Assert.IsFalse(task.Gripper.IsHolding);
                Assert.IsFalse(task.Session.Released);
                Assert.IsFalse(task.Session.Awarded);
                Assert.Less(task.Driver.CurrentVelocity.sqrMagnitude, 0.000001f);
                foreach (PrizePhysics prize in task.Prizes)
                    if (prize && prize.gameObject.activeSelf)
                    {
                        Assert.AreEqual(0, prize.AttemptId);
                        Assert.IsFalse(prize.Released);
                        Assert.Less(prize.Body.velocity.sqrMagnitude, 0.000001f);
                        Assert.Less(prize.Body.angularVelocity.sqrMagnitude, 0.000001f);
                    }
                yield return null;
                Assert.AreEqual(0, task.Gripper.GetComponents<FixedJoint>().Length);
            }
        }

        [UnityTest, Category("Lifecycle")]
        public IEnumerator FailedRetry_RestoresEverySeededPrizeAndPreservesUsedAttempt()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            Assert.IsTrue(game.SelectChallenge(1));
            game.StartGame();
            GraspTaskController task = game.Task;
            var expected = new System.Collections.Generic.Dictionary<string, Vector3>();
            foreach (PrizePhysics prize in task.Prizes)
                if (prize && prize.gameObject.activeSelf) expected.Add(prize.PrizeId, prize.Body.position);
            Assert.GreaterOrEqual(expected.Count, 2, "The challenge must actually exercise multiple configured prizes.");
            Vector3 miss = task.TaskSpace.LocalBounds.min;
            miss.y = task.TaskSpace.LocalBounds.max.y;
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(miss), task.TaskSpace.Orientation)));
            Assert.IsTrue(task.StartGrab());
            float deadline = Time.realtimeSinceStartup + 75;
            while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(GraspState.Result, task.Session.State, "Real-time watchdog; timescale=" + Time.timeScale + "; " + task.Diagnostic);
            Assert.IsFalse(task.Session.Awarded);
            int sessionId = task.Session.SessionId;
            foreach (PrizePhysics prize in task.Prizes)
                if (prize && prize.gameObject.activeSelf) { prize.Body.velocity = Vector3.one; prize.Body.angularVelocity = Vector3.one; }
            task.Retry();
            Assert.AreEqual(GraspState.Aiming, task.Session.State);
            Assert.AreEqual(sessionId, task.Session.SessionId);
            Assert.AreEqual(1, task.Session.AttemptId);
            foreach (PrizePhysics prize in task.Prizes)
                if (prize && prize.gameObject.activeSelf)
                {
                    Assert.Less(Vector3.Distance(expected[prize.PrizeId], prize.Body.position), 0.00001f);
                    Assert.Less(prize.Body.velocity.sqrMagnitude, 0.000001f);
                    Assert.Less(prize.Body.angularVelocity.sqrMagnitude, 0.000001f);
                    Assert.AreEqual(0, prize.AttemptId);
                    Assert.IsFalse(prize.Released);
                }
        }

        [UnityTest, Category("GraspAudit")]
        public IEnumerator C03_ShapeCenterGrip_NaturallySlipsFromOffsetGravityTorque()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            Assert.IsTrue(game.SelectChallenge(2));
            game.StartGame();
            GraspTaskController task = game.Task;
            yield return WaitForPhysics(0.2f);
            PrizePhysics target = task.Prize;
            Assert.AreEqual("amber-fox", target.PrizeId);
            Assert.AreEqual(0.055f, target.Profile.centerOfMass.x, 0.0001f);
            Assert.AreEqual(0.13f, target.Profile.breakTorque, 0.0001f);
            Vector3 shapeCenter = target.CaptureBounds.center;
            float gravityTorque = Vector3.Cross(target.Body.worldCenterOfMass - shapeCenter, Physics.gravity * target.Body.mass).magnitude;
            Assert.Greater(gravityTorque, target.Profile.breakTorque, "This test must exercise an unstable offset grip, not a random failure.");
            Vector3 aligned = task.TaskSpace.ToLocal(shapeCenter);
            aligned.y = task.TaskSpace.LocalBounds.max.y;
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
            Physics.SyncTransforms();
            int slipEvents = 0;
            System.Action slipped = () => slipEvents++;
            task.Gripper.SlippedEvent += slipped;
            try
            {
                Assert.IsTrue(task.StartGrab());
                float deadline = Time.realtimeSinceStartup + 75;
                while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.AreEqual(GraspState.Result, task.Session.State, "Real-time watchdog; timescale=" + Time.timeScale + "; " + task.Diagnostic);
                Assert.GreaterOrEqual(task.ContactCount, 2, "The grasp must first form validated opposing contacts.");
                Assert.Greater(slipEvents, 0, "Natural CheckGrip must report the lost attachment.");
                Assert.IsTrue(task.Gripper.Slipped);
                Assert.IsTrue(target.Released);
                Assert.AreEqual(GraspFailure.Slipped, task.Session.Failure);
                Assert.IsFalse(task.Gripper.IsHolding);
                Assert.IsFalse(task.Session.Awarded);
            }
            finally { task.Gripper.SlippedEvent -= slipped; }
        }

        [UnityTest, Category("GraspAudit")]
        public IEnumerator C02_WrongParcel_ReallyReachesChuteButCannotAwardTarget()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            Assert.IsTrue(game.SelectChallenge(1));
            game.StartGame();
            GraspTaskController task = game.Task;
            yield return WaitForPhysics(0.2f);
            PrizePhysics wrong = null;
            foreach (PrizePhysics prize in task.Prizes)
                if (prize && prize.gameObject.activeSelf && prize.PrizeId == "mint-parcel") wrong = prize;
            Assert.NotNull(wrong);
            Assert.AreNotEqual(wrong.PrizeId, task.Challenge.targetId);
            Vector3 aligned = task.TaskSpace.ToLocal(wrong.Body.worldCenterOfMass);
            aligned.y = task.TaskSpace.LocalBounds.max.y;
            Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
            Physics.SyncTransforms();
            Assert.IsTrue(task.StartGrab());
            bool sawWrongGrip = false;
            float deadline = Time.realtimeSinceStartup + 75;
            while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
            {
                sawWrongGrip |= task.Gripper.IsHolding && task.Gripper.HeldPrize == wrong;
                yield return null;
            }
            Assert.AreEqual(GraspState.Result, task.Session.State, "Real-time watchdog; timescale=" + Time.timeScale + "; " + task.Diagnostic);
            Assert.IsTrue(sawWrongGrip);
            Assert.GreaterOrEqual(task.ContactCount, 2);
            Assert.IsTrue(wrong.Released);
            Assert.IsTrue(wrong.IsStable);
            PrizeChuteDetector chute = Object.FindObjectOfType<PrizeChuteDetector>();
            Assert.NotNull(chute);
            Assert.IsTrue(chute.Contains(wrong.AllBounds), "The wrong item must actually complete the physical drop-off.");
            Assert.AreEqual(GraspFailure.WrongTarget, task.Session.Failure);
            Assert.IsFalse(task.Session.Awarded, "A physically delivered wrong item never awards the designated target.");
            Assert.IsFalse(task.Prize.Released);
        }

        [UnityTest, Category("Stress")]
        public IEnumerator ThirtyPhysicalGrabsAndTwentyInterruptedResets_LeaveNoOldAttachments()
        {
            yield return LoadScene();
            Assert.NotNull(game);
            game.SetInputSource(new QuietInput());
            Assert.IsTrue(game.SelectChallenge(0));
            float globalDeadline = Time.realtimeSinceStartup + 600;
            int previousSession = 0;
            for (int run = 0; run < 30; run++)
            {
                Assert.Less(Time.realtimeSinceStartup, globalDeadline, "600-second soak watchdog before grab " + run);
                game.StartGame();
                GraspTaskController task = game.Task;
                Assert.Greater(task.Session.SessionId, previousSession);
                previousSession = task.Session.SessionId;
                yield return WaitForPhysics(0.2f);
                Vector3 aligned = task.TaskSpace.ToLocal(task.Prize.Body.worldCenterOfMass);
                aligned.y = task.TaskSpace.LocalBounds.max.y;
                Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
                Physics.SyncTransforms();
                Assert.IsTrue(task.StartGrab());
                float deadline = Mathf.Min(globalDeadline, Time.realtimeSinceStartup + 75);
                while (task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.AreEqual(GraspState.Result, task.Session.State, "Real-time soak watchdog at grab " + run + "; " + task.Diagnostic);
                Assert.IsTrue(task.Session.Awarded, "Physical soak grab " + run + " failed: " + task.Session.Failure);
                Assert.AreEqual(1, task.Session.AttemptId);
                Assert.IsFalse(task.Gripper.IsHolding);
                Assert.AreEqual(0, task.Gripper.GetComponents<FixedJoint>().Length);
                Assert.Less(task.Driver.CurrentVelocity.sqrMagnitude, 0.000001f);
            }
            GraspState[] checkpoints = { GraspState.Approaching, GraspState.ContactDescent, GraspState.Closing,
                GraspState.Lifting, GraspState.Transporting, GraspState.Releasing, GraspState.Settling };
            for (int reset = 0; reset < 20; reset++)
            {
                Assert.Less(Time.realtimeSinceStartup, globalDeadline, "600-second soak watchdog before reset " + reset);
                game.StartGame();
                GraspTaskController task = game.Task;
                yield return WaitForPhysics(0.2f);
                Vector3 aligned = task.TaskSpace.ToLocal(task.Prize.Body.worldCenterOfMass);
                aligned.y = task.TaskSpace.LocalBounds.max.y;
                Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
                Physics.SyncTransforms();
                Assert.IsTrue(task.StartGrab());
                GraspState checkpoint = checkpoints[reset % checkpoints.Length];
                float deadline = Mathf.Min(globalDeadline, Time.realtimeSinceStartup + 75);
                while (task.Session.State != checkpoint && task.Session.State != GraspState.Result && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.AreEqual(checkpoint, task.Session.State, "Real-time soak watchdog at reset " + reset + "; " + task.Diagnostic);
                int interruptedSession = task.Session.SessionId;
                game.RequestReset();
                Assert.IsTrue(game.ResetConfirmation); Assert.IsTrue(game.IsPaused);
                game.ConfirmReset();
                Assert.IsFalse(game.ResetConfirmation); Assert.IsFalse(game.IsPaused);
                Assert.AreEqual(1, Time.timeScale);
                Assert.Greater(task.Session.SessionId, interruptedSession);
                Assert.AreEqual(GraspState.Aiming, task.Session.State);
                Assert.AreEqual(0, task.Session.AttemptId);
                Assert.IsFalse(task.Gripper.IsHolding);
                Assert.IsFalse(task.Session.Awarded);
                Assert.IsFalse(task.Prize.Released);
                Assert.AreEqual(0, task.Prize.AttemptId);
                Assert.Less(task.Driver.CurrentVelocity.sqrMagnitude, 0.000001f);
                Assert.Less(task.Prize.Body.velocity.sqrMagnitude, 0.000001f);
                Assert.Less(task.Prize.Body.angularVelocity.sqrMagnitude, 0.000001f);
                yield return null;
                Assert.AreEqual(0, task.Gripper.GetComponents<FixedJoint>().Length);
            }
            Assert.Less(Time.realtimeSinceStartup, globalDeadline, "The soak exceeded its total real-time budget.");
        }

        // Yield render frames so an accidental global pause cannot deadlock the test runner.
        private static IEnumerator WaitForPhysics(float seconds)
        {
            float target = Time.fixedTime + seconds;
            float deadline = Time.realtimeSinceStartup + 10;
            while (Time.fixedTime < target && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.GreaterOrEqual(Time.fixedTime, target, "Physics stopped advancing; timescale=" + Time.timeScale);
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
