# REV8 — 指根/虎口蒙皮权重定点修复（run23 反馈）

日期：2026-11-18 ｜ 输入：`output/rev7/refined_hands.blend`（只读）
驱动姿态：`ActionCalibration/input_rev7/hand_rig_profile.json` A2 rev2 作者预设 fist 样本（仅作变形对比，**不构成自然拳捏验收**——A2 求解另行进行）。

## 修了什么（纯权重，零几何/骨架/贴图改动）

run23 `right_fist_axis_a`（手背）指根硬折/塌陷、`axis_b`（掌侧）虎口大块鼓包与拇指掌根扯裂。只读探测（probe_rev8.py）证明 rest 几何干净（13724 tri/手，拳姿下指根带边长比 min 0.767 无塌陷），病根是 1/d³ 权重在所有权边界产生**位移悬崖**——相邻顶点形变量突变，网格被拉裂（背面剔除露黑）。

三步手术（refine_hands_rev8.py）：
1. **关节带松弛**：15 个屈曲关节（每指 Meta→1→2→3）关节头半径带内父子权重向 50/50 拉平（带内线性衰减，BLEND 0.75）。MCP 带 0.055×手长，指间带 0.030/0.026。
2. **虎口分区**：外圈（朝拇指侧）皮肤提高拇指柱权重（≤0.95，随拇指对掌外旋走，架住开口）；内圈 50% 减量锚到 Wrist（压鼓包）。方向用 ThumbMeta.tail−质心 判定。
3. **位移悬崖协调**：拳姿下迭代混合位移差超阈的相邻边两端权重（阈值 0.15→0.02 共 7 轮至收敛），限定在虎口+关节带 0.45×手长区域防全局糊化。

编辑顶点：191 带 + 42 虎口/手 + 协调扩散 ≈3.8k 顶点权重字典；rest 校验和（三角数+顶点模长和）前后一致，骨架 22 骨/rest 矩阵/UV/rev7 肤色指甲贴图（复制并重连）全部不动。

## 数值（before → after，拳姿）

| 指标 | R | L |
|---|---|---|
| 最大位移悬崖 | 0.225 → **0.102** | 0.341 → **0.135** |
| 虎口均方根位移 | 0.114 → 0.104 | 0.160 → 0.149 |
| 指根带边长比 min | 0.767（持平，本无塌陷） | 0.767 |
| 面数/顶点/骨骼 | 13724 / 6864 / 22（不变） | 同 |

## 图像证据（同机位正交，rev7 非退化掌法线）

- `rev8_cmp_web_before/after.png`：axis_b 视角，虎口大黑楔（撕裂）→ 消失，仅余拇指甲缘 1–2px 采样缝。
- `rev8_cmp_dorsal_before/after.png`：axis_a 视角，指根背侧硬折带 → 平滑过渡，虎口上缘楔口闭合。

## 产物（output/rev8/）

`refined_hands.blend`（rest 保存）、`Hand_{R,L}_rev8.fbx`（腕局部导出，metric 0.119187 与 rev7 一致）、`Hand_{R,L}_{diffuse,roughness,normal}.png`（rev7 原图复制）、上述 4 张对比图、`refinement-summary.json`。

## Codex 合入清单

1. Unity 导入 `Hand_{R,L}_rev8.fbx` + 3 张 2K 贴图替换 canonical Models（与 rev7→canonical 同流程；贴图文件名与现 canonical 一致可直接替换路径）。
2. Capture run24 复验 axis_a/axis_b 两视角；预期指根/虎口缺陷消除。
3. **未声明项**：自然拳捏仍归 A2（本 rev 只修蒙皮变形质量）；拇指甲缘细缝与 rest 状态零改动属已知残余，不影响验收项。
