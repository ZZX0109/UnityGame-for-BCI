using System.Collections.Generic;
using Neuralife.VirtualHand.Body;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// V5-075 修复①验收（ClipAudit 缺陷①，DEFECT/HIGH）：Elena 腕部地标对齐。
    ///
    /// 出厂预制体由旧构建器产出，Elena 手实例在 localPosition=0 时腕部地标
    /// hand.L_02/hand.R_02 相对身体腕骨 hand_l/hand_r 有约 2.8166 m 的固有偏移
    ///（GLB 根 0.0145× 均匀缩放 × Armature 100×，见 Game03 Logs/WristOffsetAnalysis.txt）。
    /// ElenaWristAligner.HealBody 在父级局部空间一次性抵消该偏移（含身体侧 ~101.65×
    /// 逐轴 lossyScale，算术推断），目标残差 <2 cm。
    ///
    /// 断言映射：
    ///  A1  HealBody 后双手「地标 − 腕骨」世界距离 < 0.02 m（修复前 >1 m 证明缺陷存在）；
    ///  A2  ApplyPose("NaturalSeated") 后身体腕骨世界位 == bodySpace.TransformPoint(腕目标)
    ///      ±1 mm，且 A1 距离保持 <0.02 m（snap 与混合过渡两种模式）；
    ///  A3  全身体实例 lossyScale/localScale/localRotation 位级不变（仅平移补偿，无缩放副作用）；
    ///  A5  HealBody 仅移动两手根 localPosition，其余 Transform �部位姿位级不变；
    ///  A6  连续 10 次 AlignNow 幂等：localPosition 位级一致且全部返回 true。
    /// </summary>
    public sealed class ElenaWristAlignmentTests
    {
        const string ShippedBodyPrefab = "Assets/FirstPersonSokoban/Resources/FirstPersonFullBody.prefab";
        const float HealTolerance = 0.02f;
        const float PoseTolerance = 0.001f; // ±1 mm

        GameObject _body;
        List<Transform> _allTransforms;

        [TearDown]
        public void TearDown()
        {
            if (_body != null)
            {
                Object.DestroyImmediate(_body);
                _body = null;
            }
        }

        GameObject InstantiateShippedBody()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShippedBodyPrefab);
            Assert.That(prefab, Is.Not.Null, $"出厂预制体缺失：{ShippedBodyPrefab}");
            _body = Object.Instantiate(prefab);
            _body.name = "ElenaWristAlignmentTestsBody";
            _allTransforms = new List<Transform>(_body.GetComponentsInChildren<Transform>(true));
            return _body;
        }

        Transform HandRoot(bool left)
        {
            string target = left ? "elenahand_l" : "elenahand_r";
            foreach (var t in _body.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().StartsWith(target))
                    return t;
            return null;
        }

        Transform Landmark(bool left)
        {
            var handRoot = HandRoot(left);
            Assert.That(handRoot, Is.Not.Null, $"Elena 手实例缺失：{(left ? "L" : "R")}");
            // V5-075 门禁实测：两个手部 prefab 的源资产均为 ElenaHand_R.glb，其内没有 hand.L_*；
            // 左手实例的腕部地标实际名为 hand.R_02。本侧精确名 → 对侧精确名，与对齐器一致。
            string primary = left ? "hand.l_02" : "hand.r_02";
            string secondary = left ? "hand.r_02" : "hand.l_02";
            foreach (var t in handRoot.GetComponentsInChildren<Transform>(true))
            {
                string lowered = t.name.ToLowerInvariant();
                if (lowered == primary || lowered == secondary)
                    return t;
            }
            return null;
        }

        Transform BodyWristBone(bool left)
        {
            string target = left ? "hand_l" : "hand_r";
            var handRoot = HandRoot(left);
            foreach (var t in _body.GetComponentsInChildren<Transform>(true))
            {
                if (handRoot != null && (t == handRoot || t.IsChildOf(handRoot))) continue; // 绝不落回 Elena 子树
                string lowered = t.name.ToLowerInvariant();
                if (lowered == target || lowered.StartsWith(target)) return t;
            }
            return null;
        }

        float WristResidual(bool left)
        {
            var landmark = Landmark(left);
            var bone = BodyWristBone(left);
            Assert.That(landmark, Is.Not.Null, $"腕部地标缺失：{(left ? "hand.L_02" : "hand.R_02")}");
            Assert.That(bone, Is.Not.Null, $"身体腕骨缺失：{(left ? "hand_l" : "hand_r")}");
            return Vector3.Distance(landmark.position, bone.position);
        }

        [Test]
        public void A1_HealBody_DrivesWristResidualUnder2Cm_BothHands()
        {
            InstantiateShippedBody();

            float beforeLeft = WristResidual(true);
            float beforeRight = WristResidual(false);
            Assert.That(beforeLeft, Is.GreaterThan(1f), "出厂预制体左手应携带 ~2.8 m 固有偏移（缺陷仍在）");
            Assert.That(beforeRight, Is.GreaterThan(1f), "出厂预制体右手应携带 ~2.8 m 固有偏移（缺陷仍在）");

            int healed = ElenaWristAligner.HealBody(_body.transform, HealTolerance);
            Assert.That(healed, Is.EqualTo(2), $"HealBody 应对齐 2 只手，实际 {healed}");

            float afterLeft = WristResidual(true);
            float afterRight = WristResidual(false);
            Assert.That(afterLeft, Is.LessThan(HealTolerance), $"左手残差 {afterLeft:F4} m ≥ {HealTolerance} m");
            Assert.That(afterRight, Is.LessThan(HealTolerance), $"右手残差 {afterRight:F4} m ≥ {HealTolerance} m");

            // 方向交叉验证：补偿被身体侧 ~101.65× 逐轴 lossyScale 吸收后，手根 localPosition
            // 应落在 ~0.0277 m（2.8166/101.65）量级；>0.1 m 即补偿被写错位置。
            foreach (var left in new[] { true, false })
            {
                var handRoot = HandRoot(left);
                Assert.That(handRoot.localPosition.magnitude, Is.LessThan(0.1f),
                    $"{(left ? "左" : "右")}手根 localPosition 量级异常：{handRoot.localPosition.magnitude:F4} m");
            }
        }

        [Test]
        public void A2a_NaturalSeatedSnap_HitsBodySpaceTarget_AndKeepsHeal()
        {
            InstantiateShippedBody();
            Assert.That(ElenaWristAligner.HealBody(_body.transform, HealTolerance), Is.EqualTo(2));

            var pose = _body.GetComponentInChildren<FullBodyPoseController>(true);
            Assert.That(pose, Is.Not.Null, "出厂预制体缺 FullBodyPoseController");
            Assert.That(pose.bodySpace, Is.Not.Null, "PoseController.bodySpace 未绑定");
            Assert.That(pose.leftWrist, Is.Not.Null, "PoseController.leftWrist 未绑定");
            Assert.That(pose.rightWrist, Is.Not.Null, "PoseController.rightWrist 未绑定");
            pose.blendPoseTransitions = false; // snap 模式

            Assert.That(pose.ApplyPose(FullBodyPoseController.PoseNaturalSeated), Is.True);

            var leftTarget = pose.bodySpace.TransformPoint(
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Left));
            var rightTarget = pose.bodySpace.TransformPoint(
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Right));
            Assert.That(Vector3.Distance(pose.leftWrist.position, leftTarget), Is.LessThan(PoseTolerance),
                "snap 模式下左腕骨未命中 body-space 目标");
            Assert.That(Vector3.Distance(pose.rightWrist.position, rightTarget), Is.LessThan(PoseTolerance),
                "snap 模式下右腕骨未命中 body-space 目标");

            Assert.That(WristResidual(true), Is.LessThan(HealTolerance), "snap 传送腕骨后左手 A1 距离被破坏");
            Assert.That(WristResidual(false), Is.LessThan(HealTolerance), "snap 传送腕骨后右手 A1 距离被破坏");
        }

        [Test]
        public void A2b_BlendTransition_ConvergesToTarget_WithMonotoneResidual()
        {
            InstantiateShippedBody();
            Assert.That(ElenaWristAligner.HealBody(_body.transform, HealTolerance), Is.EqualTo(2));

            var pose = _body.GetComponentInChildren<FullBodyPoseController>(true);
            Assert.That(pose, Is.Not.Null);
            pose.blendPoseTransitions = true; // 混合过渡模式
            Assert.That(pose.ApplyPose(FullBodyPoseController.PoseNaturalSeated), Is.True);

            var leftTarget = pose.bodySpace.TransformPoint(
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Left));
            var rightTarget = pose.bodySpace.TransformPoint(
                FullBodyPoseController.GetBodySpaceWristTarget(FullBodyPoseController.PoseNaturalSeated, SeatedHandSide.Right));

            float lastLeft = Vector3.Distance(pose.leftWrist.position, leftTarget);
            float lastRight = Vector3.Distance(pose.rightWrist.position, rightTarget);
            int steps = 0;
            while ((lastLeft > PoseTolerance || lastRight > PoseTolerance) && steps < 200)
            {
                Assert.That(pose.BlendTowardCurrent(0.1f), Is.True, "混合模式应接受正的 deltaTime");
                float nextLeft = Vector3.Distance(pose.leftWrist.position, leftTarget);
                float nextRight = Vector3.Distance(pose.rightWrist.position, rightTarget);
                Assert.That(nextLeft, Is.LessThanOrEqualTo(lastLeft + 1e-5f), $"第 {steps} 步左手残差非单调递减");
                Assert.That(nextRight, Is.LessThanOrEqualTo(lastRight + 1e-5f), $"第 {steps} 步右手残差非单调递减");
                Assert.That(WristResidual(true), Is.LessThan(HealTolerance), $"第 {steps} 步左手 A1 距离被破坏");
                Assert.That(WristResidual(false), Is.LessThan(HealTolerance), $"第 {steps} 步右手 A1 距离被破坏");
                lastLeft = nextLeft;
                lastRight = nextRight;
                steps++;
            }
            Assert.That(lastLeft, Is.LessThan(PoseTolerance), $"混合过渡 {steps} 步未收敛到 ±1 mm（左 {lastLeft:F4} m）");
            Assert.That(lastRight, Is.LessThan(PoseTolerance), $"混合过渡 {steps} 步未收敛到 ±1 mm（右 {lastRight:F4} m）");
        }

        [Test]
        public void A3_HealBody_NeverTouchesAnyScaleOrRotation()
        {
            InstantiateShippedBody();
            var before = Snapshot();
            Assert.That(ElenaWristAligner.HealBody(_body.transform, HealTolerance), Is.EqualTo(2));
            var after = Snapshot();

            Assert.That(after.Count, Is.EqualTo(before.Count), "HealBody 不得增删 Transform");
            for (int i = 0; i < before.Count; i++)
            {
                Assert.That(after[i].localScale, Is.EqualTo(before[i].localScale),
                    $"{after[i].name}.localScale 被修改（修复①必须仅平移）");
                Assert.That(after[i].localRotation, Is.EqualTo(before[i].localRotation),
                    $"{after[i].name}.localRotation 被修改");
                Assert.That(after[i].transform.lossyScale, Is.EqualTo(before[i].lossyScale),
                    $"{after[i].name}.lossyScale 被修改（Elena 子树允许内部大缩放，但不许被修复改动）");
            }
        }

        [Test]
        public void A5_HealBody_MovesOnlyHandRootLocalPositions()
        {
            InstantiateShippedBody();
            var before = Snapshot();
            Assert.That(ElenaWristAligner.HealBody(_body.transform, HealTolerance), Is.EqualTo(2));
            var after = Snapshot();

            var leftRoot = HandRoot(true);
            var rightRoot = HandRoot(false);
            for (int i = 0; i < before.Count; i++)
            {
                bool isHandRoot = after[i].transform == leftRoot || after[i].transform == rightRoot;
                if (!isHandRoot)
                {
                    Assert.That(after[i].localPosition, Is.EqualTo(before[i].localPosition),
                        $"{after[i].name}.localPosition 不应被 HealBody 修改");
                }
            }
        }

        [Test]
        public void A6_AlignNow_Idempotent_AcrossTenCalls()
        {
            InstantiateShippedBody();
            Assert.That(ElenaWristAligner.HealBody(_body.transform, HealTolerance), Is.EqualTo(2));

            foreach (var left in new[] { true, false })
            {
                var handRoot = HandRoot(left);
                var aligner = handRoot.GetComponent<ElenaWristAligner>();
                Assert.That(aligner, Is.Not.Null, $"{handRoot.name} 应已补挂 ElenaWristAligner");
                aligner.Configure(left, _body.transform); // 强制重解析，模拟独立调用方
                Vector3 fixedLocal = handRoot.localPosition;
                for (int call = 1; call <= 10; call++)
                {
                    Assert.That(aligner.AlignNow(), Is.True, $"第 {call} 次 AlignNow 返回 false");
                    Assert.That(handRoot.localPosition, Is.EqualTo(fixedLocal),
                        $"第 {call} 次 AlignNow 后 localPosition 不再位级一致（幂等破坏）");
                }
            }
        }

        List<(Transform transform, string name, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Vector3 lossyScale)> Snapshot()
        {
            _allTransforms = new List<Transform>(_body.GetComponentsInChildren<Transform>(true));
            var rows = new
                List<(Transform transform, string name, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Vector3 lossyScale)>(
                    _allTransforms.Count);
            foreach (var t in _allTransforms)
                rows.Add((t, t.name, t.localPosition, t.localRotation, t.localScale, t.lossyScale));
            return rows;
        }
    }
}
