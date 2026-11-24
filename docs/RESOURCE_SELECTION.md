# 资源选型记录 (RESOURCE_SELECTION)

本文件记录本模块实际使用的每一个外部资源：来源、版本、许可证、是否进入工程、是否允许公开分发，以及兼容性验证状态。

规范要求：**新增外部依赖前必须确认实际兼容性、来源、许可证和是否允许在公开 GitHub 仓库中分发。**

工程版本：Unity **2022.3.62f3c1**（中国版），Built-in Render Pipeline，Windows x64。

---

## 1. 手部模型：Elena FF

| 项目 | 内容 |
|---|---|
| 名称 | Rigged Hand（仓库内称 Elena FF） |
| 来源 | https://sketchfab.com/3d-models/rigged-hand-eae97cc2a742413cb5338ab942b12c1e |
| 许可证 | **CC BY-SA 4.0** |
| 工程内路径 | `Assets/VirtualHand/Models/ElenaFF/ElenaHand_R.glb` |
| 授权记录 | `Assets/VirtualHand/Models/ElenaFF/ATTRIBUTION.md` |
| 是否进入工程 | **是**（继续沿用，未替换） |
| 是否允许公开分发 | **是**（CC BY-SA 4.0 允许再分发，需保留署名并以相同许可证发布衍生作品） |
| 兼容性实测 | 通过 glTFast 导入，骨骼层级、贴图、左右手方向、米制尺寸均已验证 |

**许可证义务（必须持续满足）：**

1. 保留原作者署名与来源链接。
2. 修改说明须记录：本工程修改了材质参数、导出了外部 base color 贴图、并新增了骨骼长度调节机制。
3. 衍生模型与动画须以 **CC BY-SA 4.0** 发布。
4. `ATTRIBUTION.md` 必须随可迁移包一起分发。

**当前状态：** 未替换模型。规范第 2.7 条要求，仅在明确证明 Elena FF 无法满足关节追踪、变形或目标设备性能要求时才允许提出替换方案，并需同时提供模型质量、骨骼兼容性、Unity 导入验证、许可证对比证据。**目前未提出替换，也未发现需要替换的证据。**

### 1.1 对模型文件做过的修改

| 修改 | 位置 | 说明 |
|---|---|---|
| 导出外部 base color 贴图 | `Assets/VirtualHand/Models/ElenaFF/ElenaHand_BaseColor.png` | 从 GLB 内嵌 JPEG 导出为 2048² RGBA PNG，使肤色与指甲可独立编辑 |
| 材质修正 | `Assets/VirtualHand/Materials/Generated/Elena_Skin_ElenaHand_R.mat` | `_CullMode` 0→2（双面改单面）、`metallicFactor`=0、`roughnessFactor`=1 |
| 基准备份 | `ArtSource/Textures/ElenaHand_BaseColor_original.png` | GLB 原始贴图（从 GLB 重新提取，非缓存副本） |
| 基准备份 | `ArtSource/_backup_ElenaHand_R_original.glb` | 原始 GLB |

### 1.2 GLB 优化尝试（已回退）

曾用 `ArtSource/strip_glb.py` 移除 GLB 内未引用的 964 KB 纯白 PNG（`image[2]`）并把 2048² metallicRoughness 替换为 64×64 版本，体积从 3,812,180 B 降至 2,009,236 B（**-47.3%**）。

**该优化已回退**：批处理导入优化版 GLB 时失败，为不阻塞核心模块，已从备份恢复原始 GLB。当前工程内 GLB 为原始 **3,812,180 B**。此项列为未完成的可选优化，不计入交付成果。

---

## 2. 完整人体资源：MPFB2 / MakeHuman（本轮未接入）

