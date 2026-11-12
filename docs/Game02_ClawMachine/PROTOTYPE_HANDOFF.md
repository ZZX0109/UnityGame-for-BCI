# C01 原型交付与验证边界

本次实现设计任务单限定的 **M0 + M1**。代码、场景、机柜及部件 Prefab、C01 配置、独立字体和测试已提交；当前环境没有 Unity Editor 或 C# 编译器，因此不能宣称 Unity 编译、抓取手感、物理成功率、Windows 运行或画面验收已通过。

基线：`game02/claw-machine-3d`，远端提交 `03b00836358572d4d7a3c1cce34c23f517aea877`，完整树 `d8ee8e6e819e3230c9b230ec455af879ab4157db`。本地按全部 Git blob SHA 校验恢复该树后开发。未修改 FirstPersonSokoban、包依赖或 Unity 版本。Build Settings 仅在原 PuzzleScene 后追加 ClawMachineScene；专用构建命令只构建 Claw 场景。

## 打开与操作

使用仓库原版 **Unity 2022.3.62f3c1** 打开项目；双击 `Assets/ClawMachine/Scenes/ClawMachineScene.unity`，或菜单 `Claw Machine > Open playable C01`，按 Play，再点击“开始 C01 定位教学”。无需运行生成器、联网、安装新包或连接设备。

| 操作 | 键位 / 界面 |
|---|---|
| 固定机柜 X/Z 移动 | A/D 左右，W 朝机柜内部 +Z，S 朝外 -Z |
| 正面 / 左侧 / 右侧观察 | 1 / 2 / 3；切换视角不会重映射移动轴 |
| 三种观察距离 | 滚轮，或菜单/设置的距离按钮 |
| 完整自动抓取 | Enter / Space / 抓取按钮，仅定位阶段有效 |
| 下一次尝试或本局结果 | Enter 或结果按钮 |
| 暂停与继续 | Esc；物理、驱动及阶段计时同时冻结 |
| 重开同局 | R 后确认，或界面按钮；相同 seed 恢复相同布局 |
| 取消并返回菜单 | 暂停界面的返回按钮；回收连接、速度及物体状态 |
| 设置 | 音量、文字大小、镜头距离与距离平滑 |

单个薄荷礼盒、5 次尝试、seed `20261112`。先对准左右，再侧视对准深度。一次 Enter 依次执行下降、闭爪、提升、移送、释放、稳定、结果。无付费、随机中奖、网络、EEG、BCI 或真实机械臂连接。

## 实现与资源

- `ClawSession` 为状态与结算权威，拒绝非法跳转、重复 Enter 和旧 session/attempt。`GraspTaskController` 按 FixedUpdate 执行，无旧协程回写。
- `GantryDriver` 单次固定步积分连续输入，归一化对角速度、限制加速度和 XYZ 工作区，独立 Y 升降。姿态固定；不支持的工具旋转请求会被拒绝。运动无进展或自动阶段超时报错；人工观察与定位不计超时。
- `GripperController` 为游戏级混合抓持：三指几何探针在实际奖品 Collider 上验证至少两个接触、中心偏移和高度，然后建立有断裂阈值的临时 FixedJoint。连接保留当前世界姿态，不把远处奖品瞬移到爪下。奖品保留 Rigidbody、重力和碰撞；断裂会记录滑落。
- 出口有真实地板开孔、下落通道、碰撞侧壁与托盘。全奖品 bounds 必须进入有效区域，且本轮已释放、目标 ID 匹配、连续稳定 0.3s 才可计奖一次。仅碰到爪、进入 Trigger 边缘或仍被夹持不算成功。
- reset / cancel / disable / quit 会停止驱动、立即解除连接引用、回收关节并清除奖品速度。游戏暂停使用 Time.timeScale，并在返回、重开、禁用及退出时恢复原值。
- 机柜、导轨、滑车、吊线、三爪、玻璃、控台、出口、礼盒与街机背景均为本次原创基础几何。奶油白/银色、薄荷与少量暖色；没有顶棚网格。透明窗保留碰撞，但可看到机内与收集托盘。不是 PBR 毛绒或工程级抓取仿真。
- 所有运行时连接均为序列化类型引用；无全局启动钩子，不会打开或接管 PuzzleScene。完整机柜 Prefab 自带 C01 引用；单独部件 Prefab 作为可复用模型/物理模板。场景与 Prefab 是生成器维护的独立快照，场景不依赖运行时查找或自动生成。

