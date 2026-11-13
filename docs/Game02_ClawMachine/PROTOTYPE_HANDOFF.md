# C01–C03 审查整改交付与验证边界

本轮按 2026-11-13 用户上传的审查文件扩展实现，不再限于旧任务单的 M0+M1。审查要求的源代码、内容、资源、测试和真实交付门禁已实现。**本环境没有准确版本 Unity Editor、C# 编译器和授权配置，Unity 编译、实际物理、图形、Windows 程序与视频证据仍然阻塞，不能宣称全部实机需求已验收。** 逐项状态见 `AUDIT_COMPLETION.json` 与 `AUDIT_COMPLETION.md`。

远端工作基线：`ecb1b13d52a58d804154ce31051d3d663f4f95f9`，树 `0ef44d8ececee7200732f654fed071dd3ba4c365`。保留 Unity **2022.3.62f3c1** 和原内置渲染管线。新增 TMP 固定为官方支持的 **3.0.6**；三个游戏共用 `Assets/BCIShared/TMP` 的唯一 TMP Settings 与原创建造兼容 Shader，各游戏仍使用独立授权字体。主线的新版本内容由上级集成流程同步，本游戏没有修改 Sokoban 运行代码。

## 打开与操作

使用准确版本 Editor 打开项目后，选择 `Claw Machine > Prepare and validate resources`，再打开 `Assets/ClawMachine/Scenes/ClawMachineScene.unity` 并按 Play。准备过程验证共享 TMP 资源、独立中文字体、Shader 和场景引用，不会安装 Editor、接入硬件或把空白证据当作通过。

| 操作 | 键位 / 界面 |
|---|---|
| 机器局部 X/Z 移动 | A/D 左右；W 朝机柜内部 +Z；S 朝外 -Z |
| 正面 / 左侧 / 右侧 | 1 / 2 / 3；方向不随镜头变换 |
| 有限观察旋转 | 在机器画面内按右键拖动；偏航 ±12°、俯仰 ±10° |
| 三种距离 | 滚轮或设置按钮；镜头保持机柜外的安全半径 |
| 正面 + 侧面画中画 | C 或画中画按钮；省电 320×240 / 清晰 480×360，可关闭 |
| 抓取 | Enter / Space；仅定位状态执行一次完整流程 |
| 对象选择 | 侧栏选择观察对象与抓持提示；实际抓到谁由爪下物理接触决定，不自动吸附 |
| 暂停 / 继续 | Esc；冻结运动、重力和计时；失焦自动暂停 |
| 失败后再试 | 恢复所有物品的相同 seed 布局，保留已使用尝试次数 |
| 重新挑战 | R 后确认；回收连接与速度，次数重新开始 |
| 取消 / 返回 | 暂停侧栏；清除本轮状态并恢复初始布局 |
| 设置 | 音量、文字大小、镜头平滑、距离、PIP 质量 |

## 三个挑战与抓持差异

| 挑战 | 目标与物品 | 策略与结果 |
|---|---|---|
| C01 | 单个薄荷礼盒，5 次，seed 20261113 | 规则盒体，练习正面 X 与侧面 Z 定位；侧面形成包围接触后才能抓持 |
| C02 | 礼盒、云朵兔、狐狸同时存在，目标云朵兔，4 次，seed 20261114 | 兔子有头部、耳朵与躯干的复合刚体碰撞；对准躯干，避开头耳。抓错物品即使进入出口也不计奖 |
| C03 | 三种物品，目标偏心狐狸，4 次，seed 20261115 | 右侧铜色配重对应 +0.055m 真实 Rigidbody 重心偏移。按外形中心抓会产生较大重力力矩并滑落；靠近配重点可稳定提升 |

挑战、目标 ID、seed、位置、尝试限制、抓持 profile 和成功规则均位于 `Challenges/*.asset`。Profile 分别设置质量、重心、接触深度、下降速度、表面容差、穿透上限、包围跨度、断裂力/力矩与稳定时间。没有随机中奖率，也没有软体仿真或医学疗效声明。

## P0 抓取与通用执行器

1. `GraspPlanner` 从机器上方向实际 Collider 射线获取表面。快接近高度高于所有活动奖品的完整复合 bounds；超过工作区时拒绝，不用重心作为下降终点。
2. 到达安全位置后重新读取实际表面，停下速度，转入单独 `ContactDescent`，以 profile 的低速下降到由表面定义的抓持带。
3. 下降每个固定步预测爪指/掌部碰撞，检查奖品、地板和机柜；有阻挡即停止。三指闭合采用碰撞裁剪与有限次二分，保留接触前的安全位置。
4. 物理指垫球和指杆胶囊真实参与碰撞；`ComputePenetration`、指垫中心在表面外侧、间隙/穿透阈值、相反接触法线和足够接触跨度一起决定是否可抓。探针在物体内部、同侧两个触点或深度穿入都不会被误当成功。
5. 合格后建立有断裂阈值的 FixedJoint，保持当前世界姿态。提升中检查实际断裂、锚点漂移和重力偏心力矩；滑落时解除连接并保留重力。
6. 仅本轮已释放的指定目标，完整复合 bounds 进入出口并连续稳定 0.3s，才可结算一次；其他奖品、首次碰边、旧 session/attempt 或仍被夹持均不能获奖。

