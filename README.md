# 三维抓娃娃机

Unity **2022.3.62f3c1 中国版**，目标 Windows x64。C01–C03 分别训练礼盒定位、兔子抓持和偏心狐狸抓持；C04 是旋转玩偶圆盘。

当前为源码候选版。当前验证结果以本轮实际日志和截图为准，Windows 游戏包及性能需独立验收；详见 [当前状态](Validation/CurrentStatus.md) 和 [本轮优化交付](Validation/Optimization-20261116.md)。

## 打开与操作

在 Unity Hub 用上述版本打开本工程。等待导入，执行 `Claw Machine → Prepare and validate resources`，打开 [ClawMachineScene.unity](Assets/ClawMachine/Scenes/ClawMachineScene.unity)，按 Play 后从菜单选择挑战。

| 键鼠 | 作用 |
|---|---|
| WASD | 在机柜局部 X/Z 平面定位；方向不随相机改变 |
| Enter / Space | 定位阶段开始一次完整抓取 |
| 右键拖动 / 滚轮 | 围绕机柜连续环绕观察 / 调整观察距离 |
| C | 开关侧面画中画 |
| Esc | 暂停／继续；设置页返回 |
| R | 确认后重新挑战 |

失焦自动暂停。奖品、抓持差异和设置见 [玩法说明](docs/Game02_ClawMachine/PROTOTYPE_HANDOFF.md)；本轮变更与验证结论以当前状态为准。

### C04 旋转玩偶圆盘

目标是云朵兔。瞄准时，三件奖品随圆盘以每秒 14 度旋转；用 WASD 定位爪子，按住右键环绕观察、滚轮缩放。按 Enter 时圆盘刹停，奖品保留当前位置进入真实抓取、运输和投放。抓错对象不计奖。暂停冻结圆盘，失败后重试恢复同 seed 的初始布局，已用尝试次数保留。

## 验证与 Windows 打包

工具需要 Python 3.10+；实际测试和构建另需有效 Unity 许可、Windows Build Support 和可用图形设备。先保存并关闭同一工程的 Editor，再在工程根目录执行（Editor 路径按本机安装位置修改）：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_game_delivery.py --game claw
```

交付流程与结果要求见 [GameDelivery.md](Validation/GameDelivery.md)，画面采集见 [VisualCapture.md](Validation/VisualCapture.md)。只有真实 Unity 测试通过后才构建 Windows 包；静态或托管测试不代替实机验收。

`Assets/`、`Packages/`、`ProjectSettings/` 及 `.meta` 是必要工程文件；`Validation/` 保存状态和证据。不要把 Unity 工程直接复制成 Neuralife 的 QML 游戏插件。

## 工程内容

本工程的 `Assets/` 仅导入 `ClawMachine` 和必要的 `BCIShared` 公共资源。继承的推箱子源码、场景、测试、Resources 与 `.meta` 已完整移至工程外的 `../ArchivedSources/Game02-InheritedSokoban-20261116/`，不再出现在 Project、菜单或抓娃娃运行包中。归档保留原始 SHA-256 清单与恢复说明；正式推箱子工程 `../Game01-Sokoban` 不变。

常规 Build Settings 只启用抓娃娃场景；`Tools/run_source_checks.py`、`Tools/run_delivery.py` 和截图入口仅检查或构建本游戏，不再要求推箱子地图。
