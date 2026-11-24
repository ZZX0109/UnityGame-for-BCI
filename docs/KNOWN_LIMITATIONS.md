# Neuralife Virtual Hand System — 已知限制 (KNOWN_LIMITATIONS)

2026-11-24  ·  分支 `codex/virtual-hand`  ·  基线 `fab911feb3e6b70ff23d7d9af0951eb33fc12200`
Unity **2022.3.62f3c1**（中国版）· Built-in RP · Windows x64

> 本文件记录**当前实际未能解决或未能验证**的问题。
> 每一条都注明是"已确认代码问题"、"未验证"还是"环境阻塞"，不混淆三者。

---

## 1. 已确认的代码问题

### 1.0 由测试发现并修复的 8 个缺陷（已闭环，记录供溯源）

测试的价值在这两轮体现得很直接：新增测试抓出 **8 个真实缺陷**，其中 **3 个会让功能在成品中完全不可用**。

| # | 缺陷 | 后果 | 状态 |
|---|---|---|---|
| 1 | `HandProportions.ScaleFor()` 的 `metacarpalScale` 是死代码 —— `*_base` 名字以手指前缀开头，进入手指分支后不含 `_01./_02./_03.`，在 `else return 1f` 提前返回 | 手掌长度调节完全无效 | ✅ 已修复 |
| 2 | `HandActionBinding.Key` 用 `/` 分隔（`Open/Left`），而 `InputActionAsset.FindAction` 把 `/` 解析为 "map/action" 路径 | 动作按名字永远查不到，重绑定静默失效 | ✅ 已修复（分隔符改为 `.`，并统一走 map 作用域查找） |
| 3 | `Rebind()` 先置 `_asset = null` 再调 `SaveOverrides()`，而后者开头即 `if (_asset == null) return;` | **重绑定永远不落盘** | ✅ 已修复（并改为只写运行时覆盖、不动作者默认值） |
| 4 | `SetControls()` / `UpdateControls()` 对 NaN/Infinity/超范围是**清洗后照样应用**，且**就地改写调用方数组** | 违反规范 3.2；一次 NaN 爆发会把最后可信姿态覆盖成零手 | ✅ 已修复（改为拒绝，不触碰调用方数组） |
| 5 | `HandInteractionRouter` 没有 `Update()`，且**没有任何地方调用它的 `Tick()`** | **手势→游戏事件的整条链路在成品中是死的** | ✅ 已修复（组件自 tick + 宿主显式 tick，帧锁防重复） |
| 6 | `VirtualHandHost` 把 `_leftGesture`/`_rightGesture` 传给适配器，但**从未赋值** | 适配器永远收到 `None`，游戏无法响应任何手势 | ✅ 已修复 |
| 7 | `KeyboardHandPoseProvider` 用 `candidates[(int)targetSide]` 索引长度为 2 的数组，而 `SeatedHandSide` 是 `{ Unspecified = 0, Left = 1, Right = 2 }`，默认值 `Right` = 2 | **每帧抛 `IndexOutOfRangeException`** —— 旧键盘 provider 完全不可用 | ✅ 已修复（改用显式的 0/1 侧别映射） |
| 8 | `HandRigDriver.CacheBones()` 在 native 路径**提前返回**，`_thumb`/`_fingers` 为空；且 `_allBones` 只由 legacy 路径填充 | 新增的指间自穿插检测拿不到骨链，测量恒为 `Infinity`，检测形同虚设 | ✅ 已修复（两条路径都填充指链，native 侧按地标名解析并回退到层级搜索） |
| 9 | 把「切源清态」接到 `TaskController.EnterSafeState()` 上，而该方法会**同时武装 tracking-resync 闸门** | 加载任何回放都会把任务冻住 `inputResyncSeconds`（0.3s）—— **由既有任务测试立即捕获** | ✅ 已修复（拆出 `ClearStaleState()`：切源只清状态，不武装失追重同步；失追才用 `EnterSafeState()`） |

> **第 9 项值得单独记一笔：** 它是我在本轮**自己引入**的回归，被既有任务测试在几分钟内抓到。
> 根因是两个语义被错误地耦合在一起 —— 「切换数据来源」和「追踪丢失恢复」都需要清状态，
> 但只有后者需要冻结等待重新同步。**共享一个方法看起来省事，代价是把一个 0.3 秒的冻结
> 强加到了每一次回放加载上。**

