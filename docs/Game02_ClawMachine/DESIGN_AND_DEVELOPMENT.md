# Game 02 — Claw Machine 3D｜完整游戏设计与开发规格

版本：Design v1.0 · 2026-11-12  
目标仓库：ZZX0109/UnityGame-for-BCI  
独立分支：game02/claw-machine-3d  
范围：Windows PC、三维可玩的娃娃机游戏；未来可替换为机械臂执行器，但首版**不接真实机器人、BCI、EEG、外网服务**。  
技术基线：仓库现有 Unity 2022.3.62f3c1（中国版）与旧版 Input Manager；不升级 Editor，不修改主线 Sokoban 代码/场景/材质。  

> 本文是拟开发规格，并非功能已经实现的声明。所有尺寸、时长、性能目标均为可调整的设计初值，需要 Unity 实机验证。

## 0. 定位、非目标与三条不可妥协的原则

**核心体验**：在有质感的街机厅中操纵真实结构的三轴龙门娃娃机；通过正面、左侧、右侧观察判断空间位置；执行下降、闭爪、提升、移送与投放；获得由物体运动和抓持状态决定的结果，而不是抽奖。允许有限次数的尝试、关卡重置、分数与奖品陈列。

**机制**：
- 世界坐标 Y 向上；机柜局部 X = 左右，Z = 前后，Y = 升降。W = 机柜内 +Z，S = -Z，A = -X，D = +X。**切换相机绝不重映射方向**。
- 首版是**龙门机构**：X/Z 水平导轨、Y 升降绞盘、三爪抓手；未来机械臂采用另一执行器实现，不混同机构与运动学。
- 状态机、运动约束、抓取判定和视觉演出相互解耦；Unity 物理接触与最终奖品位置必须可复验。
- **成功条件**：目标奖品实际释放并进入投放口的有效结算区，经稳定判定后计为成功；“碰到/夹住”不等于获奖。

不开发：大规模电玩城、复杂会员/货币付费系统、在线排行榜、真实布料软体、六轴机械臂、ROS 实时驱动、摄像头识别模型、VR/BCI 接入。允许预留接口，不能把未来功能假装为已实现。

## 1. 游戏循环与手感指标

开始菜单 → 选择挑战 → 初始观察/教学 → 准确定位 → Enter/Space 触发抓取 → 自动下降、闭爪、提升 → 返回投放口 → 松爪 → 等待物体稳定 → 成功/失败反馈 → 下一次尝试或结算。  
每关建议 3 次尝试；教学关可 5 次，数量由关卡配置驱动。

交互目标：从开局到第一次抓取指令不超过 20 秒即可学会；WASD 有明确动起来的反应；相机切换 0.2–0.35 秒内完成且没有镜头穿玻璃；全抓取约 5–9 秒；每个阶段给状态文字与机械音反馈，不要求玩家盯着看读条。

**基础键鼠**：
| 输入 | 行为 | 限制 |
|---|---|---|
| WASD | 局部 X/Z 平面移动滑车 | 仅 Aiming；相机切换不改变机柜轴向 |
| 1/2/3 | Front/Left/Right 固定相机预设 | 可以在瞄准阶段自由切换；动画阶段只观察 |
| 鼠标拖动 | 当前视角的小范围绕机/偏移 | 约 ±10–15°；不得穿过玻璃或遮挡主体 |
| C | 主视角与右下侧视画中画切换（第二轮） | 额外 RenderTexture 可能增加 GPU 负担 |
| Enter 或 Space | 开始一次完整自动抓取 | 按下一次只发一个命令；非 Aiming 状态忽略 |
| R | 重新挑战（双击确认或弹窗确认） | 不在抓取中直接重置而遗漏状态回收 |
| Esc | 暂停/继续；从设置页返回 | 光标解锁；物理与计时明确冻结 |

默认相机：Front 位于机柜正前方，Left/Right 位于机柜两侧，三者以箱体中心为 LookAt 目标；设场景 gizmo 显示 X/Z 方向。相机观察不跟随爪子过度晃动，切换时用位置/姿态缓动。

