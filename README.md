# 第一人称过马路

Unity **2022.3.62f3c1 中国版**，目标Windows x64。三个街区、九个任务与每任务A/B/C固定配置**已实现**。Final2运行版已通过147项EditMode、34项PlayMode（含27配置原生自动行走）、Windows构建及正常时间600秒：High1080p/RTX4060平均850.86FPS、P95 1.57ms。15路线图、15 UI gallery与正式四分辨率QA无阻塞，保留三个提示/菜单上下文P2与明确截图范围。两历史测试末尾行尾已统一LF，专项原生9/1及源码69复查通过，运行版873d与归档源码141b以明确桥接绑定，详见 [当前状态](Validation/CurrentStatus.md) 与 [本轮验收](Validation/Review-20261117-NineMissions.md)，设计见 [任务与难度](docs/DESIGN_DIVERSITY_AND_DIFFICULTY.md)。

## 打开与选择

在Unity Hub用上述版本打开工程，导入后打开 [StreetCrossingScene.unity](Assets/StreetCrossing/Scenes/StreetCrossingScene.unity)，按Play。菜单依次选择街区、任务和A/B/C固定配置，再开始任务；所有梯度均可直接练习，推荐梯度不等于锁关。

| 街区 | T1 引导 | T2 标准：主要显示风险 | T3 挑战：按需帮助 |
| --- | --- | --- | --- |
| S01 住宅社区 | 去对面便利店 | 在不同起始信号阶段判断出发时机 | 从便利店反向返回住宅入口 |
| S02 商业街 | 去书店，确认双向四条车道 | 在混合车型/排队遮挡下重新观察后去书店 | 识别药店，过街后沿人行道到实际药店门前 |
| S03 转角路口 | 去咖啡店，认识转弯和清空过程 | 自主选择安全观察站位后去咖啡店 | 去咖啡店后返回原住宅侧起点 |

任务稳定ID为`S01-T1`至`S03-T3`。A/B/C是同一任务的三组固定seed配置，不代表额外难度等级；相同任务版本和配置重开时应复现同样开局。T2的20/20/8秒预演必须完整推进信号与车流，不是只切灯色。

## 键鼠与返程

| 键鼠 | 作用 |
| --- | --- |
| WASD / 鼠标 | 连续行走 / 环顾街道 |
| Shift | 按住快步走 |
| E | 查看附近可见的提示牌 |
| F1 | 打开帮助；按需任务也可主动查看，不扣安全星 |
| Esc | 暂停／继续；设置或帮助先返回 |
| R | 确认后从第一段重开整个任务 |

在路沿确认信号并主动观察各必需车道，再沿横道与人行道去实际目的地。不要穿过横道外车道直奔店招。暂停或失焦会冻结世界；舒适、字号、音量设置不是难度处罚。

往返任务到咖啡店后先显示本段回顾，选择“准备好了，继续返程”再走第二段。返程保留交通与信号进度，方向、进入条件及观察证据重新建立；去程观察和安全标记不能直接算作返程通过。重新开始会回到第一段，并重置本次任务的交通开局与观察记录。

## 安全成绩与推荐

只有整项任务各段均安全完成才记录成绩：信号进入、合法路线、进入前可见道路观察与移动碰撞等关键条件必须满足。安全完成基础1星；没有危险接近再加1星，没有静止接触再加1星。往返成绩取各段最低星，任一段关键违规时整项任务为0星，不能被另一段成功覆盖。等待、时间与主动帮助次数单独记录，不加减安全星，也没有抢时间奖励；长等待先提醒，不因合理等待强制失败。

每个任务、revision及A/B/C分别保存最高星和是否安全完成。反复完成同一配置不会增加不同配置完成数；同一梯度的三组配置均安全完成后推荐下一梯度，最高推荐T3。高阶可自由练习，但不会跳过未完成前序梯度的推荐。原有S01/S02/S03情境星级与舒适设置保留，新任务成绩独立存储。

观察记录来自相机朝向、道路可见性与停留时间，不是眼动或注意力测量。结果页分别解释每段信号、观察、路线、危险接近、静止接触和移动碰撞。

## 验证与交付

工具需要Python3.10+；原生验证另需精确版本Unity许可、Windows Build Support与真实图形设备。先保存并关闭本工程Editor，再执行：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_game_delivery.py --game street
```

命令的当前覆盖范围与实际结果以 [CurrentStatus](Validation/CurrentStatus.md) 为准；不能仅靠命令成功推断九任务×三配置的全部玩法已验收。交付入口见 [GameDelivery.md](Validation/GameDelivery.md)，画面采集见 [VisualCapture.md](Validation/VisualCapture.md)。本轮运行验证已完成；LF归档桥接已完成，最终源码提交/SHA以CurrentStatus及交付manifest为准。

[2026-11-16旧运行包](BuildOutput/Review-20261116-StreetFinal2/StreetCrossing-Windows64.zip) 和 [旧验收报告](Validation/Review-20261116-Street.md) 仅用于实施前回溯，不含本轮九任务；原始证据与运行记录继续保留。`Assets/`、`Packages/`、`ProjectSettings/`和`.meta`是必要工程文件，不要把Unity工程直接复制成Neuralife的QML插件。

本轮运行包：[九任务Windows ZIP](BuildOutput/Missions-20261117-Final2/StreetCrossing-Windows64.zip)。新27配置行走为timeScale3的真实引擎自动测试，不是27次人工试玩；旧Acceptance与正式600为timeScale1。帮助/耗时不减安全星，逐段结果不可被后段成功覆盖。GPU/GC计数器不可用；完整菜单、放大字号与全部交互不由现有截图推断通过。
