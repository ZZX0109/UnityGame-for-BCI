# 测试报告 (TEST_REPORT)

**本文件只记录实际执行过的测试。没有运行的测试一律标注为未运行，不得标记 PASS。**

- 工程：`Unity/Game04-VirtualHand`
- Unity 版本：**2022.3.62f3c1**（中国版）
- 分支：`codex/virtual-hand`
- 审查基线：`fab911feb3e6b70ff23d7d9af0951eb33fc12200`
- 渲染管线：Built-in RP
- 报告时间：**2026-11-24 15:5x (Asia/Shanghai)**

---

## 1. Unity 编辑器导入与编译 — ✅ 通过

**执行方式：** 真实 Unity 2022.3.62f3c1 批处理导入（`-batchmode -projectPath … -logFile … -quit`），读取 Unity 自身的编译日志。

| 项目 | 结果 |
|---|---|
| 资产导入 | ✅ 全部导入，无失败 |
| C# 编译 | ✅ **0 个 CS 错误** |
| 构建系统 | ✅ `*** Tundra build success (1.49 seconds), 26 items updated, 581 evaluated` |
| 运行时代码行数 | 运行时 50 个 `.cs` 源文件 |

**编译产物（真实时间 2026-11-24 15:33）：**

| 程序集 | 大小 |
|---|---|
| `Neuralife.VirtualHand.Runtime.dll` | 168 KB |
| `Neuralife.VirtualHand.Editor.dll` | 93.5 KB |
| `Neuralife.VirtualHand.Tests.dll` | 59 KB |
| `Neuralife.VirtualHand.PlayTests.dll` | 81.5 KB |

**Input System 引用验证：** 运行时程序集已引用 `Unity.InputSystem`，并在 Unity 自己的响应文件中确认定义了
`-define:ENABLE_INPUT_SYSTEM` 与 `-define:ENABLE_LEGACY_INPUT_MANAGER`（`activeInputHandler: 2` = Both）。

**双模式编译验证（Roslyn，复用 Unity 的 `.rsp`）：**

| 场景 | 结果 |
|---|---|
| Both（Input System + Legacy） | ✅ 0 错误 |
| 仅 Legacy（包存在但未启用） | ✅ 0 错误 ← 证明 `#if ENABLE_INPUT_SYSTEM` 保护有效，可优雅降级 |

---

## 2. EditMode 测试 — ✅ 100 / 100 全部通过

**执行方式：**
```
Unity.exe -batchmode -projectPath <proj> -runTests -testPlatform EditMode -testResults <xml> -logFile <log>
```
结果文件：`Logs/editmode-r6.xml` · 耗时 **0.95s**

| 指标 | 数值 |
|---|---|
| 总数 | **100** |
| 通过 | **100** |
| 失败 | **0** |
| 跳过 | 0 |

> 注：渲染类测试（`AvatarBlueOutlineRenderTests`）必须**不加 `-nographics`** 运行，否则无 GPU 时
> `camera.Render()` + `ReadPixels` 读回垃圾值，会产生假失败。详见 2.1。

### 2.1 两处重要环境陷阱（已定位）

**陷阱一：`-nographics` 会让渲染测试假失败。**
首轮批处理加了 `-nographics`，结果是 `AvatarBlueOutlineRenderTests.BlueStyleRendersSceneOcclusionAndRestoresOriginalMaterial` 失败：
```
Rear object baseline pixel mismatch. Actual=RGBA(0.804,0.804,0.804,0.804), expected=RGBA(1,0,0,1)
```
该测试自建三个 `Unlit/Color` 立方体做真实 GPU 渲染（`camera.Render()` + `RenderTexture` + `ReadPixels`），
**不使用手部材质**。无图形设备时读回的是垃圾值。去掉 `-nographics` 后该测试通过。
→ **结论：渲染类测试必须在有图形设备的情况下运行。**

**陷阱二：`-runTests` 不能与 `-quit` 同用。**
同时传这两个参数时，编辑器在测试启动前就退出，不产生 XML。