## 2. 场景与美术说明书

### 2.1 空间分层
Scene：ClawMachineScene.unity
- ArcadeShell：小型街机厅，仅作背景；其它机器低细节、非交互
- MachineRoot：实体机柜，单独可复用 prefab
- VisualCabinet：外壳、门框、灯牌、控台、侧面玻璃、奖品出口
- GantryRoot：X 桥架、Z 滑车、Y 升降、软线缆、三爪夹具
- PrizeBay：可碰撞地板、柔性外观的刚体奖品、目标标识
- PrizeChute：重力掉落通道、Trigger 计分区及防重复计分
- CameraRig：Front / Left / Right 摄像机，可能的 PIP 相机
- Managers：ClawGameManager / GraspTaskController / LevelLoader / Audio
- Canvas：菜单、HUD、抓取阶段、结算、暂停和设置

尺寸初值（米；最终用 Prefab 模型尺寸校正）：机柜宽 1.2、深 1.0、高 2.0；展示区地台离地约 0.7；抓取平台宽 0.95、深 0.8；水平 X/Z 安全范围由机柜边界 Collider 推导，不写死视觉模型尺寸。各模型统一 1 Unity unit = 1m，pivot、朝向、比例、碰撞体均需有规范。

### 2.2 视觉主题与资产验收
主题：当代高品质街机厅，奶白/银色机身、深灰结构件、冷白灯带、局部薄荷/粉蓝点缀、彩色娃娃。不是纯白立方体 Demo，也不是耀眼赛博霓虹。  
机柜要有真实可见的横梁、滑块、导轨、吊线、三指爪接头、抓取按钮、奖品出口与玻璃厚度。  
推荐 PBR 材质：金属粗糙度区别、细微塑料表面变化、娃娃绒毛法线/贴图、环境接触阴影。首版先用简单模型+可靠碰撞与贴图，不追求实时软体；三指的可视物理位置和抓持检测区必须一致。  
透明玻璃避免夸张反射遮挡、避免多个高开销透明层叠；目标奖品颜色/标签清楚。UI 不占据主要观察区域。
**资源规范**：Assets/ClawMachine/Models、Materials、Textures、Prefabs、Audio、Scenes；所有资源与 .meta 一同提交；外部资产附来源、用途、授权与再分发说明。不可擅自把付费 Asset Store 原始资产公开上传。

### 2.3 机柜摄像机与视觉通路
- Front 看水平 X 位置，Left/Right 看 Z 深度与遮挡；透视校准保持一致机柜方向。
- 玩家可在抓取前左右切换或在可选 PIP 中看到正面/侧面两个画面；三视角不会改变抓爪移动方向。
- 观察 UI 在相机切换时显示“当前视角”和固定 X/Z 方位小罗盘；辅助相机渲染仅选机器相关 Layer。
- 接近玻璃时不让相机进入玻璃或透穿模型；射线检测不被透明材质视觉属性误导。
- 后续机械臂摄像机可复用观察接口，但不能假定每个视角自动代表机器人传感器标定结果。

## 3. 机械运动与抓取算法

### 3.1 三轴运动
以 GantryDriver 提供可靠目标位移。X/Z 归属于机器局部坐标；Y 为独立升降，统一通过 Transform/受控物理运动，不通过给物体直接随机施力来模拟电机。  
限制：禁止超出机柜工作空间；速度与加速度采用缓入缓出（样条/SmoothDamp 或显式速度约束）；运行中不得穿越机柜、奖品出口挡板。建议初值 X/Z 速度 0.35m/s、Y 下降 0.25m/s、Y 上升 0.32m/s；运动误差容差 1cm，超过 2s 无进展则报超时；均可配置。
- 逻辑层将“移动到局部位置”转换为世界 Pose。
- 驱动器报告 Idle/Moving/Error 及当前位置、实际目标、限位/异常原因。
- 瞄准阶段的键盘输入先归一化再积分（帧率无关），切换 Camera 不干预坐标系。
- Y 降至基于安全高度与接触高度计算的区间，不能把爪子穿入娃娃内再判定成功。