**验证：** 修复后 EditMode **100/100**（`Logs/editmode-r8.xml`）、PlayMode **68/71**（`Logs/playmode-r8.xml`，余 3 条为第 1.5 节的配置冲突）。Windows 构建与 UnityPackage 导出已在第 8 轮**复跑通过**（2026-11-24 17:21 / 17:23：`Succeeded, size 92MB, errors 0, warnings 0`，`Logs/build-r8.log`；包 5.84 MB，`Logs/export-r8.log`）；Burst AOT 仍为关闭状态（第 5.5 节），任何性能结论须一并披露。

---

### 1.0b 测试无法替代的部分（关于"侧别正确"的一个教训）

规范第 9 条要求验收「左右手长度、掌宽和侧别正确」。**侧别这一项无法用骨骼局部测量判定**，
我第一版测试因此得出了误导性的结论。

原因：左右手**共用同一个网格与同一套内部骨骼布局**，镜像完全编码在**根节点旋转**里。
因此：

- 在骨骼局部空间里测量 → 左右手完全相同，无信息量
- 把拇指偏移投影到"食指→小指"轴上 → **该轴本身也被镜像**，点积是不变量，同样无信息量

第一版测试就是踩了第二条，测出左右手"拇指偏移完全相同（都是 -0.0520）"，
并据此误报"有一只手镜像错了"。实际是**测量方法无效**。

**正确的判定方式**（已改为实际断言）：左手旋转必须等于右手旋转绕 YZ 平面的反射。
实测二者夹角 **0.000°**：

```
右手旋转 qr = (x, y, z, w)
反射后     = (x, -y, -z, w)
与左手旋转的夹角 = 0.000°  ✅
```

**这是本轮一个真实的方法论教训：** 一个 FAIL 的断言不一定意味着产品有问题，
也可能是断言本身在不变量上做判断。**先证明"这个量真的能区分两种情况"，再据此断言。**

### 1.1 镜像手失追联动未实现

`HandTrackingValidity` 按手独立工作（这满足"左手失追不影响右手"的要求），
但**镜像模式下操作手失追时，由它推算的对侧手没有被一并处理**。

规范第 5.3 条要求："镜像模式下，操作手失追必须同时处理由它推算的对侧手。"
目前会造成"一只手冻结、另一只仍在动"的不一致。

**位置：** `Tracking/HandTrackingValidity.cs` + 宿主层的镜像联动（缺失）
**影响：** 仅镜像辅助模式
**状态：** 未修复

### 1.2 关节延迟统计未实现

规范第 5.3 条要求建立"手指关节有效率及延迟统计"。

- 有效率：已实现（`HandTrackingValidity.LastJointValidityRatio`）
- 延迟统计：**未实现**

**原因：** 需要有真实关节数据才有意义，而 XR 数据接入被第 5.1 节阻塞。
**状态：** 未实现

### 1.3 `FirstPersonPrefabTests` 单条失败（结果已过期，需重跑）

- 测试：`BothAnatomicalWristsReachTheirGripPosesInAHostWithoutTheDemo`
- 位置：`FirstPersonPrefabTests.cs:29`
- 断言：`Assert.AreEqual(left, driver.IsMirrored)`
- 信息：`Expected: True But was: False`

**静态排查结论（本轮完成）：**

| 检查项 | 结果 |
|---|---|
| `ElenaHand_L.prefab` 的 `_isMirrored` | **1** ✅ |
| `ElenaHand_R.prefab` 的 `_isMirrored` | **0** ✅ |
| `FirstPersonHands.prefab` 中 `leftHand` 指向 | `ElenaHand_L` ✅ |
| 同上 `rightHand` 指向 | `ElenaHand_R` ✅ |
| 运行时是否有代码覆盖 `SetMirrored` | 无 ✅ |

接线与序列化值都正确，所以**该失败很可能是过期结果** ——
`FirstPersonHands.prefab` 在 13:05 被重建，而这份失败记录来自 12:0x。

**状态：已通过第 8 轮复跑确认。** EditMode **100/100**（`Logs/editmode-r8.xml`，
2026-11-24 16:37），镜像/侧别相关的 PlayMode 用例同步通过（见 `docs/TEST_REPORT.md` 第 3 节）。

### 1.4 手势识别的捏取判定是启发式

`HandGestureRecognizer.PinchDistance()` 用 20 路控制量的组合误差估算拇指与食指的接近程度，
**不是几何距离**。

**原因：** 不需要每帧查询蒙皮网格，开销低，且在键盘/回放来源下没有真实关节可用。
**影响：** 拇指解剖结构差异大的用户可能需要调阈值。
**状态：** 设计取舍，非缺陷；接入真实 XR 后应改为几何距离