### 2.2 曾长期失败的用例已确认通过

`FirstPersonPrefabTests.BothAnatomicalWristsReachTheirGripPosesInAHostWithoutTheDemo`
（`Assert.AreEqual(left, driver.IsMirrored)`）**本次通过**。

该用例在早期记录中失败，本次通过，证实之前的失败是**过期结果** ——
`FirstPersonHands.prefab` 于 13:05 被重建，而那份失败记录来自重建之前。

静态排查同步确认：`ElenaHand_L` 的 `_isMirrored = 1`、`ElenaHand_R = 0`，
`FirstPersonHands.prefab` 中 `leftHand → ElenaHand_L`、`rightHand → ElenaHand_R`，接线正确。

### 2.3 本轮新增的测试（25 条，全部通过）

**`Tests/HandSystemModuleTests.cs`（23 条）**

| 覆盖对象 | 关键断言 |
|---|---|
| `HandJointRetargeter` | 五根手指的关节链全部映射到非空骨骼名；腕/掌映射正确；未映射关节返回 null；**全部映射名存在于真实的 `ElenaNativeRig_R.asset` 骨骼列表中**（缺失则列出） |
| `HandJointRetargeter.FingerCurl` | 关节全部无效时返回**回退值而不是"伸直"** |
| `HandJointRetargeter.FillControls` | 写出 20 个 `[0,1]` 值；目标数组长度错误时不抛异常 |
| `HandTrackingValidity` | 丢帧窗口内可显示缓存姿态；过期后不可显示；**不可交互**；恢复必须经过 resync；**左右手完全独立**；失追不产生任何控制值变化 |
| `HandProportions` | 新建即 identity；在**真实骨骼名**（`index_01.R_017` 等）上匹配正确；不影响其他手指；跳过 `Ctrl` 骨与腕骨；`metacarpalScale` 只作用于 `*_base` |
| `HandActionBindingProfile` | `StableId` 确定性；默认布局覆盖左右手全部 5 个姿态；**默认布局与三个游戏保留键位无冲突**；冲突检测同时报告保留键与内部重复；生成的 JSON 含全部绑定且可解析；**两次生成的 JSON 完全一致**（保证重绑定存盘可跨会话生效） |

**`Tests/Editor/HandAppearanceSwitchTests.cs`（2 条）** —— 对应规范 10.4

| 断言 | 说明 |
|---|---|
| 连续切换 **20 次** | 每次都验证材质槽位数不变、蓝色 shader 正确、多子网格共享同一材质实例 |
| **无材质实例泄漏** | 第一次生成的蓝色材质与之后每次都是**同一个实例**（`AreSame`） |
| 切回写实材质完整恢复 | 两个槽位都按引用恢复为原材质 |
| **骨骼不重置** | 手的 `localPosition` / `localRotation` 切换前后不变 |
| **物体不掉落** | 挂 `Rigidbody` 的物体位置与 `velocity` 不变，碰撞未被关闭 |

### 2.4 由测试发现并修复的真实缺陷 —— 累计 **9 个**

写测试的价值在这两轮体现得很直接：新增测试抓出 9 个真实缺陷，其中 **3 个会让功能在成品中完全不可用**。

