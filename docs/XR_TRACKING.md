# XR 手部追踪 (XR_TRACKING)

真实 XR 五指追踪的架构、关节映射、校准与失追处理，以及**当前为什么无法交付真实追踪**。

> ## 🚫 当前状态：阶段 D 阻塞
>
> **`com.unity.xr.hands@1.4.2` 无法从 Unity 中国版注册表 `packages.unity.cn` 获取。**
>
> 因此：
> - `XRHandsPoseProvider` **没有接入任何真实 XR 数据**，其实现被 `#define HAS_XR_HANDS` 关闭。
> - **尚未实现真实的五指光学追踪。**
> - 任何键盘预设、手柄 Grip 或镜像手**都不得称为真实光学追踪**。
>
> 本文件描述的是**已完成的架构与算法部分**，以及包可用后需要做的接入工作。

---

## 1. 为什么包不可得

尝试结果（Unity Package Manager 实际解析）：

| 包 | 版本 | 结果 |
|---|---|---|
| `com.unity.xr.hands` | 1.4.2 | ❌ **cannot be found** |

处理方式（遵守规范）：

- ❌ 未擅自替换 Unity 版本
- ❌ 未擅自更换注册表
- ❌ 未引入第二套追踪系统
- ✅ 从 `manifest.json` 移除该条目
- ✅ 在 `KNOWN_LIMITATIONS.md` 与 `TEST_REPORT.md` 中记录
- ✅ 把可独立验证的部分（映射表、重定向数学、有效性状态机）实现为**不依赖该包**的纯逻辑

**解除阻塞需要的条件（三选一）：**

1. 提供 `com.unity.xr.hands@1.4.2` 的本地 `.tgz`，放入 `Packages/` 并改为 `file:` 引用。
2. 换用可访问该包的注册表（需批准，且需重新验证与 glTFast / Burst / 2022.3 中国版的依赖关系）。
3. 确认该包对中国版 Editor 的可用版本（可能不是 1.4.2），并提供版本依据。

---

## 2. 已完成的架构

### 2.1 数据模型

`Tracking/HandTrackingFrame.cs` —— 实时 XR 位姿的**独立运行时类型**，
**没有塞进旧的 `HandMotionFrame`**（规范第 4.2 条明确要求）：

| 字段 | 用途 |
|---|---|
| `timestamp` | 帧时间戳 |
| `side` | 左手 / 右手 |
| `trackingOriginId` | 追踪空间标识，重定位后可检测变化 |
| `wristPosition` / `wristRotation` | 腕部位姿 |
| `wristPositionValid` / `wristRotationValid` | 腕部有效性（分开标记） |
| `jointPoses[]` / `jointValidity[]` | 各关节位姿与有效性 |
| `inputSource` | 输入来源 |
| `isMirrored` / `isEstimated` | 是否镜像 / 是否估计 |
| `validityWindow` | 数据有效期 |
| `sourceSwitchReason` | 来源切换信息 |

**设计要点：** 腕部位姿与手指数据分开，因为二者可以来自不同来源
（例如手柄追踪手腕 + Grip 模拟手指），而这种情况**必须显式标记为手柄模拟，不是光学追踪**。

### 2.2 关节映射表

`Tracking/HandJointRetargeter.cs` —— 规范第 5.2 条要求的
`XRHandJointID → Elena Native Rig Bone` 显式映射表。

**明确没有假设 XR 关节顺序等于 20 路控制量顺序。** 映射是独立表格：

| 追踪关节 | Elena 骨骼 |
|---|---|
| `Wrist` | `hand.R_02` |
| `Palm` | `hand.R.001_011` |
| `ThumbMetacarpal` | `thumb_base.R_03` |
| `ThumbProximal` | `thumb_01.R_08` |
| `ThumbDistal` | `thumb_02.R_09` |
| `ThumbTip` | `thumb_03.R_010` |
| `IndexMetacarpal` | `index_base.R_012` |
| `IndexProximal` | `index_01.R_017` |
| `IndexIntermediate` | `index_02.R_018` |
| `IndexDistal` | `index_03.R_019` |
| `IndexTip` | `index_03.R_end_053` |
| `MiddleMetacarpal` | `middle_base.R_020` |
| `MiddleProximal` | `middle_01.R_025` |
| `MiddleIntermediate` | `middle_02.R_026` |
| `MiddleDistal` | `middle_03.R_027` |
| `MiddleTip` | `middle_03.R_end_056` |
| `RingMetacarpal` | `ring_base.R_028` |
| `RingProximal` | `ring_01.R_033` |
| `RingIntermediate` | `ring_02.R_034` |
| `RingDistal` | `ring_03.R_035` |
| `RingTip` | `ring_03.R_end_059` |
| `LittleMetacarpal` | `pinky_base.R_036` |
| `LittleProximal` | `pinky_01.R_041` |
| `LittleIntermediate` | `pinky_02.R_042` |
| `LittleDistal` | `pinky_03.R_043` |
| `LittleTip` | `pinky_03.R_end_062` |