### 1.5 手指比例与演示任务几何标定冲突（**已用 A/B 实验确认**）

**现象：** `PlayModeTaskTests` 3 条失败，全部是几何断言：

| 用例 | 失败信息 |
|---|---|
| `Carry_CompletesWithContinuousRestInsideDropZone` | `too many weighted skin vertices penetrate the box` |
| `ManualPinch_BothHandsHaveOpposingPadContact` | `thumb needs a facing contact patch within 3 mm` |
| `RealReplay_CarriesAndHandsOffThroughSavedButtons` | `open fingers intrude while awaiting grip` |

**受控 A/B 实验（已执行）：**

```
HandProportions = 用户设定(1.1 / 1.2)  →  PlayModeTaskTests  total=9  passed=6  failed=3
HandProportions = identity (全 1)      →  PlayModeTaskTests  total=9  passed=9  failed=0
```

**结论：这不是代码缺陷，而是真实冲突。** 手指加长 10–20% 之后，演示任务的几何标定不再成立。

**受影响的既有标定：**

- 箱子抓取的顶点侵入判定（`taskContactRange`、`leftTaskHoldCurl` / `rightTaskHoldCurl`）
- 拇指与食指指腹的 3 mm 对面接触判定
- 抓取前的张开手指侵入检查

**根因：** `ElenaNativeRigProfile.fingerSpheres` 与拇指间隙由 `ElenaNativeProfileBuilder`
从**未修改的 GLB** 采样而来，**不会跟随 `HandProportions`**。
因此骨骼被缩放后，接触球体仍停留在原始位置，判定随之失真。

**当前处理：** 按产品所有者明确要求，`HandProportions` 保留 1.1 / 1.2 的设定值，
所以这 3 条在**当前配置下持续失败**。

**两条可行路线（二选一）：**

| 路线 | 做法 | 代价 |
|---|---|---|
| **A. 演示任务用 identity** | 把 `HandProportions` 设回全 1；或用两份资产分别给"外观"与"任务" | 失去手指比例调节的外观效果 |
| **B. 重新标定任务几何** | 让 `fingerSpheres` / 拇指间隙随 `HandProportions` 一起缩放，并重新导出任务标定 | 需要重跑标定流程，且会覆盖既有校准结果（规范约束 2 禁止随意覆盖） |

**状态：未解决。已记录，不影响手部模块的独立交付。**

---

## 2. 未验证的功能（代码已写，测试未跑）

以下功能**代码已实现并通过编译**，但**没有任何运行测试结果**。
在测试执行前，它们**不得**被标记为完成：

| 功能 | 文件 | 缺什么测试 |
|---|---|---|
| 自定义键位重绑定 | `Input/HandActionBindingProfile.cs` | 重绑定/保存/重启保留/恢复默认 |
| Input Actions 运行时驱动 | `Input/InputActionHandProvider.cs` | 实际按键驱动手部 |
| UI 焦点屏蔽 | `InputActionHandProvider` / `KeyboardHandPoseProvider` | 菜单聚焦时按键不生效 |
| 手势识别（张开/握拳/捏/指） | `Interaction/HandGestureRecognizer.cs` | 各手势识别正确率 |
| 交互边沿与帧号锁 | `Interaction/HandInteractionRouter.cs` | 一次握拳只投递一次 |
| 教学引导手 | `Presentation/GuidanceHandController.cs` | 显示/混合/隐藏 |
| 任务多输入安全闸门 | `Runtime/TaskController.cs` | 失追不触发抓取、不错误释放 |
| 回放协议校验与切源清态 | `Runtime/ReplaySource.cs` | 乱序检测、间隙检测、切源清 pending |
| 视觉模式连续切换 | `Presentation/HandAppearanceController.cs` | 连续 20 次切换无材质泄漏 |

**规范第 10 章要求的测试大部分未执行，原因见 `TEST_REPORT.md` 第 6 节。**

---

## 3. 视觉问题

### 3.1 GLB 模型没有法线贴图

`ElenaHand_R.glb` 没有法线贴图，皮肤表面细节（毛孔、指节皱褶）比带烘焙
Cycles 法线贴图的 FBX 手更平。

**不能共用：** 两个模型 UV 布局不同，法线贴图无法互换。
**状态：** 未解决，需要美术制作匹配 UV 的法线贴图

### 3.2 蒙皮骨骼列表含 30 个零权重 Ctrl 骨

蒙皮共 69 个关节，其中 **30 个是零权重的 IK 控制骨**，会让绑定姿势与运行时
蒙皮矩阵数组多占约 260 KB。

