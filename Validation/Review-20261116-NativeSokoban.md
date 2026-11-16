# Game01 当前原生验收结果 · 2026-11-16

本机许可已恢复，最终运行源为 **`d779000b98d91ad8e5a5e243afcf58cfa6499d4f44fbb9171335bf38c5aaf98e`**。真实 Unity EditMode **80/80**、PlayMode **48/48**，0失败、0跳过；包含八关完整行走/推箱/通关存档。Windows x64构建成功，最终独立包完成 **600.002秒、118556帧，平均197.59FPS、P95 6.97ms**。四个实际分辨率PNG均匹配，最终同源八关initial/top共16图已完整核验。

上述通过有明确边界：性能全程存在外部Game02 Editor背景负载；GPU专属帧时和逐帧GC分配计数不可用；资源仅记录21份趋势样本，不声称全面无泄漏。菜单按钮/设置页四分辨率截图及完整人工键鼠八关试玩未覆盖。最终16图的NUnit测试1/1通过，但Editor关闭超时，原始脚本非零退出记录保留，不能称整条采集命令自然成功退出。

旧cd6/4808/ea08指纹、无Library、待许可、Shader待重测等仅属于过去时段。原逐步盘点全文保存于 [历史进度快照](History/Review-Sokoban-Native-Progress-20261116.md)，不代表上述最终状态；原始日志与XML没有改写。

## 可交付文件与绑定

- 工程：`C:/Users/Ait/Desktop/Neuralife/Unity/Game01-Sokoban`，分支`codex/optimize-game01`，原生测试与运行包记录构建时基线`989944e4346115660a9278e2e1a368f5321b04de`。最终源码包的`source-manifest.json`记录本轮提交，二者以相同d779运行源字节绑定。
- 最终EXE：`BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2/FirstPersonSokoban.exe`。
- ZIP：`BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2.zip`，**78,441,511字节**；SHA256 **`392c1dc9302be30f41c7417b423799357412a72adf53d6f58d095968ccea347e`**。
- EXE SHA256：`059c313352892718258a120b87bdc6fc7c23ce67181084abe14f86f204f5dddb`。`run-binding.json`另绑定Runtime DLL和原生build-manifest的SHA256。
- 原生测试/版本/源绑定：`BuildOutput/Review-20261116-SokobanNative/FinalEvidence-RuntimeAudit2/`，实际轮次`EditMode-RuntimeAudit3/`与`PlayMode-RuntimeAudit3/`。
- 正式独立包数据：`BuildOutput/Review-20261116-SokobanRuntimeAudit-Final1/`，包含`audit-summary.json`、118556行`frames.csv`、四档原生PNG、前中后`concurrent-processes.jsonl`、`run-binding.json`、Player日志及独立复算结果。
- 最终八关16图：`BuildOutput/Review-20261116-SokobanVisual-Final/Screenshots/`，全部2728×1867。NUnit、原始日志、关闭超时、原脚本failure、`post-shutdown-verification.json`与逐图SHA清单位于其父目录。

ZIP包含可运行文件、原生测试证据、正式审计原始记录与最终16图。原生`build-manifest.json`保持字节不变，继续匹配运行时run-binding；ZIP共172份文件；`delivery-manifest.json`覆盖除其自身外的171份文件，包括附加证据。ZIP完整性及全部原生manifest文件SHA均已验证。独立只读复核确认234个Assets/Packages/ProjectSettings文件的实算指纹与XML、包及run-binding一致。

## 真实测试内容

八关Walkthrough加载实际PuzzleScene，以CharacterController逐格行走、实际射线选箱、推箱动画和通关存档完成路线，不以传送或直接修改BoardState清关。最少推数仍为 **1/7/22/12/4/10/5/32**。其余回归覆盖菜单继续保留局面/Undo/位置/时间/距离、动画回滚、取消确认、首次教学/死锁、辅助开关、revision全通判断及搜索取消。

新增原生图像测试使用真实RenderTexture像素验证：文字正面可见、墙遮后消失、移墙恢复、背面不显示镜像、动态font atlas仍绑定正确。真实L08场景还核查所有墙面与箱体标签采用深度材质且箱面编号朝外。普通启动无审计runner、临时存档隔离、真实Restart/切关和结束恢复也有原生回归。静态文件/GUID/importer/catalog、字体覆盖及Python工具38/38通过，但这些不是Unity测试的替代品。

最终16图：`VisualCapture-results.xml`为1/1 Passed且16张PNG完整。Editor PID50748在结果写完、Input System Shutdown及licensing断开后仍滞留超过360秒；只核对本项目/PID后结束，保留`shutdown-timeout.json`和原脚本failed摘要。随后独立调用原工具`check_xml/check_captures`核验真实版本、完整场景/视角、PNG CRC/尺寸、逐图SHA与源指纹前后一致，结果写入`post-shutdown-verification.json`，没有覆盖原失败记录。已目检L01/L08原图及八关initial/top总览，未见原文字穿墙/倒写重叠，中文HUD完整。