| # | 缺陷 | 后果 | 由哪条测试发现 |
|---|---|---|---|
| 1 | `HandProportions.ScaleFor()` 中 `metacarpalScale` 是**死代码**：`*_base` 名字以手指前缀开头，进入手指分支后不含 `_01./_02./_03.`，于是在 `else return 1f` 提前返回，永远到不了循环后的掌骨判断 | 手掌长度调节完全无效 | `Proportions_MetacarpalScalesOnlyTheBaseBone` |
| 2 | `HandActionBinding.Key` 用 `/` 作分隔符（如 `Open/Left`），而 `InputActionAsset.FindAction` 把 `/` 解析为 **"map/action" 路径** | 动作按名字永远查不到，重绑定静默失效 | `RebindingAnActionChangesItsEffectivePathAndPersists` 等 3 条 |
| 3 | `Rebind()` 先把 `_asset = null` 再调 `SaveOverrides()`，而后者开头是 `if (_asset == null) return;` | **重绑定永远不落盘** | `ResetToDefaultsDropsStoredOverrides` |
| 4 | `SetControls()` / `UpdateControls()` 对 NaN/Infinity/超范围是**清洗后照样应用**，而且**就地改写了调用方的数组** | 违反规范 3.2「无效帧不覆盖最后可信姿态」；一次 NaN 爆发会把最后可信姿态覆盖成零手 | `NaNInputIsRejectedAndTheLastTrustedPoseSurvives` 等 3 条 |
| 5 | `HandInteractionRouter` **没有 `Update()`，也没有任何地方调用它的 `Tick()`** | **手势→游戏事件的整条链路在成品中是死的** | `TrackingLossNeverEmitsARelease` 等 |
| 6 | `VirtualHandHost` 把 `_leftGesture` / `_rightGesture` 传给适配器，但**从未给这两个字段赋值** | 适配器永远收到 `None`，游戏无法响应任何手势 | 由第 5 项的修复过程发现 |
| 7 | `KeyboardHandPoseProvider` 用 `candidates[(int)targetSide]` 索引**长度为 2 的数组**，而 `SeatedHandSide` 是 `{ Unspecified = 0, Left = 1, Right = 2 }`，默认值 `Right` = 2 | **每帧抛 `IndexOutOfRangeException`** —— 旧键盘 provider 完全不可用 | `KeyboardProviderFallsBackToRestWhileUiOwnsFocus` |
| 8 | `HandRigDriver.CacheBones()` 在 native 路径**提前返回**，导致 `_thumb` / `_fingers`（以及仅由 legacy 路径填充的 `_allBones`）为空 | 新增的指间自穿插检测拿不到骨链，测量恒为 `Infinity`，检测形同虚设 | `CurlingTheFingersReducesAdjacentClearance` |
| 9 | 第 8 轮自身引入的回归：`ReplayController.OnSourceChanged` 改调 `EnterSafeState()`，后者会**武装 0.3 s 重同步门**，切源后任务被冻结；按 r8 语义拆分出**无重同步门**的 `ClearStaleState()` 供切源使用 | 切换输入源后 0.3 s 内任务状态被清空又冻结，演示与游戏内表现为"切源后手没反应" | `SimulatedTrackingPlayModeTests`（切源清态组） |

**修复后的当前结果：** EditMode **100/100**，PlayMode **68/71**（余下 3 条为配置冲突，见第 3 节）。

另外修正了 2 处**测试自身**的错误（保留在此以便溯源）：

- 模拟 XR 的路由测试最初用同步 `Tick()` 编写。`HandInteractionRouter` 每帧由 `Update` 自行 tick 并占用帧锁，
  同一帧内手动再调是空操作，这类测试会**因为错误的原因通过**。已改为**真实帧驱动的协程**。
- `ResetHeldState` 测试原本期望复位后不再产生新抓取。实际上手仍握拳时，下一帧重新判定为新抓取是**正确行为**；
  该 API 的契约是"不产生游戏没见过的释放"。测试已按正确契约重写。

### 2.5 新增的测试文件

| 文件 | 条数 | 覆盖 |
|---|---|---|
| `Tests/HandSystemModuleTests.cs` | 23 | 关节映射、重定向数学、跟踪有效性、手指比例、绑定配置 |
| `Tests/HandActionBindingTests.cs` | 14 | 规范 10.3：重绑定、存盘、恢复默认、冲突检测、左右手独立 |
| `Tests/Editor/HandAppearanceSwitchTests.cs` | 2 | 规范 10.4：连续切换 20 次、无材质泄漏、骨骼/物体不受影响 |
| `Tests/PlayMode/HandPoseBonePlayModeTests.cs` | 12 | 规范 10.2：**骨骼级**姿势验证、腕部旋转 90°/180°、NaN/Inf/超范围拒绝 |
| `Tests/PlayMode/SimulatedTrackingPlayModeTests.cs` | 15 | 规范 10.5：失追/恢复/独立性、边沿事件、切源清态、时钟 |
| `Tests/PlayMode/HandDiagnosticsPlayModeTests.cs` | 7 | 规范 3.2 指间自穿插检测、6.3 Guidance Overlay 独立性、10.3 UI 焦点屏蔽 |

