# 第一人称过马路

Unity **2022.3.62f3c1 中国版**，目标 Windows x64。S01–S03 通过连续行走、左右观察和行人信号判断完成过街。

2026-11-16 四车道版本通过 **96 项原生编辑器测试、19 项运行测试和 69 项工具测试**，完成 Windows 构建、三情境15个同机位截图及四分辨率运行截图。RTX 4060、1080p高档连续600秒平均 **197.66 FPS**、P95 **6.89毫秒**；另有娃娃机Editor后台运行，未宣称GPU完全独占。详见 [当前状态](Validation/CurrentStatus.md) 和 [街道修复记录](Validation/Review-20261116-Street.md)。

运行包：[StreetCrossing-Windows64.zip](BuildOutput/Review-20261116-StreetFinal2/StreetCrossing-Windows64.zip)。关于后续九任务、分级提示和返程流程的 [设计建议](docs/DESIGN_DIVERSITY_AND_DIFFICULTY.md) 尚未实现，本轮仍为S01–S03三个情境。

## 打开与操作

在 Unity Hub 用上述版本打开本工程，等待导入后打开 [StreetCrossingScene.unity](Assets/StreetCrossing/Scenes/StreetCrossingScene.unity)，按 Play 后从菜单选择情境。资源核对入口为 `Street Crossing → Prepare → Validate Delivery`。

| 键鼠 | 作用 |
|---|---|
| WASD / 鼠标 | 连续行走 / 环顾街道 |
| Shift | 按住快步走 |
| E | 查看附近可见的提示牌 |
| Esc | 暂停／继续；设置页返回 |
| R | 确认后重开当前情境 |

在路沿主动观察两侧车流和行人灯，再沿斑马线到达目的地；暂停或失焦会冻结世界。情境规则见 [设计说明](docs/Game03_StreetCrossing/DESIGN_AND_DEVELOPMENT.md)，本轮变更与验证结论以当前状态为准。

## 验证与 Windows 打包

工具需要 Python 3.10+；实际测试和构建另需有效 Unity 许可、Windows Build Support 和可用图形设备。先保存并关闭同一工程的 Editor，再在工程根目录执行（Editor 路径按本机安装位置修改）：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_game_delivery.py --game street
```

交付流程与结果要求见 [GameDelivery.md](Validation/GameDelivery.md)，画面采集见 [VisualCapture.md](Validation/VisualCapture.md)。只有真实 Unity 测试通过后才构建 Windows 包；静态或托管测试不代替实机验收。

`Assets/`、`Packages/`、`ProjectSettings/` 及 `.meta` 是必要工程文件；`Validation/` 保存状态和证据。不要把 Unity 工程直接复制成 Neuralife 的 QML 游戏插件。
