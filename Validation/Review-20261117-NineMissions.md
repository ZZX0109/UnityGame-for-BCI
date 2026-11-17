# 九任务 Final2 实施与验收 · 2026-11-17

九任务功能已实现，Final2运行验证完成。实际运行版指纹 **`873d40206c5f0711c1053f319fb317ced75ac075adffcab39579ffa5f62c7156`**：147/147 EditMode、34/34 PlayMode与Windows全部自然0；27固定配置全部原生自动行走；15路线图、15 UI gallery与正式四档截图QA无阻塞，保留三个非阻塞P2。正常时间正式600秒850.8626FPS、P95 1.5741ms，满足本轮数值目标。

源码复现收尾已完成，最终交付源码指纹 **`141b2c1e341a2c75150cf742c8a58ffd24289fa5413a40aa84ef5a2f6e09b877`**。完整运行实测873d与归档141b通过`Missions-20261117-LFVerification/normalization-bridge.json`连接：仅两个历史测试文件最后闭合大括号后各去掉一个CR，原始字节、精确offset和旧/新SHA均保留，无token/行数变化；production源码/资产/包/设置及原Windows负载相同。当前源补测Edit9/9（PID22504）、Play1/1（PID44908）均自然0、零skip、Unity2022.3.62f3c1，补测后源未变，全SourceChecks5命令/69再次通过。完整147/34与27配置/600不冒充在另一不同运行版本重跑，也不把补测加成新的全套计数。

## 功能与测试口径

S01-T1至S03-T3、三组固定seed，T2完整预演20/20/8秒；实际便利店/住宅/书店/药店/咖啡店目标、S01反向与S03往返。引导/风险/按需辅助和F1不扣安全分；每段保留安全条件，整任务取最低星，关键违规不由返程成功抵消。独立任务/revision/variant最高星与三配置推荐不锁任务。

27是9任务×3配置的自动路线数量，不是27个独立NUnit case或27次人工试玩。新路线timeScale3但使用真实CharacterController与原步行/观察/交通/判奖逻辑，无直接成功flag；旧三Acceptance正常timeScale1。测试PersistMissionProgress=false/MemoryStore隔离成绩。

## 本轮原始证据

| 集合 | 实际结果 / 范围 |
| --- | --- |
| `Missions-20261117-Final2` | Prepare、147Edit、34Play、Build自然0、源873d前后一致；原Windows manifest/ZIP不改 |
| `Missions-20261117-SourceFinal` | 原873d版本5命令成功、Tools69/69；141b收尾5命令/69另已通过 |
| `Missions-20261117-RouteVisual-Final2` | 捕获1/1、15原图、Editor自然0、visual-review：0阻塞/2语义P2 |
| `Missions-20261117-Smoke-Final2` | 1秒smoke performance=false/natural2为预期；15张1080p UI gallery，QA0阻塞/1菜单上下文P2 |
| `Missions-20261117-Audit600-Final2` | 正式600、4真实PNG、process/run-binding、Root CSV复算、同任务资源review、正式4res视觉QA |

以上目录均在BuildOutput。正式600：PID41320自然0，600.0005212秒、510518帧、平均850.862594FPS、P95/P99/max=1.5741/2.0734/6.7135ms；NVIDIA RTX4060、High、1920×1080、timeScale1、无gallery。S02-T1动态世界约610.0128秒含暖机、观察活动609.9728秒；无硬timeout重开。Root独立CSV复算已保存root-csv-review.json，未用加速路线替代性能。

资源：21总样本含基线，比较排除iteration-1后的20次重建。只覆盖三街区T1/variant0，同组7/7/6；五类对象和reserved增量0，allocated+63592/+26832/+4696B、managed+266240/+237568/+163840B。审计/日志有内存增量，不能据有限样本推断一般无泄漏。GPU帧时及GC分配计数器不可用/-1，不冒充零成本或以CPU替代GPU。

## 截图与元数据边界

1. 中间导航路点显示最终店名，容易看作沿途中央店牌；不是实际书店/咖啡店目标误配。
2. 未转头和真正遮挡的提示均提调整站位，应后续区分。终点图贴门且Canvas隐藏，只覆盖门前，不是全店面、结果页或人工通关。
3. Gallery菜单仅尾部，缺固定所选任务摘要，不能从末个T3说明尾部确认当前选择；不推断逻辑错误。正式720/1080/1440/4K QA仅S02-T1/A正常字号HUD，0新增布局P2，不覆盖全部菜单/大字号/所有状态。

原Native summary/build-manifest旧模板29868字段不是本轮并发观测，保留原字节并由Evidence INDEX说明；正式过程以新process/monitor为准，只说观察范围无其它Unity/Player/编译，不称全OS GPU完全独占。模块化街景不是照片级真实，不声明完整人工玩法验收。

## 运行包与归档收尾

Windows ZIP：`BuildOutput/Missions-20261117-Final2/StreetCrossing-Windows64.zip`，27,745,862字节，SHA256 `5131f6f9377ba0c3f856b7cc2d2b1caee7d773084bdd8161a7105425d09459a3`。146负载、EXE进程与原build-manifest已同源核验。

最终独立Evidence：`BuildOutput/Missions-20261117-EvidenceDelivery/StreetCrossing-Missions-Evidence-final-lf-20261117.zip`，40,937,032字节，SHA256 **`daa54d46987148596de0b27daf6630e16acb4e02d7007ba770d3c98c12382a1e`**。104份原始文件及INDEX/manifest合106条目，CRC/逐文件SHA/打包前后inventory全部一致，包含整个LFVerification；不重复Windows负载，873d原始Evidence与所有日志不改。

Source ZIP在Root统一提交后绑定HEAD，提交号/路径/SHA由source-manifest与校验清单记录。运行包仍以873d原manifest和Windows146文件SHA绑定，交付源码用141b与归一化桥接说明，不强行改原manifest伪装同一个全目录字节指纹。Full1和旧20261116检查不继承本轮PASS。
