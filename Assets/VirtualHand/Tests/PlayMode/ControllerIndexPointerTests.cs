#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class ControllerIndexPointerTests
    {
        private const string PrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";

        [TestCase(SeatedHandSide.Left)]
        [TestCase(SeatedHandSide.Right)]
        public void RealPrefabUsesTrackedIndexAndRestoresRegularGrip(SeatedHandSide side)
        {
            var root = new GameObject("Index pointer prefab test origin");
            root.SetActive(false);
            string key = "Test.IndexPointer." + Guid.NewGuid().ToString("N");
            PlayerPrefs.DeleteKey(key);
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.IsNotNull(prefab);
                var settings = SeatedHandSettings.CreateDefaults();
                settings.operatingHand = side;
                settings.displayMode = SeatedHandDisplayMode.Mirror;
                Assert.IsTrue(settings.Save(key));

                GameObject instance = Object.Instantiate(prefab, root.transform);
                var pair = instance.GetComponent<ControllerHandPair>();
                Assert.IsNotNull(pair);
                pair.enabled = false;
                pair.trackingOrigin = root.transform;
                pair.sagittalPlane = root.transform;
                SetSettingsKey(pair, key);

                var pointer = instance.GetComponent<ControllerIndexPointer>();
                Assert.IsNotNull(pointer);
                Assert.AreSame(pair, pointer.handPair);
                Assert.IsNotNull(pointer.leftIndexAnatomy.tip);
                Assert.IsNotNull(pointer.rightIndexAnatomy.tip);
                pair.enabled = true;
                root.SetActive(true);

                var sample = new ControllerHandPair.GripSample
                {
                    tracked = true,
                    grip = 0.9f,
                    pose = new Pose(new Vector3(side == SeatedHandSide.Left ? -0.17f : 0.17f, 0.9f, 0.42f),
                        Quaternion.Euler(-18f, 21f, 9f))
                };
                ApplySample(pair, side, sample);

                Assert.AreEqual(ControllerHandPair.PoseSource.Controller,
                    side == SeatedHandSide.Left ? pair.LeftSource : pair.RightSource);
                Assert.AreEqual(ControllerHandPair.PoseSource.Mirror,
                    side == SeatedHandSide.Left ? pair.RightSource : pair.LeftSource);
                Assert.IsTrue(pointer.TryGetIndexRay(side, out Ray ray));
                var sampleTime = typeof(ControllerHandPair).GetField("samplesAt",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                sampleTime.SetValue(pair, Time.realtimeSinceStartupAsDouble - 1);
                Assert.IsFalse(pointer.TryGetIndexRay(side, out _), "Expired wrist sample must not inspect a sign.");
                ApplySample(pair, side, sample);
                Assert.IsTrue(pointer.TryGetIndexRay(side, out _), "A fresh sample restores pointing.");

                var anatomy = side == SeatedHandSide.Left
                    ? pointer.leftIndexAnatomy
                    : pointer.rightIndexAnatomy;
                Assert.Less(Vector3.Distance(ray.origin, anatomy.tip.position), 0.0001f);
                Vector3 anatomicalDirection = anatomy.tip.position - anatomy.baseJoint.position;
                Assert.Greater(anatomicalDirection.sqrMagnitude, 1e-8f);
                Assert.Greater(Vector3.Dot(ray.direction, anatomicalDirection.normalized), 0.9999f);

                SeatedHandSide mirroredSide = side == SeatedHandSide.Left
                    ? SeatedHandSide.Right
                    : SeatedHandSide.Left;
                Assert.IsFalse(pointer.TryGetIndexRay(mirroredSide, out Ray ignoredMirrorRay));

                FirstPersonWrist sourceWrist = side == SeatedHandSide.Left ? pair.leftWrist : pair.rightWrist;
                Transform[] indexBones =
                {
                    FindSegment(sourceWrist.visualRoot, "index_01"),
                    FindSegment(sourceWrist.visualRoot, "index_02"),
                    FindSegment(sourceWrist.visualRoot, "index_03")
                };
                for (int i = 0; i < indexBones.Length; i++)
                    Assert.IsNotNull(indexBones[i]);
                var regularGripRotations = new Quaternion[indexBones.Length];
                for (int i = 0; i < indexBones.Length; i++)
                    regularGripRotations[i] = indexBones[i].localRotation;

                Assert.IsTrue(pointer.RequestPointingDisplayPreset(side, true));
                ApplySample(pair, side, sample);
                float poseChange = 0f;
                for (int i = 0; i < indexBones.Length; i++)
                    poseChange = Mathf.Max(poseChange,
                        Quaternion.Angle(regularGripRotations[i], indexBones[i].localRotation));
                Assert.Greater(poseChange, 0.1f);
                Assert.AreEqual(0.9f, sample.grip, 1e-6f);

                Assert.IsTrue(pointer.RequestPointingDisplayPreset(side, false));
                ApplySample(pair, side, sample);
                for (int i = 0; i < indexBones.Length; i++)
                    Assert.Less(Quaternion.Angle(regularGripRotations[i], indexBones[i].localRotation), 0.01f);

                sourceWrist.visualRoot.gameObject.SetActive(false);
                Assert.IsFalse(pointer.TryGetIndexRay(side, out Ray hiddenRay));
                sourceWrist.visualRoot.gameObject.SetActive(true);
                pair.ApplyControllerSamples(default, default);
                Assert.IsFalse(pointer.TryGetIndexRay(side, out Ray lostTrackingRay));
            }
            finally
            {
                Object.DestroyImmediate(root);
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void SyntheticGripSampleBuildsRayFromCalibratedLandmarks()
        {
            var root = new GameObject("Synthetic index pointer origin");
            root.SetActive(false);
            string key = "Test.IndexPointer.Synthetic." + Guid.NewGuid().ToString("N");
            PlayerPrefs.DeleteKey(key);
            Mesh mesh = null;
            try
            {
                var pairObject = new GameObject("Synthetic hand pair");
                pairObject.transform.SetParent(root.transform, false);
                var pair = pairObject.AddComponent<ControllerHandPair>();
                pair.enabled = false;
                pair.trackingOrigin = root.transform;
                SetSettingsKey(pair, key);

                var wristObject = new GameObject("Synthetic left wrist");
                wristObject.transform.SetParent(pairObject.transform, false);
                var wrist = wristObject.AddComponent<FirstPersonWrist>();
                var visual = new GameObject("Synthetic skinned hand visual");
                visual.transform.SetParent(wristObject.transform, false);
                wrist.trackingSpace = root.transform;
                wrist.visualRoot = visual.transform;
                pair.leftWrist = wrist;

                var rendererObject = new GameObject("Synthetic skinned renderer");
                rendererObject.transform.SetParent(visual.transform, false);
                var renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                mesh = new Mesh();
                renderer.sharedMesh = mesh;

                var baseObject = new GameObject("Index distal base landmark");
                baseObject.transform.SetParent(visual.transform, false);
                baseObject.transform.localPosition = new Vector3(0f, 0f, 0.2f);
                var tipObject = new GameObject("Index fingertip landmark");
                tipObject.transform.SetParent(visual.transform, false);
                tipObject.transform.localPosition = new Vector3(0.04f, 0.02f, 0.38f);

                var pointer = pairObject.AddComponent<ControllerIndexPointer>();
                pointer.handPair = pair;
                pointer.leftIndexAnatomy = new ControllerIndexPointer.IndexAnatomy
                {
                    baseJoint = baseObject.transform,
                    tip = tipObject.transform
                };
                pair.enabled = true;
                root.SetActive(true);

                var sample = new ControllerHandPair.GripSample
                {
                    tracked = true,
                    grip = 0.37f,
                    pose = new Pose(new Vector3(-0.25f, 0.6f, 0.8f), Quaternion.Euler(12f, 31f, -7f))
                };
                pair.ApplyControllerSamples(sample, default);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, pair.LeftSource);
                Assert.IsTrue(pointer.TryGetIndexRay(SeatedHandSide.Left, out Ray ray));
                Assert.Less(Vector3.Distance(ray.origin, tipObject.transform.position), 0.0001f);
                Vector3 expected = tipObject.transform.position - baseObject.transform.position;
                Assert.Greater(Vector3.Dot(ray.direction, expected.normalized), 0.9999f);

                visual.SetActive(false);
                Assert.IsFalse(pointer.TryGetIndexRay(SeatedHandSide.Left, out Ray hiddenRay));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (mesh)
                    Object.DestroyImmediate(mesh);
                PlayerPrefs.DeleteKey(key);
            }
        }

        private static void ApplySample(ControllerHandPair pair, SeatedHandSide side,
            ControllerHandPair.GripSample sample)
        {
            if (side == SeatedHandSide.Left)
                pair.ApplyControllerSamples(sample, default);
            else
                pair.ApplyControllerSamples(default, sample);
        }

        private static Transform FindSegment(Transform root, string segment)
        {
            if (!root)
                return null;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (ElenaNativeRigProfile.MatchesBone(transforms[i].name, segment))
                    return transforms[i];
            return null;
        }

        private static void SetSettingsKey(ControllerHandPair pair, string key)
        {
            var serialized = new SerializedObject(pair);
            serialized.FindProperty("seatedHandSettingsPlayerPrefsKey").stringValue = key;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