**移除代价：** 需要重新编码 `JOINTS_0` 访问器并重映射全部权重索引。
**状态：** 延后到建模阶段，当前不阻塞功能

### 3.3 蓝色 Shader 的透明层叠排序

`AvatarBlueOutline.shader` 用深度预pass（`Cull Back`）抑制内部面叠加，
但**双手交叠时（例如握拳相扣）仍可能出现顺序相关的混合异常**。

这是透明渲染的固有限制，不是 bug。
**状态：** 已知限制

### 3.4 单面渲染后手腕截面可能透空

材质已从双面（`_CullMode: 0`）改为单面（`_CullMode: 2`）以避免内部面叠加。
手模型在手腕处是开放几何，**从腕口向内看可能穿透**。

**状态：** 待实际观察确认；若明显则需改回双面或补腕部封盖

### 3.5 完整 Avatar 的遗留问题（**独立问题，不阻塞手部模块**）

已报告的以下问题**根因仍未确认**：

- 裤装 Alpha 异常
- 身体网格可见性异常
- 材质条纹

**规范第 9 条明确要求：不得把这些问题标为已修复，也不得让它们阻塞手部模块的独立交付。**
**状态：** 未确认根因，独立跟踪

---

## 4. 第三方依赖状态

### 4.1 已成功解析并进入工程

| 依赖 | 版本 | 在工程内 | 许可证 | 实测 |
|---|---|---|---|---|
| glTFast | 6.13.1（本地 tgz） | ✅ | Unity Companion | ✅ 导入正常 |
| Unity Burst | 1.8.21 | ✅ | Unity Companion | ✅ 既有 |
| Unity UGUI | 1.0.0 | ✅ | Unity Companion | ✅ 既有 |
| Unity XR Management | **4.4.1** | ✅ | Unity Companion | ✅ 解析成功 |
| Unity OpenXR | **1.11.0** | ✅ | Unity Companion | ✅ 解析成功 |
| Unity Input System | **1.7.0** | ✅ | Unity Companion | ✅ 解析成功 + 编译通过（两种宏组合） |
| Elena FF 手模型 | Sketchfab `eae97cc2` | ✅ | **CC BY-SA 4.0** | ✅ 见 `ATTRIBUTION.md` |

### 4.2 未能进入工程

| 依赖 | 版本 | 原因 |
|---|---|---|
| **Unity XR Hands** | 1.4.2 | ❌ **`packages.unity.cn` 注册表无此包** |
| PICO OpenXR SDK | 标签 1.4.0 | ❌ 未获取；许可证限制再分发；**SHA 未记录** |

**XR Hands 不可得是阶段 D 的核心阻塞。** 未擅自替换 Unity 版本、未更换注册表、
未引入第二套追踪系统。详见 `XR_TRACKING.md`。

### 4.3 GLB 优化已回退

`ArtSource/strip_glb.py` 曾把 GLB 从 3,812,180 B 压到 2,009,236 B（**-47.3%**），
但在批处理导入优化版时失败。**为不阻塞核心模块，已从备份恢复原始 GLB。**

当前工程内 GLB = **3,812,180 B**（原始）。
**状态：** 可选优化，未完成，不计入交付成果

---

## 5. 硬件 / 环境阻塞

### 5.1 PICO 设备 — `HARDWARE_NOT_TESTED`

构建机未连接任何 PICO 头显。真实 XR 手部追踪、双目显示、深度遮挡、
眩晕与延迟评估**全部未验证**。

- PCVR 手柄追踪：无设备
- PCVR 光学手部追踪：无设备 **且** 需先解除 XR Hands 阻塞
- PICO 一体机手部追踪：无设备
- PICO 手柄/手部模式切换：无设备

**可用回退：** `ControllerGripPoseProvider` 提供手柄 Grip 模拟。
⚠️ **手柄模拟不等同于光学手部追踪，必须显式标记。**

### 5.2 三个游戏的隔离副本 — 环境不存在

本机**不存在 `AI-Game/GAME*` 目录**，无法创建隔离测试副本。

因此规范第八部分要求的以下内容**全部未实施**：

- Game01/Game02/Game03 的适配器实现
- 每个游戏一个专属交互的验证
- 原游戏键盘玩法回归测试
- 正式合并方案

**本轮未修改任何正式游戏分支。**

### 5.3 Unity 编辑器锁竞争

Unity 编辑器或 Hub 持有 `Temp/UnityLockfile` 时，任何
`Unity.exe -batchmode` 调用都会以
`Fatal Error! It looks like another Unity instance is running with this project open.`
退出（退出码 `140063` / `0x40000015`）。

