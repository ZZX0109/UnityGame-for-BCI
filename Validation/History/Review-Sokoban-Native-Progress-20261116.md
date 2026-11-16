# 推箱子 Unity 实测盘点 · 2026-11-16

本文件仅记录本轮原始证据与后续待办，不更新 `CurrentStatus.md`、不代替完整验收，也不把不同源码轮次的通过项拼接为全套通过。

## 当前源码与运行环境

- 工程：`Unity/Game01-Sokoban`，分支 `codex/optimize-game01`。
- 已提交基线：`989944e4346115660a9278e2e1a368f5321b04de`；下述修复尚未提交。
- 当前 `Assets/Packages/ProjectSettings` 源文件指纹：`cd6eb6801a3e482ac3c356cb76cb4818b7f3c1fe8271afe64a9fddfb8172cb51`。该指纹是状态盘点，**尚无与之对应的完整通过 XML**。
- 原始日志确认 Editor `2022.3.62f3c1 (1623fc0bbb97)`、图形设备 NVIDIA GeForce RTX 4060。许可握手警告后确实进入了真实 NUnit 测试，不能再将本轮简单归为“未安装 Editor / 无许可”。
- 启动前核验无本工程 Unity 进程；旧 `Library/Bee` 已安全移动保存至 `BuildOutput/BeeBackup-Review-20261116/`，未删除旧源码。
- 用户随后确认存在自动清理工具。三个工程的 Unity 进程同时结束，三个 `Library` 同时被清除。当前本工程 `Library` 不存在；本报告生成时保持停止，不自动重启测试，等待主任务确认清理工具已暂停。

## 原始实测结果

| 轮次 | 真实结果 | 失败 / 中断 | 原始证据 |
| --- | --- | --- | --- |
| EditMode Attempt1 | 79 项，78 通过、1 失败、0 跳过 | `LabMaterialOwnershipTests.PaletteInitializationIsIdempotentAndOwnedResourcesAreReleased`：编辑态销毁 owner 后生成材质未释放 | `BuildOutput/Review-20261116-SokobanNative/EditMode-Attempt1/results.xml` 与 `editor.log` |
| EditMode Attempt2 | 79 项，78 通过、1 失败、0 跳过 | 所有权用例通过；`LabShaderTests.IncludedMaterialsRetainRuntimeMetallicAndEmissionVariants("LabSurfaceTemplate", false)`：非发光模板误启用 `_EMISSION` | `BuildOutput/Review-20261116-SokobanNative/EditMode-Attempt2/results.xml` 与 `editor.log` |
| EditMode Attempt3 | **无有效测试结果** | 进入 Test Runner 后 exit 15，未生成 XML；与本轮外部清理中断相符，不记通过 | `BuildOutput/Review-20261116-SokobanNative/EditMode-Attempt3/editor.log` |
| PlayMode / 八关完整行走 | 未运行 | 等待可靠的 Editor 运行窗口 | 不提供通过声明 |
| 原生画面捕获 | 未运行 | 不以地图 SVG / 源码图册代替画面 | 不提供截图验收声明 |
| Windows Player 构建 | 未运行 | 应在完整测试通过后构建 | 尚无本轮独立包 |

Attempt2 已写完 XML，并进入 Input System Shutdown 与许可通道断开后仍滞留超过两分钟。只在核对项目路径、PID 4192、XML 与 Shutdown 日志后结束了该进程；记录保存在同目录 `shutdown-timeout.json`。该停止没有改变 XML 中的失败结论。

## 已保留的修复

1. `Assets/FirstPersonSokoban/Scripts/Presentation/LabMaterials.cs` 增加 `ExecuteAlways`。该组件没有 `Update/Awake` 自动生成逻辑；只有显式 `Initialize()` 创建资源。此标记使其已有 `OnDestroy` 清理在编辑态 owner 销毁时也执行。原所有权断言未降低，在 Attempt2 中已实际通过。
2. `Assets/FirstPersonSokoban/Resources/Materials/LabSurfaceTemplate.mat` 明确为非发光：保留 `_METALLICGLOSSMAP`，移除 `_EMISSION`，`m_LightmapFlags=4`（EmissiveIsBlack），发光颜色为黑。Attempt3 的导入后只读检查仍保留这些值；**对应 Shader 用例尚待重测**。
3. `LabSelectionTemplate.mat` 是 Unity 实际导入后的规范化序列化结果，仍保留 `_EMISSION`、金属粗糙度纹理及非黑发光色；没有将其改为非发光。
4. 主任务补入 `com.unity.modules.screencapture=1.0.0` 后，真实 Unity 已更新 `Packages/packages-lock.json`，含 builtin imageconversion 依赖。本工程没有引入 TMP 或创建 `Assets/BCIShared`。

