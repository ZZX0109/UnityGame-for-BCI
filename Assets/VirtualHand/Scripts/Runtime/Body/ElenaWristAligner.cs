using System;
using UnityEngine;

namespace Neuralife.VirtualHand.Body
{
    /// <summary>
    /// V5-075 修复①（ClipAudit 缺陷①，DEFECT/HIGH）：Elena GLB 的内部节点链
    /// （根节点 0.0145249× 均匀缩放 × Armature 100×）使得手部实例在
    /// localPosition=(0,0,0)、单位旋转下，其腕部地标 hand.L_02/hand.R_02 相对
    /// 身体腕骨 hand_l/hand_r 存在约 2.8166 m 的固有偏移
    /// （见 Game03-Integration/Logs/WristOffsetAnalysis.txt 的 DEFECT 判定）。
    ///
    /// 本组件在挂载/绑定时测量“地标 − 腕骨”的世界差，并在手部实例父级的局部空间里
    /// 施加一次反向平移将其抵消：InverseTransformVector 自动吸收父链旋转与逐轴
    /// lossyScale（含身体侧约 101.65× 的授权骨骼缩放——该数值为算术推断，非解析 FBX）。
    /// 仅平移补偿：不改任何 localScale（验收 A3 守护）、不重蒙皮、不重绑骨。
    /// 幂等：补偿后差值 ≈0，重复调用为 no-op（容差 toleranceMeters，默认 2 cm）。
    ///
    /// 引用按名字在运行时解析（不序列化 Transform 引用）——嵌套预制体内部对象
    /// 无法从外部组件持久引用，且出厂预制体（旧构建器产出）没有本组件，
    /// 由 SokobanFullBodyBinding/StreetCrossingFullBodyBinding 调 HealBody 补挂并自愈。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElenaWristAligner : MonoBehaviour
    {
        [Tooltip("true 时对齐 hand.L_02→hand_l，否则对齐 hand.R_02→hand_r。")]
        public bool isLeft;

        [Tooltip("对齐判定的世界剩余差容差（米），验收目标 <0.02。")]
        public float toleranceMeters = 0.02f;

        const string LogPrefix = "[ElenaWristAligner] ";

        Transform _landmark;
        Transform _wristBone;
        Transform _scopeRoot;
        bool _resolved;

        /// <summary>设置左右手并使引用缓存失效（下次 AlignNow 重新解析）。</summary>
        public void Configure(bool left) => Configure(left, null);

        /// <summary><paramref name="alignmentScope"/> 限定腕骨回退搜索范围（多化身场景防跨实例误绑）。</summary>
        public void Configure(bool left, Transform alignmentScope)
        {
            isLeft = left;
            _scopeRoot = alignmentScope;
            _resolved = false;
        }

        /// <summary>
        /// 执行一次腕部对齐；返回对齐后剩余差是否 ≤ toleranceMeters。
        /// 已对齐（差值 ≤ 容差）时为幂等 no-op 并返回 true。
        /// </summary>
        public bool AlignNow()
        {
            if (!ResolveRefs()) return false;

            // 审计采纳（audit-math MED）：腕骨必须就是手部实例父级或其祖先，否则补偿
            // 相对被传送链不共线，写入只会制造新错位——放弃优于写坏。
            if (transform.parent == null || !transform.parent.IsChildOf(_wristBone))
            {
                Debug.LogWarning($"{LogPrefix}{SideLabel()} 腕骨不在手部实例父链上，放弃补偿。");
                return false;
            }

            Vector3 worldDelta = _landmark.position - _wristBone.position;
            float before = worldDelta.magnitude;
            if (before <= toleranceMeters) return true; // 已对齐：幂等 no-op

            Transform parent = transform.parent;
            Vector3 localShift = parent != null ? parent.InverseTransformVector(worldDelta) : worldDelta;
            if (!IsFinite(localShift) || localShift.sqrMagnitude < 1e-12f)
            {
                Debug.LogWarning($"{LogPrefix}{SideLabel()} 局部平移无效（父级缩放退化或计算溢出），放弃补偿。");
                return false;
            }
            // 审计采纳（audit-math LOW）：父级某轴缩放极小但非零时局部平移可能巨大且有限，
            // 与世界差比例失真即拒绝写入（<1 µm 下限已由 sqrMagnitude 守护）。
            if (localShift.magnitude > 1000f * Mathf.Max(before, 1f))
            {
                Debug.LogWarning($"{LogPrefix}{SideLabel()} 局部平移 {localShift.magnitude:F4} 与世界差 {before:F4} m 比例异常，放弃补偿。");
                return false;
            }
            transform.localPosition -= localShift;

            float after = (_landmark.position - _wristBone.position).magnitude;
            Debug.Log($"{LogPrefix}{SideLabel()} 腕部补偿：{before:F4} m → {after:F4} m（localPosition -= {localShift:F6}）。");
            return after <= toleranceMeters;
        }

        /// <summary>
        /// 在整个身体实例上自愈全部 Elena 手：按名字找 ElenaHand_L/R，缺组件则补挂，
        /// 逐手执行 AlignNow。返回成功对齐的手数（0/1/2）。对已对齐预制体是 no-op。
        /// </summary>
        public static int HealBody(Transform bodyRoot, float toleranceMeters = 0.02f)
        {
            if (bodyRoot == null) return 0;
            int healed = 0;
            foreach (var handRoot in bodyRoot.GetComponentsInChildren<Transform>(true))
            {
                // 审计采纳（audit-math LOW）：忽略大小写并剥离 "(Clone)"，运行时克隆根也命中。
                bool left = IsHandRootName(handRoot.name, true);
                if (!left && !IsHandRootName(handRoot.name, false)) continue;
                var aligner = handRoot.GetComponent<ElenaWristAligner>();
                if (aligner == null) aligner = handRoot.gameObject.AddComponent<ElenaWristAligner>();
                aligner.toleranceMeters = toleranceMeters;
                aligner.Configure(left, bodyRoot);
                if (aligner.AlignNow()) healed++;
            }
            return healed;
        }

        /// <summary>解析并缓存腕骨与地标引用（每次 Configure 后失效重析）。</summary>
        bool ResolveRefs()
        {
            if (_resolved && _landmark != null && _wristBone != null) return true;
            _landmark = FindLandmark(transform, isLeft);
            _wristBone = FindBodyWristBone(transform, isLeft, _scopeRoot);
            if (_landmark == null)
                Debug.LogWarning($"{LogPrefix}{SideLabel()} 未找到 Elena 腕部地标 hand.{(isLeft ? "L" : "R")}_02。");
            if (_wristBone == null)
                Debug.LogWarning($"{LogPrefix}{SideLabel()} 未找到身体腕骨 hand_{(isLeft ? "l" : "r")}。");
            _resolved = _landmark != null && _wristBone != null;
            return _resolved;
        }

        /// <summary>
        /// 身体腕骨只从手部实例的祖先链找（Elena 骨骼是点号命名且只在自身子树内，
        /// 祖先链上只有身体骨骼，前缀匹配安全）；找不到再回退全根层级搜索并排除自身子树。
        /// 匹配语义与构建器 FindWristBone 一致：小写后全等或前缀匹配。
        /// </summary>
        static Transform FindBodyWristBone(Transform handRoot, bool left, Transform scopeRoot)
        {
            string target = left ? "hand_l" : "hand_r";
            // 审计采纳（audit-math LOW）：先全链精确等值扫描、再前缀扫描，
            // 避免链上 "hand_l_offset" 之类的中间节点抢先命中。
            for (Transform t = handRoot.parent; t != null; t = t.parent)
                if (t.name.ToLowerInvariant() == target) return t;
            for (Transform t = handRoot.parent; t != null; t = t.parent)
                if (t.name.ToLowerInvariant().StartsWith(target)) return t;

            // 审计采纳（audit-math MED）：回退搜索限定在身体根（缺省场景根）子树内，
            // 多化身同场景时绝不跨实例误绑。
            Transform scope = scopeRoot != null ? scopeRoot : handRoot.root;
            if (scope == null) return null;
            foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            {
                if (t == handRoot || t.IsChildOf(handRoot)) continue; // 绝不落回手部自身子树
                if (NameMatches(t.name, target)) return t;
            }
            return null;
        }

        static bool NameMatches(string name, string target)
        {
            string lowered = name.ToLowerInvariant();
            return lowered == target || lowered.StartsWith(target);
        }

        /// <summary>Elena 腕部地标查找，与构建器 FindElenaWrist 保持一致（精确段 → 宽松回退）。</summary>
        static Transform FindLandmark(Transform handRoot, bool left)
        {
            // V5-075 门禁实测：ElenaHand_L.prefab 与 ElenaHand_R.prefab 的 m_SourcePrefab guid
            // 均指向 ElenaHand_R.glb，该 GLB 只有 hand.R_02 / hand.R.001_011 两个 hand.* 节点，
            // 不存在任何 hand.L_*。即左手实例的腕部地标实际名为 hand.R_02——先按本侧精确名，
            // 再按对侧精确名兜底，最后走宽松回退。
            string primary = left ? "hand.L_02" : "hand.R_02";
            string secondary = left ? "hand.R_02" : "hand.L_02";
            var children = handRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in children)
                if (ElenaNativeRigProfile.MatchesBone(t.name, primary)) return t;
            foreach (var t in children)
                if (ElenaNativeRigProfile.MatchesBone(t.name, secondary)) return t;
            foreach (var t in children)
                if (t != handRoot && MatchesLooseWristLandmark(t.name, left))
                    return t;
            return null;
        }

        // 审计采纳（audit-math LOW）：宽松回退按左右构造前缀——MatchesBone 的数字段
        // 前缀恒为 "hand.R_"，左手不可用；wrist 命名作为次选。
        static bool MatchesLooseWristLandmark(string name, bool left)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith(left ? "hand.l_" : "hand.r_", StringComparison.OrdinalIgnoreCase)) return true;
            return name.IndexOf("wrist", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 审计采纳（audit-math LOW）：忽略大小写并剥离运行时克隆后缀 "(Clone)"。
        static bool IsHandRootName(string name, bool left)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int idx = name.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) name = name.Substring(0, idx);
            return string.Equals(name, left ? "ElenaHand_L" : "ElenaHand_R", StringComparison.OrdinalIgnoreCase);
        }

        string SideLabel() => isLeft ? "L" : "R";

        static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }
}
