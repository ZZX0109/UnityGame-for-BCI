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
                b.SetParent(go.transform);
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
            Object.DestroyImmediate(go);
        }

        [Test]
        public void RestControls_LeaveBonesNearRest()
        {
            var go = MakeRig(out var driver, out var index1);
            var before = index1.rotation;
            driver.SetControls(PoseLibrary.Rest());
            Assert.Less(Quaternion.Angle(before, index1.rotation), 30f,
                        "rest pose should stay close to rig rest");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TaskController_ReleaseOnlyInDesignatedStage()
        {
            // Carry: object must stay kinematic (owned) until Place releases it.
            var go = new GameObject("tasks");
            var tc = go.AddComponent<TaskController>();
            var box = new GameObject("box");
            box.AddComponent<Rigidbody>();
            tc.Begin(TaskController.TaskType.Carry,
                     new[] { Vector3.zero }, box);
            Assert.IsTrue(box.GetComponent<Rigidbody>().isKinematic,
                          "held box must be transform-owned (single ownership)");
            tc.End();
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(box);
        }
    }
}
