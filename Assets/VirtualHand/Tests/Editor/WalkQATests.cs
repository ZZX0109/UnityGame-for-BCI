#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Walk-quality assurance over the verified gait rig: ground-plane safety across a full
    /// cycle, mid-swing foot lift, linear phase scaling, the binding-layer body-yaw rate cap
    /// and the locomotion yaw deadzone.  The synthetic rig conventions (bone chain, stride
    /// injection, warm-up, drive pattern) are copied verbatim from <see cref="WalkCycleDriverTests"/>
    /// so both fixtures exercise the identical rig.
    /// </summary>
    public sealed class WalkQATests
    {
        const float Delta = 0.0005f;
        const float ThighAmplitude = 15f;
        const float KneeAmplitude = 30f;

        [Test]
        public void FullCycleKeepsFootBonesAboveGroundPlane()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                driver.Evaluate(1f / 60f); // self-binds the bones and captures the rest pose
                root.transform.rotation = Quaternion.Euler(0f, 37f, 0f); // yaw-robustness of the ground plane

                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                WarmToFullBlend(driver);

                Transform leftAnkle = FindBone(root.transform, "foot_l");
                Transform rightAnkle = FindBone(root.transform, "foot_r");
                Transform leftBall = FindBone(root.transform, "ball_l");
                Transform rightBall = FindBone(root.transform, "ball_r");
                Assert.IsNotNull(leftAnkle);
                Assert.IsNotNull(rightAnkle);
                Assert.IsNotNull(leftBall);
                Assert.IsNotNull(rightBall);

                for (int i = 0; i < 48; i++)
                {
                    locomotion.Integrate(1f / 48f);
                    driver.Evaluate(0.1f);

                    Assert.GreaterOrEqual(leftAnkle.position.y, 0.10f - Delta,
                        $"left ankle dipped below the ground plane at sample {i}");
                    Assert.GreaterOrEqual(rightAnkle.position.y, 0.10f - Delta,
                        $"right ankle dipped below the ground plane at sample {i}");
                    Assert.GreaterOrEqual(leftBall.position.y, 0.05f - Delta,
                        $"left ball dipped below the ground plane at sample {i}");
                    Assert.GreaterOrEqual(rightBall.position.y, 0.05f - Delta,
                        $"right ball dipped below the ground plane at sample {i}");
                }

                // The 48 samples must cover a full stride (path ≈ 1 m), i.e. the sweep is not vacuous.
                Assert.AreEqual(1f, locomotion.PathDistanceMeters, 0.0005f,
                    "the sweep should integrate exactly one stride of path length");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void MidSwingLiftsFootAboveStanceHeight()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                driver.Evaluate(1f / 60f); // self-binds the bones and captures the rest pose

                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                WarmToFullBlend(driver);

                // Path 0.25 m at stride 1 → phase q = π/2: left foot mid-swing, right foot stance.
                locomotion.Integrate(0.25f);
                driver.Evaluate(0.1f);

                float swing = FindBone(root.transform, "foot_l").position.y;
                float stance = FindBone(root.transform, "foot_r").position.y;

                // Closed form: ankle_y = 0.80 − 0.40·cos(hip) − 0.30·cos(hip + knee)
                // with hip = −15°·cos(π/2) = 0 and knee = 30°·max(0, sin π/2)² = 30°.
                Assert.AreEqual(0.14019f, swing, 0.0005f,
                    "the mid-swing ankle should sit at the closed-form lift height");
                Assert.AreEqual(0.10f, stance, 0.0005f,
                    "the stance ankle should stay exactly at its rest height");
                Assert.Greater(swing - stance, 0.03f,
                    "mid-swing must visibly lift the foot above the stance foot");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void PhaseRateScalesLinearlyWithSpeed()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                driver.Evaluate(1f / 60f); // self-binds the bones and captures the rest pose

                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                WarmToFullBlend(driver);

                locomotion.Integrate(0.2f);
                driver.Evaluate(0.1f);
                float slowPhase = driver.PhaseRadians;
                Assert.Greater(slowPhase, 0f, "the slow walk must advance the phase");

                locomotion.SetLocomotionInput(new Vector2(0f, 2f), 0f);
                locomotion.Integrate(0.2f);
                driver.Evaluate(0.1f);
                float fastIncrement = driver.PhaseRadians - slowPhase;

                Assert.AreEqual(slowPhase * 2f, fastIncrement, 0.0005f,
                    "doubling the speed must double the phase rate");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void BodyYawLagsHeadYaw()
        {
            GameObject rig = CreateBindingRig(out AvatarBodyCalibrationBinding binding, out Transform head,
                out Transform origin);
            try
            {
                head.position = new Vector3(-0.8f, 1.55f, 2.1f);
                Assert.IsTrue(binding.ApplyCalibration(), binding.LastError);
                Assert.IsTrue(binding.RefreshBinding(1f / 60f), binding.LastError);
                Quaternion baseline = origin.rotation;

                head.rotation = Quaternion.Euler(0f, 90f, 0f);
                Assert.IsTrue(binding.RefreshBinding(1f / 60f), binding.LastError);

                float step = Quaternion.Angle(baseline, origin.rotation);
                Assert.AreEqual(12f, step, 0.0005f,
                    "one 1/60 s refresh must move the body yaw by exactly the 720°/s cap");
                Assert.That(step, Is.LessThan(90f - Delta),
                    "the body origin must lag the head instead of snapping onto it");
            }
            finally
            {
                Destroy(rig);
            }
        }

        [Test]
        public void TurnYawRateCapped()
        {
            GameObject rig = CreateBindingRig(out AvatarBodyCalibrationBinding binding, out Transform head,
                out Transform origin);
            try
            {
                head.position = new Vector3(-0.8f, 1.55f, 2.1f);
                Assert.IsTrue(binding.ApplyCalibration(), binding.LastError);
                Assert.IsTrue(binding.RefreshBinding(1f / 60f), binding.LastError);

                head.rotation = Quaternion.Euler(0f, 90f, 0f);
                // 90° at 720°/s needs ≥ 8 steps of 1/60 s; 90 steps must converge.
                for (int i = 0; i < 90; i++)
                    Assert.IsTrue(binding.RefreshBinding(1f / 60f), binding.LastError);

                Assert.AreEqual(0f, Quaternion.Angle(origin.rotation, Quaternion.Euler(0f, 90f, 0f)), 0.5f,
                    "repeated capped steps must converge on the head yaw");
            }
            finally
            {
                Destroy(rig);
            }
        }

        [Test]
        public void SmallYawRateIsDeadzoned()
        {
            GameObject root = new GameObject("Deadzone Rig");
            LowerBodyPoseController locomotion = root.AddComponent<LowerBodyPoseController>();
            try
            {
                Assert.AreEqual(2f, LowerBodyPoseController.YawDeadzoneDegreesPerSecond, 0.0001f,
                    "the deadzone contract is 2°/s");

                locomotion.SetLocomotionInput(new Vector2(0f, 0f), 0.5f);
                locomotion.Integrate(1f);
                Assert.AreEqual(0f, locomotion.YawDegrees, Delta,
                    "sub-deadzone yaw rates must not rotate the body");

                locomotion.SetLocomotionInput(new Vector2(0f, 0f), -0.5f);
                locomotion.Integrate(1f);
                Assert.AreEqual(0f, locomotion.YawDegrees, Delta,
                    "negative sub-deadzone yaw rates must not rotate either");

                locomotion.SetLocomotionInput(new Vector2(0f, 0f), 60f);
                locomotion.Integrate(1f / 60f);
                Assert.AreEqual(1f, locomotion.YawDegrees, 0.05f,
                    "rates at or above the deadzone must integrate normally");
            }
            finally
            {
                Destroy(root, locomotion, null);
            }
        }

        static GameObject CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver)
        {
            GameObject root = new GameObject("Walk QA Rig");
            locomotion = root.AddComponent<LowerBodyPoseController>();
            driver = root.AddComponent<WalkCycleDriver>();
            Transform leftThigh = CreateBone(root.transform, "thigh_l", new Vector3(0.09f, 0.80f, 0f));
            Transform leftCalf = CreateBone(leftThigh, "calf_l", new Vector3(0f, -0.40f, 0f));
            Transform leftFoot = CreateBone(leftCalf, "foot_l", new Vector3(0f, -0.30f, 0f));
            CreateBone(leftFoot, "ball_l", new Vector3(0f, -0.05f, 0.12f));
            Transform rightThigh = CreateBone(root.transform, "thigh_r", new Vector3(-0.09f, 0.80f, 0f));
            Transform rightCalf = CreateBone(rightThigh, "calf_r", new Vector3(0f, -0.40f, 0f));
            Transform rightFoot = CreateBone(rightCalf, "foot_r", new Vector3(0f, -0.30f, 0f));
            CreateBone(rightFoot, "ball_r", new Vector3(0f, -0.05f, 0.12f));
            return root;
        }

        static GameObject CreateBindingRig(
            out AvatarBodyCalibrationBinding binding,
            out Transform head,
            out Transform origin)
        {
            GameObject rig = new GameObject("Binding QA Rig");
            SeatedArmPreview preview = rig.AddComponent<SeatedArmPreview>();
            binding = rig.AddComponent<AvatarBodyCalibrationBinding>();
            GameObject headObject = new GameObject("Tracked Head");
            GameObject originObject = new GameObject("Body Origin");
            head = headObject.transform;
            origin = originObject.transform;

            binding.preview = preview;
            binding.trackedHead = head;
            binding.headTrackingValid = true;
            binding.bodyOrigin = origin;
            binding.calibration = new AvatarBodyCalibration
            {
                shoulderWidth = new AvatarBodyMeasurement(0.44f, AvatarBodyMeasurementSource.Measured),
                upperArmLength = new AvatarBodyMeasurement(0.33f, AvatarBodyMeasurementSource.ManualEdit),
                forearmLength = new AvatarBodyMeasurement(0.29f, AvatarBodyMeasurementSource.Measured),
                torsoHeight = new AvatarBodyMeasurement(0.58f, AvatarBodyMeasurementSource.Measured),
                eyeHeightFromPelvis = new AvatarBodyMeasurement(0.62f, AvatarBodyMeasurementSource.ManualEdit),
                seatedPelvisHeightFromFloor = new AvatarBodyMeasurement(0.49f, AvatarBodyMeasurementSource.Measured)
            };
            return rig;
        }

        static Transform CreateBone(Transform parent, string name, Vector3 localPosition)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            return bone;
        }

        static Transform FindBone(Transform root, string name)
        {
            // Transform.Find with a bare name only matches direct children, so search
            // the anatomical chain (thigh → calf → foot → ball) recursively.
            foreach (Transform child in root)
            {
                if (child.name == name)
                    return child;
                Transform hit = FindBone(child, name);
                if (hit != null)
                    return hit;
            }

            return null;
        }

        static void SetStride(WalkCycleDriver driver, float strideMeters)
        {
            SerializedObject so = new SerializedObject(driver);
            so.FindProperty("strideLengthMeters").floatValue = strideMeters;
            so.ApplyModifiedProperties();
        }

        static void WarmToFullBlend(WalkCycleDriver driver)
        {
            // dt = 0.1 s (the clamp ceiling) six times → blend = 1 − e⁻¹² ≈ 0.999994.
            for (int i = 0; i < 6; i++)
                driver.Evaluate(0.1f);
        }

        static void Destroy(GameObject root, LowerBodyPoseController locomotion, WalkCycleDriver driver)
        {
            if (locomotion != null)
                UnityEngine.Object.DestroyImmediate(locomotion);
            if (driver != null)
                UnityEngine.Object.DestroyImmediate(driver);
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }

        static void Destroy(GameObject root)
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
#endif
