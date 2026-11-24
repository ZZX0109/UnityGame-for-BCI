# 当前验证状态

2026-11-23 · 建模整体精细：真实行人（70 MB 原 FBX 精简为 4 MB / 4 动画）、加密松树、错缝混凝土铺装、无菱形灰泥立面、程序化渐变天空均已接入 S01–S03；EditMode 147/147。PlayMode 当前 `39 passed / 5 failed / 3 skipped`（维护门碰撞 ×2、S03 转弯包络、任务入口、VR 菜单），**未全绿**；未重新打包，未做完整游玩/帧率验收。证据见 `ArtSource/Game03VisualUpgrade/RESULTS.md` 与 `Results/visual-upgrade-test/overall-refined-S01-*.png`。下面 2026-11-20/17 的 34/34 是历史运行快照，不代表当前工作树。

2026-11-20 11 时 · 课程观察时效修复。Windows 键鼠包与 5 项课程专项、9 项兼容回归见 BuildOutput/CourseRefresh-20261120-1110/。更新版 PCVR 在 PICO-PCVR-20261120T033812599，已验签 APK 在 PICO-Android-20261120T033842199。PICO 头显未实测。

2026-11-20 · 六阶段课程、GitHub 街景模型与街道几何已经集成。[设计与出处](../docs/DESIGN_DIVERSITY_AND_DIFFICULTY.md)。本轮原生证据位于 BuildOutput/StreetUpgrade-20261120-095425，edit.xml 为 147/147，regression.xml 为 28/28（含旧九任务的 27 组车流配置），release.xml 为最终资源快照专项 16/16。Windows 普通包在该目录的 Windows/；PCVR 为 PICO-PCVR-20261120T024044015，已验签的 ARM64 APK 为 PICO-Android-20261120T024114650。三种包均检查了路锥署名。未在头显测量帧时或做实际过街试玩。

## 2026-11-17 九任务记录

三个街区、九任务、每任务A/B/C三配置、真实目的地、渐进辅助与反向/往返已实现。Final2运行版指纹 **`873d40206c5f0711c1053f319fb317ced75ac075adffcab39579ffa5f62c7156`** 的原生、实际行走、Windows、路线图、UI gallery与正常时间600秒均已完成。源码可复现收尾已完成：交付源码指纹 **`141b2c1e341a2c75150cf742c8a58ffd24289fa5413a40aa84ef5a2f6e09b877`**。Root只移除两个历史测试末尾闭合大括号后的各一个CR（无代码token/行数变化），生产源码、资产、包/设置及原146 Windows负载保持字节相同。完整147/34、27配置行走及600原始记录仍属于运行快照873d，当前141b另外通过受影响原生Edit9/9、Play1/1及全SourceChecks5命令/69；两组不相加、不伪称全套在不同运行版本重跑。桥接及原测试字节保存在`BuildOutput/Missions-20261117-LFVerification/normalization-bridge.json`。

## 本轮 Final2 实际结果

| 验证 | 实际结果 | 原始证据 |
| --- | --- | --- |
| Prepare / EditMode / PlayMode / Build | 全部自然退出0；**147/147 EditMode、34/34 PlayMode**，源前后一致 | `BuildOutput/Missions-20261117-Final2/Evidence/`、原Windows build-manifest |
| 九任务×三配置 | **27配置原生自动行走全部完成**，含反向/往返及判定；测试timeScale=3，不是人工试玩 | 同轮PlayMode XML/日志 |
| 原运行版源码检查 | 5命令全部成功，Tools **69/69**、源绑定873d；141b收尾全SourceChecks5命令/69再次通过 | `BuildOutput/Missions-20261117-SourceFinal/` |
| 15路线图 | XML1/1、15原图、Editor自然0、源873d；逐图QA0阻塞/2语义P2 | `BuildOutput/Missions-20261117-RouteVisual-Final2/visual-review.json` |
| 短smoke / UI gallery | 1秒smoke自然exit2、performance=false为预期；gallery15张1920×1080核验，QA0阻塞/1上下文P2 | `BuildOutput/Missions-20261117-Smoke-Final2/` |
| 正式四分辨率 | 720p/1080p/1440p/4K真实PNG全部匹配；初始S02-T1/A正常字号HUD逐张QA0阻塞/0新增布局P2 | `BuildOutput/Missions-20261117-Audit600-Final2/Runtime/visual-review.json` |
| 正式600秒 | 自然exit0，**600.0005212秒、510518帧、850.8626FPS、P95 1.5741ms、P99 2.0734ms、最大6.7135ms**；High1080/RTX4060/timeScale1 | 同正式目录summary/CSV/process/`root-csv-review.json` |
| 资源趋势 | 21总样本含初始基线；比较其后20重建、3街区各T1/variant0，7/7/6同组五类对象首末增量0 | 正式目录`same-mission-resource-review.json`，不作通用无泄漏PASS |
| LF归档桥接 / 证据包 | **已完成双指纹桥接、原生专项9/1、源码69复查；Evidence逐文件SHA/CRC/前后inventory一致** | `BuildOutput/Missions-20261117-LFVerification/`及独立Evidence |