**已验证（静态）：** 全部 25 个映射名均存在于 Unity 实际采样的
`ElenaNativeRig_R.asset` 骨骼列表中。

**顺序约束：** `TrackedJoint` 枚举与 Unity 的 `XRHandJointID` **同序**，
包可用后可 `(TrackedJoint)(int)xrJointId` 直接转换，**但必须校验数量一致**。

### 2.3 重定向算法

**明确没有把全部关节压缩成一个 Grip 值**（规范第 5.2 条禁止）。

`FingerCurl()` —— 每根手指的弯曲度来自**该指自己的关节链**，
取相邻关节段之间的夹角，以掌心前向轴为参考（使整只手旋转不影响读数）。
关节无效时**回退到上一帧值**，而不是报告一个"伸直的假手指"。

`FillControls()` —— 写入 20 路控制量：
- 每根手指独立弯曲
- 指尖段取 0.9 系数
- `ApplySpread()` 用相邻近节骨骼的横向张角计算张开度

**为什么仍然输出 20 路控制量：** 规范第 5.2 条要求"当前 20 路控制量继续用于键盘、
CyberGlove 和 EEG 预设；完整 XR 关节输入允许采用更细粒度的骨骼旋转通道"。
因此本实现同时保留了两条路径：20 路控制量（统一接口）+ 逐骨骼旋转（更细粒度）。

### 2.4 校准

`Tracking/HandTrackingCalibration.cs` —— 规范第 5.2 条要求的校准项全覆盖：

| 校准项 | 字段 |
|---|---|
| 手腕与模型腕点位置 | `wristPositionOffset` |
| 骨骼初始旋转 | `wristRotationOffsetEuler` |
| 不同手长 | `handLengthScale` |
| 不同手掌宽度 | `palmWidthScale` |
| 掌心方向 | `palmAxisOffsetEuler` |
| 拇指对掌 | `thumbOppositionBias` |
| 左右手坐标系 | 左右各一份 `SideCalibration` |

**每个值都必须来自真实测量**：`SideCalibration.measured` 标志位区分"已测量"与"默认"，
`IsPlausible` 拒绝非有限值与超出 ±60% 的缩放。
`TryApplyWrist()` 在标定不可用时**返回 false 并原样透传**，
让调用方能够标记为"未校准显示"，而不是静默套用一个错误的变换。

### 2.5 失追处理

`Tracking/HandTrackingValidity.cs` —— 规范第 5.3 条逐项实现：

| 要求 | 实现 |
|---|---|
| 丢失数帧时短暂保持可信姿态 | `holdSeconds`（默认 0.15s）内 `MayDisplayCachedPose` 为真 |
| 追踪过期后标记该手不可交互 | `IsInteractive` 转为 false |
| 不能持续使用已失效的动作 | `MissingFrames` 累计，超窗口后不再假装有效 |
| **失追后不得突然展开拳头并错误释放物体** | 姿态保持，**绝不主动张开**；`HandInteractionRouter` 在不可信时不产生任何事件 |
| 恢复追踪时重新同步坐标与姿态 | `NeedsResync` + `resyncSeconds` 窗口 |
| **左手失追不影响独立有效的右手** | 两手各持一个 `HandTrackingValidity` 实例，互不干扰 |
| 镜像模式下操作手失追须同时处理对侧手 | 见下方说明 |

**镜像手注意：** 镜像手由操作手推算。操作手失追时，镜像手的有效性必须一并处理，
否则会出现"一只手冻住、另一只还在动"的不一致。**该联动逻辑尚未实现** ——
目前 `HandTrackingValidity` 是按手独立的，镜像联动需要在宿主层补上。

### 2.6 有效性与延迟统计

`HandTrackingValidity.LastJointValidityRatio` 提供**手指关节有效率**。

**延迟统计尚未实现。** 规范第 5.3 条要求建立"手指关节有效率及延迟统计"，
有效率已实现，延迟统计待包可用后一并补齐（需要真实关节数据才能测量有意义的值）。