**影响：** EditMode / PlayMode 测试、Windows 构建、UnityPackage 导出、
场景生成**全部无法执行**。编译验证因此改用 Unity 自带的 Roslyn（见 `TEST_REPORT.md` 第 1 节）。

**排查方式：** 通过 Restart Manager P/Invoke（`rstrmgr.dll` 的
`RmStartSession` / `RmRegisterResources` / `RmGetList`）定位占用进程。
本沙箱中 WMI/CIM 不可用。

**处理：** 关闭编辑器后删除 `Temp/UnityLockfile`。

### 5.4 XR 包版本的待确认项
即使未来接入 PICO，以下**版本歧义必须先记录清楚**（规范第 2.5 条要求）：

| 项 | 状态 |
|---|---|
| PICO SDK 标签 | `1.4.0` |
| 实际 Git SHA | **未获取** |
| 标签内 `package.json` 声明版本 | `1.4.1`（与标签号不一致） |
| 声明的 OpenXR 依赖 | `com.unity.xr.openxr@1.8.2` |
| 本工程实际 OpenXR | `1.11.0` |
| 两者差异是否可解析 | **未验证** |

---

### 5.5 Burst AOT 原生链接器在当前环境不可用（**构建阻塞，已用临时处置绕过**）

**现象：** Windows 播放器构建在 Burst AOT 步骤失败，与工程代码无关：

```
Burst internal compiler error: System.UnauthorizedAccessException: 对路径的访问被拒绝
   at System.IO.Path.InternalGetTempFileName(Boolean checkHost)
   at Burst.Compiler.IL.Helpers.CommandLineArguments.WriteToTempFile(...)
   at Burst.Compiler.IL.Aot.AotCompiler.Link(...)
```

**已排除的可能（都有实测证据）：**

| 假设 | 实测 | 结论 |
|---|---|---|
| 工程代码编译失败 | 编译 0 错误；`AsyncPluginsFromLinker` 之外的步骤全部正常 | 排除 |
| 磁盘空间 | C 盘剩余 **336 GB** | 排除 |
| TEMP 目录不可写 | 同目录下 PowerShell 调用 `Path.GetTempFileName()` **成功** | 排除（对 shell 而言） |
| TEMP 路径值不合适 | 改到工程内 → 异常变为 `DirectoryNotFoundException`（工程 `Temp/` 被构建清空）；改到工程外新建目录 → 回到 `UnauthorizedAccessException` | **与路径值无关** |
| TEMP 里 `tmp*.tmp` 过多（65535 上限） | 实测仅 **20** 个 | 排除 |

**未确认的根因：** Unity/Burst 进程侧创建临时文件被拒。可能是进程令牌、ACL 或安全软件干预，
**未取得决定性证据**，因此不做断言。

**当前处置：** 写入 `ProjectSettings/BurstAotSettings_StandaloneWindows.json`，
把 `EnableBurstCompilation` 设为 `false`（Unity 已采纳，文件被归一化到 Version 5）。
构建随即成功：**92.8 MB / 170 文件 / 0 错误**。

> **⚠️ 该项属于必须随性能结论披露的配置变更。**
> AOT 关闭后 Burst job 以托管代码运行，**功能等价但更慢**。
> **不得**在此构建上做性能对比或宣称性能达标。
>
> **恢复方式：** 删除 `ProjectSettings/BurstAotSettings_StandaloneWindows.json`，
> 或把其中的 `EnableBurstCompilation` 改回 `true`。

---

## 6. 明确不得宣称的成果（规范第十三部分第 7 条）

未经真实验证，**不得**声称：

- ❌ 已实现所有 PICO 型号的逐指追踪
- ❌ EEG 可以准确还原五指动作
- ❌ 已经通过临床康复效果验证
- ❌ 模拟手势等同真实手部动作
- ❌ 手部模块已经在全部正式游戏中上线
- ❌ 仅凭 Blender 渲染就完成 VR 视觉验收

补充（本文件证据支持）：

- ✅ EditMode **100/100**、PlayMode **68/71** 为第 8 轮实测（`Logs/editmode-r8.xml` / `playmode-r8.xml`）；余 3 条见第 1.5 节
- ⚠️ Windows 构建/导出包含第 8 轮全部修复（2026-11-24 17:21/17:23 复跑，`Logs/build-r8.log`、`Logs/export-r8.log`）
- ⚠️ 性能仅限编辑器内测量（`PERFORMANCE.md`）；设备帧率未测，且构建关闭了 Burst AOT（第 5.5 节）