### 3.2 有限状态机（必须单一权威）
Idle → Aiming → Lowering → Closing → Lifting → Transporting → Releasing → Settling → Result → Aiming/End。
附属状态 Paused / Resetting / Error；状态跃迁提供事件，调用者不能跨状态强行驱动。每次抓取 sessionId/attemptId 唯一；UI 与音效监听状态变化。

- Lowering：速度受限，碰撞或达到安全最低位置即停止，不可穿地。
- Closing：三个爪指协调开合；闭合持续可配置，形成抓持候选与接触事件。
- Lifting：只有有效抓持才约束奖品随爪运动；途中可因不稳定/碰撞被释放，掉落遵从重力。
- Transporting：运动到出货口上方指定姿态，途中夹持失败可掉落。
- Releasing：解除夹具/奖品约束，爪指打开；监听实际下落。
- Settling：给奖品一定结算窗口（建议 1.5–3s），不立即依据首次触碰判成功。
- Reset：停止正在运行的 Coroutine、关闭旧关节/连接、重置 Rigidbody 速度和角速度、恢复所有物品初始状态；重复 R/Enter 不能触发多次流程。

### 3.3 首版抓取物理（游戏级，不宣称工程级）
使用 hybrid grasp：
1. Prize 采用 Rigidbody + 多个简化 Collider，表面摩擦、质量、重心由 PrizeProfile 设置；
2. 三指使用可视动画与接触检测区域，采用至少两个接触点、中心偏移、抓持高度/包围程度等指标判断是否形成临时附着；
3. 成功时建立临时物理 FixedJoint 或受约束的抓取跟随，禁止无碰撞理由的瞬间“吸附”；
4. 如果抓持力不足、偏心/遮挡/其他物体冲突，保持自然掉落或主动断开约束；
5. 奖品进入 chute 触发区并在结算窗口中保持有效、与本轮 attemptId 对应，才算获奖；
6. 失败需要记录原因枚举：MISALIGNED、NO_CONTACT、UNSTABLE_GRIP、SLIPPED、MISSED_CHUTE、TIMEOUT、CANCELLED。

**重要边界**：Unity PhysX + 刚体奖品不能准确代表软体毛绒、接触压力和真实摩擦系数；首版用于可玩游戏，不宣称精确模拟真实夹爪力学。禁止把“固定随机 20% 中奖”作为抓取判定主逻辑。可用固定物理随机种子及可选难度辅助，但成功必须主要由动作和接触条件决定。

### 3.4 未来机械臂可替换设计
采用任务级驱动接口；机械控制使用世界 Pose、速度和状态反馈，避免使用只有 XZ 的输入语义。机器人轨迹规划与真实硬件安全层不属于游戏管理器。

~~~csharp
using UnityEngine;
public interface IManipulatorDriver
{
    Pose CurrentToolPose { get; }
    bool IsBusy { get; }
    bool TryMoveTo(Pose targetWorldPose, float maxSpeedMetersPerSecond);
    bool TrySetGripper(float openness01); // 0=闭合，1=张开
    void Stop();
}
~~~

- GantryDriver：把 Pose 中的 XYZ 映射为滑台/升降轴，固定夹爪姿态，返回限位/失败；
- RobotArmDriver（未来）：关节状态、末端姿态、运动学求解、轨迹执行、抓手控制；不得直接把 Unity Transform 写入真实硬件；
- 统一任务控制器 GraspTaskController 的状态机不依赖具体驱动；实际的摄像机标定、传感器测距、抓取位姿预测与外部 ROS 通信另建 Adapter；
- 安全接口必须支持 EStop、运动状态、超时、限位、碰撞保护、人工接管与设备脱机处理；游戏客户端只是请求方，不能成为真实设备的唯一安全保护措施。
- 参考 Unity 2022.3 Articulation Body/机器人模型接口；真实机器人接入前单独评估 ROS 2 与 Unity Robotics 版本兼容。

