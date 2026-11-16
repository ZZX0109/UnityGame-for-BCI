# 街道画面与四车道修复（2026-11-16）

本轮接续 `codex/optimize-game03`。用户反馈文字穿模、街景缺乏真实感，并明确要求增加车道；因此本轮将原双向两车道改为双向四车道，替代上一版“保持道路宽度不变”的约束。没有增加情境或 BCI 接口。

## 已实施

- 四条真实交通车道，中心为 z=-4.95/-1.65/+1.65/+4.95 米，道路宽 13.2 米；车辆分配、信号灯、停止线、横道、判定区域及左右观察目标同步调整。
- 左右观察分别要求相应方向的两条来车车道均可见；可见的内侧车辆不能替代被遮住的外侧车道。沿用左右观察操作，不增加操作步骤。
- 人行道、出生点、目的地、建筑、设施、背景角色和 S03 遮挡向外移动 3.3 米；出生点 z=-11.3、目的地 z=10.8。三情境原 seed 与其余交通动力参数保留，步行窗口按新距离延长。
- 世界文字改用有深度测试的独立字体材质；字体图集重建时同步更新，避免默认 GUI 字体材质把字画在墙面之前。
- S02 移除重叠雨篷，文字牌与新雨篷、牌面保留明确空间间距。
- 转弯路肩按最大车身包络生成。左转提前在 x=-6 完成转向，路口预留退出点仍为 x=-2.6，避免车尾扫进横道旁的人行道或提前放行。
- 提高树篦至树池顶面之上，镜像转角楼真实网格与碰撞形状，消除邻楼构件穿插。
- Build Settings 仅启用 StreetCrossingScene，产品名改为 StreetCrossing；保留历史推箱子源文件，未删除 Game01 或历史内容。
- 补齐公共 TMP 中文换行资源以及 Screen Capture 内建模块。

## 本轮真实证据

| 项目 | 结果 | 证据 |
|---|---|---|
| Unity 2022.3.62f3c1 原生编辑器测试 | 85/85，通过，零跳过 | `BuildOutput/Review-20261116-StreetDelivery/Evidence/EditMode-results.xml` |
| 实际字体遮挡、四车道车辆、几何边界 | 6/6，通过，零跳过 | `BuildOutput/Review-20261116-StreetNative4/PlayMode-new-results.xml` |
| 清空 Library 后重新导入与资源准备 | 通过，进程返回 0 | `BuildOutput/Review-20261116-StreetNative4/prepare.log` |
| 工具单元测试 | 67/67，通过 | 2026-11-16 本轮实际 unittest 复核 |
| 三情境完整过街、全部 PlayMode | 18/18，通过，零跳过 | `BuildOutput/Review-20261116-StreetDelivery/Evidence/PlayMode-results.xml` |
| 独立 Windows x64 包 | 构建通过，清单中 138 个文件逐一 SHA-256 复核通过 | `BuildOutput/Review-20261116-StreetDelivery/StreetCrossing-Windows64.zip` |
| Game-view 截图 | 采集中 | `BuildOutput/Review-20261116-StreetVisual/`；不继承旧截图 |
| 四分辨率、10 分钟性能、20 次重开／切换内存 | 尚未完成 | 不以测试执行耗时或编辑器 FPS 替代 |

渲染日志确认使用 NVIDIA GeForce RTX 4060 / Direct3D 11。6 项原生测试包含实际 RenderTexture 像素检查：先确认文字可见，再确认不透明墙遮住文字，移开墙后文字恢复；这不是仅匹配 Shader 源码的静态检查。

已测试和构建的源码指纹为 `febebc6d6781c8140da13265c877de89ba30f6ad5d18679008482472aede5b27`，Windows ZIP SHA-256 为 `f3c073809884180b2c69a8bc0814afa84969b3673816f77889f9fa35a1c510a7`。后续独立 Player 验收入口若改变运行源码，须生成新证据和新包，不能直接沿用上述指纹。

前几轮完整 PlayMode 被自动清理工具终止，并同时删除三个项目的 Library；未生成 XML 的运行均不计为通过。用户已确认暂停自动清理，当前使用全新证据目录重跑。被中断的 Test Runner 临时场景已移到 BuildOutput 事故证据目录保存。

`Assets/FirstPersonSokoban` 是项目从同一 Git 仓库分支继承的历史内容，非当前街道运行依赖。本轮仅隔离入口，没有声称所有历史 Resources 资源已经从包体剥离。