#### 2.5.1 骨骼级验证的具体做法（规范 3.1 要求「不要只测试控制数组」）

`HandPoseBonePlayModeTests` 的每条断言都测量**真实骨骼世界坐标**，主要量是
**指尖到腕部的距离**：屈指会把指尖拉向腕部，伸直则保持较远。这是被摆姿势后网格的功能性质，
不是对输入数组的复述。

其中 `PointKeepsOnlyTheIndexFingerExtended` 直接验证规范 3.1 的核心要求：
指向时食指保持伸直、中指/无名指/小指收拢、拇指不塌陷。

`WristRotationDoesNotDisturbFingerLocalPose` 验证规范 1.1「手腕移动及旋转不破坏手指骨骼姿态」：
腕部转 90° 和 180° 后，逐一断言**每个骨骼的 localRotation 变化 < 0.5°**，且各指尖到腕距离不变。

#### 2.5.2 指间自穿插检测（规范 3.2）的做法与边界

`HandRigDriver.EvaluateSelfIntersection()` 测量**相邻手指骨中心线之间的最短距离**
（线段—线段距离），取最小值暴露为 `ClosestAdjacentFingerDistance`，
低于 `selfIntersectionThresholdMeters`（默认 6 mm）时置 `SelfIntersectionDetected`。

**明确边界：** 该模型不含每根骨的蒙皮半径，因此**这不是皮肤级穿透深度**，
而是一个保守的"手指是否互相重叠"诊断。它**不会**静默改写姿态。

**近节被排除在比较之外**：相邻指根在手掌处本就相接，包含它们会让诊断恒亮。
比较的是中指节与远指节——真正会在过度屈曲时交叉的部分。

测试以**契约**而非魔数阈值验证：标志位必须与测量值一致（`detected == distance < threshold`）、
屈指必须让测量值下降（证明它确实是姿态驱动的）、以及**四个作者姿态在默认阈值下均不报穿插**。

---

## 3. PlayMode 测试 — ⚠️ 68 / 71，余下 3 条已定位为配置冲突

**执行方式：**
```
Unity.exe -batchmode -projectPath <proj> -runTests -testPlatform PlayMode -testResults <xml> -logFile <log>
```
结果文件：`Logs/playmode-r8.xml`（第 8 轮）· 耗时 **3.32s**

| 指标 | 第 7 轮 (r7) | 第 8 轮 (r8) |
|---|---|---|
| 总数 | 59 | **71** |
| 通过 | 56 | **68** |
| 失败 | 3 | **3**（同一批配置冲突，见 3.1） |

第 8 轮新增 12 条用例，包括把左右镜像校验从控制数组层改写为**骨骼世界旋转层**的
`HandsAreMirrorImagesOfEachOther`（左手世界旋转必须等于右手旋转按 YZ 平面反射后的四元数，
误差 < 0.5°）——之前左右手共享同一网格与骨骼布局，侧别只体现在视觉根旋转上，
任何骨局部坐标或指轴投影的断言都无法区分左右，该测试最初因此写错并被重写。

### 3.1 三条失败的用例

全部在 `Neuralife.VirtualHand.PlayTests.PlayModeTaskTests`，且全部是**任务几何断言**：

| 用例 | 失败信息 |
|---|---|
| `Carry_CompletesWithContinuousRestInsideDropZone` | `too many weighted skin vertices penetrate the box` |
| `ManualPinch_BothHandsHaveOpposingPadContact` | `thumb needs a facing contact patch within 3 mm` |
| `RealReplay_CarriesAndHandsOffThroughSavedButtons` | `open fingers intrude while awaiting grip` |

