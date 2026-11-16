# 当前验证状态 · 2026-11-16 原生复验

分支 `codex/optimize-game01`。本机 Unity **2022.3.62f3c1** 的许可已恢复；此前许可阻塞、外部自动清理中断和真实失败日志均保留，不能与最终通过证据混用。完整过程见 [本轮原生报告](Review-20261116-NativeSokoban.md)。

| 验证 | 本轮实际结果 | 证据 |
| --- | --- | --- |
| Unity EditMode | **80/80，0失败、0跳过** | `BuildOutput/Review-20261116-SokobanNative/EditMode-RuntimeAudit3/` |
| Unity PlayMode | **48/48，0失败、0跳过**，包含八关完整行走/推箱/通关存档与会话保护 | `BuildOutput/Review-20261116-SokobanNative/PlayMode-RuntimeAudit3/` |
| 四实际分辨率活动HUD截图 | **1280×720、1920×1080、2560×1440、3840×2160均与原生PNG像素匹配** | `BuildOutput/Review-20261116-SokobanRuntimeAudit-Final1/` |
| 当前最终源八关 initial/top 16图 | **1/1 NUnit通过、16张PNG完整且前后d779不变**；Editor关闭超时，原脚本非零退出保留，另独立验证图像数据 | `BuildOutput/Review-20261116-SokobanVisual-Final/` |
| Windows x64 | **实际构建成功**；正式审计Player自然exit0 | `BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2/` |
| 1080p High 连续600秒 | **118556帧 / 600.002秒，平均197.59FPS，P95 6.97ms，P99 9.00ms，最大15.66ms**；达到平均≥60FPS、P95≤20ms指标 | 同正式审计目录的 `audit-summary.json`、`frames.csv`，独立CSV复算吻合 |
| 20次真实重开与20次切关 | **21份GC后资源样本已记录**；按同关分组对象/组件/材质/网格数量净增均0；不推断全面无泄漏PASS | `memorySamples` 与 `independent-verification.json` |
| Python工具 / 字体 / 静态 | 工具 **38/38**；字体覆盖、文件/GUID/importer/catalog检查通过 | 本轮命令输出；不替代原生测试 |

最终运行源指纹：`d779000b98d91ad8e5a5e243afcf58cfa6499d4f44fbb9171335bf38c5aaf98e`。原生XML与Windows build-manifest绑定此指纹；运行包记录构建时基线commit `989944e`，最终源码提交以源码包内`source-manifest.json`为准，二者的运行源字节指纹相同。正常启动不会创建审计入口；仅传`--bci-audit-output <绝对空目录>`启用，使用临时存档与设置。

性能条件与限制：NVIDIA GeForce RTX 4060、AMD Ryzen 9 9950X、Direct3D11，最大地图L08的静止活动局面，10秒预热后600秒；VSync=0、targetFrameRate=-1，逐帧记录包括仪器CSV写入开销。外部Game02手动Unity Editor PID29868全程存在，119次前/中/后进程快照记录该负载，**不是完全独占GPU或无背景负载的测试**。主线程计数器可用；GPU专属帧时和逐帧GC分配计数不可用，明确记unsupported/-1，不能称GPU帧时通过或零分配。

视觉边界：720p次级中文约9–11px偏小；远距离箱号与偏亮地面高光列P2局限。截图无明显HUD越界、关键箱体遮挡或世界文字穿墙；菜单按钮/设置面板的四分辨率人工截图、完整键鼠人工八关试玩尚未覆盖，**不标全面视觉或人工玩法PASS**。21份样本中L08纹理45→48→48，其他同关纹理计数不增；原生分配同关差值约0.03–0.11MiB，可能含缓存与审计数据，仍仅为趋势记录。

旧状态保存于 [Native复验前快照](History/CurrentStatus-before-NativeReview-20261116.md)；更早证据仍见 `Evidence-Optimization-20261116/`、`Validation/History/` 与各Attempt目录。八关地图、解锁、阈值、存档key与艺术馆风格保留，未修改正式宿主。

最终Windows ZIP：`BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2.zip`（78,441,511字节），SHA256 `392c1dc9302be30f41c7417b423799357412a72adf53d6f58d095968ccea347e`。包内附原生XML、正式审计原始CSV/PNG/进程记录与最终16图；`delivery-manifest.json`覆盖附加证据，原生`build-manifest.json`保持不变。
