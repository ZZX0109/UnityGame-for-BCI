#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Neuralife.VirtualHand.Body;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>
    /// Edit-mode coverage for <see cref="WalkCycleDriver"/>: manifest-name auto binding,
    /// distance-driven phase, frame-rate-independent move blend, the crosschecked gait
    /// math (stance-straight knee, mid-swing knee peak, right-leg anti-phase) and the
    /// exact return to rest once the avatar stops.  The synthetic rig uses identity bone
    /// rotations, hip-forward = +Z and leg segments 0.40 m + 0.30 m, so every expected
    /// angle below is computed from the reviewed gait formulas.
    /// </summary>
    public sealed class WalkCycleDriverTests
    {
        const float Delta = 0.0005f;
        const float ThighAmplitude = 25f;
        const float KneeAmplitude = 45f;

        [Test]
        public void SerializedFieldNamesMatchBuilderContract()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SerializedObject so = new SerializedObject(driver);
                foreach (string field in new[]
                {
                    "locomotion", "leftThigh", "leftCalf", "leftFoot",
                    "rightThigh", "rightCalf", "rightFoot",
                    "strideLengthMeters", "thighAmplitudeDegrees", "kneeAmplitudeDegrees", "blendTauSeconds"
                })
                {
                    Assert.IsNotNull(so.FindProperty(field),
                        $"serialized field '{field}' must exist — the builder wires it by name");
                }
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void AutoBindsManifestBonesAndHoldsRestWhenIdle()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                driver.Evaluate(1f / 60f); // first call self-binds via the manifest bone names

                Assert.AreEqual(0f, driver.MoveBlend, Delta,
                    "a stationary avatar must keep the move blend at zero");
                Assert.AreEqual(0f, locomotion.PathDistanceMeters, Delta,
                    "no input means no integrated path");

                Transform leftThigh = FindBone(root.transform, "thigh_l");
                Transform rightFoot = FindBone(root.transform, "foot_r");
                Assert.IsNotNull(leftThigh, "the manifest name thigh_l must auto-bind");
                Assert.IsNotNull(rightFoot, "the manifest name foot_r must auto-bind");
                Assert.AreEqual(0f, Quaternion.Angle(leftThigh.localRotation, Quaternion.identity), 0.01f,
                    "an idle avatar must hold the captured rest pose");
                Assert.AreEqual(0f, Quaternion.Angle(rightFoot.localRotation, Quaternion.identity), 0.01f,
                    "an idle avatar must hold the captured rest pose");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void AutoStrideMatchesZeroDriftCondition()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                driver.Evaluate(1f / 60f); // bind first so the runtime leg length is measured

                float legLength = 0.7f; // synthetic rig: 0.40 m thigh + 0.30 m shank
                float expected = 4f * legLength * Mathf.Sin(ThighAmplitude * Mathf.Deg2Rad);
                Assert.AreEqual(expected, driver.ResolvedStrideLengthMeters, 0.001f,
                    "the auto stride must satisfy S = 4·L_eff·sin(A_h)");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void PhaseFollowsMonotonicPathDistance()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                driver.Evaluate(1f / 60f);

                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                locomotion.Integrate(0.25f);
                Assert.AreEqual(Mathf.PI / 2f, driver.PhaseRadians, 0.001f,
                    "phase = 2π · path / stride");

                // Reversing must keep advancing the phase: path length is monotonic.
                // Total path 0.25 + 0.10 = 0.35 m → phase 2π·0.35.
                locomotion.SetLocomotionInput(new Vector2(0f, -1f), 0f);
                locomotion.Integrate(0.1f);
                Assert.AreEqual(2f * Mathf.PI * 0.35f, driver.PhaseRadians, 0.001f,
                    "reversing must step the phase forward, never rewind it");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void MoveBlendRampIsFrameRateIndependent()
        {
            GameObject rootA = CreateRig(out LowerBodyPoseController locomotionA, out WalkCycleDriver driverA);
            GameObject rootB = CreateRig(out LowerBodyPoseController locomotionB, out WalkCycleDriver driverB);
            try
            {
                locomotionA.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                driverA.Evaluate(0.05f);
                Assert.AreEqual(1f - Mathf.Exp(-1f), driverA.MoveBlend, 0.001f,
                    "one 0.05 s step must move the blend by exactly 1 − exp(−dt/τ)");

                locomotionB.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                driverB.Evaluate(0.025f);
                driverB.Evaluate(0.025f);
                Assert.AreEqual(driverA.MoveBlend, driverB.MoveBlend, 0.001f,
                    "two 0.025 s steps must reach the same blend as one 0.05 s step");
            }
            finally
            {
                Destroy(rootA, locomotionA, driverA);
                Destroy(rootB, locomotionB, driverB);
            }
        }

        [Test]
        public void GaitMathStanceStraightAndMidSwingPeak()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                WarmToFullBlend(driver); // phase stays 0 while the blend ramps

                locomotion.Integrate(0.25f); // path 0.25 m → phase π/2
                driver.Evaluate(0.1f);

                Transform leftCalf = FindBone(root.transform, "calf_l");
                Transform rightCalf = FindBone(root.transform, "calf_r");
                Transform leftThigh = FindBone(root.transform, "thigh_l");
                Transform rightFoot = FindBone(root.transform, "foot_r");
                Assert.IsNotNull(leftCalf);
                Assert.IsNotNull(rightCalf);
                Assert.IsNotNull(leftThigh);
                Assert.IsNotNull(rightFoot);

                Assert.AreEqual(KneeAmplitude, Quaternion.Angle(leftCalf.localRotation, Quaternion.identity), 0.01f,
                    "the swing knee must peak at A_k in mid-swing");
                Assert.AreEqual(0f, Quaternion.Angle(rightCalf.localRotation, Quaternion.identity), 0.001f,
                    "the stance knee must stay exactly straight (no mid-stance buckling)");
                Assert.AreEqual(0f, Quaternion.Angle(leftThigh.localRotation, Quaternion.identity), 0.01f,
                    "the hip must pass through zero at mid-swing");
                Assert.AreEqual(0f, Quaternion.Angle(rightFoot.localRotation, Quaternion.identity), 0.01f,
                    "the stance foot must keep its rest orientation");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void RightThighIsExactAntiPhaseOfLeft()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                WarmToFullBlend(driver);

                locomotion.Integrate(0.6f); // phase 0.6·2π ≈ 216°, a generic off-nominal phase
                driver.Evaluate(0.1f);

                Transform leftThigh = FindBone(root.transform, "thigh_l");
                Transform rightThigh = FindBone(root.transform, "thigh_r");
                Assert.IsNotNull(leftThigh);
                Assert.IsNotNull(rightThigh);
                Assert.Greater(Quaternion.Angle(leftThigh.localRotation, Quaternion.identity), 1f,
                    "the warm rig must actually be mid-gait for this assertion to bite");
                Assert.AreEqual(0f,
                    Quaternion.Angle(rightThigh.localRotation, Quaternion.Inverse(leftThigh.localRotation)), 0.01f,
                    "the right hip must mirror the left hip (anti-phase cosine)");
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        [Test]
        public void StoppingBlendsBackToExactRest()
        {
            GameObject root = CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver);
            try
            {
                SetStride(driver, 1f);
                locomotion.SetLocomotionInput(new Vector2(0f, 1f), 0f);
                WarmToFullBlend(driver);
                locomotion.Integrate(0.5f);
                driver.Evaluate(0.1f);
                Transform leftThigh = FindBone(root.transform, "thigh_l");
                Assert.IsNotNull(leftThigh);
                Assert.Greater(Quaternion.Angle(leftThigh.localRotation, Quaternion.identity), 1f,
                    "the warm rig must actually be mid-gait for this assertion to bite");

                locomotion.SetLocomotionInput(Vector2.zero, 0f);
                for (int i = 0; i < 16; i++)
                    driver.Evaluate(0.05f); // residual e⁻¹⁶ ≈ 1e-7 → below the rest snap threshold

                Assert.AreEqual(0f, driver.MoveBlend, Delta,
                    "the blend must decay to zero after the avatar stops");
                foreach (Transform bone in new[]
                {
                    FindBone(root.transform, "thigh_l"), FindBone(root.transform, "calf_l"),
                    FindBone(root.transform, "foot_l"), FindBone(root.transform, "thigh_r"),
                    FindBone(root.transform, "calf_r"), FindBone(root.transform, "foot_r")
                })
                {
                    Assert.IsNotNull(bone);
                    Assert.AreEqual(0f, Quaternion.Angle(bone.localRotation, Quaternion.identity), 0.0001f,
                        $"{bone.name} must be snapped exactly back to rest");
                }
            }
            finally
            {
                Destroy(root, locomotion, driver);
            }
        }

        static GameObject CreateRig(out LowerBodyPoseController locomotion, out WalkCycleDriver driver)
        {
            GameObject root = new GameObject("Walk Test Rig");
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
    }
}
#endif