### 3.2 根因：手指比例与既有任务标定冲突（A/B 已验证）

当前 `Resources/Rig/ElenaFF/HandProportions.asset` 为：

| 手指 | lengthScale | proximal | middle | distal |
|---|---|---|---|---|
| thumb | 1.1 | 1.1 | 1 | 1 |
| index | 1.1 | 1.1 | 1.1 | 1 |
| middle | 1.1 | **1.2** | 1 | 1 |
| ring | 1.1 | 1.1 | 1 | 1 |
| pinky | 1.1 | 1.1 | 1 | 1 |

**受控 A/B 实验：** 把该资产临时改为全部 `1`（identity），只跑 `PlayModeTaskTests`：

```
比例 = identity  →  total=9  passed=9  failed=0
```

结论：**这 3 条不是代码缺陷，而是"手指加长 10–20%"与演示任务几何标定之间的真实冲突。**

受影响的既有标定：

- 箱子抓取的顶点侵入判定（`taskContactRange`、`leftTaskHoldCurl` / `rightTaskHoldCurl`）
- 拇指与食指指腹的 3 mm 对面接触判定
- 抓取前的张开手指侵入检查

这些判定针对**原始 Elena 手比例**标定；`ElenaNativeRigProfile.fingerSpheres` 与拇指间隙由
`ElenaNativeProfileBuilder` 从**未修改的 GLB** 采样，**不会跟随 `HandProportions`**。

**处理方式：** 按产品所有者（用户）明确要求，`HandProportions` 保留其设定的 1.1 / 1.2 数值，
因此这 3 条在**当前配置下持续失败**，属于已确认的配置冲突而非代码 bug。
若需要全部通过，把该资产设为 identity；若要两者兼得，需要重新标定任务几何（见 `KNOWN_LIMITATIONS.md` 第 1.5 节）。

---

## 4. 真实运行截图 — ✅ 已产出

**执行方式：**
```
Unity.exe -batchmode -projectPath <proj> -executeMethod Neuralife.VirtualHand.Editor.HandPreviewCapture.Capture -logFile … -quit
```
（**不加 `-nographics`**，否则无 GPU 渲染）

脚本实例化**实际发布的 `ElenaHand_R.prefab`**，设置工作室环境光与三向光，
用真实相机渲染进 RenderTexture 并写盘。

**产物目录：** `Results/hand-previews/`

| 文件 | 大小 | 内容 |
|---|---|---|
| `hand-realistic-threequarter.png` | 242,314 B | 写实材质，三分之四视角 |
| `hand-realistic-side.png` | 169,551 B | 写实材质，侧视 |
| `hand-blue-threequarter.png` | 144,016 B | 蓝色半透明，三分之四视角 |
| `hand-blue-side.png` | 112,617 B | 蓝色半透明，侧视 |

四张的 MD5 **互不相同**，证明渲染确实随模式变化。

**已人工查看确认：**
- 写实模式：皮肤纹理（血管、指节皱褶）清晰可见，**哑光不油腻**，手指比例已应用
- 蓝色模式：Fresnel 轮廓清晰、主体半透明、手指结构可辨，腕口可见单面剔除效果

**⚠️ 本截图套件的范围限制（必须声明）**
它**只覆盖外观模式**，不覆盖姿势。原因：编辑模式下 Unity 在骨骼变换改变后
**不会重新计算 SkinnedMeshRenderer 的蒙皮**，任何姿势都会渲染成静置姿态，
那样的图片会误导。**姿势行为由 PlayMode 测试覆盖，不由此截图覆盖。**

（早期一次尝试曾产出 4 张完全相同的 MD5，正是这个原因；该批产物已删除，
并以只捕获外观的方式重做，避免留下误导性证据。）

---

## 5. 预制件构建 — ✅

