using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using StreetCrossing.Integration;

namespace StreetCrossing.Tests.EditMode
{
    /// <summary>
    /// Uses the delivered FirstPersonFullBody prefab so the wrist/palm rotation
    /// contract is checked against Elena's real baked hand-root offsets.
    /// </summary>
    public sealed class StreetTrackedHandsOrientationTests
    {
        const string FullBodyPrefabPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";
        GameObject _instance;

        [TearDown]
        public void TearDown()
        {
            if (_instance)
                Object.DestroyImmediate(_instance);
        }

        [TestCase(SeatedHandSide.Left)]
        [TestCase(SeatedHandSide.Right)]
        public void TrackedPalmRotationKeepsWristLandmarkAtBoneAndMovesPalmFrame(SeatedHandSide side)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FullBodyPrefabPath);
            Assert.IsNotNull(prefab, "The test requires the delivered full-body prefab.");

            _instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Assert.IsNotNull(_instance);

            FullBodyPoseController controller = _instance.GetComponentInChildren<FullBodyPoseController>(true);
            Assert.IsNotNull(controller);

            // Awake is not guaranteed when Unity instantiates a prefab in EditMode.
            // Cache the authored rig before measuring its palm frame.
            HandRigDriver[] drivers = controller.GetComponentsInChildren<HandRigDriver>(true);
            Assert.AreEqual(2, drivers.Length, "The prefab should contain both Elena hand drivers.");
            foreach (var candidate in drivers)
                Assert.IsTrue(candidate && candidate.CacheBones(),
                    "The real Elena driver must resolve and initialize its finger rig.");

            ElenaWristAligner.HealBody(_instance.transform);
            Transform wristBone = side == SeatedHandSide.Left ? controller.leftWrist : controller.rightWrist;
            Assert.IsNotNull(wristBone);

            HandRigDriver driver = null;
            foreach (var candidate in drivers)
            {
                if (candidate && candidate.IsMirrored == (side == SeatedHandSide.Left))
                {
                    driver = candidate;
                    break;
                }
            }
            Assert.IsNotNull(driver);

            Transform modelWrist = FindLandmark(driver.transform, "hand.R_02");
            Transform middleBase = FindLandmark(driver.transform, "middle_01.R_025");
            Transform indexBase = FindLandmark(driver.transform, "index_01.R_017");
            Assert.IsNotNull(modelWrist);
            Assert.IsNotNull(middleBase);
            Assert.IsNotNull(indexBase);

            var trackedHands = _instance.AddComponent<StreetTrackedHands>();
            trackedHands.Bind(controller, null);

            Quaternion initialPalmFrame = PalmFrame(modelWrist, middleBase, indexBase);
            Quaternion targetPalmFrame = Quaternion.Euler(17f, -28f, 11f) * initialPalmFrame;
            Vector3 wristPosition = wristBone.position;
            Vector3 handRootLocalPosition = driver.transform.localPosition;
            Quaternion handRootLocalRotation = driver.transform.localRotation;
            Vector3 handRootLocalScale = driver.transform.localScale;

            Assert.IsTrue(trackedHands.ApplyTrackedPalmOrientation(side, targetPalmFrame),
                "The component must map the tracked palm frame onto the body wrist bone.");

            Quaternion actualPalmFrame = PalmFrame(modelWrist, middleBase, indexBase);
            Assert.Less(Vector3.Distance(wristPosition, wristBone.position), 0.0001f,
                "Rotating the wrist must not translate its skeletal pivot.");
            Assert.Less(Vector3.Distance(modelWrist.position, wristBone.position), 0.02f,
                "The Elena wrist landmark must remain aligned to the body wrist bone.");
            Assert.Less(Quaternion.Angle(targetPalmFrame, actualPalmFrame), 0.5f,
                "The palm and finger-base frame must follow the tracked rotation.");
            Assert.Less(Vector3.Angle(targetPalmFrame * Vector3.forward, actualPalmFrame * Vector3.forward), 0.5f,
                "The palm's forward axis must follow the tracked rotation.");
            Assert.Less(Vector3.Distance(handRootLocalPosition, driver.transform.localPosition), 0.0001f,
                "The calibrated Elena root offset must stay baked.");
            Assert.Less(Quaternion.Angle(handRootLocalRotation, driver.transform.localRotation), 0.01f,
                "The calibrated Elena root rotation must stay baked.");
            Assert.Less(Vector3.Distance(handRootLocalScale, driver.transform.localScale), 0.0001f,
                "The calibrated Elena root scale must stay baked.");
        }

        static Transform FindLandmark(Transform root, string semanticName)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (ElenaNativeRigProfile.MatchesBone(candidate.name, semanticName))
                    return candidate;
            return null;
        }

        static Quaternion PalmFrame(Transform wrist, Transform middle, Transform index)
        {
            Vector3 forward = middle.position - wrist.position;
            Vector3 normal = Vector3.Cross(forward, index.position - wrist.position);
            Assert.Greater(forward.sqrMagnitude, 1e-12f, "Palm forward landmark axis is degenerate.");
            Assert.Greater(normal.sqrMagnitude, 1e-12f, "Palm normal landmark axis is degenerate.");
            return Quaternion.LookRotation(forward, normal);
        }
    }
}

