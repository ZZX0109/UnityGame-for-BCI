using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class FirstPersonPrefabTests
    {
        [Test]
        public void BothAnatomicalWristsReachTheirGripPosesInAHostWithoutTheDemo()
        {
            var origin = new GameObject("Host tracking space");
            try
            {
                origin.transform.SetPositionAndRotation(new Vector3(3, 1, 2), Quaternion.Euler(0, 73, 0));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab");
                Assert.IsNotNull(prefab);
                var root = UnityEngine.Object.Instantiate(prefab, origin.transform);
                Assert.IsEmpty(root.GetComponentsInChildren<Camera>(true));
                Assert.IsEmpty(root.GetComponentsInChildren<ReplayController>(true));
                Assert.IsEmpty(root.GetComponentsInChildren<TaskController>(true));
                var pair = root.GetComponent<ControllerHandPair>();
                for (int i = 0; i < 2; i++)
                {
                    bool left = i == 0;
                    var wrist = left ? pair.leftWrist : pair.rightWrist;
                    var driver = left ? pair.leftHand : pair.rightHand;
                    Assert.AreEqual(left, driver.IsMirrored);
                    wrist.trackingSpace = origin.transform;
                    var pose = new Pose(new Vector3(left ? -0.2f : 0.2f, 1, 0.4f), Quaternion.Euler(15, 22, 5));
                    Assert.IsTrue(wrist.ApplyTrackedPose(pose, true));
                    var bones = driver.GetComponentsInChildren<Transform>(true);
                    Transform Bone(string name) => Array.Find(bones, t => ElenaNativeRigProfile.MatchesBone(t.name, name));
                    var pivot = Bone("hand.R_02").position;
                    Assert.Less(Vector3.Distance(pivot, origin.transform.TransformPoint(pose.position)), 0.001f);
                    var forward = Bone("middle_01").position - pivot;
                    Assert.Less(Vector3.Angle(forward, wrist.transform.forward), 0.5f);
                    var across = (Bone("pinky_01").position - Bone("index_01").position).normalized;
                    Assert.Greater(Vector3.Dot(across, wrist.transform.right) * (left ? -1f : 1f), 0.5f);
                }
                pair.leftWrist.ApplyTrackedPose(Pose.identity, false);
                Assert.IsFalse(pair.leftHand.gameObject.activeInHierarchy);
                Assert.IsTrue(pair.rightHand.gameObject.activeInHierarchy);
                Assert.IsTrue(origin.activeInHierarchy);
            }
            finally { UnityEngine.Object.DestroyImmediate(origin); }
        }
    }
}