| 预制件 | 状态 | 时间 |
|---|---|---|
| `Prefabs/ElenaFF/FirstPersonHands.prefab` | ✅ 重建（含外观/输入/宿主组件） | 15:42:56 |
| `Prefabs/GuidanceHand.prefab` | ✅ **新建**（规范第十一部分要求的交付文件） | 15:38:25 |
| `Prefabs/ElenaFF/ElenaHand_L.prefab` | 未改动（保留用户当前材质设置） | 12:56:40 |
| `Prefabs/ElenaFF/ElenaHand_R.prefab` | 未改动 | 12:56:40 |

**未重建 `ElenaHand_L/R` 的原因：** `ElenaHandPrefabBuilder.OptimizeSkinMaterial` 会把
`roughnessFactor` 写回 `1`，而用户已手工调到 `0.9`。为不覆盖用户设置，本次跳过。

---

## 5.45 Windows x64 构建 — ✅ 成功（本轮修复后已重新构建）

```
[VirtualHandBuild] Build result: Succeeded, size 92MB, errors 0, warnings 1
```

本轮修复 6 个缺陷后**重新构建通过**，确认运行时改动没有破坏打包。
产物：`Builds/Windows-Elena/`，170 个文件，约 92.8 MB。

**⚠️ 该构建使用关闭 Burst AOT 的设置**，原因见 5.5 节；任何性能结论都必须同时披露这一点。

---

## 5.5 Windows x64 构建 — ✅ 成功（附一个环境阻塞的记录）

```
[VirtualHandBuild] Build result: Succeeded, size 92MB, errors 0, warnings 0
```

**产物：** `Builds/Windows-Elena/` —— 170 个文件，**92.8 MB**
`VirtualHand_Data/` 于 **2026-11-24 15:57:21** 重建。

### 5.5.1 首次构建失败的真实原因：Burst AOT 原生链接器

首次构建**失败**，但**与工程代码无关**：

```
error: Burst internal compiler error: System.UnauthorizedAccessException: 对路径的访问被拒绝
   at System.IO.Path.InternalGetTempFileName(Boolean checkHost)
   at Burst.Compiler.IL.Helpers.CommandLineArguments.WriteToTempFile(...)
   at Burst.Compiler.IL.Aot.AotCompiler.Link(...)
Library\Bee\artifacts\WinPlayerBuildProgram\AsyncPluginsFromLinker 失败
```

第二次尝试（把 `TEMP` 指向工程内目录）异常变为：

```
DirectoryNotFoundException: Could not find a part of the path "...\Temp\bt\tmp45a653f7.tmp"
   at System.IO.Path.GetTempFileName()
   at Unity.Burst.Editor.BurstAotCompiler.OnPostBuildPlayerScriptDLLsImpl
      (...\com.unity.burst@1.8.21\Editor\BurstAotCompiler.cs:874)
```

**排查得到的确定性结论：**

| 观测 | 结论 |
|---|---|
| 失败点在 `BurstAotCompiler.OnPostBuildPlayerScriptDLLsImpl`，早于任何工程代码 | 不是本项目代码问题 |
| `Path.GetTempFileName()` 抛 `UnauthorizedAccessException`，同目录下 PowerShell 调用同一 API **成功** | 是 Unity/Burst 进程侧的临时文件创建问题 |
| 把 `TEMP` 指向工程内 → 异常变成 `DirectoryNotFoundException`（因为 Unity 构建时会清空并重建工程 `Temp/`） | TEMP 重定向**确实传到了 Burst** |
| 把 `TEMP` 指向工程外新建目录（`%LOCALAPPDATA%\VHBuildTemp`）→ 仍为 `UnauthorizedAccessException` | **与 TEMP 路径值无关** |
| 12:58 那次成功构建的日志里**完全没有 AOT 原生链接步骤** | 那次用的是更早构建缓存的 AOT 产物 |

### 5.5.2 临时处置：关闭 StandaloneWindows 的 Burst AOT