| 项目 | 内容 |
|---|---|
| 名称 | MPFB2 (MakeHuman Plugin For Blender 2) |
| 来源 | https://github.com/makehumancommunity/mpfb2 |
| 工具代码许可证 | **GPLv3** |
| 官方资产许可证 | **CC0** |
| 本轮用途 | **无**。仅保留为后续完整 Avatar、手臂、肩颈和躯干连接的路线 |
| 是否进入工程 | 否（本轮未新增） |
| 是否允许公开分发 | 工具代码 GPLv3；官方资产 CC0。**每件额外服装或头发仍需单独核对许可证。** |

**本轮明确未做：** 没有用 MPFB 手替换 Elena FF 手。规范第 2.2 条要求优先通过人体尺寸校准、腕部对齐和蒙皮过渡处理两套资产的差异——该工作属于完整 Avatar 路线，不阻塞手部模块。

历史 MPFB 人体候选、服装与 Blender 工程**全部原样保留在 `ArtSource/`**，未删除。

---

## 3. XR 手部追踪：Unity XR Hands

| 项目 | 内容 |
|---|---|
| 名称 | com.unity.xr.hands |
| 目标版本 | **1.4.2** |
| 官方文档 | https://docs.unity.cn/Components/com.unity.xr.hands.html |
| 许可证 | Unity Companion License |
| **实际解析结果** | **失败 — 无法从 `packages.unity.cn` 获取** |
| 是否进入工程 | **否** |
| 兼容性实测 | **无法验证（包不存在于当前注册表）** |

**这是阶段 D 的阻塞根因。** Package Manager 返回 "cannot be found"。已尝试的版本：`1.4.2`。未擅自替换 Unity 版本，未擅自改用其他注册表。

**当前替代方案：** `Tracking/HandJointRetargeter.cs` 内含一份与 `XRHandJointID` 同序的 `TrackedJoint` 枚举镜像，使重定向与关节有效性逻辑**现在就能编译和单元测试**，无需该包。包可用后按 `(TrackedJoint)(int)xrJointId` 转换即可，但必须校验数量一致。

详见 `XR_TRACKING.md`。

---

## 4. XR 基础包（已成功解析）

规范第 2.4 条列出的候选组合，实际解析结果如下。**注意：这是 Package Manager 的解析结果，不代表已在 PICO 设备上通过运行验证。**

| 包 | 声明版本 | 实际解析版本 | 状态 |
|---|---|---|---|
| `com.unity.inputsystem` | 1.7.0 | **1.7.0** | ✅ 解析成功 |
| `com.unity.xr.management` | 4.4.1 | **4.4.1** | ✅ 解析成功 |
| `com.unity.xr.openxr` | 1.11.0 | **1.11.0** | ✅ 解析成功 |
| `com.unity.xr.hands` | 1.4.2 | — | ❌ **注册表无此包** |

`packages-lock.json` 已生成（`Packages/packages-lock.json`）。

依赖关系检查：`com.unity.xr.management` 在 lock 中声明依赖 `com.unity.xr.legacyinputhelpers@2.1.2` 与最低 `com.unity.inputsystem@1.6.3`；本工程显式锁定 1.7.0，解析到 1.7.0，**无冲突**。与既有 `com.unity.cloud.gltfast@6.13.1`（本地 tgz）和 `com.unity.burst@1.8.21` 同样**无冲突**。

**未在三个正式游戏分支中提前加入 XR 依赖。**

---

## 5. PICO 设备适配（本轮未接入）

| 项目 | 内容 |
|---|---|
| 名称 | PICO Unity OpenXR SDK |
| 选定标签 | **1.4.0** — https://github.com/Pico-Developer/PICO-Unity-OpenXR-SDK/tree/1.4.0 |
| 手部追踪文档 | https://developer.picoxr.com/document/unity-openxr/hand-tracking/ |
| 许可证 | **含限制性条款** |
| 是否进入工程 | **否** |
| 是否允许公开分发 | **未经授权不得把 SDK 源码或二进制直接公开上传到本项目** |

**必须记录的版本歧义（规范明确要求）：** 该标签内的 `package.json` 将包版本标为 `1.4.1`，并声明依赖 `com.unity.xr.openxr@1.8.2`。因此**不能只记录"PICO SDK 1.4"**。

