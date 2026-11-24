using System;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>Headless-ish checks that drive a HandRigDriver by controls and verify
    /// bones actually move (commands-to-bones), plus task stage transitions.</summary>
    public class RigAndTaskTests
    {
        GameObject MakeRig(out HandRigDriver driver, out Transform index1)
        {
            var go = new GameObject("rig");
            var arm = go.AddComponent<HandRigDriver>();
            // minimal fake rig: Thumb1..3, Index1..3 with children as joints
            var parent = new GameObject("Index1.R");
            parent.transform.SetParent(go.transform);
            var child = new GameObject("Index2.R");
            child.transform.SetParent(parent.transform);
            child.transform.position = new Vector3(0, 0, 1);
            index1 = parent.transform;
            // fill remaining expected bones so CacheBones succeeds
            foreach (var n in new[] { "Thumb1", "Thumb2", "Thumb3", "Middle1", "Middle2", "Middle3",
                                      "Ring1", "Ring2", "Ring3", "Little1", "Little2", "Little3" })
            {
                var b = new GameObject(n + ".R");
                b.transform.SetParent(go.transform);
                b.transform.position = new Vector3(1, 0, 0);
            }
            driver = arm;
            Assert.IsTrue(arm.CacheBones(), "CacheBones should find the fake rig");
            return go;
        }

        [Test]
        public void FistControls_RotateBones()
        {
            var go = MakeRig(out var driver, out var index1);
            var before = index1.rotation;
            driver.SetControls(PoseLibrary.Fist());
            Assert.IsTrue(Quaternion.Angle(before, index1.rotation) > 30f,
                          "fist control must visibly rotate Index1");
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void RestControls_LeaveBonesNearRest()
        {
            var go = MakeRig(out var driver, out var index1);
            var before = index1.rotation;
            driver.SetControls(PoseLibrary.Rest());
            Assert.Less(Quaternion.Angle(before, index1.rotation), 30f,
                        "rest pose should stay close to rig rest");
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void PoseApi_LivesOnPoseLibrary()
        {
            // ReplayController consumes these; they must resolve on PoseLibrary
            // (DHS compile follow-up: they previously lived on IntentMapper).
            Assert.AreEqual(20, PoseLibrary.GripPose().Length);
            Assert.AreEqual(20, PoseLibrary.OpenPose().Length);
        }

        [Test]
        public void TaskController_Carry_ReachesLiftWithSeparatedAnchors()
        {
            // DHS finding 2 regression: the PRODUCTION grip convention — builder
            // places the L rig at x=+0.25 and sets leftGripOffset=+0.25, R rig at
            // x=−0.25 with rightGripOffset=−0.25; tests must verify the same
            // convention, not an ad-hoc mirrored one.
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var left = new GameObject("l").transform;
            var right = new GameObject("r").transform;
            tc.leftGripOffset = new Vector3(0.25f, 0, 0);    // production offsets
            tc.rightGripOffset = new Vector3(-0.25f, 0, 0);
            left.position = tc.leftGripOffset;               // anchors on their grips
            right.position = tc.rightGripOffset;
            tc.leftAnchor = left;
            tc.rightAnchor = right;
            var box = new GameObject("box");
            box.transform.position = Vector3.zero;
            box.AddComponent<Rigidbody>();
            tc.Begin(TaskController.TaskType.Carry,
                     new[] { new Vector3(0, 0, 1f) }, box);
            tc.OnIntent("both", 0.016);
            Assert.AreEqual(TaskController.Stage.Lift, tc.CurrentStage,
                            "anchors on the production grip points must grip");
            Assert.IsTrue(box.GetComponent<Rigidbody>().isKinematic);
            tc.End();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(left.gameObject);
            UnityEngine.Object.DestroyImmediate(right.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
        }

        [Test]
        public void TaskController_WrongAnchorGeometry_NeverGrips()
        {
            // and if anchors are NOT on their grip points, Approach must hold
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var left = new GameObject("l").transform;
            var right = new GameObject("r").transform;
            tc.leftGripOffset = new Vector3(0.25f, 0, 0);    // production offsets
            tc.rightGripOffset = new Vector3(-0.25f, 0, 0);
            left.position = Vector3.zero;    // both at the box center: not on grips
            right.position = Vector3.zero;
            tc.leftAnchor = left;
            tc.rightAnchor = right;
            var box = new GameObject("box");
            box.transform.position = Vector3.zero;
            tc.Begin(TaskController.TaskType.Carry, new[] { Vector3.zero }, box);
            tc.OnIntent("both", 0.016);
            Assert.AreEqual(TaskController.Stage.Approach, tc.CurrentStage,
                            "anchors off their grip points must not latch the grip");
            tc.End();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(left.gameObject);
            UnityEngine.Object.DestroyImmediate(right.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
        }

        [Test]
        public void TaskController_Handoff_EntersTransferAndNeverDropsOnRelease()
        {
            // DHS finding 3 regression: the handoff path must pass through the
            // Transfer stage, and SenderReleaseAfterReceiverSecure must NOT drop
            // the object (ownership passes to the receiver; it stays transform-held).
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var left = new GameObject("l").transform;
            var right = new GameObject("r").transform;
            tc.leftGripOffset = new Vector3(0.25f, 0, 0);    // production offsets
            tc.rightGripOffset = new Vector3(-0.25f, 0, 0);
            left.position = tc.leftGripOffset;               // anchors on their grips
            right.position = tc.rightGripOffset;
            tc.leftAnchor = left;
            tc.rightAnchor = right;
            var box = new GameObject("box");
            box.AddComponent<Rigidbody>();
            tc.Begin(TaskController.TaskType.Handoff,
                     new[] { Vector3.zero, new Vector3(2f, 0, 0) }, box);
            // anchors on their grip points -> sender latches, enters Lift
            tc.OnIntent("both", 0.016);
            Assert.AreEqual(TaskController.Stage.Lift, tc.CurrentStage);
            // big dt jumps to the path midpoint -> handoff must enter Transfer
            tc.OnIntent("both", 1.0);
            Assert.AreEqual(TaskController.Stage.Transfer, tc.CurrentStage,
                            "handoff must pass through Transfer at the midpoint");
            // receiver at its grip point, stable -> a valid RIGHT intent latches it
            right.position = box.transform.position + tc.rightGripOffset;
            tc.OnIntent("rest", 0.1);
            Assert.AreEqual(TaskController.Stage.Transfer, tc.CurrentStage,
                            "rest must never confirm the receiver");
            tc.OnIntent("right", 0.1);
            tc.OnIntent("right", 0.1);
            tc.OnIntent("right", 0.2);
            Assert.AreEqual(TaskController.Stage.Follow, tc.CurrentStage,
                            "receiver secure must pass ownership and resume following");
            Assert.IsTrue(box.GetComponent<Rigidbody>().isKinematic,
                          "object must stay owned (never dropped) through handoff");
            tc.End();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(left.gameObject);
            UnityEngine.Object.DestroyImmediate(right.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
        }

        [Test]
        public void TaskController_ResetRestoresPoseAndPhysics()
        {
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var box = new GameObject("box");
            box.transform.position = new Vector3(0, 0.06f, 0);  // resting on the table
            box.AddComponent<Rigidbody>();
            tc.Begin(TaskController.TaskType.Carry, new[] { Vector3.zero }, box);
            box.transform.position = new Vector3(5, 5, 5);      // dragged away by task
            tc.End();
            Assert.AreEqual(TaskController.Stage.Idle, tc.CurrentStage);
            Assert.AreEqual(new Vector3(0, 0.06f, 0), box.transform.position,
                            "reset must put the box back on its resting spot");
            Assert.IsFalse(box.GetComponent<Rigidbody>().isKinematic,
                           "reset must restore physics ownership");
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(box);
        }

        [Test]
        public void ReplayController_Restart_FiresOnReset()
        {
            var go = new GameObject("rc");
            go.AddComponent<ReplaySource>();
            var rc = go.AddComponent<ReplayController>();
            bool fired = false;
            rc.OnReset += () => fired = true;
            rc.Restart();
            Assert.IsTrue(fired, "Restart must broadcast reset to task/UI subscribers");
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void ReplaySource_LoadFile_RejectsMissingAndRemote()
        {
            // v1: local Windows JSONL only; no UnityWebRequest module
            Assert.Throws<System.IO.IOException>(() =>
                ReplaySource.LoadFile("Z:/definitely/missing/replay.jsonl"));
            Assert.Throws<System.IO.IOException>(() =>
                ReplaySource.LoadFile("https://example.org/replay.jsonl"));
        }

        [Test]
        public void ReplayUIBindings_SerializedRefs_StartTasksAfterReenable()
        {
            // Integration regression: task buttons must work through serialized
            // component references alone — no editor lambdas and no delegates
            // serialized into the scene.
            // EditMode semantics: Unity does NOT call OnEnable/OnDisable on plain
            // MonoBehaviours outside Play mode (we deliberately avoid
            // ExecuteAlways), so this test calls the public BindButtons() pair —
            // the exact statements OnEnable/OnDisable run in the player. Real
            // re-enable and scene save/reload are validated by Codex in-scene.
            var rcGo = new GameObject("rc");
            rcGo.AddComponent<ReplaySource>();
            var rc = rcGo.AddComponent<ReplayController>();
            var tasksGo = new GameObject("tasks");
            var tc = tasksGo.AddComponent<TaskController>();
            tc.replay = rc;

            var host = new GameObject("bindingsHost");
            var b = host.AddComponent<ReplayUIBindings>();
            b.ui = host.AddComponent<DemoUI>();
            b.tasks = tc;                                    // serialized scene ref
            b.carryButton = MakeButton("BtnCarry");
            b.handoffL2RButton = MakeButton("BtnHandoffL2R");
            b.handoffR2LButton = MakeButton("BtnHandoffR2L");

            var box = new GameObject("CarryBox");            // found by name at runtime
            box.AddComponent<Rigidbody>();
            var zone = new GameObject("DropZone");           // release requires a designated zone
            zone.transform.position = new Vector3(1f, 0f, 0f);

            b.BindButtons();                                 // == OnEnable work
            host.SetActive(false);                           // OnDisable unbinds
            host.SetActive(true);                            // re-activate...
            b.BindButtons();                                 // ...rebind like OnEnable
            b.carryButton.onClick.Invoke();
            Assert.AreEqual(TaskController.TaskType.Carry, tc.Type,
                            "carry button must start the task via serialized refs");
            tc.End();

            b.handoffR2LButton.onClick.Invoke();
            Assert.AreEqual(TaskController.TaskType.Handoff, tc.Type);
            Assert.IsFalse(tc.LeftIsSender,
                            "R2L entry must make the right hand the sender");

            UnityEngine.Object.DestroyImmediate(rcGo);
            UnityEngine.Object.DestroyImmediate(tasksGo);
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(b.carryButton.gameObject);
            UnityEngine.Object.DestroyImmediate(b.handoffL2RButton.gameObject);
            UnityEngine.Object.DestroyImmediate(b.handoffR2LButton.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
            UnityEngine.Object.DestroyImmediate(zone);
        }

        [Test]
        public void Place_RequiresContinuousRestInsideDropZone()
        {
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var left = new GameObject("l").transform;
            var right = new GameObject("r").transform;
            tc.leftGripOffset = new Vector3(0.25f, 0, 0);
            tc.rightGripOffset = new Vector3(-0.25f, 0, 0);
            left.position = tc.leftGripOffset;
            right.position = tc.rightGripOffset;
            tc.leftAnchor = left; tc.rightAnchor = right;
            var box = new GameObject("box");
            box.AddComponent<Rigidbody>();
            var zone = new GameObject("zone").transform;
            zone.position = Vector3.zero;
            tc.dropZone = zone;
            tc.Begin(TaskController.TaskType.Carry, Array.Empty<Vector3>(), box);
            tc.OnIntent("both", 0.016);
            tc.OnIntent("rest", 0.1);
            Assert.AreEqual(TaskController.Stage.Place, tc.CurrentStage,
                            "one short rest frame must not release");
            tc.OnIntent("rest", 0.9);
            Assert.AreEqual(TaskController.Stage.Done, tc.CurrentStage,
                            "0.8s continuous valid rest must release");
            tc.End();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(left.gameObject);
            UnityEngine.Object.DestroyImmediate(right.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
            UnityEngine.Object.DestroyImmediate(zone.gameObject);
        }

        [Test]
        public void Place_WithoutDropZone_NeverReleases()
        {
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var left = new GameObject("l").transform;
            var right = new GameObject("r").transform;
            tc.leftGripOffset = new Vector3(0.25f, 0, 0);
            tc.rightGripOffset = new Vector3(-0.25f, 0, 0);
            left.position = tc.leftGripOffset;
            right.position = tc.rightGripOffset;
            tc.leftAnchor = left; tc.rightAnchor = right;
            var box = new GameObject("box");
            box.AddComponent<Rigidbody>();
            tc.Begin(TaskController.TaskType.Carry, Array.Empty<Vector3>(), box);
            tc.OnIntent("both", 0.016);
            for (int i = 0; i < 20; i++) tc.OnIntent("rest", 0.1);
            Assert.AreEqual(TaskController.Stage.Place, tc.CurrentStage,
                            "missing drop zone must disable release");
            Assert.IsTrue(box.GetComponent<Rigidbody>().isKinematic,
                          "object must remain held without a designated zone");
            tc.End();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(left.gameObject);
            UnityEngine.Object.DestroyImmediate(right.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
        }

        [Test]
        public void TaskInput_InvalidOrLowConfidence_NeverGrips()
        {
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var left = new GameObject("l").transform;
            var right = new GameObject("r").transform;
            tc.leftGripOffset = new Vector3(0.25f, 0, 0);
            tc.rightGripOffset = new Vector3(-0.25f, 0, 0);
            left.position = tc.leftGripOffset;
            right.position = tc.rightGripOffset;
            tc.leftAnchor = left; tc.rightAnchor = right;
            var box = new GameObject("box");
            box.AddComponent<Rigidbody>();
            tc.Begin(TaskController.TaskType.Carry, Array.Empty<Vector3>(), box);
            tc.OnIntent("both", 0.016, inputValid: false, confidence: 1f);
            Assert.AreEqual(TaskController.Stage.Approach, tc.CurrentStage);
            tc.OnIntent("both", 0.016, inputValid: true, confidence: 0.2f);
            Assert.AreEqual(TaskController.Stage.Approach, tc.CurrentStage);
            tc.End();
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(left.gameObject);
            UnityEngine.Object.DestroyImmediate(right.gameObject);
            UnityEngine.Object.DestroyImmediate(box);
        }

        static UnityEngine.UI.Button MakeButton(string name)
        {
            var go = new GameObject(name);
            return go.AddComponent<UnityEngine.UI.Button>();
        }
    }
}
