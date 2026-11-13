> 更新：用户后续上传审计并明确要求完成全部改进，现阶段以 AUDIT_IMPLEMENTATION_2026-11-13.md 为实施与验收矩阵；下面 M0+M1-only 限制描述的是首轮历史范围。

# Game 03 · Coding Agent 实施任务单

先阅读同目录 DESIGN_AND_DEVELOPMENT.md。本分支只增加 Assets/StreetCrossing，不修改 main、FirstPersonSokoban 或 ClawMachine。必须保持仓库 Unity 2022.3.62f3c1 中国版与旧 Input Manager 可用；需新建独立 asmdef、场景、Prefab、存档命名空间及 .meta。

## M0：可走的第一人称街区
- 建立 StreetCrossingScene.unity：住宅楼立面、人行道、缓坡路沿、双向车道、停止线、斑马线、公交站目的地、主灯组。先灰盒完成道路布局。
- 用 CharacterController 实现 WASD/鼠标/快走/ESC/Pause/R，匹配写实步速、FOV，禁止玩家穿墙/穿车；不采用 Crossy Road 跳格。
- 划分 Approach/Kerb/CrosswalkEntry/Conflict/CrosswalkExit/Destination 区域；显示目的地、世界中的信号灯、极简任务提示。
- 将行驶侧、方向和停止线坐标写入场景配置而非分散硬编码。

## M1：可信的完整交通闭环
- 定义 TrafficPhase 和单一权威 IntersectionController，相位来自可编辑数据：VehicleGreen→VehicleAmber→AllRedBeforeWalk→Walk→WalkFlashing→AllRedAfterWalk。
- TrafficSignalView 只订阅状态；车辆只有经过冲突授权才可穿越停止线；人行横道有人且未安全清空不得给冲突车流放行。
- VehicleLanePath + VehicleTrafficManager + VehicleController：车辆循车道行驶、反应式制动、停车线停稳、合理起步、车距、防追尾、远端对象池回收；不使用赛车物理模拟作为第一版必要条件。
- CrossingEvaluator：红灯进入违规、行人绿灯进入合法、闪烁期间已过街者允许完成；到达对侧目的地才成功。独立记录危险接近和真实碰撞。
- 先完成 S01 一条街可安全跨越，不要先做五套街景。

## M2：三情境与 UI
- S02 增加可复现的车流密度和等待判断；S03 新增真实转弯车路径及让行逻辑。
- 加 2–5 个简单 NPC 行人、完整 HUD/菜单/暂停/结算、信息性失败解释和设置。
- S04 遮挡与 S05 放学时段延后；不使用随机不可躲避的车撞玩家制造难度。

## EditMode 测试
1. 信号相位顺序、时长/边界、暂停一致性；
2. 行人信号与冲突机动车授权互斥，路口清空规则；
3. 可控车速的停车距离/不越线/排队；
4. 同 seed 生成相同车序列和时刻；
5. 绿灯进入后灯变化不被误判为闯红灯，红灯进入可标记；
6. 观察事件、危险接近、碰撞和到达结果不会重复计数；
7. S01 存在可完成的安全通行窗口。

## PlayMode / 人工验收
- 玩家能清楚看到灯、左右车流和安全等待区，不能穿入建筑/车辆；
- 连续 10 周期不得让信号相互冲突；红灯机动车逐渐停车，不瞬间急停或穿过停止线；
- 绿灯过街与灯转闪烁/清空阶段符合设计，车辆不能撞上已经正确进入斑马线的人；
- 停车、排队、起步平滑；有车靠近时 HUD 不能替代实际观察；
- 20 次重置/切换无重复车、旧车队回写、时间不同步；
- Windows Build 实机测 1280×720、1366×768、1920×1080、1280×1024，至少一完整 S01 人工通关；记录性能、Console 和短视频。
- 交付：逐文件修改、EditMode/PlayMode 原始 XML、相位轨迹日志、GUI 录屏、Windows ZIP、授权信息、known issues。没有 Editor 就不声称通过。

## 限制
**只开发 M0+M1 并提交可验收样机**。首版白天双向街道，半写实而非巨大开放世界；真实交通规则的地区特异性需要以后明确，当前时长为游戏配置，不得声称合规真实法规。