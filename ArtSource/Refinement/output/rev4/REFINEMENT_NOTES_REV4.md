# Refinement rev4 — final notes (GLM task B, 2026-11-18 06:22 run)

## 本轮达成（对照 05:45 与 06:10 反馈）

1. **可读视图**：8 张渲染全部可用——palm/back/side/fist（R/L 各四），手占
   画幅约 75%，五指/指甲/腕完整展开、互不遮挡、无透视缩短。取景公式：
   `ortho_scale = max(extents.y,0.05)*margin*2*1.78`（16:9 全宽），配
   `to_track_quat("-Z","Y")` 瞄准（曾三次失败的 matrix→euler 往返已弃用，
   相机矩阵约定经 A/B 渲染对照后确定）。
2. **掌面法线**：`world_palm_frame` 改用**真实腕点 Wrist.head + 两 MCP
   tail** 构造非共线三角（Wrist.tail 是两 MCP 中点，三点近共线——Codex
   指出的退化），叉乘长度 < 1e-4 时显式报错；符号经两轮渲染实证翻转
   （该 rig 的 Wrist.tail 穿掌体积指向 MCP 行，cross 恒指手背），代码内
   注明实证依据。掌心空心投票与拇指位置判定两方案均实测不可靠后弃用。
3. **权重**：区域链（每指自己 chain + 掌骨 + Wrist/Forearm）+ 一次邻域
   平滑，跨指拉扯消除。
4. **rest 硬化**：骨架/网格 animation_data 全清后再保存/取景（baseline
   无 Action 已实证，防御性保留）。
5. **导出**：腕局部 0.19m 规则原样（metresPerSourceUnit=0.119187 为归一化
   因子非手长，未额外改 scale）；rev4 blend/FBX/贴图完整同源。

## 如实声明

- 洋红已消（rev3），肤色自然偏亮——底色由 baseline 烘焙 diffuse 决定，
  真正换肤需 A 源纹理重烘（A 负责源纹理）。
- "palm"/"back" 视图为轴对齐近似；真掌面法线视图的最终确认需 A 轴映射
  交付后由 Codex Unity 复验。fist/pinch 为诊断渲染非产品姿势 PASS。
- 全部未经 Unity 验证；DHS 402 无二审。

## 复现

```
D:/SoftwareManagement/apps/Blender/blender.exe --background --factory-startup \
  --python ArtSource/Refinement/refine_hands_rev4.py
```