## 4. 第一版内容与难度

建议 5 关（首个里程碑只做前三关），难度由奖品可抓性和空间布局产生，而不是单纯降低虚构中奖率。

| 关 | 奖品及布局 | 引入的新技能 | 配置与失败反馈 |
|---|---|---|---|
| C01 定位教学 | 单个长方形硬质奖品，中央开阔 | X/Z、三视角、Enter 触发 | 5 次尝试，轻度自动纠正；提示应有前后偏差 |
| C02 指定目标 | 3 个硬质物品分布、指定其中一个 | 多视角选目标、遮挡分辨 | 目标信息可视化，不抓错物体 |
| C03 重心差异 | 圆柱、长条、块状三种质量分布 | 合理抓点、重心 | 偏心抓取更易滑落 |
| C04 堆叠遮挡 | 多件奖品部分堆叠 | 观察隐藏空间、选择抓法 | 必须保留碰撞与近距观察 |
| C05 稳定移送 | 目标更重、出口位置更偏 | 连续抓持与运输稳定 | 运输途中脱落、投放失败有明确解释 |

每关数据必须有 prizeId、targetId、seed、spawnPose、attemptLimit、cameraPolicy、graspProfile、successRule，存成 ScriptableObject 或易读取 JSON；禁止大量场景硬编码。初始对象随机分布应给固定种子与“重开同局/洗牌新局”区别。

**结算**：抓到目标后记录成功、实际尝试数、目标可视信息、抓取点和路径；不建议以“消耗时间最少”单独定义优秀表现。失败时显示有针对性的简短原因（例如“抓爪偏右，接触不足”），不是只写 Failure。

## 5. UI、声音、设置
- 主菜单：机器局部场景作为动态背景，右侧“开始挑战/关卡/操作/设置”，保持机器主体可见；
- HUD：顶部关卡名与剩余尝试，侧边小浮层显示当前 Front/Left/Right 视角、相机切换提示、固定 X/Z 朝向和抓取流程状态；底部明确 Enter/Space 抓取；
- 抓取中：交互输入锁定但相机可切换；阶段标签“下降/闭合/提升/返回/释放”，失败不回退时间或强制快进；
- 可选无障碍：字号、镜头平滑、色觉友好形状提示、手柄不在首轮、独立音量调节、可关闭动态镜头；
- 音频：机柜环境低噪、电机 X/Z、绞盘下降、爪子闭合、金属接触、奖品落地、成功/失败提示、UI hover/click；不能只靠音效告知结果；
- 后处理：以清晰玻璃视野与机内接触阴影优先。保留项目现有渲染管线，不为了轻微 Bloom 直接整体迁移 URP。

## 6. 代码与资源结构

