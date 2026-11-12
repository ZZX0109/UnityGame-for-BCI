# Game 02 · Coding Agent 实施任务单

先阅读同目录 DESIGN_AND_DEVELOPMENT.md；该文档是唯一的本游戏设计依据。本分支只开发 Assets/ClawMachine，不修改 FirstPersonSokoban、StreetCrossing、main 或 Unity 版本。Unity 2022.3.62f3c1 / 旧 Input Manager / Windows x64；新增文件必须含 .meta、正确 asmdef 和许可说明。

## M0：工程隔离与灰盒场景
- 确认 ProjectVersion.txt、Packages/manifest.json、现有 Build Settings。
- 创建 Assets/ClawMachine/Scenes/ClawMachineScene.unity、独立目录/程序集、机柜/导轨/滑车/线缆/爪子/出口/奖品区的 Prefab。生成资源要安全幂等，避免覆盖已存在资产。
- Front/Left/Right 三相机；玩家按 1/2/3 切换；WASD 永远映射机器局部坐标，不随相机旋转。
- 创建三种观察距离、机柜边界 Gizmo 与禁越界测试。首轮灰盒可以不用精美素材。

## M1：最小完整抓取闭环
- 实现 IManipulatorDriver、GantryDriver、GripperController、GraspTaskController、PrizePhysics、PrizeChuteDetector、ClawGameManager、HUD。
- 允许 WASD X/Z 定位；Enter 一次启动完整状态：Aiming→Lowering→Closing→Lifting→Transporting→Releasing→Settling→Result。
- 初版只需 C01 单物体、受控抓持代理、明确是否进入出口及抓取失败原因；禁止随机成功率代替物理判断。
- 在任何状态中暂停、取消、重置、程序退出后，停止协程、回收约束、还原物体位置/速度，不能残留悬挂奖品。
- 只在场景中引用组件；不通过文件名字符串假定完成连接。

## M2：内容与质量
- 增加 C02 指定目标、C03 三种几何/重心差异；奖品 Profile 采用配置资源，加入可追溯 seed。
- 完成 UI、失败原因提示、抓爪视角高亮、机械音效、场景基础美术；再做 C04/C05 与 PIP。
- 任何 PBR 模型/字体/音效应列出来源、授权、可分发状态。

## EditMode 自动测试
1. 坐标系与相机切换不改变 WASD 动作方向；
2. X/Z/Y 限位、速度与输入去抖；
3. 抓取状态机拒绝非法跳转与重复 Enter；
4. 无抓持不带走奖品、成功结算要求目标奖品实际进入出口且每轮一次；
5. Reset 清除 session、计数、速度/连接与状态；
6. 固定 seed 同局可复现。

## PlayMode / 实机验收
- 三视角对齐清楚、玻璃不挡观察；至少一种奖品能成功拾取、移送、释放并落入出货口。
- 故意偏离抓点触发不同于成功的结果；不会穿箱体/机器底板。
- 连续 30 次抓取+20 次重开，含各阶段暂停/重置，无 NRE、重复计数、旧协程回写。
- 1280×720/1366×768/1920×1080 HUD；Windows x64 真机可运行。
- 请交付：修改文件清单、编译日志、EditMode/PlayMode 原始 XML、关卡数据、截图或短视频、Windows ZIP、已知问题。无 Unity 运行权限时标记未验证，不能猜测测试通过。

## 限制
**只实现 M0+M1 后先给出可验收原型**。不要直接展开机械臂、ROS、BCI、软体和额外关卡；机械臂只留下稳定任务接口，真实设备必须另有硬件安全层。