新版Windows运行ZIP为`BuildOutput/Missions-20261117-Final2/StreetCrossing-Windows64.zip`，27,745,862字节，SHA256 `5131f6f9377ba0c3f856b7cc2d2b1caee7d773084bdd8161a7105425d09459a3`。146负载SHA、EXE进程绑定与原build-manifest均由本轮run-binding核验，后续测试LF归一不改变运行源码/资产/Windows负载字节。

## 实测范围与三个P2

- 新27配置测试在timeScale=3下使用真实CharacterController、原步行/观察/交通/判奖逻辑，不直接写成功flag；旧三Acceptance及正式600为timeScale1。PersistMissionProgress=false/MemoryStore隔离真实成绩，不称27次人工试玩。
- 正式600独立于gallery，S02-T1静止玩家/动态交通，WorldTime610.0128秒含10秒预热、观察活动累计609.9728秒。软提醒没有硬超时重开，timeoutRestarts=0正确；CSV由Root独立复算吻合。GPU帧时和逐帧GC分配计数unsupported/-1，不是零GC。
- 资源仅比较20重建：S01/S02/S03各7/7/6样本，GameObject/Behaviour/Material/Mesh/Texture及reserved首末增量0；allocated+63592/+26832/+4696B，managed+266240/+237568/+163840B。初始baseline不混入上述增量；范围不是九任务全部内存，也不是无限重开无泄漏结论。
- 路线P2：中间路点显示最终店名容易被当作沿途店牌；未转头与真正遮挡都提示调整站位，需后续细分。实体书店x9.6、咖啡店x16.4与中央便利店名称正确。终点为贴门近景且Canvas隐藏，不是全店面/结果页/人工通关。
- Gallery P2：三张菜单仅滚动底部，缺固定当前所选任务摘要，不能由末个T3说明尾部确认当前选择；不是已证实的逻辑错误，不算完整任务卡检查。四分辨率仅初始S02-T1/A正常字号HUD，不覆盖全菜单/大字号/全部状态。三项P2均未阻断本轮运行验收，源码不为其扩改。
- 原Native summary/build-manifest的externalUnityEditorBaselinePid29868是复制旧模板字段，不是本轮观测；原字节不改。正式Player并发以新process/monitor为准，只描述观察范围无其它Unity/Player/编译，不声称全OS GPU完全独占。

完整报告见 [九任务验收](Review-20261117-NineMissions.md)，操作见 [README](../README.md)。Full1及2026-11-16结果不继承本轮PASS；旧记录原样保留。未引入安全岛/无信号横道/新冲突规则/雨夜，未覆盖正式宿主或合并main。

最终Evidence：`BuildOutput/Missions-20261117-EvidenceDelivery/StreetCrossing-Missions-Evidence-final-lf-20261117.zip`，40,937,032字节，SHA256 `daa54d46987148596de0b27daf6630e16acb4e02d7007ba770d3c98c12382a1e`；104份原始证据、106 ZIP条目，含完整LFVerification，旧873d原证据包保留不覆盖。最终Source ZIP在Root统一提交后生成，其提交号、路径和SHA以source-manifest/校验清单记录；不修改原生Windows manifest补写提交号。