其余 `README.md` 与工程配置的主任务修改保持原样，本盘点未提交或覆盖它们。

## 恢复后的验证顺序

确认自动清理已暂停后，继续持有本工程锁，恢复包导入与真实编译。完整重跑 EditMode；通过后运行排除 `VisualCapture` 的全部 PlayMode，必须包含 `LevelWalkthroughTests` 的八关完整路径，不传送、不直接改棋盘、不跳过失败项。

随后独立运行实际画面捕获，并使用真实通过的 XML、精确 Editor 版本和当前源码指纹绑定 Windows 构建。目标输出为 `BuildOutput/Review-20261116-SokobanWindows/`。若源码或资源继续修复，必须更新相应实测证据；不能使用 Attempt1/2 的部分通过替代最终完整测试。

## 清理暂停后的完成结果（16:10，TextFixed）

用户确认自动清理已暂停后重新导入 Library。EditMode Attempt4、Attempt5 均79/79，PlayMode Attempt2 为45/45，均无跳过。八关真实 CharacterController 行走/推箱/存档路径全部通过。PlayMode Attempt1 的44/45失败是站位辅助测试fixture指向L02墙格；改到可推动的东侧并明确断言StanceOffset，未改地图或规则。

实际16张捕获揭示原font.material把背面编号/墙后字画到箱体前面，已增加独立深度文字shader与palette拥有的字体材质、atlas重建同步，并给HUD提示加浅底板。新增所有权、真实像素前可见/墙遮/移墙恢复/背面不透/atlas同步和L08场景标签集成断言。

最终TextFixed实测：EditMode-TextFixed1 80/80、PlayMode-TextFixed1 47/47、VisualCapture 1/1，均0失败0跳过且Editor自然exit0。八关路径再次全部通过。源指纹 `4808c57b06374fddf6fd3f0ff214253e6f57a1eedc4b12bef4e18f39f6980203` 与真实XML、16张PNG和Windows manifest完全相同。静态校验、字体覆盖与Tools 38/38通过。

新原生证据位于 `BuildOutput/Review-20261116-SokobanNative/FinalEvidence-TextFixed/`；实际原图位于 `BuildOutput/Review-20261116-SokobanVisual-TextFixed/Screenshots/`（16张，2728×1867）；Windows入口 `BuildOutput/Review-20261116-SokobanWindows-TextFixed/FirstPersonSokoban.exe`，build原生exit0；ZIP `BuildOutput/Review-20261116-SokobanWindows-TextFixed.zip`，SHA256 `d385fe28a9597791e248895d14d01b9cf24d270786cc3cfb623e04fc1410d95a`。修复前包和16图保留为对照；最终包应使用TextFixed后缀。

已目检L01/L08原图及八关缩略总览：中文HUD完整、箱后说明与倒写编号不再透出，前面编号正常，提示底板提高对比。此为本轮截图检查，不等同于人工八关试玩、四实际分辨率或10分钟性能验收。后续仅命令行启用RuntimeAudit的验证入口与包将另行记录，不倒改此TextFixed证据。

## 独立包审计入口与预检（17:25，正式600秒尚未运行）

新增仅 `--bci-audit-output <绝对空目录>` 启用的通用GameRuntimeAudit与Sokoban adapter。正常启动不创建runner；审计在场景Awake前切到临时InMemory存档与设置、关闭原始输入/失焦暂停，选最大L08（160格、77墙）真实活动画面；完成或失败恢复原store/全局状态并退出。此场景采样是静止活动局面，非自动解八关。

最终源指纹 `d779000b98d91ad8e5a5e243afcf58cfa6499d4f44fbb9171335bf38c5aaf98e`；原生 `EditMode-RuntimeAudit3` 80/80、`PlayMode-RuntimeAudit3` 48/48，八关真实路径再次通过，新增普通启动无driver/临时store/真实重开/恢复断言通过。新Windows独立包 `BuildOutput/Review-20261116-SokobanWindows-RuntimeAudit2/` 原生构建成功。

Smoke1仅1秒，意在预检，首次720请求被原生启动全屏异步覆盖成4K；如实保留不匹配证据。common随后显式退出全屏、在原生end-frame后有限重试，并要求250ms稳定，仍只以实际PNG像素判定。Smoke2实际四PNG全匹配1280×720、1920×1080、2560×1440、3840×2160，通过PNG CRC/尺寸检查；720使用2次请求，其余各1次；21条GC后内存/对象样本记录20次真实Restart与20次换关。

两次Smoke均只测1秒，`performancePassed=false`、exit2符合预期，绝不记10分钟PASS。最终600秒须另起全新目录，并记录其他Unity/Player负载。当前Release Player主线程recorder可用；GC recorder及GPU FrameTiming功能不可用，明确记录-1/unsupported，不得宣称零分配或以CPU时间冒充GPU时间。
