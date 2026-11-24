# HANDOFF — 穿衣全身 VR 人体样板第一版（2026-11-20 批次 3）

实际模型：**glm-5.3-flash / 最高档**（ZCode 本机会话）。活动窗口 20:54:51 → 20:58:16 +0800（date/stat 取证），可证明工具活动 ≈3.5 分钟；排队/等待不计。

## 1. 交付物（全部在本目录）

| 文件 | SHA-256（前8） | 说明 |
|---|---|---|
| `neutral_adult_avatar.blend` | `989b4b69` | 可编辑主场景：7 网格 + game_engine 骨架（53 骨）+ 14 材质 + 14 图像，1.70 m |
| `neutral_adult_avatar.fbx` | `d6a0c1e6` | 含骨架/蒙皮，嵌入贴图，无叶骨无动画 |
| `neutral_adult_avatar.glb` | `fc1b74c0` | glTF 二进制备用格式 |
| `front.png` / `side.png` / `three_quarter.png` / `wrist_closeup.png` | 见上 | 统一 EEVEE 64 采样 + 三点布光，1000×1500 / 腕口 1024² |
| `avatar_manifest.json` | `37bad843` | 脚本自动记录：资产来源、许可、SHA、骨名、顶点数、缩放记录 |
| `build_mpfb_avatar_patched.py` | — | 原脚本的**仅本目录副本**：修正 `bpy.mathutils`→`mathutils`（Blender 5.2.3 无前者）、`BLENDER_EEVEE_NEXT`→`BLENDER_EEVEE`、`parents[2]`→`parents[4]`。原脚本未动 |
| `verify_reopen.py` / `inspect_materials.py` / `render_extra_views.py` | — | 验收与阶段 3 实测脚本 |

## 2. 阶段 1 — 官方眼睛资源（全部通过）

- 唯一 URL（官方页主源）单次下载：HTTP 200，Content-Length 280,737,770 == 实收（严格相等），curl exit 0，SHA `b542127a…0107`；**517 成员 CRC 全 PASS**；仅隔离解压 `eyes/`（34 文件）。
- `low-poly.mhclo`（CC0 内嵌声明，basemesh hm08）→ `low-poly.obj`（182 v/f，可读，无外部 mtllib）→ `../materials/brown.mhmat` + `brown_eye.png` 齐全。34 文件 SHA：`system-assets-cc0-fresh-20261120/eyes_sha256.txt`。
- 旧 `.partial` 未访问/未覆盖/未续传/未删除。

## 3. 阶段 2 — 构建（已验证）

- 七角色显式参数构建 exit 0：皮肤(skins02 bronze) + 眼(low-poly) + 眉 + 发 + 毛衣 + 工装裤 + 布鞋，全部官方 CC0。
- **新 Blender 5.2.3 进程重开验收 PASS**（exit 0，`reopen_report.json`）：13 对象 / 7 网格 / 14 材质 / 14 图像 / 1 骨架，无错误。
- 缩放：uniform 1.70 m（manifest `final_height_m=1.7000000`）；评估顶点 25,847。

## 4. 阶段 3 — 材质实测（区别于源码推断）

隔离 Blender 打开成品 .blend（`material_inspection.json`，exit 0）：

- **14/14 图像全部解码成功，missing=0**；bronze 皮肤 diffuse (2048²) 与 `sss.png` 均实际载入，`Human.body` 为节点组（ENHANCED_SSS）。
- **cortu 裤子实测无 litsphere 节点**：节点为 Principled+BUMP+NORMAL_MAP+2×TEX_IMAGE（diff 2048²/norm 2048² 已解码）。证实此前源码推断（MakeSkin 跳过 litsphere 键）在运行时成立——这是**节点级证据，非"看起来正常"**。

## 5. 已知几何问题（如实报告，不做滤镜掩盖）

`front.png` 目检发现三处，均不阻塞交付但需下一轮修复：

1. 胸部两点：毛衣与身体之间小片皮肤穿出（delete-mask 未覆盖的接触区）；
2. 腰部两侧：毛衣下摆与裤腰交界处有小撕裂状开口；
3. 左裤脚：裤脚下缘与鞋相交处有剪切/闪面（z-fighting 或穿模）。

## 6. 未验证项

- 四视图未做逐像素审美评审（本会话自检 + 几何问题已列；正式审美评审归三名 Luna/顾问流程）。
- 蓝色半透明轮廓变体本批**未做**（任务允许"若本批做"；为保写实母版纯净，留待几何问题修复后独立做）。
- FBX/GLB 未在 Unity/引擎侧导入验证（本轮禁止运行 Unity）。
- 眉毛仅默认色未按 ART_DIRECTION 调深棕（母版保持资产原样，调色属预览轮）。

## 7. 下一步建议

1. 修三处几何穿模（优先查毛衣 delete-mask 与裤脚-鞋交界）→ 重出四视图；
2. 通过审美评审后补蓝色轮廓独立变体与 Unity 导入验证（届时解锁）；
3. `avatar_manifest.json` 中 explicit 外部文件的 license 字段为脚本保守值 "Not inferred"，本文档与预检文件已按官方页补正为 CC0。
