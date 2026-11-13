using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ClawMachine.Tests.EditMode
{
    public sealed class CoreAuditTests
    {
        [Test]
        public void SurfaceContact_RequiresAnOutsideCenterAndRejectsDeepPenetration()
        {
            using (var fixture = new GeometryFixture())
            {
                SphereCollider pad = fixture.Pads[0];
                pad.transform.position = new Vector3(0.126f, 0.03f, 0);
                Physics.SyncTransforms();
                SurfaceContact touching = GraspContactEvaluator.Sample(pad, fixture.Box, 0.008f, 0.003f);
                Assert.IsTrue(touching.Valid);
                Assert.IsFalse(touching.Penetrating);
                Assert.AreEqual(0.001f, touching.Gap, 0.0001f);
                Assert.Greater(Vector3.Dot(touching.Normal, Vector3.right), 0.99f);

                pad.transform.position = new Vector3(0.115f, 0.03f, 0); Physics.SyncTransforms();
                SurfaceContact deep = GraspContactEvaluator.Sample(pad, fixture.Box, 0.008f, 0.003f);
                Assert.IsTrue(deep.Penetrating);
                Assert.IsFalse(deep.Valid);
                Assert.Greater(deep.Depth, 0.003f);

                pad.transform.position = Vector3.zero; Physics.SyncTransforms();
                SurfaceContact inside = GraspContactEvaluator.Sample(pad, fixture.Box, 0.008f, 0.003f);
                Assert.IsTrue(inside.Penetrating, "ClosestPoint(center)==center is an invalid inside probe, never a contact.");
                Assert.IsFalse(inside.Valid);
            }
        }

        [Test]
        public void Grasp_NeedsOpposingNormalsAndSpanRatherThanTwoSameSideTouches()
        {
            using (var fixture = new GeometryFixture())
            {
                fixture.PositionOpposingPads();
                GraspFailure opposed = GraspContactEvaluator.Evaluate(new Vector3(0, 0.03f, 0), fixture.Prize, fixture.Pads, out int contacts);
                Assert.AreEqual(GraspFailure.None, opposed);
                Assert.AreEqual(2, contacts);

                fixture.Pads[1].transform.position = new Vector3(0.126f, 0.03f, 0.03f); Physics.SyncTransforms();
                Assert.AreEqual(GraspFailure.UnstableGrip,
                    GraspContactEvaluator.Evaluate(new Vector3(0, 0.03f, 0), fixture.Prize, fixture.Pads, out contacts));
                Assert.AreEqual(2, contacts);

                fixture.Pads[1].transform.position = Vector3.zero; Physics.SyncTransforms();
                Assert.AreEqual(GraspFailure.Penetrating,
                    GraspContactEvaluator.Evaluate(new Vector3(0, 0.03f, 0), fixture.Prize, fixture.Pads, out _));
                fixture.PositionOpposingPads();
                Assert.AreEqual(GraspFailure.Misaligned,
                    GraspContactEvaluator.Evaluate(new Vector3(0.1f, 0.03f, 0), fixture.Prize, fixture.Pads, out _));
            }
        }

        [Test]
        public void Planner_UsesRaycastSurfaceAndAllPrizeClearanceInsteadOfCenterOfMass()
        {
            using (var fixture = new GeometryFixture())
            {
                fixture.Body.position = new Vector3(0, 0.3f, 0);
                fixture.Body.centerOfMass = new Vector3(0, 0.09f, 0);
                var decoration = new GameObject("Compound high feature"); decoration.transform.SetParent(fixture.Prize.transform, false);
                decoration.transform.localPosition = new Vector3(0, 0.27f, 0);
                var upper = decoration.AddComponent<SphereCollider>(); upper.radius = 0.05f;
                Set(fixture.Prize, "contactColliders", new Collider[] { fixture.Box, upper });
                GameObject otherObject = Object.Instantiate(fixture.Prize.gameObject, fixture.Root.transform);
                PrizePhysics other = otherObject.GetComponent<PrizePhysics>();
                other.Configure(new PrizeSpawnDefinition { prizeId = "tall-neighbor", displayName = "Neighbor", spawnPosition = Vector3.zero, graspProfile = fixture.Profile }, 10);
                other.Body.position = new Vector3(0.6f, 1, 0);
                Physics.SyncTransforms();
                Pose start = fixture.Space.HomePose;
                PrizePhysics[] all = { fixture.Prize, other };
                GraspPlan first = GraspPlanner.Plan(fixture.Space, start, all);
                Assert.IsTrue(first.IsSafe);
                Assert.IsTrue(first.HasSurface);
                Assert.AreEqual(fixture.Prize, first.Candidate);
                Assert.AreEqual(fixture.Box.bounds.max.y - fixture.Profile.surfaceCaptureDepth, first.ContactPose.position.y, 0.0001f);
                Assert.GreaterOrEqual(first.SafeApproach.position.y, fixture.Prize.AllBounds.max.y + fixture.Profile.approachClearance - 0.0001f);
                Assert.GreaterOrEqual(first.SafeApproach.position.y, other.AllBounds.max.y + fixture.Profile.approachClearance - 0.0001f,
                    "Safe approach must clear a taller neighboring prize even when the ray targets the lower box.");
                Assert.Greater(first.SafeApproach.position.y, first.ContactPose.position.y);
                fixture.Body.centerOfMass = new Vector3(0, -0.09f, 0);
                GraspPlan changedMass = GraspPlanner.Plan(fixture.Space, start, all);
                Assert.AreEqual(first.ContactPose.position, changedMass.ContactPose.position, "Moving only COM cannot change the contact surface.");
            }
        }

        [Test]
        public void Planner_RejectsTallCompoundPrizeWhenNoSafeClearanceExists()
        {
            using (var fixture = new GeometryFixture())
            {
                var tall = new GameObject("Feature above workspace"); tall.transform.SetParent(fixture.Prize.transform, false);
                tall.transform.localPosition = new Vector3(0, 2, 0);
                var collider = tall.AddComponent<SphereCollider>(); collider.radius = 0.1f;
                Set(fixture.Prize, "contactColliders", new Collider[] { fixture.Box, collider });
                Physics.SyncTransforms();
                GraspPlan plan = GraspPlanner.Plan(fixture.Space, fixture.Space.HomePose, new[] { fixture.Prize });
                Assert.IsFalse(plan.IsSafe, "A clamp cannot turn an impossible clearance into a valid approach.");
                Assert.AreEqual(fixture.Space.HomePose.position, plan.SafeApproach.position);
                Assert.AreEqual(fixture.Space.HomePose.position, plan.ContactPose.position);
            }
        }

        [Test]
        public void SimulatorGuard_RejectsHardwareOverspeedBadWorkspaceAndCommandsAfterStop()
        {
            using (var fixture = new GeometryFixture())
            {
                var driver = new AuditDriverContract { IsSimulation = true };
                var guard = new ManipulatorSafetyGuard(driver, fixture.Space);
                Assert.IsTrue(guard.Move(fixture.Space.HomePose, 0.35f));
                Assert.AreEqual(1, driver.MoveCalls);
                Assert.IsFalse(guard.Move(fixture.Space.HomePose, 0.7f));
                Assert.IsFalse(guard.Move(new Pose(Vector3.one * 100, Quaternion.identity), 0.35f));
                Assert.IsFalse(guard.Jog(new Vector3(float.NaN, 0, 0)));
                guard.EmergencyStop();
                Assert.IsTrue(guard.Stopped);
                Assert.IsFalse(guard.Move(fixture.Space.HomePose, 0.35f));
                Assert.IsFalse(guard.Jog(Vector3.right * 0.2f));
                Assert.AreEqual(1, driver.MoveCalls);
                guard.ResetStop(); Assert.IsTrue(guard.Move(fixture.Space.HomePose, 0.35f));
                driver.IsSimulation = false;
                Assert.IsFalse(guard.Ready, "The game refuses an executor that does not explicitly declare simulation.");
                Assert.IsFalse(guard.Move(fixture.Space.HomePose, 0.35f));
            }
        }

        [Test]
        public void AlternateSimulator_RejectsInvalidPoseAndSupportsWorldSpaceResetAndStop()
        {
            using (var fixture = new GeometryFixture())
            {
                fixture.Body.isKinematic = true;
                var gripper = fixture.Root.AddComponent<GripperController>();
                var alternate = fixture.Root.AddComponent<SimulatedRobotArmDriver>();
                Set(alternate, "simulatedTool", fixture.Body); Set(alternate, "taskSpace", fixture.Space); Set(alternate, "gripper", gripper);
                Assert.IsTrue(alternate.IsConfigured);
                Pose invalidRotation = new Pose(fixture.Space.HomePose.position, new Quaternion(float.NaN, 0, 0, 1));
                Assert.IsFalse(alternate.TryMoveTo(invalidRotation, 0.35f));
                Assert.IsFalse(alternate.TryReset(invalidRotation));
                Assert.IsFalse(alternate.TryMoveTo(fixture.Space.HomePose, float.PositiveInfinity));
                Assert.IsFalse(alternate.TryJog(Vector3.right));
                Assert.IsTrue(alternate.TryReset(fixture.Space.HomePose));
                Assert.AreEqual(fixture.Space.HomePose.position, alternate.CurrentToolPose.position);
                Assert.IsTrue(alternate.TryMoveTo(fixture.Space.PoseAtHeight(Vector3.zero, 1), 0.35f));
                Assert.IsTrue(alternate.IsBusy);
                alternate.Stop(); Assert.IsFalse(alternate.IsBusy);
                Assert.AreEqual(Vector3.zero, alternate.CurrentVelocity);
            }
        }

        [Test]
        public void PredictedToolMotion_RejectsFloorAndCabinetPenetrationBeforeMoving()
        {
            var tool = new GameObject("Predicted tool collision fixture");
            var floor = new GameObject("Floor");
            var wall = new GameObject("Cabinet wall");
            try
            {
                tool.transform.position = new Vector3(0, 0.6f, 0);
                var body = tool.AddComponent<Rigidbody>(); body.isKinematic = true;
                var gripper = tool.AddComponent<GripperController>();
                var fingers = new Transform[3]; var pads = new SphereCollider[3];
                for (int i = 0; i < 3; i++)
                {
                    var finger = new GameObject("Finger " + i); finger.transform.SetParent(tool.transform, false);
                    finger.transform.localPosition = new Vector3(0, 0, (i - 1) * 0.2f);
                    fingers[i] = finger.transform; pads[i] = finger.AddComponent<SphereCollider>(); pads[i].radius = 0.025f;
                }
                floor.transform.position = new Vector3(0, 0.5f, 0);
                var floorCollider = floor.AddComponent<BoxCollider>(); floorCollider.size = new Vector3(1, 0.1f, 1);
                wall.transform.position = new Vector3(0.15f, 0.6f, 0);
                var wallCollider = wall.AddComponent<BoxCollider>(); wallCollider.size = new Vector3(0.05f, 0.5f, 1);
                Set(gripper, "toolBody", body); Set(gripper, "fingers", fingers);
                Set(gripper, "contactColliders", pads); Set(gripper, "physicalColliders", new Collider[] { pads[0], pads[1], pads[2] });
                Set(gripper, "environmentColliders", new Collider[] { floorCollider, wallCollider });
                Physics.SyncTransforms();
                Assert.IsTrue(gripper.IsConfigured);
                Assert.IsTrue(gripper.CanDescend(Vector3.zero, new PrizePhysics[0]));
                Assert.IsFalse(gripper.CanDescend(Vector3.down * 0.06f, new PrizePhysics[0]), "A predicted floor penetration must stop descent.");
                Assert.IsFalse(gripper.CanDescend(Vector3.right * 0.12f, new PrizePhysics[0]), "A predicted cabinet penetration must stop motion.");
                Assert.AreEqual(new Vector3(0, 0.6f, 0), tool.transform.position, "Prediction itself must not move the tool.");
            }
            finally { Object.DestroyImmediate(tool); Object.DestroyImmediate(floor); Object.DestroyImmediate(wall); }
        }

        [Test]
        public void Task_AcceptsAWorldPoseExecutorWithoutGantryDependencyAndRejectsLiveSwap()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ClawMachine/Prefabs/Machine/ClawMachine.prefab");
            Assert.NotNull(template);
            GameObject root = Object.Instantiate(template);
            try
            {
                GraspTaskController task = root.GetComponentInChildren<GraspTaskController>(true);
                Assert.NotNull(task);
                var driver = root.AddComponent<AuditManipulatorStub>();
                Assert.IsTrue(task.SetDriver(driver));
                Assert.AreSame(driver, task.Driver);
                Assert.IsTrue(task.StartChallenge());
                Assert.AreEqual(1, driver.ResetCalls);
                Assert.AreEqual(task.TaskSpace.HomePose.position, driver.CurrentToolPose.position);
                Assert.IsTrue(task.StartGrab());
                Assert.AreEqual(GraspState.Approaching, task.Session.State);
                Assert.AreEqual(1, driver.MoveCalls);
                Assert.IsTrue(task.TaskSpace.Contains(driver.LastCommand.position));
                var other = root.AddComponent<AuditManipulatorStub>();
                Assert.IsFalse(task.SetDriver(other), "Executor changes are blocked in an active attempt.");
                task.Cancel(true);
                other.Simulation = false;
                Assert.IsFalse(task.SetDriver(other), "No hardware driver is authorized by the game client.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Task_ExecutorFaultBeforeOrAfterTick_AbortsWithoutClosing()
        {
            foreach (bool faultOnTick in new[] { false, true })
            {
                GameObject root = CloneMachine(out GraspTaskController task, out AuditManipulatorStub driver);
                try
                {
                    Assert.IsTrue(task.StartChallenge()); Assert.IsTrue(task.StartGrab());
                    bool closed = false;
                    task.Session.Changed += state => closed |= state == GraspState.Closing;
                    driver.FaultOnTick = faultOnTick;
                    if (!faultOnTick) driver.ReportedStatus = ManipulatorStatus.Error;
                    Tick(task);
                    Assert.AreEqual(GraspState.Error, task.Session.State);
                    Assert.AreEqual(GraspFailure.Configuration, task.Session.Failure);
                    Assert.AreEqual(faultOnTick ? 1 : 0, driver.TickCalls, "Fault feedback is checked on both sides of executor Tick.");
                    Assert.IsFalse(closed);
                    Assert.IsFalse(task.Gripper.IsHolding);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void Task_IdleAtWrongPose_TimesOutRatherThanPretendingToArrive()
        {
            float oldFixedDelta = Time.fixedDeltaTime;
            GameObject root = CloneMachine(out GraspTaskController task, out AuditManipulatorStub driver);
            try
            {
                Time.fixedDeltaTime = 0.02f;
                Assert.IsTrue(task.StartChallenge());
                driver.HoldCommandPosition = true;
                Assert.IsTrue(task.StartGrab());
                Assert.AreEqual(ManipulatorStatus.Idle, driver.Status);
                Assert.Greater(Vector3.Distance(driver.CurrentToolPose.position, driver.LastCommand.position), 0.008f);
                bool closed = false;
                task.Session.Changed += state => closed |= state == GraspState.Closing;
                for (int i = 0; i < 110 && task.Session.State != GraspState.Error; i++) Tick(task);
                Assert.AreEqual(GraspState.Error, task.Session.State);
                Assert.AreEqual(GraspFailure.Timeout, task.Session.Failure);
                Assert.IsFalse(closed);
                Assert.AreEqual(1, driver.MoveCalls);
                Assert.IsFalse(task.Gripper.IsHolding);
            }
            finally { Time.fixedDeltaTime = oldFixedDelta; Object.DestroyImmediate(root); }
        }

        [Test]
        public void ChallengeCatalog_HasThreeBoundTargetsAndAnOffsetMassProfile()
        {
            string[] ids = { "C01", "C02", "C03" };
            string[] targets = { "mint-parcel", "cloud-bunny", "amber-fox" };
            for (int i = 0; i < ids.Length; i++)
            {
                ClawChallengeDefinition challenge = AssetDatabase.LoadAssetAtPath<ClawChallengeDefinition>("Assets/ClawMachine/Challenges/" + ids[i] + ".asset");
                Assert.NotNull(challenge, ids[i]);
                Assert.AreEqual(ids[i], challenge.challengeId);
                Assert.AreEqual(targets[i], challenge.targetId);
                Assert.IsNotEmpty(challenge.spawns);
                PrizeProfile targetProfile = null;
                foreach (PrizeSpawnDefinition spawn in challenge.spawns)
                {
                    Assert.NotNull(spawn.graspProfile); Assert.IsNotEmpty(spawn.prizeId);
                    if (spawn.prizeId == challenge.targetId) targetProfile = spawn.graspProfile;
                }
                Assert.NotNull(targetProfile, "Every target ID must have a configured spawn/profile.");
                if (i == 2) Assert.Greater(targetProfile.centerOfMass.sqrMagnitude, 0.0001f);
            }
        }

        private sealed class GeometryFixture : System.IDisposable
        {
            public readonly GameObject Root = new GameObject("Contact and planning fixture");
            public readonly PrizePhysics Prize;
            public readonly Rigidbody Body;
            public readonly BoxCollider Box;
            public readonly PrizeProfile Profile;
            public readonly SphereCollider[] Pads = new SphereCollider[3];
            public readonly ClawTaskSpace Space;
            public GeometryFixture()
            {
                var volume = Root.AddComponent<BoxCollider>(); volume.enabled = false;
                volume.center = new Vector3(0, 1, 0); volume.size = new Vector3(2, 2, 2);
                var release = new GameObject("Release"); release.transform.SetParent(Root.transform, false);
                Space = Root.AddComponent<ClawTaskSpace>();
                Set(Space, "machineFrame", Root.transform); Set(Space, "workspaceVolume", volume); Set(Space, "releasePoint", release.transform);
                var prizeObject = new GameObject("Box prize"); prizeObject.transform.SetParent(Root.transform, false);
                Box = prizeObject.AddComponent<BoxCollider>(); Box.size = new Vector3(0.22f, 0.24f, 0.20f);
                Body = prizeObject.AddComponent<Rigidbody>();
                Prize = prizeObject.AddComponent<PrizePhysics>();
                Set(Prize, "body", Body); Set(Prize, "mainCollider", Box); Set(Prize, "machineRoot", Root.transform);
                Profile = ScriptableObject.CreateInstance<PrizeProfile>();
                Prize.Configure(new PrizeSpawnDefinition { prizeId = "box", displayName = "Box", spawnPosition = Vector3.zero, graspProfile = Profile }, 10);
                Body.position = Vector3.zero;
                for (int i = 0; i < Pads.Length; i++)
                {
                    var padObject = new GameObject("Pad " + i); padObject.transform.SetParent(Root.transform, false);
                    Pads[i] = padObject.AddComponent<SphereCollider>(); Pads[i].radius = 0.015f;
                }
                PositionOpposingPads();
            }
            public void PositionOpposingPads()
            {
                Pads[0].transform.position = new Vector3(0.126f, 0.03f, 0);
                Pads[1].transform.position = new Vector3(-0.126f, 0.03f, 0);
                Pads[2].transform.position = new Vector3(0, 0.03f, 0.3f);
                Physics.SyncTransforms();
            }
            public void Dispose() { Object.DestroyImmediate(Root); Object.DestroyImmediate(Profile); }
        }
        private sealed class AuditDriverContract : IManipulatorDriver
        {
            public bool IsConfigured => true;
            public bool IsSimulation { get; set; }
            public Pose CurrentToolPose => Pose.identity;
            public Vector3 CurrentVelocity => Vector3.zero;
            public ManipulatorStatus Status => ManipulatorStatus.Idle;
            public string Error => "";
            public bool IsBusy => false;
            public int MoveCalls;
            public bool TryMoveTo(Pose pose, float speed) { MoveCalls++; return true; }
            public bool TryJog(Vector3 velocity) => true;
            public bool TryReset(Pose pose) => true;
            public bool TrySetGripper(float openness) => true;
            public void Tick(float dt) { }
            public void Stop() { }
        }
        private static void Set(object owner, string field, object value)
        {
            FieldInfo info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, field); info.SetValue(owner, value);
        }
        private static GameObject CloneMachine(out GraspTaskController task, out AuditManipulatorStub driver)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ClawMachine/Prefabs/Machine/ClawMachine.prefab");
            Assert.NotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            task = root.GetComponentInChildren<GraspTaskController>(true);
            Assert.NotNull(task);
            driver = root.AddComponent<AuditManipulatorStub>();
            Assert.IsTrue(task.SetDriver(driver));
            return root;
        }
        private static void Tick(GraspTaskController task)
        {
            MethodInfo tick = typeof(GraspTaskController).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(tick); tick.Invoke(task, null);
        }
    }

}
