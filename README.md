# 第一人称三维推箱子

Unity **2022.3.62f3c1 中国版**，目标 Windows x64。八关空间规划游戏，使用 WASD 和鼠标操作。

2026-11-16 本机许可已恢复：真实 Unity EditMode **80/80**、PlayMode **48/48**（含八关完整行走）通过，Windows x64包已构建。四个实际分辨率HUD截图与连续600秒1080p High采样已完成：平均 **197.59FPS**、P95 **6.97ms**；测试有已记录的外部Unity编辑器背景负载。完整证据与视觉/CPU/GPU限制见 [当前状态](Validation/CurrentStatus.md) 和 [原生验收报告](Validation/Review-20261116-NativeSokoban.md)。菜单按钮与设置页四分辨率截图未覆盖，不声明全面人工验收通过。

## 打开与操作

在 Unity Hub 用上述版本打开本工程，等待导入后打开 [PuzzleScene.unity](Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity)，按 Play。仅资源确实缺失时使用 `First Person Sokoban → Setup Missing Game Assets`。

| 键鼠 | 作用 |
|---|---|
| WASD / 鼠标 | 行走 / 转动视角 |
| E | 对准相邻箱子正面，推动一格 |
| Z | 撤销上次推动 |
| R | 连按两次确认重置 |
| Esc | 暂停／继续；设置页先返回上一层 |

失焦自动暂停，返回后手动继续。详细关卡与人工检查见 [八关说明](Validation/LevelRedesign-20261113.md) 和 [测试清单](Validation/ManualTest-20261112.md)。

## 验证与 Windows 打包

工具需要 Python 3.10+；实际测试和构建另需有效 Unity 许可、Windows Build Support 和可用图形设备。先保存并关闭同一工程的 Editor，再在工程根目录执行（Editor 路径按本机安装位置修改）：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_delivery.py
```

交付流程在真实 Unity 测试通过后生成新的 `BuildOutput/Delivery-*`；静态或托管测试不代替实机验收。画面采集见 [VisualCapture.md](Validation/VisualCapture.md)。运行包中的 EXE、Data 目录、DLL 和字体许可须一起保留。

`Assets/`、`Packages/`、`ProjectSettings/` 及 `.meta` 是必要工程文件；`Validation/` 保存状态和证据。不要把 Unity 工程直接复制成 Neuralife 的 QML 游戏插件。

## 本轮本机交付

可运行目录：`BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2/FirstPersonSokoban.exe`。EXE、Data、DLL、字体许可和Validation目录一起保留；旧TextFixed包与截图保留作对照，最终使用RuntimeAudit2包。

普通启动按上表操作。独立包仅在显式传入 `--bci-audit-output <绝对空目录>` 时启用审计，默认预热10秒并采样600秒；`--bci-audit-seconds 1`只可用于短预检，不能算性能通过。审计临时隔离存档、禁用键鼠命令/失焦暂停，完成后恢复状态并自动退出。四分辨率直接捕获真实像素，不缩放证据图；20次重开资源仅记录同关趋势，不作全面无泄漏承诺。

720p次级说明文字偏小、远距箱号及地面偏亮高光属于已记录的P2视觉局限。GPU专属帧时与逐帧GC分配计数器不可用；本轮报告的是实际帧时间与可用的CPU主线程数据，不能把缺数据当零成本。

本轮完整ZIP：`BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2.zip`（含运行文件、原生XML、600秒CSV/四档PNG和最终八关16图）；SHA256见[当前状态](Validation/CurrentStatus.md)。最终16图的XML与源一致性核验通过，但Editor关闭超时，原始非零退出与独立图像核验均保留。