~~~text
Assets/ClawMachine/
  Scenes/ClawMachineScene.unity
  Scripts/
    Core/ClawGameManager.cs
    Core/GraspTaskController.cs
    Core/ClawChallengeDefinition.cs
    Drivers/IManipulatorDriver.cs
    Drivers/GantryDriver.cs
    Drivers/ManipulatorStatus.cs
    Gripper/GripperController.cs
    Gripper/GraspContactEvaluator.cs
    Prize/PrizeProfile.cs
    Prize/PrizePhysics.cs
    Prize/PrizeChuteDetector.cs
    Camera/CraneCameraController.cs
    Camera/CameraPreset.cs
    Input/ClawInputController.cs
    UI/ClawGameUI.cs
    Audio/ClawAudioController.cs
  Prefabs/Machine/ClawMachine.prefab
  Prefabs/Prize/*.prefab
  Materials/  Models/  Textures/  Audio/
  Challenges/C01.asset ... C05.asset
  Tests/EditMode/ Tests/PlayMode/
  Editor/ClawSceneBuilder.cs
Docs/Game02_ClawMachine/
  DESIGN_AND_DEVELOPMENT.md
  AGENT_TASKS.md
~~~

目录按游戏独立组织，有自己的 asmdef、GUID 和 .meta。新依赖先评估版本；不能覆盖 FirstPersonSokoban 资产。Build Settings 新增 ClawMachineScene，但保留原有 PuzzleScene；独立 Windows build 应明确构建入口和输出目录。

## 7. 里程碑与每阶段交付定义

**M0｜工程隔离与灰盒（半至 2 天）**  
Branch 隔离；机柜、XYZ 轨道、三相机、Prefab、场景；保证原 Sokoban 不变化。图形灰盒允许，必须一眼看懂 X/Z/Y。

**M1｜可玩的机械循环（2–4 天）**  
WASD 定位、固定三视角、Enter 一键完成状态机、基本抓持与落物判定；C01 教学关；稳定 Reset/Restart。提交实际 Unity 操作录屏与 PlayMode 测试。

**M2｜物理、关卡与 UI（3–5 天）**  
三种奖品形状、独立 PrizeProfile、C02/C03 关卡、成功/失败原因、HUD、投放口、完整音效；无明显玻璃遮挡问题。

**M3｜视觉交付与扩展（3–5 天）**  
PBR、灯光、机柜细节、C04/C05、PIP 可选、性能与存档测试、Windows 打包；先确保前三关验收通过后才追加内容。

以上时间只是开发任务量级，不包括真实机械臂集成、设备采购、ROS 环境和算法训练；实际以功能验收为准。

## 8. 测试、指标与交付门槛

**EditMode 核心**：
- X/Z 方向严格机器坐标系；视角切换不影响动作；
- 工作空间裁剪、速度/加速度、反向移动、零输入；
- 状态机合法跳转、Enter 重入屏蔽、Attempt 只递增一次；
- 目标奖品匹配、进出 chute 仅结算一次、无目标失败不得假成功；
- 取消/重置将内部状态归初始，空奖品/丢引用不会 NRE。

**PlayMode / 人工**：
- Front、Left、Right 看见机内奖品且不穿玻璃；
- 正面 X 与侧面 Z 定位能被玩家理解；多角度观察比单视角更有意义；
- 不居中/偏心抓取、不同质量、掉落等带来合理行为变化；
- 连续 30 次抓取、20 次重置、在每个动画阶段 Esc/R 不引发重复执行、无限约束、丢物体/虚假投放；
- 奖品真正进入出货口才成功；掉在旁边不算；
- 1920×1080 / 1366×768 / 1280×720 的 UI、相机与透明材质效果；
- Win x64 真机至少目标平均 60fps（推荐对 1% low / Profiler 帧时间也记录），不得把自动化测试数量代替画面和手感的实测。

**证据**：场景/Prefab/.meta，EditMode 与 PlayMode 原始 XML，Unity 编译日志，Windows 构建日志、至少三段视频（正面/侧面/成功抓取）、素材授权台账、known issues、测试与版本 SHA。

## 9. 风险与明确降级策略

1. 软体娃娃穿模或接触不稳定 → 优先刚体+简化接触/约束，视觉用毛绒材质，不在首版做实时柔体。
2. 夹爪三个关节数值不稳 → 先视觉闭合+可靠接触代理，再替换为关节动力学仿真；记录与真实夹爪的差异。
3. 画中画对 GPU 开销高 → M1 只做三视角键切，PIP 可关闭且后置。
4. 真实机械臂误认为只需换 Prefab → 严格区分 GantryDriver / RobotArmDriver，机器人接口和安全系统后置。
5. 外部模型授权不清 → 用临时原创占位物，授权确认前不提交商用资源。
6. AI 为了视觉重构核心运动 → Unity 核心驱动测试冻结，优先修改资产、Prefab、UI 与 Shader 参数。

## 10. 开工任务交付口径

Agent 先读取当前仓库 manifest、ProjectVersion、Sokoban 场景与 CI，报告兼容性与变更范围，再创建 Assets/ClawMachine 模块。**首轮只完成 M0+M1**，不能仅生成 C# 代码就宣称已可玩。按 AGENT_TASKS.md 提交真实变更、测试、录像或无法验证项。主分支和第三个游戏分支保持隔离。
