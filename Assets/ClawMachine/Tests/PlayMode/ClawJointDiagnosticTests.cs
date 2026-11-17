// The optional Infinity setting affects only this test's FixedJoint.breakTorque;
// it does not change the profile, CheckGrip, colliders, breakForce, or mass scales.
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
    [Category("Diagnostic")]
    public sealed class ClawJointDiagnosticTests
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
        public IEnumerator C03_Aligned_NativeBreakTorque_First20Fixed()
        {
            yield return Diagnose(false);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator C03_Aligned_DiagnosticInfinityTorque_First20Fixed()
        {
            yield return Diagnose(true);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator C03_Aligned_ExplicitHeldColliderPairs_NativeLimits_First20Fixed()
        {
            yield return Diagnose(false, true);
        }

        private IEnumerator Diagnose(bool diagnosticInfinityTorque, bool diagnosticIgnorePairCollisions = false)
        {
            string label = diagnosticIgnorePairCollisions ? "EXPLICIT_HELD_COLLIDER_PAIRS_NATIVE_LIMITS" :
                diagnosticInfinityTorque ? "DIAGNOSTIC_TORQUE_INFINITY" : "NATIVE_LIMITS";
            float previousTimeScale = Time.timeScale;
            bool previousFocusPause = ClawGameManager.AutoPauseOnFocusLoss;
            ClawGameManager game = null;
            GraspTaskController task = null;
            Action<GraspState> handler = null;
            FixedJoint captured = null;
            float originalJointTorque = 0;
            var pairStates = new List<PairState>();
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
                Assert.NotNull(game);
                game.SetInputSource(new QuietInput());
                Assert.IsTrue(game.SelectChallenge(2));
                game.StartGame();
                task = game.Task;
                Assert.IsTrue(task.IsConfigured);
                for (int index = 0; index < 10; index++) yield return new WaitForFixedUpdate();
                PrizePhysics prize = task.Prize;
                float originalProfileTorque = prize.Profile.breakTorque;
                Rigidbody tool = task.Gripper.GetComponent<Rigidbody>();
                Assert.NotNull(prize); Assert.NotNull(tool);
                Vector3 aligned = task.TaskSpace.ToLocal(prize.Body.worldCenterOfMass);
                aligned.y = task.TaskSpace.LocalBounds.max.y;
                Assert.IsTrue(task.Driver.TryReset(new Pose(task.TaskSpace.ToWorld(aligned), task.TaskSpace.Orientation)));
                Physics.SyncTransforms();
                bool lifting = false;
                handler = state => {
                    if (state != GraspState.Lifting) return;
                    lifting = true;
                    captured = task.Gripper.GetComponent<FixedJoint>();
                    Assert.NotNull(captured);
                    originalJointTorque = captured.breakTorque;
                    Debug.Log("ClawJointDiagnostic " + label + " CREATED " + Snapshot(captured, tool, prize));
                    if (diagnosticInfinityTorque)
                    {
                        captured.breakTorque = float.PositiveInfinity;
                        Debug.Log("ClawJointDiagnostic TEST-ONLY native torque break disabled; profile still=" + prize.Profile.breakTorque.ToString("G9") +
                            "; CheckGrip remains enabled; breakForce=" + captured.breakForce.ToString("G9"));
                    }
                    if (diagnosticIgnorePairCollisions)
                    {
                        var field = typeof(GripperController).GetField("physicalColliders", BindingFlags.Instance | BindingFlags.NonPublic);
                        Assert.NotNull(field);
                        var physical = (Collider[])field.GetValue(task.Gripper);
                        Assert.NotNull(physical);
                        foreach (Collider finger in physical)
                        {
                            Assert.NotNull(finger);
                            Assert.AreSame(tool, finger.attachedRigidbody, "Every finger collider must belong to the joint's tool body.");
                            foreach (Collider surface in prize.ContactColliders)
                            {
                                Assert.NotNull(surface);
                                Assert.AreSame(prize.Body, surface.attachedRigidbody, "Every prize collider must belong to the connected body.");
                                bool previous = Physics.GetIgnoreCollision(finger, surface);
                                pairStates.Add(new PairState { A = finger, B = surface, Ignored = previous });
                                Physics.IgnoreCollision(finger, surface, true);
                                Assert.IsTrue(Physics.GetIgnoreCollision(finger, surface));
                                Debug.Log("ClawJointDiagnostic pair " + finger.name + " / " + surface.name + "; previousIgnore=" + previous);
                            }
                        }
                        Debug.Log("ClawJointDiagnostic TEST-ONLY explicit held collider pair count=" + pairStates.Count +
                            "; native breakTorque=" + captured.breakTorque.ToString("G9") + "; native breakForce=" + captured.breakForce.ToString("G9") +
                            "; profile torque=" + prize.Profile.breakTorque.ToString("G9") + "; CheckGrip remains enabled.");
                    }
                };
                task.Session.Changed += handler;
                Assert.IsTrue(task.StartGrab());
                float deadline = Time.realtimeSinceStartup + 75;
                while (!lifting && task.Session.State != GraspState.Error && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue(lifting, "The diagnostic must first reach an actual validated grip.");

                float peakForce = 0, peakTorque = 0;
                for (int index = 1; index <= 20; index++)
                {
                    yield return new WaitForFixedUpdate();
                    if (captured)
                    {
                        peakForce = Mathf.Max(peakForce, captured.currentForce.magnitude);
                        peakTorque = Mathf.Max(peakTorque, captured.currentTorque.magnitude);
                    }
                    Debug.Log("ClawJointDiagnostic " + label + " fixed=" + index +
                        "; state=" + task.Session.State + "; failure=" + task.Session.Failure +
                        "; holding=" + task.Gripper.IsHolding + "; jointAlive=" + (bool)captured +
                        "; CheckGripTorque=" + task.Gripper.LastGripTorque.ToString("G9") +
                        "; CheckGripDrift=" + task.Gripper.LastGripDrift.ToString("G9") +
                        "; measuredPeakForce=" + peakForce.ToString("G9") + "; measuredPeakTorque=" + peakTorque.ToString("G9") +
                        "; " + Snapshot(captured, tool, prize));
                }
                if (diagnosticIgnorePairCollisions)
                {
                    Assert.Greater(pairStates.Count, 0);
                    Assert.IsTrue(task.Gripper.IsHolding && captured, "Explicit held collider pairs must preserve the aligned attachment under original native limits.");
                    Assert.AreEqual(0.13f, captured.breakTorque, 0.000001f);
                    Assert.AreEqual(18f, captured.breakForce, 0.000001f);
                    Assert.AreEqual(originalProfileTorque, prize.Profile.breakTorque);
                    Assert.AreEqual(0.13f, prize.Profile.breakTorque, 0.000001f, "The eccentric-gravity threshold must remain unchanged.");
                }
                Debug.Log("ClawJointDiagnostic " + label + " END; Diagnostic only, not successful gameplay acceptance.");
            }
            finally
            {
                try
                {
                    if (task && handler != null) task.Session.Changed -= handler;
                    if (captured) captured.breakTorque = originalJointTorque;
                    foreach (PairState pair in pairStates)
                        if (pair.A && pair.B) Physics.IgnoreCollision(pair.A, pair.B, pair.Ignored);
                    if (game) game.ReturnToMenu();
                }
                finally
                {
                    ClawGameManager.AutoPauseOnFocusLoss = previousFocusPause;
                    Time.timeScale = previousTimeScale;
                }
            }
        }

        private static Vector3 PhysicsAnchor(Rigidbody body, Vector3 anchor)
        {
            return body.position + body.rotation * Vector3.Scale(body.transform.lossyScale, anchor);
        }

        private static string Snapshot(FixedJoint joint, Rigidbody tool, PrizePhysics prize)
        {
            Rigidbody body = prize.Body;
            Vector3 offset = body.worldCenterOfMass - tool.position;
            string text = "toolPhysics=" + tool.position.ToString("F8") + "; toolTransform=" + tool.transform.position.ToString("F8") +
                "; prizePhysics=" + body.position.ToString("F8") + "; prizeTransform=" + body.transform.position.ToString("F8") +
                "; toolRotation=" + tool.rotation.ToString("F8") + "; prizeRotation=" + body.rotation.ToString("F8") +
                "; comWorld=" + body.worldCenterOfMass.ToString("F8") + "; comLocal=" + body.centerOfMass.ToString("F8") +
                "; linearVelocity=" + body.velocity.ToString("F8") + "; angularVelocity=" + body.angularVelocity.ToString("F8") +
                "; inertia=" + body.inertiaTensor.ToString("F8") + "; inertiaRotation=" + body.inertiaTensorRotation.ToString("F8") +
                "; gravityTorque=" + Vector3.Cross(offset, Physics.gravity * body.mass).magnitude.ToString("G9");
            if (!joint) return text + "; joint missing (solver impulse no longer readable)";
            Vector3 ownPhysics = PhysicsAnchor(tool, joint.anchor);
            Vector3 connectedPhysics = PhysicsAnchor(body, joint.connectedAnchor);
            return text + "; ownAnchorLocal=" + joint.anchor.ToString("F8") + "; connectedAnchorLocal=" + joint.connectedAnchor.ToString("F8") +
                "; ownAnchorPhysicsWorld=" + ownPhysics.ToString("F8") + "; connectedAnchorPhysicsWorld=" + connectedPhysics.ToString("F8") +
                "; physicsAnchorError=" + Vector3.Distance(ownPhysics, connectedPhysics).ToString("G9") +
                "; transformAnchorError=" + Vector3.Distance(tool.transform.TransformPoint(joint.anchor), body.transform.TransformPoint(joint.connectedAnchor)).ToString("G9") +
                "; currentForce=" + joint.currentForce.ToString("F8") + "; currentTorque=" + joint.currentTorque.ToString("F8") +
                "; breakForce=" + joint.breakForce.ToString("G9") + "; breakTorque=" + joint.breakTorque.ToString("G9") +
                "; massScale=" + joint.massScale.ToString("G9") + "; connectedMassScale=" + joint.connectedMassScale.ToString("G9") +
                "; preprocessing=" + joint.enablePreprocessing;
        }
    }
}
