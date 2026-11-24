# 跨游戏接入 (GAME_INTEGRATION)

如何把同一套 `FirstPersonHands.prefab` 接入不同游戏，而不复制或改写手部控制代码。

---

## 1. 设计边界

**核心交付（规范第八部分）：**

| 产物 | 路径 |
|---|---|
| 第一人称双手 Prefab | `Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab` |
| 统一宿主组件 | `Integration/VirtualHandHost.cs` |
| 游戏适配接口 | `Integration/IGameHandActionAdapter.cs` |

**手部模块不直接修改各游戏业务规则。** 它只做三件事：

1. 显示双手（外观）
2. 把输入变成手部姿态（追踪/输入）
3. 把手势事件**交给宿主**，由游戏自己决定对应什么操作

---

## 2. 适配接口契约

```csharp
public interface IGameHandActionAdapter
{
    void OnHandUpdate(HandTrackingFrame left, HandTrackingFrame right,
                      HandGestureEvent leftGesture, HandGestureEvent rightGesture);
}
```

**接入规则：**

| 允许 | 禁止 |
|---|---|
| 读取手势事件并映射为游戏操作 | 在适配器里改手部骨骼 |
| 读取手部位姿做碰撞检测 | 在适配器里改追踪数据 |
| 决定"握拳 = 推箱子"之类的业务映射 | 在适配器里改输入来源 |

**实现位置：** 在游戏工程内新建一个 `MonoBehaviour` 实现该接口，
挂到 `FirstPersonHands.prefab` 的根节点上，并赋给 `VirtualHandHost.adapterBehaviour`。

---

## 3. 单一消费点

`VirtualHandHost` 是**唯一**调用适配器的地方。

**为什么重要：** 如果同时从 `Update` 和渲染前回调各执行一次手部更新，
一次握拳可能被投递两次，导致按一次键推两个箱子、或抓取被重复触发。

保障机制：

- `HandInteractionRouter.Tick()` 有**帧号锁**，同一帧第二次调用是空操作
- 手势边沿（`GraspStart` / `GraspEnd`）只在状态真正变化的那一帧产生
- 适配器每帧只被调用一次，且同时收到腕部帧与手势，不会出现"有手势无帧"的组合

`HandInteractionRouter` **刻意不自己调用适配器**，就是为了守住这一点。

---

## 4. 三个游戏的接入方案

> ### 🚫 当前状态：阶段 E 阻塞
>
> **本机不存在 `AI-Game/GAME*` 目录**，无法创建隔离测试副本。
> 以下为**接入方案**，**尚未在任何游戏上实施，也没有任何测试结果**。
>
> 规范第 8.4 条要求先在三个游戏的**测试副本**中安装，不得直接修改正式游戏分支。
> **本轮未修改任何正式游戏分支。**

### 4.1 Game01：第一人称推箱子

| 用途 | 方案 |
|---|---|
| 第一视角显示双手 | 挂 `FirstPersonHands.prefab` 到相机下 |
| 推动前显示抬手/推掌教学 | `GuidanceHandController.ShowPose()` 播教学姿态 |
| 触发游戏已有合法推动 | 适配器把 `Point`/`Open` 手势映射到 `BoxInteractor` 的现有入口 |
| 推动完成后恢复自然手姿 | 释放教学手，输入路由交还正常来源 |

**约束（规范 8.1）：**

- ❌ 不能绕过现有 `BoxInteractor`、`PuzzleGameManager`、碰撞和死锁规则
- ❌ 同一操作不能产生重复推动 → 依赖第 3 节的帧号锁与边沿事件
- ✅ 适配器**只调用**游戏已有入口，不自己改箱子位置

### 4.2 Game02：3D 抓娃娃机

| 用途 | 方案 |
|---|---|
| 第一视角显示左右手 | 同上 |
| 手柄或手势进行机柜交互 | 适配器接收手势事件，转成机柜按钮的既有输入 |
| 对准/抓取/释放的手部反馈 | 手势事件驱动手指姿态（外观层），不驱动抓爪 |
| 教学手示范操作 | `GuidanceHandController` |

**约束（规范 8.2）：**

- ❌ 抓爪运动和物理夹持**仍由娃娃机自己的状态机控制**
- ❌ 虚拟手**不能直接修改奖品位置或判奖结果**
- ✅ 适配器只做"手势 → 输入事件"的翻译

### 4.3 Game03：红绿灯过马路