## 600秒性能与21份资源样本

NVIDIA GeForce RTX4060、AMD Ryzen9 9950X、Direct3D11、实际High、1920×1080。选择最大L08（160格、77墙）的静止活动局面，预热10秒后连续600秒；VSync=0、targetFrameRate=-1，所有帧保留，含CSV记录开销。不是菜单空场景，也不等同八关人工动态游玩的全时性能。

| 指标 | 实测 |
| --- | --- |
| 连续采样 | 600.0021488秒，118556帧 |
| 平均FPS / 平均帧时 | 197.5926 FPS / 约5.061ms |
| P95 / P99 / 最大帧时 | 6.9726 / 8.9975 / 15.6571ms |
| CPU主线程平均 | 5.0598ms；计数器可用 |
| GPU帧时 / 每帧GC分配 | unsupported / -1；不能以CPU时间替代GPU时间或宣称零分配 |
| 实际backbuffer截图 | 1280×720、1920×1080、2560×1440、3840×2160均逐像素尺寸匹配 |

平均≥60FPS、P95≤20ms及600秒持续指标在本次条件下达到，Player自然exit0。119次前/中/后进程记录均存在外部Game02手动Editor PID29868，无观察到其他新增Unity/游戏进程；这是**有已记录背景负载的本机实测，不是完全独占GPU测试**。原始CSV帧数、全程1080p及P95/P99/max已独立复算吻合。FrameTiming功能未启用或不可用、GC recorder无有效值，明确保留unsupported/-1。

20次真实Restart及20次换关后，含初始基线共21份GC后样本。按相同关卡分组比较，GameObject/MonoBehaviour/Material/Mesh净增均0；L08纹理45→48→48，其他同关纹理净增0。Unity已分配内存同关差值约0.03–0.11MiB、托管差值0–0.094MiB，可能含缓存与审计记录；这只支持本轮趋势观察，不推断无限重开或所有运行过程无泄漏。

## 保留的修复与视觉限制

保留编辑态palette资源清理、非发光模板flags=4、Unity规范材质序列化、世界文字深度/背面剔除与font atlas同步、HUD提示底板及纠正站位辅助fixture。模板`m_LockedProperties`的空字段尾空格是Unity规范序列化，未为消除diff提示改变d779字节。普通游戏输入与原有地图/阈值/解锁/存档key不变；命令行审计才抑制输入、隔离存档并最终恢复退出。

720p的Canvas缩放为2/3：15号副标题实际10px、14号统计说明9.33px、16号控制说明10.67px，属于次级小字P2。仓库没有强制最小字号要求，不虚构全面视觉达标。远距箱号偏小、地面偏亮高光也作为P2保留，艺术馆风格未重做。当前截图未展开菜单按钮/设置页，不能据此标其四分辨率PASS；完整人工键鼠八关试玩仍未运行。

## 历史失败与恢复（非当前状态）

| 阶段 | 当时真实结果与处置 |
| --- | --- |
| EditMode Attempt1 | 78/79；编辑态owner销毁未释放生成材质。新增ExecuteAlways后后续所有权断言通过。 |
| EditMode Attempt2 | 78/79；黑色非发光模板误启用EMISSION。flags=4修正后完整回归通过。 |
| EditMode Attempt3 | 外部自动清理导致exit15且无XML，不记测试通过。旧Bee安全备份及原始日志保留。 |
| PlayMode Attempt1 | 44/45；辅助fixture站在L02朝墙方向。改为可推动东侧并显式验证StanceOffset，不改推动规则。 |
| TextFixed中间版 | 80Edit/47Play、16图1/1与Windows构建通过，源4808，包/图均保留作对照，非最终d779。 |
| RuntimeAudit Smoke1 | 仅1秒预检；首次720请求被启动全屏异步覆盖成4K，如实记录尺寸失败。新增原生窗口等待/有限重试，仍按真实PNG判定。 |
| RuntimeAudit Smoke2 | 四实际尺寸与21样本正确；仅1秒故performance=false/exit2，不记10分钟PASS。 |
| 最终d77916图 | 图像与1/1 XML完整但关闭超时；保留原脚本非零退出与独立图像核验，不伪称自然退出。 |

历史进度快照、旧CurrentStatus、所有Attempt/Smoke原始证据持续保存；没有删除历史源码或改写旧日志。本报告只把当前实际结论放在顶部，历史PASS不自动继承。