`GraspTaskController` 只依赖世界 Pose 的 `IManipulatorDriver`、`ClawTaskSpace` 与模拟抓手服务。位置、速度、tick、jog、reset、状态/错误反馈、停止和工作区约束分离；只接受声明为模拟的执行器。场景内的 `GantryDriver` 和可切换的 `SimulatedRobotArmDriver` 遵守相同合约。后者是替代的世界空间工具模拟器，**没有真实机械臂模型、IK、标定、ROS、串口、网络通信或硬件控制**。未来真机仍需独立硬件安全层；游戏程序不能成为唯一安全保护。

状态机检查执行器 Error，也检查实际到位距离与姿态；驱动器声称 Idle 但尚未到达不会被当作完成。自动阶段没有进展或超时会停止并回收约束。无协程在重开后继续回写。

## UI、声音与原创资源

- CanvasScaler、锚点、安全区域和可滚动侧栏替代 IMGUI；所有文字使用独立 **Claw UI Sans** 动态 TMP 位图字体，源自授权 Noto Sans CJK SC 子集。字型创建前载入共享 Shader；每个文字先在 inactive 状态绑定字体再激活，避免依赖全局默认字体。
- PIP 只拥有一个额外 Camera 和 RenderTexture，不复制 AudioListener。切换质量、关闭、禁用和销毁均释放资源；界面清除旧纹理引用。
- 移动、快接近、慢接触、闭爪、提升、移送、释放、滑落、成功、失败有不同原创确定性合成音及电机层；暂停和取消清理音频。
- 原创圆角盒/椭球 Mesh 模块、模块化机柜部件、三类奖品、织物/塑料/拉丝纹理及织物法线图，使用原 Standard 金属度/光滑度参数；没有付费或授权不明的模型。视觉是小规模原创游戏资源，不冒充已验证的写实 PBR 或真实毛绒力学。没有顶棚网格。
- 资源生成器保护已编辑的生成文件并保留 GUID；场景和完整机柜 Prefab 为自带引用的独立快照。单部件 Prefab 作为可复用模型/碰撞模板，在完整机柜中绑定环境碰撞引用。

## 验证与交付

实际静态输出见 `STATIC_VALIDATION.txt`。完整场景/Prefab 的局部引用、GUID/importer、三个挑战与独立 profile、表面规划边界、源代码隔离、原始纹理和字体可复现检查通过；它们不代替 C# 编译或 Unity 物理运行。

新增/更新 Unity 测试覆盖：状态/重入/结算/seed/限位；内探针与深穿透拒绝；相反接触/包围跨度；真实表面高度；模拟接口与错误反馈；机柜/地板预测停止；相机限制/PIP 生命周期；三种分辨率布局；中文字体与活跃 Canvas 重建；不同声音；完整导入资源引用。PlayMode 有实际 `Acceptance_C01` / `Acceptance_C02` / `Acceptance_C03`、自然偏心滑落、抓错物品投放、每阶段暂停重开、失败恢复、30 次真实抓取与 20 次中途重开测试。

**这些 Unity 测试在本环境均未运行。没有真实编译日志、NUnit XML、Windows EXE/ZIP、画面截图或视频，不能把源码测试数量当成已通过的游戏验收。**

在有授权的准确 Editor、Windows Build Support 和可用图形设备的机器执行：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_game_delivery.py --game claw
```

统一流程先运行 `ClawMachine.Editor.ClawSceneBuilder.PrepareForValidation`，再执行本游戏 EditMode / PlayMode。只有真实 XML 非零且全部通过、没有跳过、三个 Acceptance 类别均实际出现、实际 Editor 版本匹配、源指纹未变，才构建 Windows 并校验完整程序目录后打包。PIP/TMP 需要图形设备，**不要加 `-nographics`**；Null renderer 跳过图形测试会使严格门禁失败。旧 `Tools/run_delivery.py` 仍是 Sokoban 专用流程，不能用来证明本游戏。

构建入口 `ClawMachine.Editor.ClawSceneBuilder.BuildWindows` 支持 `-buildOutput <新的输出文件夹>`；默认 `BuildOutput/ClawMachine/ClawMachine.exe`。没有准确 Editor 时统一流程记录 blocked，不生成假的 XML、ZIP 或视频。`ENVIRONMENT_BLOCKED.json` 保留开发工作区运行的原始摘要；其中 sourceCommit 是运行当时 HEAD，实际未提交工作区由 sourceBeforeImport 标识，不是发布版运行证明。

仍须在目标 Windows 机器完成三视角/PIP可读性、三分辨率实际布局、抓持与滑落手感、30/20 长程稳定性、音效舒适度、CPU/GPU 性能与录屏验收。C04/C05、机器人 IK/真机、BCI/EEG、软体不属于此次上传审查要求，未实现也未宣称实现。
