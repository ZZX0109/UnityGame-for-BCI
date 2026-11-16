# 当前验证状态 · 2026-11-16

本表对应 `codex/optimize-game03` 的最终运行源指纹 **`7269772a0cd86ec5c372569fa881ad345123fd8f618ddd5d62bd6f1fbf45038a`**，包括493个 `Assets/`、`Packages/`、`ProjectSettings/` 文件。原生Prepare、EditMode、PlayMode、Windows构建和最终15图采集前后指纹一致。最终Git提交及源码包由源码交付manifest记录，不能把构建时Git基线当作最终提交。

本轮完成四车道道路及配套交通几何、转弯边界、文字深度遮挡与店招可读性修复，保持S01–S03及保护式六阶段信号。许可证可用，自动清理任务已由用户暂停。完整说明见 [街道修复记录](Review-20261116-Street.md)。

| 验证项 | 本轮实际结果 | 原始证据 |
| --- | --- | --- |
| 公共工具与源码检查 | **69/69工具测试通过**；字体覆盖、项目、规则及完整性检查通过 | `BuildOutput/Review-20261116-StreetSourceFinal2/` |
| Unity 2022.3.62f3c1 EditMode | **96/96通过**，零失败、跳过和不确定 | `BuildOutput/Review-20261116-StreetFinal2/Evidence/EditMode-results.xml` |
| 原生PlayMode | **19/19通过**，零失败、跳过和不确定；包含三情境实际过街、四车道、S03遮挡站位、物理边界、文字像素遮挡与招牌视域 | 同目录 `PlayMode-results.xml` |
| 独立Windows构建 | **完成**，Prepare、两组测试和Build均自然退出0；运行包清单与ZIP完整性核验 | `BuildOutput/Review-20261116-StreetFinal2/` |
| 三情境同机位画面 | **15张已采集并逐张检查**：出生点、路沿左右观察位、横道中段、目的地；捕获XML **1/1通过**、Editor自然退出0，同源绑定 | `BuildOutput/Review-20261116-StreetFinalVisual2/` |
| 四分辨率运行画面 | 实际 **1280×720、1920×1080、2560×1440、3840×2160** PNG；逐图检查当前S02游戏HUD，标题、目标、暂停、教程与操作栏在屏内，未见中文截字或教程与底栏重叠；尺寸、CRC和SHA核验通过 | `BuildOutput/Review-20261116-StreetAudit6002/Runtime/`；`root-independent-review.json` |
| 1080p高档10分钟性能 | **通过本轮数值目标**：600.004秒、118598帧，平均 **197.662 FPS**，P95 **6.8938ms**、P99 **8.8789ms**、最大 **21.1706ms**；Player自然退出0 | 同目录 `audit-summary.json`、`frames.csv` 及父目录 `process.json` |
| 重开／切换资源趋势 | 已执行20次真实重开及20次情境切换，记录21份稳定点样本；各同情境组GameObject、Behaviour、Material、Mesh、Texture首末增量均0；原生已分配内存约增18.7–70.7KB，托管约增53.2–98.3KB | 正式运行summary内 `memorySamples`；有限样本不标全面无泄漏PASS |

性能来自实际 **NVIDIA GeForce RTX 4060 / Direct3D11 / 1920×1080 / High** 独立Player，预热10秒。S02累计真实活动约610秒，包含两次自然超时重开；原始帧记录包含重开与CSV记录开销。根代理独立复算118598行CSV、连续帧号、全部1080p尺寸和P95，与summary一致。

Windows包：[StreetCrossing-Windows64.zip](../BuildOutput/Review-20261116-StreetFinal2/StreetCrossing-Windows64.zip)，27,697,893字节，SHA-256 **`5bec6b8a164c4d99b36f45b66b574b81bc87e90430a3b89c1d81b1679220b94c`**。源码与独立证据归档的最终路径、SHA及提交见本轮修复记录和各自交付manifest；不修改原生build-manifest字节来补写后续测试。

## 证据边界与待完善项

- 外部娃娃机Unity Editor（PID 29868）在后台运行，性能测试未宣称无背景负载或GPU完全独占。采样期间没有再启动本轮其他Editor、Player、编译或压缩。
- 本机GPU FrameTiming及GC分配计数不可用，原值为不支持或`-1`。帧时是实际运行帧间隔，不是GPU单项耗时；不能推导零GC。
- 四分辨率截图覆盖当前S02初始游戏HUD，15个机位覆盖三情境指定视角；尚未完成全部菜单、设置、按钮及每个游戏状态的人工操作检查。真实行走原生测试不标为全面人工试玩。
- 场景保留原创低多边形模块风格，部分路面高光、远处细节与S03原遮挡设施的生活化造型仍可打磨；本轮没有宣称照片级真实感或全部美术问题消失。
- 一秒smoke用于分辨率、真实重开／切换与采样流程检查，性能结果为false、自然退出2，保留原结果，不与正式600秒通过混用。
- 旧的85/18及96/18等测试、指纹变化的诊断截图、关闭超时与失败日志均属历史，不继承到当前版本。上一版状态保存于 [历史快照](History/CurrentStatus-before-FinalReview-20261116.md)。
- 只将本游戏场景列入构建启动场景，保留分支继承的推箱子源码；未宣称所有旧Resources资产均已从包中剥离。没有覆盖Neuralife正式宿主或合并main。

[多样性与难度设计稿](../docs/DESIGN_DIVERSITY_AND_DIFFICULTY.md) 是下一轮候选方案，九任务、反向与返程、独立任务成绩及新交通规则均**尚未实现**，不计本轮PASS。