若后续接入，须完整记录：

- 实际 Git SHA（当前仅有标签名 `1.4.0`，**SHA 未获取**）
- 实际包版本（`package.json` 报 `1.4.1`）
- 最终解析到的 OpenXR 版本（SDK 声明需 `1.8.2`，与当前工程的 `1.11.0` 存在潜在差异，**未验证**）

规范第 2.5 条禁止同时启用互相冲突的 XR Loader，或混合不同 PICO SDK 的追踪系统。**本轮未安装任何 XR Loader。**

---

## 6. glTF 导入：glTFast

| 项目 | 内容 |
|---|---|
| 名称 | com.unity.cloud.gltfast |
| 版本 | **6.13.1** |
| 来源 | 工程内本地包 `Packages/com.unity.cloud.gltfast-6.13.1.tgz` |
| 许可证 | Unity Companion License |
| 是否进入工程 | **是**（仓库既有，继续沿用） |
| 兼容性实测 | 通过。Elena FF 的骨骼层级、贴图与材质导入正常 |

**规范第 2.6 条要求：不再引入另一套 glTF 加载器。** 本轮未引入任何其他 glTF 加载器。

---

## 7. 未新增手部模型

规范第 2.7 条：当前资源足够开展功能开发。**本轮未生成任何新的手部模型**，未新增第二套手部资产。

`ArtSource/` 内的历史建模候选、服装、Blender 工程与失败实验记录**原样保留**，未为清理目录而删除任何有效源文件。

---

## 8. 本轮新增到工程的资源清单

| 资源 | 类型 | 来源 | 许可证 | 进入工程 |
|---|---|---|---|---|
| `ElenaHand_BaseColor.png` | 贴图 | 由 Elena FF GLB 内嵌 JPEG 导出 | CC BY-SA 4.0（同 Elena FF） | 是 |
| `HandProportions.asset` | ScriptableObject | 本工程生成 | 本工程 | 是 |
| `HandTrackingCalibration.cs`（资产由用户测量生成） | ScriptableObject 类型 | 本工程代码 | 本工程 | 类型已进入，实例未创建 |
| `HandActionBindingProfile.cs`（资产由用户创建） | ScriptableObject 类型 | 本工程代码 | 本工程 | 类型已进入，实例未创建 |

**未引入任何新的第三方模型、贴图或二进制依赖。**

---

## 9. 汇总表

| 资源 | 来源 | 版本 / SHA | 许可证 | 依赖 | 进入工程 | 可公开分发 | Unity 兼容性实测 |
|---|---|---|---|---|---|---|---|
| Elena FF 手模型 | Sketchfab | eae97cc2… | CC BY-SA 4.0 | glTFast | 是 | 是（需署名+同许可） | ✅ 导入正常 |
| MPFB2 | GitHub | 未接入 | GPLv3 / 资产 CC0 | Blender | 否 | 工具 GPLv3 | 未测（本轮不用） |
| Unity XR Hands | Unity 注册表 | 1.4.2 | Unity Companion | OpenXR | **否** | 是 | ❌ **注册表无此包** |
| XR Management | Unity 注册表 | **4.4.1** | Unity Companion | LegacyInputHelpers | 是 | 是 | ✅ 解析成功 |
| XR OpenXR | Unity 注册表 | **1.11.0** | Unity Companion | XR Management | 是 | 是 | ✅ 解析成功 |
| Input System | Unity 注册表 | **1.7.0** | Unity Companion | — | 是 | 是 | ✅ 解析成功 + 编译通过 |
| PICO OpenXR SDK | GitHub 标签 1.4.0 | SHA **未获取**，包版本 1.4.1 | 含限制性条款 | OpenXR ≥1.8.2 | **否** | **需授权** | 未测 |
| glTFast | 工程内 tgz | 6.13.1 | Unity Companion | Burst | 是 | 是 | ✅ 导入正常 |
