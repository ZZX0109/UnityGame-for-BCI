# 过马路最终验收 · 2026-11-16

当前运行源码指纹为 `7269772a0cd86ec5c372569fa881ad345123fd8f618ddd5d62bd6f1fbf45038a`，范围是 `Assets/`、`Packages/`、`ProjectSettings/` 的原始文件字节。下表均为这一版本的新证据；旧 `StreetDelivery`、`StreetFinalDelivery*`、`StreetFinal` 和前几组截图保留为历史诊断，不继承其 PASS。

| 当前检查 | 实际结果 | 证据目录（BuildOutput 下） |
| --- | --- | --- |
| 完整静态与工具检查 | 5 条命令全通过，Tools **69/69**；字体、工程、规则和 Street 完整性通过 | `Review-20261116-StreetSourceFinal2/` |
| Unity 2022.3.62f3c1 原生 EditMode | **96/96**，零跳过 | `Review-20261116-StreetFinal2/Evidence/` |
| 同版本原生 PlayMode | **19/19**，零跳过；包含三情境实际输入过街、四车道、遮挡调整、几何与招牌像素可见性 | 同上 |
| Windows x64 构建 | 成功；Prepare、Edit、Play、Build 均自然 exit 0，前后同源 | `Review-20261116-StreetFinal2/Windows/` |
| 实际路线画面 | 捕获测试 **1/1**、15 张原始 PNG，原始图逐张检查；Editor 自然 exit 0，捕获前后同源 | `Review-20261116-StreetFinalVisual2/` |
| 独立 Player 预检 | seconds=1，4 实际分辨率及 20 次切换／重开完成；自然 **exit 2** 是预期，`performancePassed=false` | `Review-20261116-StreetAuditSmoke2/` |
| 独立 Player 正式性能 | 10 秒预热后连续 **600.0040196 秒**、**118598 帧**，自然 exit 0、`performancePassed=true` | `Review-20261116-StreetAudit6002/` |

## 本轮修复和实际画面核对

道路为双向四车道、总宽 13.2 米。车辆路线、停止线、信号、观察目标、保护区、人行道与出生／目的地区域已同步；三情境的 seed 保持稳定。转弯路肩按最大车身扫掠生成，左转曲线提前完成，预留退出点仍按真实位置计算；树篦与转角楼的可见几何和碰撞边界已修正。

世界文字使用深度测试材质，动态字体图集重建后同步纹理。主便利店招牌和英文牌面整体移到雨篷上方，缩短雨篷投影，中文中心最终为 y=3.78；75° FOV 的真实渲染回归要求整字在画面内，开关真实雨篷后字形可见像素至少保留 99%。删除雨篷下冗余的绿色“目的地”浮字，保留 HUD 目的地及绿色地垫；底部操作说明改为 Paper 底栏上的 18 号 Ink 字，避免白色斑马线掩掉白字。

所有 15 张路线截图已实际逐张查看：三张出生点无 cyan shader 占位色，三张目的地的中英文主牌完整，绿色半字消失；底栏在横道白线上可读，与提示卡保留间隔。可见公交站、遮挡提示、行人信号和沿街店牌未发现其它确定半字。Shader 异步编译须稳定后再采集。根代理还独立查看了关键路线图及正式 Player 的四张分辨率图。

这属于文字与几何可读性核对。街景仍是原创低多边形模块外观，不等于照片级写实，也不证明任意镜头和所有动画帧均无美术问题。S03 遮挡设施的生活化造型、路面强高光等美术建议留到下一轮。

## 正式 600 秒与资源观察

环境为 **AMD Ryzen 9 9950X、NVIDIA GeForce RTX 4060、Direct3D 11、High、1920×1080**。正式采样开始约 UTC 10:28:28，结束约 UTC 10:38:28；实际 Player PID 42132 自然退出。外部 Game02 Editor PID 29868 作为既有并发基线保留，未操作，不能称完全独占环境。

- 平均 **197.662 FPS**；P95 **6.8938 ms**、P99 **8.8789 ms**，最大 **21.1706 ms**。
- 原始 CSV 118598 行帧号连续，全部为 1920×1080；根代理独立复算与 summary 一致。阈值沿用平均至少 60 FPS、P95 不超过 20 ms，未为结果调整。
- S02 实际活动累计 610.007 秒（含预热）；原 300 秒训练超时通过公开 `StartPractice` 自然重开 2 次，全部帧保留，未剔除重开峰值。
- GPU 帧时功能与逐帧 GC recorder 在此构建不可用，报告为 `not_supported_or_unavailable`／`-1`。不能由此推断 GPU 帧时或“零 GC”。主线程 recorder 可用。
- 正式四张 PNG 实际为 **1280×720、1920×1080、2560×1440、3840×2160**；逐 chunk CRC、尺寸和 SHA-256 已核验，未缩放。根代理逐张确认初始 S02 的标题、观察、暂停、目标、教程和底栏在屏内。范围不扩展到所有菜单状态。

20 次真实切换／重开另取 21 个 GC 后样本，按相同情境比较；不能直接比较三种街区的不同对象数。

| 情境 | 同情境样本数 | GameObject / Behaviour / Material / Mesh / Texture 首末差 | Unity allocated 首末增量 | Managed 首末增量 |
| --- | --- | --- | --- | --- |
| S01 | 7 | 全部 0 | 70,672 bytes | 98,304 bytes |
| S02 | 7 | 全部 0 | 24,232 bytes | 98,304 bytes |
| S03 | 6 | 全部 0 | 18,680 bytes | 53,248 bytes |

各组 reserved memory 稳定。这是有限 20 轮的资源趋势观察，包含引擎缓存，不据此标记一般意义的“无内存泄漏”。原始样本、同情境比较、独立复算和 run binding 保留在正式审计目录。146 个 Windows 清单文件逐项 SHA 匹配，exe 与实际审计进程记录匹配；原生 `build-manifest.json` 字节未被修改。

## 交付与边界

- 当前 Windows 包：`BuildOutput/Review-20261116-StreetFinal2/StreetCrossing-Windows64.zip`，SHA-256 `5bec6b8a164c4d99b36f45b66b574b81bc87e90430a3b89c1d81b1679220b94c`。
- 当前证据包：`BuildOutput/Review-20261116-EvidenceDelivery/StreetCrossing-Evidence-20261116.zip`；包含本报告引用的新原始 XML、日志、图片、CSV、summary、process、source checks 与 run binding，SHA 以同目录清单为准。
- 源码包：最终提交后在 `BuildOutput/Review-20261116-SourceDelivery/` 生成，含 commit、分支、原始文件 SHA 和相同运行源指纹；不含 Library、Windows 负载和历史 BuildOutput。

本轮仍实现 S01／S02／S03 三个情境。[多样性与难度建议](../docs/DESIGN_DIVERSITY_AND_DIFFICULTY.md)中的九任务仅为下一轮讨论稿，未实现、不计验收，也不替代当前规格。星级中一星不可达等设计问题仅记入该讨论，不修改当前冻结规则。用户视觉确认、完整人工 Windows 过街与视频录制仍与本轮自动化和助手图像审查分开；没有覆盖正式宿主，没有 push 或合并 main。
