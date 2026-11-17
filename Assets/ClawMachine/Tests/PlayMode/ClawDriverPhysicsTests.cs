using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ClawMachine.Tests.PlayMode
{
    // Local PhysicsScene creation and Simulate require a real Play-mode runner.
    public sealed class ClawDriverPhysicsTests
    {
        private readonly List<AsyncOperation> pendingUnloads = new List<AsyncOperation>();
        [UnityTearDown]
        public IEnumerator FinishIsolatedSceneUnloads()
        {
            foreach (AsyncOperation operation in pendingUnloads) if (operation != null) while (!operation.isDone) yield return null;
            pendingUnloads.Clear();
        }
        [Test]
        public void Gantry_RejectsNonFinitePoseSpeedAndAllSixOutsideLimits()
        {
            using (var fixture = new DriverFixture(pendingUnloads))
            {
                GantryDriver driver = fixture.Driver;
                Pose valid = driver.WorldPose(driver.Workspace.center);
                foreach (float speed in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Assert.IsFalse(driver.TryMoveTo(valid, speed), "Invalid speed " + speed);
                Assert.IsFalse(driver.TryMoveTo(new Pose(new Vector3(float.NaN, 1, 0), Quaternion.identity), 0.35f));
                Assert.IsFalse(driver.TryMoveTo(new Pose(new Vector3(0, float.PositiveInfinity, 0), Quaternion.identity), 0.35f));
                Assert.IsFalse(driver.TryMoveTo(new Pose(valid.position, new Quaternion(float.NaN, 0, 0, 1)), 0.35f));
                Assert.IsFalse(driver.TryReset(new Pose(valid.position, new Quaternion(float.NaN, 0, 0, 1))));
                Assert.IsFalse(driver.TryReset(new Pose(valid.position, new Quaternion(0, 0, 0, 0))));
                Bounds limits = driver.Workspace;
                for (int axis = 0; axis < 3; axis++)
                {
                    Vector3 below = limits.center; below[axis] = limits.min[axis] - 0.01f;
                    Vector3 above = limits.center; above[axis] = limits.max[axis] + 0.01f;
                    Assert.IsFalse(driver.TryMoveTo(driver.WorldPose(below), 0.35f), "Below axis " + axis);
                    Assert.IsFalse(driver.TryMoveTo(driver.WorldPose(above), 0.35f), "Above axis " + axis);
                }
                Assert.IsFalse(driver.IsBusy);
                driver.ResetPose(Vector3.one * 100);
                Assert.Less(Vector3.Distance(limits.max, driver.LocalPosition), 0.00001f);
                driver.ResetPose(Vector3.one * -100);
                Assert.Less(Vector3.Distance(limits.min, driver.LocalPosition), 0.00001f);
            }
        }

        [Test]
        public void Gantry_Stop_CancelsMotionAndLeavesNoDeferredMovement()
        {
            using (var fixture = new DriverFixture(pendingUnloads))
            {
                GantryDriver driver = fixture.Driver;
                Assert.IsTrue(driver.TryMoveTo(driver.WorldPose(driver.Workspace.center + Vector3.right * 0.2f), 0.35f));
                driver.Step(0.02f); fixture.Physics.Simulate(0.02f);
                Assert.IsTrue(driver.IsBusy);
                Assert.Greater(driver.LocalPosition.x, 0);
                driver.Stop();
                Vector3 stopped = driver.CurrentToolPose.position;
                for (int i = 0; i < 10; i++) { driver.Step(0.02f); fixture.Physics.Simulate(0.02f); }
                Assert.IsFalse(driver.IsBusy);
                Assert.Less(Vector3.Distance(stopped, driver.CurrentToolPose.position), 0.000001f);
            }
        }

        [Test]
        public void HeldWorldJog_At144HzCommandsAnd50HzPhysicsMovesContinuously()
        {
            float oldFixedDelta = Time.fixedDeltaTime;
            try
            {
                Time.fixedDeltaTime = 0.02f;
                using (var fixture = new DriverFixture(pendingUnloads))
                {
                    int fixedSteps = 0;
                    float lastX = fixture.Driver.LocalPosition.x;
                    for (int render = 1; render <= 288; render++)
                    {
                        Assert.IsTrue(fixture.Driver.TryJog(Vector3.right * 0.35f));
                        float elapsed = render / 144f;
                        while (fixedSteps < 100 && (fixedSteps + 1) * 0.02f <= elapsed + 0.000001f)
                        {
                            fixture.Driver.Tick(0.02f); fixture.Physics.Simulate(0.02f); fixedSteps++;
                            float x = fixture.Driver.LocalPosition.x;
                            Assert.Greater(x, lastX, "A held direction must survive render frames without a physics tick.");
                            lastX = x;
                        }
                    }
                    Assert.AreEqual(100, fixedSteps);
                    Assert.Greater(fixture.Driver.LocalPosition.x, 0.5f, "The old per-frame tiny-target bug moved too slowly or stopped.");
                    Assert.LessOrEqual(fixture.Driver.LocalPosition.x, 0.70001f, "The simulator must obey the 0.35m/s speed bound.");
                    Assert.IsTrue(fixture.Driver.IsBusy);
                    Assert.LessOrEqual(fixture.Driver.CurrentVelocity.magnitude, 0.35001f);
                }
            }
            finally { Time.fixedDeltaTime = oldFixedDelta; }
        }

        private static void Set(object owner, string field, object value)
        {
            FieldInfo info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, field); info.SetValue(owner, value);
        }
        // An isolated physics scene avoids changing global Physics.autoSimulation or
        // advancing arbitrary objects in the scene currently open in the Editor.
        private sealed class DriverFixture : System.IDisposable
        {
            public readonly GameObject Root;
            public readonly GantryDriver Driver;
            public readonly GripperController Gripper;
            public readonly PhysicsScene Physics;
            private readonly Scene scene;
            private readonly List<AsyncOperation> pendingUnloads;
            public DriverFixture(List<AsyncOperation> pendingUnloads)
            {
                this.pendingUnloads = pendingUnloads;
                scene = SceneManager.CreateScene("Claw driver isolated fixture " + System.Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                Physics = scene.GetPhysicsScene();
                Root = new GameObject("Machine frame"); SceneManager.MoveGameObjectToScene(Root, scene);
                var volume = Root.AddComponent<BoxCollider>();
                volume.center = new Vector3(0, 1.25f, 0); volume.size = new Vector3(4, 0.9f, 4); volume.enabled = false;
                var tool = new GameObject("Tool"); tool.transform.SetParent(Root.transform);
                var body = tool.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
                Gripper = tool.AddComponent<GripperController>();
                Set(Gripper, "toolBody", body);
                Driver = Root.AddComponent<GantryDriver>();
                Set(Driver, "machineRoot", Root.transform); Set(Driver, "workspaceVolume", volume);
                Set(Driver, "toolBody", body); Set(Driver, "gripper", Gripper);
                Driver.ResetPose(volume.center);
                Assert.IsTrue(Driver.IsConfigured);
            }
            public void Dispose()
            {
                Object.DestroyImmediate(Root);
                if (scene.IsValid() && scene.isLoaded) pendingUnloads.Add(SceneManager.UnloadSceneAsync(scene));
            }
        }
    }
}