运行时依赖仅为仓库现有 Unity 模块。`IClawInputSource` 分离键盘输入；`IManipulatorDriver` 保留任务级 Pose/夹爪/停止接口。当前任务规划仍直接使用 GantryDriver 的工作区、局部坐标、Aim/Step/ResetPose；未来 RobotArm 不是即插即用，必须再拆分任务空间、tick/reset 及安全适配器，不能声称只替换 Prefab 即可接入真机。

## 验证记录

实际执行的 Python 检查和原始输出见 `STATIC_VALIDATION.txt`。这些检查覆盖文件/GUID/importer、完整序列化引用、开孔的中央落物通路、确定性资源生成与字体覆盖；原 Sokoban 的八关可解性和现有 22 项 Python 工具测试仍通过。

新增 **14 项 EditMode** 测试：有限状态、重入、暂停、目标/释放/身份/稳定结算条件、重置、尝试上限、相机方向、六向限位、非法数值、Stop、144Hz 输入配合 50Hz 物理、独立 seed、不改变 Unity 全局随机状态、完整场景引用。新增 **2 项 PlayMode** 测试：真实对齐后抓取释放并落入出口；持有中暂停再重开，检查连接与身份回收。**两组 Unity 测试均未执行，没有可交付的真实 XML。**

在已安装且授权的准确版本 Windows Editor 上，可运行：

```powershell
$project = (Get-Location).Path
New-Item -ItemType Directory -Force BuildOutput/ClawMachineEvidence | Out-Null
& $env:UNITY_EDITOR_PATH -batchmode -nographics -projectPath $project -runTests -testPlatform EditMode -testFilter ClawMachine.Tests.EditMode -testResults BuildOutput/ClawMachineEvidence/EditMode.xml -logFile BuildOutput/ClawMachineEvidence/EditMode.log
& $env:UNITY_EDITOR_PATH -batchmode -nographics -projectPath $project -runTests -testPlatform PlayMode -testFilter ClawMachine.Tests.PlayMode -testResults BuildOutput/ClawMachineEvidence/PlayMode.xml -logFile BuildOutput/ClawMachineEvidence/PlayMode.log
# 必须人工检查 XML 为非零测试且无 failed / skipped，再执行构建。
& $env:UNITY_EDITOR_PATH -batchmode -quit -projectPath $project -executeMethod ClawMachine.Editor.ClawSceneBuilder.BuildWindows -logFile BuildOutput/ClawMachineEvidence/WindowsBuild.log
```

或在 Test Runner 按 `ClawMachine.Tests` 过滤，执行两种模式，再菜单 `Claw Machine > Build Windows C01`。输出入口 `BuildOutput/ClawMachine/ClawMachine.exe`，本次未生成或伪造 EXE / ZIP。

人工验收仍需：正/左/右视角清晰度；正面对 X、侧面对 Z；成功和偏离抓取；每个阶段暂停与重置；30 次连续抓取及 20 次重开；1280×720、1366×768、1920×1080 中文 UI；Windows 真机性能及操控舒适度。保存真实 Editor 日志、XML、三视角截图/视频与构建记录后才可标记这些项目通过。

## 可复现编辑

```bash
python docs/Game02_ClawMachine/generate_prototype.py --check
python docs/Game02_ClawMachine/validate_prototype.py
python Tools/validate_project.py
```

普通 `generate_prototype.py` 只会创建缺失资源或更新与上次生成记录一致的资源；遇到手工改过的生成文件会停止，`.meta` GUID 永远先校验。确实要从脚本重建这些文件时，审查差异后用 `--force`。`GeneratedAssets.json` 记录内容摘要。字体重建与许可见 `ASSET_LICENSES.md`。

## 后续范围

尚未实现：C02–C05、多物体/重心挑战、PIP、鼠标小角度绕机、完整电机循环声、PBR 美术、存档、硬件/ROS/BCI/EEG/软体。C01 采用接触代理和刚性礼盒；FixedJoint 参数、物理实际成功路径与全部镜头/UI 需要在准确 Editor 内验收调整。此提交是可在 Unity 中打开和检验的 M0+M1 源码原型，不是已通过 Windows 实测的发布版本。