| 用途 | 方案 |
|---|---|
| 玩家低头能看到自己的手 | 双手 Prefab 挂在相机下，随头显移动 |
| 食指指向标牌或可查看物体 | `HandGestureRecognizer` 的 `Point` 手势 + 射线检测由 Game03 做 |
| 观察/等待动作教学 | `GuidanceHandController` |
| 视觉模式跟随用户设置 | `HandAppearanceController` + `AvatarVisualStyle` |

**约束（规范 8.3）：**

- ❌ 过街交通信号、车辆位置、碰撞、观察评估和安全判定**仍由 Game03 控制**
- ❌ **手指指向不能替代头部左右观察**，除非未来实验方案明确修改评估定义
- ✅ 适配器把指向手势报给 Game03，由 Game03 决定是否算作"观察"

---

## 5. 隔离集成流程（规范 8.4）

三个游戏**每个**至少要完成以下 9 项：

| # | 项目 | 验证方式 |
|---|---|---|
| 1 | 安装模块 | 按 `INSTALLATION.md` 导入 |
| 2 | 显示左右手 | 场景中可见，位置正确 |
| 3 | 一个游戏专属交互 | 该游戏的核心玩法通过手势可用 |
| 4 | 切换写实/蓝色模式 | 运行时切换正常 |
| 5 | 暂停与继续 | 暂停时手部冻结，恢复后正常 |
| 6 | 失追恢复 | 断开追踪后手不错误释放，恢复后重新同步 |
| 7 | 禁用并卸载模块 | `VirtualHandHost.IsEnabled = false` 后游戏原操作恢复 |
| 8 | 确认原游戏键盘玩法仍正常 | **逐一测试该游戏原有全部按键** |
| 9 | 确认无重复触发 | 一次操作只产生一次游戏效果 |

**三个测试副本全部通过后，再提供正式合并方案。**

---

## 6. 多输入来源的安全控制（规范 3.4）

`TaskController` 已实现以下闸门：

| 要求 | 实现 |
|---|---|
| 回放结果不应与实时 XR 控制同时写入同一骨骼 | `TaskController.LiveSourceOwnsHands` —— 为真时 `UpdateTaskHands` 直接返回 |
| XR 追踪丢失不能触发新的抓取 | `NotifyInputTrusted(false)` → `EnterSafeState()` → `OnIntent` 直接返回 |
| 输入恢复后先回到安全状态再允许新事件 | `AwaitingSafeState` + `inputResyncSeconds`（默认 0.3s）窗口 |
| **不能通过切换视觉模式改变 Rigidbody 或物体所有权** | `NotifyVisualModeChanged()` 刻意留空；全量搜索确认外观路径不触碰物理 |

**关于"失追不能错误释放"：** 失追时任务**保持当前阶段与物体所有权**，
只是停止推进。这样不会出现"追踪丢了 → 手张开 → 游戏以为玩家松手 → 掉落物体"。

`EnterSafeState()` 会清除 pending 计时器与 latch，但**不清除 `_heldObject`** —— 这是刻意的。

---

## 7. 相关文件

| 文件 | 作用 |
|---|---|
| `Integration/IGameHandActionAdapter.cs` | 适配接口 + `HandGestureType` + `HandGestureEvent` |
| `Integration/VirtualHandHost.cs` | 宿主：启用/禁用、单一消费点、暂停与继续 |
| `Interaction/HandActionDefinitions.cs` | 动作定义与 20 路控制写入 |
| `Interaction/HandGestureRecognizer.cs` | 从实际驱动值识别手势 |
| `Interaction/HandInteractionRouter.cs` | 边沿事件 + 帧号锁 + 失追屏蔽 |
| `Presentation/HandAppearanceController.cs` | 写实/蓝色模式切换 |
| `Presentation/GuidanceHandController.cs` | 教学引导手（独立实例，不碰玩家手） |
| `Runtime/TaskController.cs` | 多输入安全闸门 |

---

## 8. 尚未完成

| 项目 | 状态 |
|---|---|
| Game01 隔离副本安装与测试 | 🚫 环境不存在 |
| Game02 隔离副本安装与测试 | 🚫 环境不存在 |
| Game03 隔离副本安装与测试 | 🚫 环境不存在 |
| 三个游戏各一个专属交互验证 | 🚫 环境不存在 |
| 原游戏键盘玩法回归测试 | 🚫 环境不存在 |
| 正式合并方案 | 🚫 前置未完成 |

**在没有隔离副本测试结果之前，不得声明手部模块已在任何正式游戏中上线。**