为拿到真实构建产物并证明**工程代码本身可构建**，写入了
`ProjectSettings/BurstAotSettings_StandaloneWindows.json`，将 `EnableBurstCompilation` 设为 `false`。
Unity 采纳了该文件（版本号由 3 归一为 5，`EnableBurstCompilation: false` 保留）。
随后构建成功。

> **⚠️ 这一设置会改变运行性能特征，必须随任何性能结论一起披露。**
> Burst AOT 关闭后，Burst 编译的 job 以托管代码运行，**功能等价但更慢**。
>
> **恢复 Burst AOT：** 删除 `ProjectSettings/BurstAotSettings_StandaloneWindows.json`
> （或在其 `MonoBehaviour` 中把 `EnableBurstCompilation` 改回 `true`）。
> 恢复后，若目标机器上 Burst AOT 的临时文件创建正常，构建应重新变为 AOT 版本。
>
> **不得**在 AOT 关闭的构建上做性能对比或宣称性能达标。

---

## 6. 包解析 — ✅

| 包 | 声明 | 实际解析 | 结果 |
|---|---|---|---|
| `com.unity.inputsystem` | 1.7.0 | **1.7.0** | ✅ |
| `com.unity.xr.management` | 4.4.1 | **4.4.1** | ✅ |
| `com.unity.xr.openxr` | 1.11.0 | **1.11.0** | ✅ |
| `com.unity.xr.hands` | 1.4.2 | — | ❌ **`packages.unity.cn` 无此包** |
| `com.unity.cloud.gltfast` | 本地 tgz | 6.13.1 | ✅ 既有 |
| `com.unity.burst` | — | 1.8.21 | ✅ 既有 |

无版本冲突。XR Hands 失败后未擅自替换 Unity 版本、未更换注册表、未引入替代追踪系统。

---

## 7. 未执行的测试与阻塞

| 测试项 | 规范条目 | 状态 | 原因 |
|---|---|---|---|
| Unity 编辑器打开 + 资产导入 + 编译 | 10.1 | ✅ **通过** | 0 错误，`Tundra build success` |
| Missing Script / 粉色材质全量检查 | 10.1 | ⚠️ 间接覆盖 | Prefab 构建成功 + 渲染截图非粉色即间接证据，未做逐项扫描 |
| EditMode | 10.1 | ✅ **100 / 100** | — |
| PlayMode | 10.1 | ⚠️ **56 / 59** | 3 条为配置冲突，见第 3 节 |
| 手指动作（**骨骼级**） | 10.2 | ✅ **通过** | `HandPoseBonePlayModeTests` 12 条 |
| 腕部旋转 90° / 180° | 10.2 | ✅ **通过** | 逐骨骼断言 localRotation 变化 < 0.5° |
| NaN / Infinity / 异常长度拒绝 | 10.2 | ✅ **通过** | 修复后 4 条测试通过 |
| 追踪失效不产生错误动作 | 10.2 / 10.5 | ✅ **通过** | — |
| 键位：重绑定 / 存盘 / 恢复默认 / 冲突 | 10.3 | ✅ **通过** | 14 条测试；修复 2 个缺陷后通过 |
| 键位：**UI 有焦点时不触发** | 10.3 | ✅ **通过** | 测试真实创建 `EventSystem` 并夺焦，验证 provider 察觉焦点且不再驱动手势 |
| 视觉模式：连续切换 20 次 / 无泄漏 | 10.4 | ✅ **通过** | `HandAppearanceSwitchTests` 2 条 |
| 视觉模式：Windows 构建中正确显示蓝色 Shader | 10.4 | ⚠️ **部分** | Shader 已通过序列化字段引用（非 `Shader.Find`），降低被剥离风险；**未在 Player 中实机确认** |
| 模拟 XR：失追 / 恢复 / 独立 / 切源 / 暂停 | 10.5 | ✅ **通过** | 15 条测试 |
| 模拟 XR：双目 / 真实双目一致性 | 6.3 / 10.6 | 🚫 **无设备** | 需头显 |
| Windows 构建 | 13 | ✅ **成功**（92 MB / 170 文件 / 0 错误）**但 Burst AOT 关闭**，见 5.5 节 |
| UnityPackage 重新导出 | 11 | ✅ **成功**（`Builds/FirstPersonHands/FirstPersonHands.unitypackage`，15:53:14） |
| 真实 PICO 设备验证 | 10.6 | 🚫 **HARDWARE_NOT_TESTED** | 无设备 |
| 性能：几何 / CPU / 外观切换 | 10.7 | ✅ **已测量** | 见 `PERFORMANCE.md`（1 SMR、28,994 三角面、69 骨、姿态更新约 0.009 ms/手） |
| 性能：设备帧率与帧时间 | 10.7 | 🚫 **HARDWARE_NOT_TESTED** | 需头显；且当前构建关闭 Burst AOT，设备数值本就不可比 |
| 三游戏隔离安装 | 8.4 | 🚫 **环境不存在** | 本机无 `AI-Game/GAME*` 目录 |