---

## 3. 包可用后需要完成的工作

1. 在 `manifest.json` 恢复 `com.unity.xr.hands`，在 asmdef 中打开 `HAS_XR_HANDS` 宏。
2. `XRHandsPoseProvider` 接入 `XRHandSubsystem`，逐项检查（规范 5.1）：
   - 子系统是否存在且 `running`
   - 左右手 `isTracked`
   - 每个 `XRHandJoint` 的位姿是否有效
   - 当前数据来自 `Dynamic` 还是 `BeforeRender` 更新
   - 是否出现部分关节丢失
   - 是否发生追踪空间重定位
3. 用 XR Hands 官方 `HandVisualizer` 作为**诊断工具**比对设备原始关节与 Elena 手模型。
   ⚠️ `HandVisualizer` **仅用于验证设备数据，不取代最终 Elena FF 虚拟手**。
4. 实测并填入 `HandTrackingCalibration` 的各项数值。
5. 补齐镜像手失追联动与延迟统计。
6. 按 `TEST_REPORT.md` 第 6 节执行模拟 XR 测试（规范 10.5）与真实设备测试（规范 10.6）。

---

## 4. PICO 设备验证（规范 5.4）

**必须分开验证的五种模式：**

| 模式 | 状态 |
|---|---|
| 桌面无 XR 设备 | ✅ 可验证（键盘/手柄模拟） |
| PCVR 手柄追踪 | 🚫 无设备 |
| PCVR 光学手部追踪 | 🚫 无设备（且需先解除 XR Hands 阻塞） |
| PICO 一体机手部追踪 | 🚫 **HARDWARE_NOT_TESTED** |
| PICO 手柄与手部追踪切换 | 🚫 无设备 |

**规范明确要求：** 如果 PCVR 运行时没有暴露手部关节，**不得宣称已实现真实五指跟踪**；
应显示功能不可用并保留手柄模拟模式。

**接入 PICO 时必须记录（规范 2.5）：**

- 实际 Git SHA（当前仅有标签 `1.4.0`，**SHA 未获取**）
- 实际包版本（`package.json` 报 `1.4.1`，与标签号不一致）
- 最终解析到的 OpenXR 版本（SDK 声明需 `1.8.2`，当前工程为 `1.11.0`，**差异未验证**）

**禁止：** 同时启用互相冲突的 XR Loader；混合不同 PICO SDK 的追踪系统。
SDK 许可证含限制性条款，**不得未经授权把 SDK 源码或二进制上传到公开仓库**。

---

## 5. 输入来源标记

规范第 1.3 / 4.1 条要求每种输入记录真实来源。`IHandPoseProvider.SourceCategory` 取值：

| 值 | 含义 | 来源 |
|---|---|---|
| `device` | 真实硬件 | XR 光学关节、手柄 |
| `replay` | 离线文件 | 数据手套姿态回放 |
| `manual` | 手动输入 | 键盘、编辑器滑条 |
| `mirror` | 由对侧推算 | 镜像辅助模式 |
| `inferred` | 辅助推断 | EEG 整体运动意图 |

**可以来自不同来源，但必须显式配置**（规范 4.1）：

- VR 手柄追踪腕部位置 + Grip 模拟手指 → **不等同于光学手部追踪**
- EEG 推测握拳意图 + 辅助生成握拳姿态 → **不等同于测得真实五指关节**

`TryGetWrist()` 对键盘类 provider 返回 **false**，正是为了不让"没有腕部追踪"
被误报成"有腕部追踪"。

---

## 6. 相关文件

| 文件 | 作用 | 状态 |
|---|---|---|
| `Tracking/HandTrackingFrame.cs` | 实时跟踪帧数据结构 | ✅ 完成 |
| `Tracking/HandJointRetargeter.cs` | 关节映射 + 重定向 | ✅ 映射与算法完成，静态验证通过 |
| `Tracking/HandTrackingCalibration.cs` | 校准资产 | ✅ 类型完成，**实例需实测填入** |
| `Tracking/HandTrackingValidity.cs` | 有效性状态机 | ✅ 完成；镜像联动待补 |
| `Input/XRHandsPoseProvider.cs` | XR 数据接入 | ⛔ **空壳，被宏关闭，未接入真实数据** |
| `Interaction/HandGestureRecognizer.cs` | 手势识别 | ✅ 完成（可脱离 XR 运行） |
| `Interaction/HandInteractionRouter.cs` | 交互事件边沿 + 失追屏蔽 | ✅ 完成 |
