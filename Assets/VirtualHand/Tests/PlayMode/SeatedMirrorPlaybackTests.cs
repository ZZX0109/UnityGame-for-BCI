#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class SeatedMirrorPlaybackTests
    {
        [TestCase(SeatedHandSide.Left)]
        [TestCase(SeatedHandSide.Right)]
        public void TrackedWristDrivesOppositeMeshAndTrackingLossClearsBoth(SeatedHandSide side)
        {
            var root = new GameObject("Test seated origin");
            string key = "Test.Mirror." + Guid.NewGuid().ToString("N");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(2f, 1f, -3f), Quaternion.Euler(0, 55f, 0));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab");
                var pair = Object.Instantiate(prefab, root.transform).GetComponent<ControllerHandPair>();
                pair.enabled = false;
                var fields = new SerializedObject(pair);
                fields.FindProperty("seatedHandSettingsPlayerPrefsKey").stringValue = key;
                fields.ApplyModifiedPropertiesWithoutUndo();
                var settings = SeatedHandSettings.CreateDefaults();
                settings.operatingHand = side;
                settings.displayMode = SeatedHandDisplayMode.Mirror;
                settings.leftWristOffset = new SeatedHandSettings.WristOffset(new Vector3(-.02f, .045f, .01f), Vector3.zero);
                settings.rightWristOffset = new SeatedHandSettings.WristOffset(new Vector3(.03f, .02f, -.04f), Vector3.zero);
                Assert.IsTrue(settings.Save(key));
                pair.sagittalPlane = root.transform;
                pair.enabled = true;
                bool left = side == SeatedHandSide.Left;
                var source = left ? pair.leftWrist : pair.rightWrist;
                var target = left ? pair.rightWrist : pair.leftWrist;
                var sample = new ControllerHandPair.GripSample { tracked = true, grip = .45f,
                    pose = new Pose(new Vector3(left ? -.17f : .17f, .9f, .42f), Quaternion.Euler(-18f, 21f, 9f)) };
                pair.ApplyControllerSamples(left ? sample : default, left ? default : sample);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, left ? pair.LeftSource : pair.RightSource);
                Assert.AreEqual(ControllerHandPair.PoseSource.Mirror, left ? pair.RightSource : pair.LeftSource);
                Assert.IsTrue(source.visualRoot.gameObject.activeInHierarchy && target.visualRoot.gameObject.activeInHierarchy);
                Vector3 ReflectPoint(Vector3 v) => v - 2 * Vector3.Dot(v-root.transform.position,root.transform.right)*root.transform.right;
                Assert.Less(Vector3.Distance(ReflectPoint(source.transform.position), target.transform.position), .0001f);
                Assert.Greater(Vector3.Dot(Vector3.Reflect(source.transform.forward, root.transform.right), target.transform.forward), .999f);
                Assert.Greater(Vector3.Dot(Vector3.Reflect(source.transform.up, root.transform.right), target.transform.up), .999f);
                foreach (var landmark in new[] {"thumb_03", "index_03", "pinky_03"})
                {
                    var a = Array.Find(source.visualRoot.GetComponentsInChildren<Transform>(true), x => ElenaNativeRigProfile.MatchesBone(x.name, landmark));
                    var b = Array.Find(target.visualRoot.GetComponentsInChildren<Transform>(true), x => ElenaNativeRigProfile.MatchesBone(x.name, landmark));
                    Assert.IsTrue(a && b, landmark);
                    Assert.Less(Vector3.Distance(ReflectPoint(a.position), b.position), .002f, landmark);
                }
                // Activity on the opposite physical controller must never sustain assistance.
                pair.ApplyControllerSamples(left ? default : sample, left ? sample : default);
                Assert.AreEqual(ControllerHandPair.PoseSource.Hidden, pair.LeftSource);
                Assert.AreEqual(ControllerHandPair.PoseSource.Hidden, pair.RightSource);
                Assert.IsFalse(source.visualRoot.gameObject.activeSelf || target.visualRoot.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); PlayerPrefs.DeleteKey(key); }
        }
    }
}
#endif
