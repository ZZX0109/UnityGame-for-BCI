using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClawMachine.Tests.EditMode
{
    public sealed class ClawRuleTests
    {
        private const string ScenePath = "Assets/ClawMachine/Scenes/ClawMachineScene.unity";

        [Test]
        public void Attempt_HasOneEntryAndRejectsSkippedPhases()
        {
            var session = new ClawSession();
            Assert.IsFalse(session.BeginAttempt());
            session.Reset(5, true);
            Assert.IsFalse(session.Advance(GraspState.Lowering), "Attempt entry must allocate an ID through BeginAttempt.");
            Assert.AreEqual(0, session.AttemptId);
            Assert.IsTrue(session.BeginAttempt());
            Assert.AreEqual(1, session.AttemptId);
            Assert.IsFalse(session.BeginAttempt(), "A held or repeated Enter cannot start a second run.");
            Assert.IsFalse(session.Advance(GraspState.Transporting));
            Assert.AreEqual(GraspState.Lowering, session.State);
            Assert.IsFalse(session.Advance(GraspState.Closing), "The slow contact descent cannot be skipped.");
            Assert.IsTrue(session.Advance(GraspState.ContactDescent));
            Assert.IsTrue(session.Advance(GraspState.Closing));
            Assert.IsFalse(session.Advance(GraspState.Settling));
            Assert.AreEqual(1, session.AttemptId);
        }

        [Test]
        public void Pause_FreezesTransitionsAndBlocksAttemptEntry()
        {
            var session = new ClawSession();
            session.Reset(5, true);
            session.Pause(true);
            Assert.IsFalse(session.BeginAttempt());
            Assert.AreEqual(0, session.AttemptId);
            session.Pause(false);
            Assert.IsTrue(session.BeginAttempt());
            session.Pause(true);
            Assert.IsFalse(session.Advance(GraspState.ContactDescent));
            Assert.AreEqual(GraspState.Lowering, session.State);
            session.Pause(false);
            Assert.IsTrue(session.Advance(GraspState.ContactDescent));
        }

        [Test]
        public void Award_RequiresReleaseCurrentIdentityTargetAndStableContainment()
        {
            var session = new ClawSession();
            session.Reset(5, true);
            Assert.IsTrue(session.BeginAttempt());
            int epoch = session.SessionId;
            int attempt = session.AttemptId;
            Assert.IsFalse(session.TryAward(epoch, attempt, "target", "target", true));
            ToReleasing(session);
            session.MarkReleased();
            Assert.IsTrue(session.Advance(GraspState.Settling));
            Assert.IsFalse(session.TryAward(epoch - 1, attempt, "target", "target", true));
            Assert.IsFalse(session.TryAward(epoch, attempt - 1, "target", "target", true));
            Assert.IsFalse(session.TryAward(epoch, attempt, "other", "target", true));
            Assert.IsFalse(session.TryAward(epoch, attempt, "target", "target", false));
            Assert.IsFalse(session.TryAward(epoch, attempt, "", "", true));
            session.Pause(true);
            Assert.IsFalse(session.TryAward(epoch, attempt, "target", "target", true));
            session.Pause(false);
            Assert.IsTrue(session.TryAward(epoch, attempt, "target", "target", true));
            Assert.IsFalse(session.TryAward(epoch, attempt, "target", "target", true), "One attempt can award only once.");
        }

        [Test]
        public void NeverReleasedPrize_CannotAwardEvenIfAtChute()
        {
            var session = new ClawSession();
            session.Reset(5, true);
            Assert.IsTrue(session.BeginAttempt());
            ToReleasing(session);
            Assert.IsTrue(session.Advance(GraspState.Settling));
            Assert.IsFalse(session.TryAward(session.SessionId, session.AttemptId, "target", "target", true));
            Assert.IsFalse(session.Awarded);
        }

        [Test]
        public void Reset_InvalidatesOldRunAndClearsCountersPauseAndOutcome()
        {
            var session = new ClawSession();
            session.Reset(5, true);
            Assert.IsTrue(session.BeginAttempt());
            int oldSession = session.SessionId;
            session.RecordFailure(GraspFailure.Misaligned);
            session.Pause(true);
            session.Reset(5, false);
            Assert.Greater(session.SessionId, oldSession);
            Assert.AreEqual(0, session.AttemptId);
            Assert.AreEqual(5, session.Remaining);
            Assert.AreEqual(GraspState.Idle, session.State);
            Assert.IsFalse(session.Paused);
            Assert.IsFalse(session.Released);
            Assert.IsFalse(session.Awarded);
            Assert.AreEqual(GraspFailure.None, session.Failure);
            Assert.IsFalse(session.TryAward(oldSession, 1, "target", "target", true));
        }

        [Test]
        public void AttemptLimit_EndsAfterFinalFailedAttempt()
        {
            var session = new ClawSession();
            session.Reset(2, true);
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Assert.IsTrue(session.BeginAttempt());
                Assert.AreEqual(attempt, session.AttemptId);
                ToReleasing(session);
                session.MarkReleased();
                Assert.IsTrue(session.Advance(GraspState.Settling));
                session.RecordFailure(GraspFailure.NoContact);
                Assert.IsTrue(session.Advance(GraspState.Result));
                Assert.IsTrue(session.Retry());
                Assert.AreEqual(attempt == 2 ? GraspState.End : GraspState.Aiming, session.State);
            }
            Assert.AreEqual(0, session.Remaining);
            Assert.IsFalse(session.BeginAttempt());
        }

        [Test]
        public void Direction_UsesMachineAxesAtEveryCameraAndNormalizesDiagonal()
        {
            var root = new GameObject("Camera direction fixture");
            try
            {
                var controller = root.AddComponent<CraneCameraController>();
                var views = new Camera[3];
                for (int i = 0; i < views.Length; i++)
                {
                    var cameraObject = new GameObject("View " + i);
                    cameraObject.transform.SetParent(root.transform);
                    cameraObject.transform.rotation = Quaternion.Euler(0, i * 90, 0);
                    views[i] = cameraObject.AddComponent<Camera>();
                }
                Set(controller, "views", views);
                Set(controller, "lookTarget", root.transform);
                controller.Select(1);
                // One main camera now orbits continuously. Camera heading must
                // never remap manipulator axes, including at the back of a turn.
                for (int step = 0; step < 16; step++)
                {
                    controller.OrbitBy(new Vector2(45, step % 2 == 0 ? 5 : -5));
                    Assert.AreEqual(Vector3.forward, ClawMotion.MachineDirection(Vector2.up));
                    Assert.AreEqual(Vector3.right, ClawMotion.MachineDirection(Vector2.right));
                    Assert.AreEqual(Vector3.back, ClawMotion.MachineDirection(Vector2.down));
                    Assert.AreEqual(Vector3.left, ClawMotion.MachineDirection(Vector2.left));
                    Assert.LessOrEqual(ClawMotion.MachineDirection(Vector2.one).magnitude, 1.000001f);
                    Assert.Less(Vector3.Distance(new Vector3(1, 0, 1).normalized,
                        ClawMotion.MachineDirection(Vector2.one)), 0.000001f);
                    Assert.AreSame(views[0], controller.CurrentCamera);
                    for (int i = 0; i < views.Length; i++) Assert.AreEqual(i == 0, views[i].enabled);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Workspace_ClampsAllSixLimitsAndAccelerationIsBounded()
        {
            var workspace = new Bounds(new Vector3(0, 1.25f, 0), new Vector3(0.8f, 0.9f, 0.6f));
            Assert.AreEqual(workspace.max, ClawMotion.Clamp(Vector3.one * 100, workspace));
            Assert.AreEqual(workspace.min, ClawMotion.Clamp(Vector3.one * -100, workspace));
            Assert.AreEqual(workspace.center, ClawMotion.Clamp(workspace.center, workspace));
            Vector3 velocity = ClawMotion.Accelerate(Vector3.zero, Vector3.right * 0.35f, 1.5f, 0.02f);
            Assert.AreEqual(0.03f, velocity.x, 0.000001f);
            Assert.LessOrEqual((ClawMotion.Accelerate(velocity, Vector3.left * 0.35f, 1.5f, 0.02f) - velocity).magnitude, 0.030001f);
            Assert.AreEqual(velocity, ClawMotion.Accelerate(velocity, Vector3.zero, 1.5f, 0));
        }

        [Test]
        public void Seed_IsRepeatableWithoutChangingGlobalRandom()
        {
            var challenge = ScriptableObject.CreateInstance<ClawChallengeDefinition>();
            Random.State original = Random.state;
            try
            {
                Random.InitState(731);
                Random.State before = Random.state;
                float expectedNext = Random.value;
                Random.state = before;
                challenge.seed = 10;
                Vector3 first = challenge.SpawnForSeed();
                Assert.AreEqual(first, challenge.SpawnForSeed());
                Assert.AreEqual(expectedNext, Random.value, "Challenge layout must not consume the global random stream.");
                challenge.seed = 11;
                Assert.AreNotEqual(first, challenge.SpawnForSeed());
            }
            finally { Random.state = original; Object.DestroyImmediate(challenge); }
        }

        [Test]
        public void Chute_RequiresFullContainmentReleaseCurrentAttemptAndContinuousDwell()
        {
            var root = new GameObject("Chute rule fixture");
            var profile = ScriptableObject.CreateInstance<PrizeProfile>();
            var challenge = ScriptableObject.CreateInstance<ClawChallengeDefinition>();
            try
            {
                var chute = root.AddComponent<PrizeChuteDetector>();
                var volume = root.AddComponent<BoxCollider>();
                volume.isTrigger = true; volume.size = Vector3.one;
                Set(chute, "settlementVolume", volume);
                var prizeObject = new GameObject("Prize");
                prizeObject.transform.SetParent(root.transform);
                var collider = prizeObject.AddComponent<BoxCollider>(); collider.size = Vector3.one * 0.2f;
                var body = prizeObject.AddComponent<Rigidbody>();
                var prize = prizeObject.AddComponent<PrizePhysics>();
                Set(prize, "body", body); Set(prize, "mainCollider", collider); Set(prize, "machineRoot", root.transform);
                challenge.graspProfile = profile; challenge.spawnPosition = Vector3.zero;
                profile.stableDwell = 0.3f;
                prize.ResetPrize(challenge); body.position = Vector3.zero;
                prize.BeginAttempt(3, 1); chute.Arm(3, 1);
                Physics.SyncTransforms();
                Assert.IsFalse(chute.Observe(prize, 1), "A prize that was never released is not eligible.");
                prize.MarkReleased();
                Assert.IsFalse(chute.Observe(prize, 0.2f));
                Assert.IsTrue(chute.Observe(prize, 0.2f));
                body.position = new Vector3(0.45f, 0, 0); Physics.SyncTransforms();
                Assert.IsFalse(chute.Observe(prize, 1), "Partial overlap must reset the dwell window.");
                Assert.AreEqual(0, chute.StableTime);
                body.position = Vector3.zero; body.velocity = Vector3.right; Physics.SyncTransforms();
                Assert.IsFalse(chute.Observe(prize, 1), "A fast-moving prize cannot settle.");
                body.velocity = Vector3.zero;
                Assert.IsFalse(chute.Observe(prize, 0.2f), "Stable dwell restarts after movement or exit.");
                prize.BeginAttempt(3, 2); prize.MarkReleased();
                Assert.IsFalse(chute.Observe(prize, 1), "Old chute ownership cannot award a new attempt.");
                chute.Clear(); Assert.AreEqual(0, chute.StableTime);
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(profile); Object.DestroyImmediate(challenge);
            }
        }

        [Test]
        public void SerializedC01Scene_HasCompleteTypedReferencesAndThreeViews()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GraspTaskController task = null;
                CraneCameraController cameras = null;
                int taskCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (GraspTaskController found in root.GetComponentsInChildren<GraspTaskController>(true)) { task = found; taskCount++; }
                    foreach (CraneCameraController found in root.GetComponentsInChildren<CraneCameraController>(true)) cameras = found;
                }
                Assert.AreEqual(1, taskCount);
                Assert.NotNull(task);
                Assert.IsTrue(task.IsConfigured, "The playable scene must wire its own references before Awake.");
                Assert.NotNull(cameras);
                Assert.IsTrue(cameras.IsConfigured);
                Assert.AreEqual("C01", task.Challenge.challengeId);
                Assert.AreEqual(task.Challenge.prizeId, task.Challenge.targetId);
                Assert.AreEqual(5, task.Challenge.attemptLimit);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ToReleasing(ClawSession session)
        {
            Assert.IsTrue(session.Advance(GraspState.ContactDescent));
            Assert.IsTrue(session.Advance(GraspState.Closing));
            Assert.IsTrue(session.Advance(GraspState.Lifting));
            Assert.IsTrue(session.Advance(GraspState.Transporting));
            Assert.IsTrue(session.Advance(GraspState.Releasing));
        }
        private static void Set(object owner, string field, object value)
        {
            FieldInfo info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, field);
            info.SetValue(owner, value);
        }

    }
}
