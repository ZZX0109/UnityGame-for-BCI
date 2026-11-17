# Game02 并行开发交接记录（2026-11-16）

本记录限定证据范围，不更新当前状态矩阵。Unity 许可已恢复，本轮已实际完成原生导入、编译和 PlayMode 物理测试；此前无许可、被清理工具中断或缺少结果 XML 的运行不能作为通过证据。

隔离物理实验使用全新独立项目，仅复制 `Assets/Packages/ProjectSettings`，未复制 `Library`。最终真实 Unity/PhysX PlayMode **4/4 通过**，PID 51816 自然退出，exit 0。通过范围仅为已保存的 `ExecutedSource`：C01、C02、C03 对准抓取均真实释放、完整入槽并稳定获奖；C03 偏心抓取仍真实断开关节并触发滑落事件，随后按实际物理归宿判奖。没有使用 Physics/UI 桩。

最终实验显式管理抓爪与当前持物的碰撞对，并按持物几何中心补偿运输释放目标。C03 的水平补偿为 0.055 m，C01/C02 为 0；19 个玩法资产的字节与来源快照完全一致，质量、重心、断裂阈值、seed、布局及判奖体积均未更改。偏心滑落后若奖品仍被闭合几何带入出口，既有策略允许在完整包含、稳定及停留时间均满足时获奖；测试仍严格验证真实滑落和最终物理条件，不能强制最终失败。

稳定保存的证据位于 [ClawIsolated 汇总](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/ISOLATED-REVIEW.md)：

- [原生 4/4 XML](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/ReleaseCenter.xml)、[原生日志](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/ReleaseCenter.log)、[启动参数](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/releasecenter-command.json)、[PID/退出码](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/releasecenter-process.txt)。
- [来源快照指纹](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/source-origin.json)、[实际执行源与 19 个玩法资产指纹](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/ReleaseCenter-source-hashes.json)、[实际执行源目录](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/ExecutedSource)。
- [建议补丁](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/suggested-physics.diff)基于原始来源快照；[补丁基线核验](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawIsolated/suggestion-base-verified.json)确认三文件字节匹配。建议文件省略诊断日志，4/4 不自动覆盖精简建议文件。

共同 TMP 持久化修复另有[独立建议包](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-CommonTMPPatch/PATCH_REVIEW.md)，尚未应用 Game02。当前 Claw 的 bootstrap 仍用 `AddObjectToAsset` 创建旧换行 TextAsset 子资产，缺少两份独立导入的换行 txt。建议包复用 Street 已验证的资源方案；只在 Claw 自己的 Settings 中迁移两引用、移除恰为 `BCI Line Start Punctuation` / `BCI Line End Punctuation` 的两个 classID 49 子资产，其他设置字节不变。[manifest](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-CommonTMPPatch/proposal-manifest.json)包含基线与建议 SHA256；[统一 diff](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-CommonTMPPatch/proposed-unified.diff)可审阅。Street 原生 96/96 EditMode、19/19 PlayMode 的参考 XML 已复制并固定指纹，不能继承为 Claw PASS；Claw 仍需集成后在新进程重导入、重启及原生验证。

此前 [ClawVisual2 的 12 张实际截图及 manifest](C:/Users/Ait/Desktop/Neuralife/Unity/Game02-ClawMachine/BuildOutput/Review-20261116-ClawVisual2/Screenshots/capture-manifest.json)仅用于诊断：主相机 front/left/right 的 pose 相同，未覆盖三种观察角度，且该次运行存在缺字。图片数量不能代表多视角验收通过，也不能据此确认后来字体或相机修改已通过。

记录时，当前 Game02 工作树仍由另一会话修改，Gripper、运输规划、UI、相机和捕获测试均可能不同于隔离快照。不得自动套用建议补丁、覆盖他人修改或把隔离 PASS 继承给当前分支；其他目录中可能继续更新的 XML 也未在本记录中绑定到最新源码。

后续须先冻结并记录最新源码指纹，再绑定该版本完成全套原生测试、30 次抓取与 20 次中断重置、实际视角及界面截图、Windows 构建，以及完整性能采样。本记录未确认这些项目通过，也未修改 `CurrentStatus.md`、README 或当前验收矩阵。