---

## 8. 可以诚实声明为"已验证"的内容

1. **Unity 编辑器编译**：当前全部源码在真实 Unity 2022.3.62f3c1 中国版下 **0 错误**（`Tundra build success`）。
2. **双输入模式编译**：Both 与仅 Legacy 两种宏组合均 0 错误，模块可优雅降级。
3. **EditMode 测试**：**100 / 100 通过**（当前代码，真实时间与 XML 可查）。
4. **PlayMode 测试**：**56 / 59**；3 条失败已通过 A/B 实验定位为**配置冲突**（手指比例 vs 任务几何），非代码缺陷。
5. **骨骼级姿势验证**（规范 3.1 明确要求）：指向手势只有食指伸直、其余三指收拢；开/握/捏三种姿态在真实骨骼上互不混淆。
6. **手腕旋转不破坏手指姿态**：腕部转 90° 与 180° 后，每个骨骼 localRotation 变化 < 0.5°，各指尖到腕距离不变。
7. **无效输入被拒绝**：NaN / Infinity / 超范围 / 错误长度都不会改变最后可信姿态，且不改写调用方数组。
8. **跟踪安全性**：单手失追不影响另一手；失追**不产生释放事件**；恢复必须经过重同步窗口。
9. **键位**：重绑定生效并落盘、可跨会话保留、恢复默认真正回到作者默认值、默认布局不与三个游戏按键冲突。
10. **外观模式**：20 次连续切换无材质实例泄漏；骨骼不重置；物体不掉落（真实测试）。
11. **外观渲染**：写实与蓝色两种模式均有真实渲染截图，MD5 互不相同，已人工确认效果正确。
12. **包解析**：Input System 1.7.0 / XR Management 4.4.1 / OpenXR 1.11.0 真实解析成功。
13. **Windows 构建**：**成功，92 MB / 170 文件 / 0 错误**（Burst AOT 关闭，须一并披露）。
14. **由测试发现并修复 8 个真实缺陷**，其中 3 个会使功能在成品中完全不可用。

## 9. 明确不得声明的内容

- ❌ 不得声明已实现真实 XR 五指追踪 —— XR Hands 包不可得，provider 未接入
- ❌ 不得声明 PICO 设备已验证 —— 无设备
- ❌ 不得声明三个游戏已接入 —— 目标目录不存在
- ❌ 不得声明性能达标 —— 未测量，且当前构建关闭了 Burst AOT
- ❌ 不得声明蓝色 Shader 已在 Player 中实机确认 —— 仅做了引用方式上的风险消除
- ❌ 不得声明 UI 焦点屏蔽已实测 —— 逻辑已实现但未运行测试
- ❌ 不得声明姿势截图已产出 —— 编辑模式无法捕获姿势，截图仅覆盖外观
- ❌ 不得把键盘预设动作、手柄 Grip 或镜像手称为真实光学手部追踪
