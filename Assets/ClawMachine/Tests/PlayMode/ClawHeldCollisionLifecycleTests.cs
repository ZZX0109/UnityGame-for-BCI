using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ClawMachine.Tests.PlayMode
{
    public sealed class ClawHeldCollisionLifecycleTests
    {
        private sealed class QuietInput : IClawInputSource
        {
            public ClawInputFrame Read() => default(ClawInputFrame);
        }
        private struct PairState
        {
            public Collider A, B;
            public bool Ignored;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HeldPairs_RestoreOriginalStatesAfterReleaseBreakResetDisableAndInactivePrize()
        {
            float previousTimeScale = Time.timeScale;
            bool previousFocusPause = ClawGameManager.AutoPauseOnFocusLoss;
            ClawGameManager game = null;
            Collider priorA = null, priorB = null;
            bool priorIgnored = false;
            try
            {
                Time.timeScale = 1;
                ClawGameManager.AutoPauseOnFocusLoss = false;
#if UNITY_EDITOR
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/ClawMachine/Scenes/ClawMachineScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
                yield return SceneManager.LoadSceneAsync("ClawMachineScene", LoadSceneMode.Single);
#endif
                yield return null;
                game = Object.FindObjectOfType<ClawGameManager>();
                Assert.NotNull(game); game.SetInputSource(new QuietInput());
                Assert.IsTrue(game.SelectChallenge(0));
                var originalStates = new List<PairState>();
                foreach (string exit in new[] { "release", "jointBreak", "reset", "disable", "inactive" })
                {
                    game.StartGame();
                    for (int step = 0; step < 10; step++) yield return new WaitForFixedUpdate();
                    GraspTaskController task = game.Task;
                    GripperController gripper = task.Gripper;
                    PrizePhysics prize = task.Prize;
                    Collider[] physical = Field<Collider[]>(gripper, "physicalColliders");
                    Assert.Greater(physical.Length, 0);
                    priorA = physical[0]; priorB = prize.Collider;
                    priorIgnored = Physics.GetIgnoreCollision(priorA, priorB);
                    Physics.IgnoreCollision(priorA, priorB, true);
                    originalStates.Clear();
                    foreach (Collider a in physical) foreach (Collider b in prize.ContactColliders)
                        originalStates.Add(new PairState { A = a, B = b, Ignored = Physics.GetIgnoreCollision(a, b) });

                    // Use the actual configured colliders and validated grasp, elevated
                    // above the bay so a released body can separate under real gravity.
                    prize.Body.position += Vector3.up * 0.25f;
                    prize.Body.velocity = prize.Body.angularVelocity = Vector3.zero;
                    // Rigidbody interpolation may leave the visible Transform one
                    // physics tick behind this test's direct pose setup. Bounds and
                    // child contact transforms must sample the same actual pose.
                    prize.Body.transform.SetPositionAndRotation(prize.Body.position, prize.Body.rotation);
                    Physics.SyncTransforms();
                    Vector3 grasp = prize.Body.worldCenterOfMass;
                    grasp.y = prize.CaptureBounds.max.y - prize.Profile.surfaceCaptureDepth;
                    Assert.IsTrue(task.Driver.TryReset(new Pose(grasp, task.TaskSpace.Orientation)));
                    Rigidbody toolBody = gripper.GetComponent<Rigidbody>();
                    Assert.NotNull(toolBody);
                    toolBody.transform.SetPositionAndRotation(toolBody.position, toolBody.rotation);
                    Physics.SyncTransforms();
                    gripper.CloseTowards(0, task.Prizes);
                    Physics.SyncTransforms();
                    Assert.AreEqual(GraspFailure.None, gripper.TryGrip(prize), exit);
                    Assert.IsTrue(gripper.IsHolding);
                    foreach (PairState pair in originalStates) Assert.IsTrue(Physics.GetIgnoreCollision(pair.A, pair.B));
                    yield return new WaitForFixedUpdate();
                    Assert.IsTrue(gripper.IsHolding, "The native C01 attachment must survive a real physics step.");

                    if (exit == "release") gripper.Release(true);
                    else if (exit == "jointBreak")
                    {
                        // Break only this test joint using gravity; the source profile stays unchanged.
                        gripper.GetComponent<FixedJoint>().breakForce = 0.001f;
                        float deadline = Time.realtimeSinceStartup + 2;
                        while (gripper.IsHolding && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
                        Assert.IsTrue(gripper.Slipped);
                    }
                    else if (exit == "reset") gripper.ResetGrip();
                    else if (exit == "disable")
                    {
                        gripper.enabled = false;
                        Assert.AreEqual(0, Field<IList>(gripper, "heldCollisionPairs").Count, "Disable must clear all leases synchronously.");
                        gripper.enabled = true;
                    }
                    else
                    {
                        prize.gameObject.SetActive(false);
                        Assert.DoesNotThrow(gripper.ResetGrip);
                        Assert.AreEqual(0, Field<IList>(gripper, "heldCollisionPairs").Count, "Inactive prize references must not retain leases.");
                        prize.gameObject.SetActive(true);
                        Physics.IgnoreCollision(priorA, priorB, priorIgnored);
                        priorA = priorB = null;
                        continue;
                    }

                    Assert.IsFalse(gripper.IsHolding, exit);
                    // Normal release waits for nonpenetrating separation, with no timer
                    // that could restore an ignored pair while the shapes overlap.
                    float restoreDeadline = Time.realtimeSinceStartup + 3;
                    while (Field<IList>(gripper, "heldCollisionPairs").Count > 0 && Time.realtimeSinceStartup < restoreDeadline)
                        yield return new WaitForFixedUpdate();
                    Assert.AreEqual(0, Field<IList>(gripper, "heldCollisionPairs").Count, exit + " left a collision lease behind.");
                    foreach (PairState pair in originalStates)
                        Assert.AreEqual(pair.Ignored, Physics.GetIgnoreCollision(pair.A, pair.B), exit + " must preserve each pair's original state, including true.");
                    Assert.AreEqual(24, prize.Profile.breakForce);
                    Assert.AreEqual(1.2f, prize.Profile.breakTorque, 0.000001f);
                    Physics.IgnoreCollision(priorA, priorB, priorIgnored);
                    priorA = priorB = null;
                    Debug.Log("Claw held collision lifecycle verified: " + exit);
                }
            }
            finally
            {
                try
                {
                    if (game) { game.Task.Gripper.enabled = true; game.ReturnToMenu(); }
                    if (priorA && priorB && priorA.gameObject.activeInHierarchy && priorB.gameObject.activeInHierarchy)
                        Physics.IgnoreCollision(priorA, priorB, priorIgnored);
                }
                finally { ClawGameManager.AutoPauseOnFocusLoss = previousFocusPause; Time.timeScale = previousTimeScale; }
            }
        }

        private static T Field<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name); return (T)field.GetValue(target);
        }
    }
}
