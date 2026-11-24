# Game04 虚拟手开发与验证结果

## 2026-11-22 08:27 多视点材质渲染、C07取景修正与2×2补格（Blender-only）

本小时遵循上一轮 Chrome 规划建议，继续冻结 rev2 `.blend` / GLB 的隔离 EEVEE 渲染，未保存或修改源资产。run-04 使用 Blender 5.2.3 LTS / EEVEE 生成 C01、C02、C03、C04、C07 的源 `.blend` 与 GLB 同机位画面，另对 C01 加高对比背景，共12张；12张均由 System.Drawing 解码为预期 1280×1600 PNG，Blender exit 0、冻结输入哈希前后相同。目视检查发现 run-04 的两张 C07 为纯背景，故本轮只计10张达到预期取景；剩余 C07 标 `FAILED_FRAMING`，不把“渲染成功”误记为计划视点通过。C02/C03/C04 的 GLB 画面仍可见裤装后方棕色区域及明显条带/块状纹理，为可重复确认的外观候选问题，不据此判定网格根因。

随后保留失败帧，在 run-05 和 run-07 分别重试 C07。run-05 的 35 mm 机位切入身体画面，但躯干过度占屏且手部被裁，标 `C07_FRAMING_INADEQUATE`。run-07 将桌面模拟取景设为13 mm、1600×1600；源与 GLB 共2张，画面同时呈现躯干、双臂/手、骨盆、腿和鞋，记录为 `C07_DESKTOP_VIEW_CANDIDATE_ONLY`。该镜头尚非注册的PICO/Unity眼点/FOV；因此不作为人体头显验收或正式视觉PASS。

受控补充 run-06 完成 C01 的2×2诊断缺格：`Human.001` 隐藏 × 导入裤装 Alpha 暂设1.0（仅内存材质副本）。和 run-02 / run-03 已存的另外三格一起逐像素比较：Alpha 变更在身体可见时影响 ROI 150,784/350,400，在身体隐藏时影响 137,328/350,400；隐藏身体的 tan 启发式像素两格均为0。Alpha 效应在身体两种状态之间有156,813/2,048,000像素的有符号 RGBA差异。这个交互结果证明两种干预在本帧并非独立相加，但不能判定具体缺陷源自人体几何、服装覆盖、UV/贴图Alpha、透明排序或其组合；tan阈值仅是启发式，不是分割真值。

run-04 / run-05 / run-06 / run-07 的各自 `process-meta.json`、完整命令、stdout/stderr、输入前后SHA、PNG解码信息和逐文件SHA清单均保留在对应运行目录。所有 Blender 进程 exit 0，源 `.blend` SHA `5b78b94ce0a6a0fe3122d47659a5a3e9604597fa4f8d2219e3c019e9ad5d34e0` 与 GLB SHA `431ad42e79d26f636c30bcdaf8f1871882852b45db5535834a102bb2febec0dc` 未改变。没有修模或进行 Unity/PICO测试。

本轮并行完成3项只读复核，派发参数请求 `gpt-6-luna` / `reasoning_effort=max`：C01可见异常检查、GLB材质/Alpha/UV审阅、渲染证据与验收门审阅。协作回执未提供可独立核验的模型身份或推理档遥测，因此只确认三项任务已交付，不把请求参数当成实际身份验证。建议均只作分析输入，没有修改文件。ZCode 界面上次显示 GLM 正在 Game03 工作；为避免中断或抢占，没有把该轮 GLM工时虚报为Game04贡献，也未达到每轮≥50% GLM要求。更正：曾在 Chrome 中发送本轮数据并看到生成状态，但后续核对显示当时活动窗口属于另一条“网络研究下一步策划”会话，不能据此认定 Game04 的“规划虚拟身体样板”专用聊天已同步。正确线程目前显示临时访问限制，本轮材料未确认送达，也没有可用的新策略答复。本地进度与协作限制见[工作进度](docs/WORK_PROGRESS.md)。

证据： [run-04 多视点包](BuildOutput/Game04-render-gate-20261122/run-04-multi-view/)、[run-05 C07首次修正](BuildOutput/Game04-render-gate-20261122/run-05-c07-camera-revision/)、[run-06 C01 2×2补格与像素比较](BuildOutput/Game04-render-gate-20261122/run-06-c01-factorial-missing-cell/)、[run-07 C07宽视场候选](BuildOutput/Game04-render-gate-20261122/run-07-c07-wide-camera/)。

## 2026-11-22 07:26 C01 Alpha 覆盖与身体网格隔离（Blender-only）

冻结输入 `.blend` / GLB SHA-256 为 `5b78b94ce0a6a0fe3122d47659a5a3e9604597fa4f8d2219e3c019e9ad5d34e0` / `431ad42e79d26f636c30bcdaf8f1871882852b45db5535834a102bb2febec0dc`；run-03 开始与结束哈希一致。Blender 5.2.3 LTS / EEVEE，均自然退出码 0。run-02 在内存中复制导入的裤子和头发材质，仅断开 Principled Alpha 的图像 Alpha 连线并设为 1.0，源资产未保存。裤装覆盖使下肢 ROI 350,400 像素中 150,784（43.03%）发生变化，tan 色启发式像素由 70,220 增至 139,433；强制不透明并未消除 tan 区，不能作为修复。头发覆盖只改变全画面 37,031 像素（1.81%），下肢 ROI 改变 48 像素。

run-03 在同一场景/中性背景下逐帧切换 `Human.001` 可见性：隐藏身体网格使下肢 ROI 226,336/350,400（64.59%）像素改变，tan 色启发式像素由 70,220 降至 0；run-02 与 run-03 的重复 baseline 图像逐像素一致。由此确认这片 tan 区依赖 `Human.001` 的可见表面；身体网格、裤子 Alpha/深度排序及二者组合的具体机制仍 UNKNOWN。tan 阈值是本项目诊断线索，不是验证标准。暂不修模；没有 Unity 导入、PICO、双目或真实 VR 验收。

证据与逐文件 SHA：[run-02 Alpha 探针报告](BuildOutput/Game04-render-gate-20261122/run-02-alpha-probe/alpha-probe-report.json)、[Alpha 像素对比](BuildOutput/Game04-render-gate-20261122/run-02-alpha-probe/alpha-probe-comparison.json)、[run-02 清单](BuildOutput/Game04-render-gate-20261122/run-02-alpha-probe/output-manifest.json)、[run-03 身体显隐报告](BuildOutput/Game04-render-gate-20261122/run-03-body-hide/body-hide-report.json)、[身体显隐像素对照](BuildOutput/Game04-render-gate-20261122/run-03-body-hide/body-hide-comparison.json)、[run-03 清单](BuildOutput/Game04-render-gate-20261122/run-03-body-hide/output-manifest.json)。Chrome 规划页仍显示临时访问限制，本轮新图没有发送给网页端评审。

## 2026-11-22 07:06 Blender C01 对照图静态复核（非验收）

在同机位对照原生 `.blend` 与冻结 rev2 GLB 的 C01 EEVEE 图像，并查看逐网格隐藏渲染。导入 GLB 的 `Human.001` 隐藏前后，躯干/下肢 ROI 35.04 万像素中有 226,251（64.57%）发生变化；可见的 tan 色下肢区域随该网格隐藏而消失。因此 `Human.001` 是该区域的可见贡献者。裤装隐藏也会显著改变同一 ROI（201,670 像素，57.55%），但这不能区分身体几何、裤装 Alpha/深度排序或两者交互。尚未运行材质 Alpha 覆盖探针或 Unity 导入，根因仍为 UNKNOWN。

本项是 Codex 对既有离线渲染的静态观察，不是正式美术评审、穿模确认或 Unity/PICO 验收。没有修改模型或 GLB。Chrome 规划页仍返回临时访问限制，因此这批 C01 图尚未送达网页端评审。证据：[原生 Blend 图](BuildOutput/Game04-render-gate-20261122/run-01/renders/source-blend_C01-neutral.png)、[导入 GLB 图](BuildOutput/Game04-render-gate-20261122/run-01/renders/imported-glb_C01-neutral.png)、[隐藏 Human.001](BuildOutput/Game04-render-gate-20261122/run-01/mesh-toggle-isolation/renders/hide-Human.001.png)、[像素对照 JSON](BuildOutput/Game04-render-gate-20261122/run-01/pixel-comparisons.json)、[执行报告](BuildOutput/Game04-render-gate-20261122/run-01/render-smoke-report.json)。

## 2026-11-20 15:46 蓝色材质与开源人体流程

Codex 集成 AD/AE/AF 交付。真实双手预制件、身体尺寸绑定和连续手臂在隔离测试场景完成手/头失追恢复及腕位保持，PlayMode 6/6 通过。初次截图肘部朝前鼓起；将默认肘弯方向改为向下后复测 6/6，并保存同机位普通/蓝色截图。新旧袖管仍有交叠，场景头部与躯干只是几何占位，不能视为完整人体外观验收。[最终组合测试](Results/vr-check-20261120-1546/body-final.xml) · [蓝色第一视角](Results/vr-check-20261120-1546/elbows-down/integrated-body-blue-first-person.png) · [普通第一视角](Results/vr-check-20261120-1546/elbows-down/integrated-body-preview-first-person.png)

蓝色材质恢复测试最初错误地在 EditMode 依赖运行时 OnDisable，随后又提前断言延迟销毁；Codex 将用例改为进入 PlayMode 并等待帧结束，最终 1/1 通过。验证多个材质槽原样恢复、重复切换复用材质、停用和销毁后还原与释放。[材质生命周期](Results/vr-check-20261120-1546/style-lifecycle.xml)。多物体透明排序、打包后着色器保留及真实 PICO 双目仍待验证。

MPFB2 v2.0.18 官方源码已下载解压，Blender 5.2 中实际创建 19,158 顶点底模和 53 根 game_engine 骨骼。首次普通模块导入失败，Codex 修正扩展包命名与临时偏好注册后成功；没有保存全局插件偏好。这里证明开源人体生成与骨架 API 可用，尚不含穿衣成品或 Unity 全身导入。系统素材和自然皮肤包下载仍在进行，第一镜像连接超时后改用官方第二镜像。[生成日志](Results/vr-check-20261120-1546/mpfb-smoke3.log) · [资源清单](ArtSource/_download/avatar-resources/manifest.json) · [素材下载](Results/vr-check-20261120-1546/asset-mirror.log)

本地素材解压工具的路径与目录清单测试 1/1 通过。本轮无新增独立 Windows 包和 PICO 佩戴结果，所有画面均来自桌面测试场景。[工具测试](Results/vr-check-20261120-1546/asset-tool-test.log)

## 2026-11-20 15:16 身体连接组件第一轮验证

肩肘解算与尺寸配置的编辑器测试 4/4 通过；连续手臂网格和头位到身体校准绑定的首轮 PlayMode 测试 4/4 通过。后者验证网格随肘点更新、复用网格、保持手位置、不见的手臂隐藏，以及尺寸来源、点头不带动肩线倾斜、缺少头位时隐藏。袖管测试使用静态夹具和模拟输出，尚不代表真实宿主中手腕接缝、连续控制或 PICO 双目通过。[编辑器测试](Results/vr-check-20261120-1446/body-edit.xml) · [首轮 PlayMode](Results/vr-check-20261120-1516/arms-play.xml)

Codex 已补显式 headTrackingValid 门控，宿主接入时须更新此值；新增“头对象仍存在但追踪无效”的恢复回归用例尚未运行。第二轮测试及 MPFB 下载的批量命令被自动审批以“策略阻止”拒绝，未提供更具体原因，这两步未执行。没有新增独立包或 PICO 佩戴结果。

通用头像候选已用 Blender 渲染，眉毛和短发已出现，但短发仍呈硬壳状，写实外观未验收。下一步使用官方皮肤、眼睛与发型资源替换候选；现有文件只是美术中间结果。[候选预览](ArtSource/Avatar/Candidates/GenericHead_three_quarter.png) · [渲染日志](Results/vr-check-20261120-1516/head.log)

## 2026-11-20 14:33 短前臂 Windows 包更新

娃娃机隔离试玩包已重建，包含上一轮通过场景验证的短前臂和食指样本有效期检查。Unity 返回构建成功及退出码 0。独立程序启动被自动审批拒绝，本轮未补充实际运行或头显证据。[构建日志](Results/vr-check-20261120-1416/claw-sleeve-build-retry.log) · [Windows 程序](Builds/Windows-ClawFirstPersonPilot/ClawPilot.exe)

## 2026-11-20 14:03 短前臂中间版本与交互验证

短前臂袖管延长 16 厘米，已在娃娃机和过马路相同坐姿机位渲染。过马路仍能看到空心袖管末端，上臂和身体连接列入下一阶段。[路沿同机位](Results/vr-check-20261120-1346/street-sleeves.png) · [机柜同机位](Results/vr-check-20261120-1346/c01-sleeves.png)

过马路指向可查看标牌时出现小型标记，遮挡、失追、UI 焦点或停用时隐藏。相关场景测试 8/8。推箱子在模拟 XR ready 状态下，经管理器正常 Update 消费握把边沿，完成一次真实棋盘推动及撤销，持续握持不重复，1/1。食指射线的过期腕样本检查及恢复 3/3。[标牌反馈](Results/vr-check-20261120-1346/marker-final.xml) · [管理器输入整条路径](Results/vr-check-20261120-1346/grip-route-fix.xml) · [腕样本有效期](Results/vr-check-20261120-1346/pointer-freshness.xml)

通用 Unity 包和隔离宿主源代码已更新。Windows 试玩保留上一版本；本轮验证在编辑器完成。[通用包](Builds/FirstPersonHands/FirstPersonHands-Unity2022.zip) · [推箱子补丁](Results/vr-check-20261120-1346/Game01-Sokoban-pilot-source.zip) · [过马路补丁](Results/vr-check-20261120-1346/Game03-StreetCrossing-pilot-source.zip)

## 2026-11-20 13:43 三个隔离宿主的第一视角截图

同一套双手已由娃娃机、过马路、推箱子各自的宿主相机渲染。三张截图均为 1280×720，使用 Unity Play Mode 和模拟头位、腕位。过马路截图在横道入口，测试用标牌在截图后才放置。推箱子侧视截图模拟向左转头 55°。两侧袖口末端在部分机位完整入画，仍有悬浮感。 [娃娃机机柜](Results/vr-check-20261120-1316/c01-today.png) · [过马路路沿](Results/vr-check-20261120-1316/street-seated.png) · [推箱子侧视](Results/vr-check-20261120-1316/sokoban-turn.png) · [推箱子正前方](Results/vr-check-20261120-1316/sokoban-seated.png)

食指指向组件已装进可迁移双手预制件，射线起点使用模型食指末节的骨骼末端。左右操作手、隐藏、失去追踪、镜像禁用和恢复普通握姿均经播放测试。Game04 编辑 54/54、播放 22/22。过马路隔离场景 6/6，覆盖第一遮挡物、非标牌、失追、原头部观察与计分，以及实际挂载的模型指向测试标牌。推箱子 4/4，分别检查了挂载后的握把边沿和宿主的推动、撤销、错位拒绝。操作手输入到原管理器的完整连续路径列入下一次独立测试。[编辑](Results/vr-check-20261120-1316/hand-release-edit.xml) · [播放](Results/vr-check-20261120-1316/hand-release-play.xml) · [标牌查看](Results/vr-check-20261120-1316/street-view.xml) · [握把与棋盘](Results/vr-check-20261120-1316/sokoban-view.xml)

已导出更新后的通用包，保留两个隔离宿主的源代码补丁。本轮沿用上一版娃娃机 Windows 包检查 OpenXR，仍返回 XR_ERROR_RUNTIME_UNAVAILABLE。推箱子、过马路此次验证使用编辑器。[通用包](Builds/FirstPersonHands/FirstPersonHands-Unity2022.zip) · [推箱子源码补丁](Results/vr-check-20261120-1316/Game01-Sokoban-pilot-source.zip) · [过马路源码补丁](Results/vr-check-20261120-1316/Game03-StreetCrossing-pilot-source.zip) · [头显启动日志](Results/vr-check-20261120-1316/headset-check.log)

## 2026-11-20 13:14 第一视角袖口、任务提示与右手暂停

在 C01 娃娃机里，两只手各有与模型腕口贴合的短袖口。我用原机柜相机固定模拟坐姿头位和双腕，将袖口开、关各拍一帧。1280×720 下，左右袖口分别改变了 9,683 和 10,849 个像素。紧凑任务卡在 16:9 与 4:3 的测试机位均显示完整，四角落在中央 84% 视野内。近脸位置、双目穿插与佩戴舒适度仍需头显实测。[同机位修前修后](Results/vr-check-20261120-1241/before-after.jpg) · [完整 C01 截图](Results/vr-check-20261120-1241/c01-delivery.png)

游玩时可按左手柄 X 或长按右手柄 A 约 0.75 秒暂停。右手暂停后先松开 A，再按一次 A 或扳机确认继续。模拟手柄回归覆盖误触、继续游戏和真实 C01 物理抓取判奖。Game04 编辑 54/54、播放 19/19；娃娃机隔离试玩的输入编辑测试 12/12、场景播放测试 13/13。[Game04 编辑](Results/vr-check-20261120-1241/game04-edit.xml) · [Game04 播放](Results/vr-check-20261120-1241/game04-play.xml) · [娃娃机输入](Results/vr-check-20261120-1241/grip-edit.xml) · [娃娃机场景](Results/vr-check-20261120-1241/pilot-delivery.xml)

已重建 Windows 试玩及可迁移包。在 Windows 试玩启动日志发现“坐姿与手部校准”缺字后，按试玩版 UI 重新生成 OFL 字库；重启后缺字警告消失。检测到 RTX 4060，OpenXR 返回 XR_ERROR_RUNTIME_UNAVAILABLE，本轮未取得头显或手柄实机结果。[Windows 入口](Builds/Windows-ClawFirstPersonPilot/启动%20PICO%20VR.cmd) · [可迁移包](Builds/FirstPersonHands/FirstPersonHands-Unity2022.zip) · [隔离试玩源码及补丁](Results/vr-check-20261120-1241/c01-isolated-sources.zip) · [Windows 构建](Results/vr-check-20261120-1241/claw-delivery-build.log) · [字库修复后启动日志](Results/vr-check-20261120-1241/openxr-font-fixed.log)

三名 GPT-6 Luna Max 同批交付了食指指向、过马路查看标牌、推箱子握把推动的候选代码与测试。推箱子和过马路的隔离副本已成功导入当前双手包；候选交互尚待合入副本并运行 Unity 回归。[三名模型与并发记录](Results/vr-check-20261120-1241/next-batch.json) · [各自交付文件](Results/vr-check-20261120-1241/candidate-status.json) · [推箱子导入](Results/vr-check-20261120-1241/new-host-1.log) · [过马路导入](Results/vr-check-20261120-1241/new-host-3.log)

## 2026-11-20 12:39 单手校准、对侧镜像与娃娃机菜单

在娃娃机主菜单或暂停页选择“坐姿与手部校准”。左、右手柄均可用自己的摇杆和扳机导航。指定操作手后，可采集该侧腕部中立位；保存即生效，取消保留原先设置。镜像手使用校准后的操作腕，操作侧丢失时，实测手与镜像手都隐藏。用实际双手预制件测试两种方向，核对掌背朝向以及拇指、食指和小指末节的镜像位置（容差 2 毫米）。[镜像截图（模拟左腕）](Results/vr-check-20261120-1209/mirror-from-left.png) · [校准设置入场图](Results/vr-check-20261120-1209/calibration-panel.png)

Game04 编辑测试 54/54、播放测试 19/19。娃娃机隔离副本 10/10，包含真实 C01 物理判奖、单手输入、菜单打开与关闭、取消不写入、暂停恢复和场景相机取景。截图使用模拟头位和腕位。Windows 试玩已构建。在本机启动 PICO 模式时，OpenXR 报告 XR_ERROR_RUNTIME_UNAVAILABLE；佩戴效果和双目帧时待连接设备核验。[编辑](Results/vr-check-20261120-1209/game04-edit.xml) · [播放](Results/vr-check-20261120-1209/game04-play.xml) · [宿主交付测试](Results/vr-check-20261120-1209/pilot-delivery.xml) · [构建](Results/vr-check-20261120-1209/claw-delivery-build.log) · [OpenXR 启动日志](Results/vr-check-20261120-1209/openxr-startup.log) · [可迁移包](Builds/FirstPersonHands/FirstPersonHands-Unity2022.zip) · [隔离宿主源码与补丁](Results/vr-check-20261120-1209/c01-isolated-sources.zip)

## 2026-11-20 12:07 第一视角双手在隔离娃娃机内跑通 C01

使用已有机柜相机和 PICO 追踪空间挂载双手。模拟手柄在菜单握紧未开始游戏，在真实 C01 场景预先对准奖品后握紧，物理爪夹起奖品、释放到出口、稳定判奖；持续握紧仅扣一次，重开后可以再次触发。桌面输入保持原交互，关闭手模块后恢复之前的输入；选择单手左右侧会同步更新握把输入。四项播放测试通过。Game04 单手设置与原用例播放 17/17。[宿主整局与挂载](Results/vr-check-20261120-1135/pilot-final.xml) · [虚拟手播放](Results/vr-check-20261120-1135/play-settings.xml) · [隔离源代码与差异](Results/vr-check-20261120-1135/c01-isolated-sources.zip)

以模拟坐姿头位和腕位从真实机柜相机渲染 1280×720 图像。逐手加入模型后，左、右分别有 39,890 和 32,957 个像素的颜色改变达到测试阈值。近景腕口截面明显，左上角任务文字贴边。真实 PICO 尚待连接，本机 Windows 启动 OpenXR 返回 XR_ERROR_RUNTIME_UNAVAILABLE。Game04 独立播放器的隐藏窗口未取得可用显示帧。[第一视角截图](Results/vr-check-20261120-1135/c01-seated-view.png) · [相机测试](Results/vr-check-20261120-1135/view-tracked.xml) · [OpenXR 日志](Results/vr-check-20261120-1135/pico-runtime.log) · [Unity 安装包](Builds/FirstPersonHands/FirstPersonHands-Unity2022.zip)

## 2026-11-20 11:31 数据手套避让与坐姿标定

修正后的 Unity 游戏场景以 40 Hz 播放 DB8 S1 的侧捏和圆柱抓握。此前左手分别在 91/269、14/289 个时点出现指间相交。本轮左手及其右手镜像合计 1116 个采样，五指中节、末节之间均未检出交叉。显示层对拇指施加有速度限制的避让，原始数据保持完整。指根、掌面、其他物体和头显体验待分别验证。[修前基线](Results/vr-check-20261120-1032/baseline/digit-intersections.json) · [修后证据](Results/vr-check-20261120-1102/smooth/digit-intersections.json) · [游戏相机侧捏图](Results/vr-check-20261120-1102/final-camera/db8_S1_lateral_grip.png)

Game04 编辑模式 46/46（中立腕位 6 项），播放模式 10/10（含世界空间校准面板）；娃娃机隔离副本握把输入 9/9。Windows 桌面预览构建成功，已启动并识别 RTX 4060，后台首张截图为黑帧，独立程序画面待确认。[编辑测试](Results/vr-check-20261120-1102/edit-final.xml) · [播放测试](Results/vr-check-20261120-1102/play-final.xml) · [娃娃机输入](Results/vr-check-20261120-1102/claw2.xml) · [隔离副本源代码与补丁](Results/vr-check-20261120-1102/isolated-claw-grip-sources.zip) · [桌面预览](Builds/Windows-GloveClearancePreview/VirtualHand.exe) · [运行日志](Results/vr-check-20261120-1102/player.log)

## 2026-11-20 11:01 单手设置与穿模逐帧基线

真实 DB8 回放按 40 Hz 采集，包含起止姿态。左手侧捏 269 个时点中有 91 个检出指间相交，圆柱抓握 289 个时点中有 14 个。右手按左手数据镜像，两手累计 1116 次采样。侧捏 3.575 秒时拇指与中指相交三角形对数达到 197。计数反映相交面片数量，穿入深度需另测。[完整记录](Results/vr-check-20261120-1032/baseline/digit-intersections.json)

Codex 用每个时点的实际拇指根、指尖和掌宽方向做了离线试验。对有相交的 210 个左右手姿势逐个向桡侧试转，均能找到消除受测中末节指间相交的角度，左手 82 个侧捏时点在所试角度中需要转至 30°。该试验使用刚性旋转的三角形，尚待运行时约束、自然度与其他部位复测。[离线试验](Results/vr-check-20261120-1032/thumb-clearance-trial.json)

Luna B/D 的设置存储与面板行为通过 Unity 编辑测试 12/12，Luna C 的诊断工具通过 3 项 Python 测试，并能读取真实基线。Luna E 的握持输入适配器在 Game02 隔离副本中通过 4/4 编辑测试。游戏整局、实际面板预制件和头显佩戴仍待验收。[校准测试](Results/vr-check-20261120-1032/calibration.xml) · [娃娃机输入测试](Results/vr-check-20261120-1032/claw.xml)

## 2026-11-20 第一视角 VR 接入原型与接触路径修复

- Luna Max 提交了可独立挂载的 `FirstPersonWrist` 和四组 EditMode 测试。Codex 修正测试夹具对象名，补上停用组件的输入保护，集成控制器双手、腕点与掌面坐标系、可导出预制件，并增加一组真实模型测试。正式工程编辑测试 28/28，播放测试 9/9。编辑进程在写完结果并关闭后停留，单独结束此次批处理进程；随后导出、独立工程导入、Windows 构建正常退出。[Luna 交付](Results/luna-development/20261120-092307-vr-wrist/dispatch.json) · [编辑测试](Results/vr-first-person-20261120/edit.xml) · [播放测试](Results/vr-first-person-20261120/play.xml)
- 已将同一份安装包导入空白 Unity 2022.3 工程，在独立宿主中验证两只手腕分别响应输入、有两套蒙皮和材质、预制件内部相机数为零。测试输入为模拟位姿。该包提供手柄驱动的腕部显示与握把开合，握持偏移支持左右分别校准。头显内的手掌朝向与控制器偏移、光学手部追踪、患者模式和游戏交互尚待验证或开发。[可迁移安装包](Builds/FirstPersonHands/FirstPersonHands-Unity2022.zip) · [独立宿主验证日志](Results/vr-first-person-20261120/host-check.log) · [详细计划和安装说明](docs/FIRST_PERSON_VR_PLAN.md)
- Codex 接手接触修复。初版仅向箱侧绕行，在 30 Hz 通过后，90 Hz 仍测出接收手嵌入 4.54 毫米。加入抬腕避开箱角并调整搬运时的桌面净空。最终按 30、72、90 Hz 模拟步长分别跑完搬运、左交右、右交左：两种交接最大嵌入依次为 0.484、0.484、0.957 毫米；搬运最深 0.238 毫米，手部最低点高于桌面 0.588 毫米。这是指定箱体与三个真实回放任务下的蒙皮测量；90 Hz 是模拟采样频率。另行构建了含该修复的桌面预览包。[30 Hz](Results/vr-first-person-20261120/final-30/task-contact.csv) · [72 Hz](Results/vr-first-person-20261120/final-72/task-contact.csv) · [90 Hz](Results/vr-first-person-20261120/contact90-arc/task-contact.csv) · [桌面预览](Builds/Windows-FirstPerson-ContactPreview/VirtualHand.exe)
- 侧捏时拇指与其他手指相交、圆柱抓握后段局部相交仍可见。下一项由 Codex 负责约束显示层的拇指路径，Luna 可补对应失败帧和回归用例。游戏副本接入、患者坐姿设置和 PICO 真机验收按计划执行。

## 2026-11-20 09:15 穿模复测

在保存的 Demo 中通过菜单播放真实任务样例，以每秒 30 帧测量左右手蒙皮与 12 厘米方块，覆盖三次完整任务，共 2787 帧。两个交接方向都出现接收手穿入箱体的情况。左交右在 15.43 秒、右交左在 7.13 秒达到 21.02 毫米，下一帧仍有 9.56 毫米。持稳后的读数是 0.48 毫米。S001 协作搬运中手进入箱体最深约 0.24 毫米，2.53 秒开始抬箱时双手最低点比可见桌面低 2.41 毫米，持续一个采样帧。[逐帧接触数据](Results/elena-clipping-20261120-0904/playback/task-contact.csv)

从六段 S1 手套样例的播放入口取左手，每段在 10% 到 90% 进度均匀取九个时点，对不同手指中、末节的蒙皮三角形做相交检查。侧捏在 40%、50%、60%、70% 四处出现拇指与中指相交，其中一部分还涉及食指和无名指。圆柱抓握在 80% 处检出 4 对拇指与中指相交的三角形。其他四段在所取时点未检出这类相交。此检查未覆盖掌心、指根或采样间隔内的姿势。[逐动作相交记录](Results/elena-clipping-20261120-0904/playback/digit-intersections.json)

本次为测量，测试进程顺利完成代表数据已采集。上版的功能验收记录继续保留，交接接近路径和侧捏里的手指穿模仍待修正。

## 2026-11-20 03:54 六段真实手套回放与 Windows 交付

打开 [Windows 试玩程序](Builds/Windows-Elena/VirtualHand.exe)，选择 **食指屈伸 → 播放 / 暂停**。已在实际程序中播放 S1 的 320 帧片段，3.2 秒时可见食指弯曲，侧栏显示手套来源和右手镜像。静息、拇指屈伸、食指屈伸、圆柱抓握、侧捏、三指握六段分别通过正式 Demo 的菜单播放，画面取自各自真实试次并标有时间。[Windows 食指实机画面](Results/elena-spread-20261120/windows-db8-index.jpg) · [六种动作对照图](Results/elena-spread-20261120/db8-six-poses.jpg)

- 修复了食指至小指侧展输入变化时指尖不动的问题。逐指测试从最小到最大输入，两只手的食指、中指、无名指和小指分别移动 36.18、40.00、37.91、28.83 毫米。同手其余指尖位移低于 0.1 毫米，镜像位移误差低于 0.2 毫米。DB8 回放中，四指共享三个指间传感器读数的归一化均值。六段样例中点固定其他控制值、仅将侧展调回中位时，食指尖位置变化 0.57 至 8.86 毫米。这两组数值均为模型几何测量。[全行程](Results/elena-spread-20261120/spread.log) · [逐片段数据及相机截图测试](Results/elena-spread-20261120/play-visual.xml)
- 正式场景连续三轮播放测试均为 9/9，包含双手捏握、S001 预测搬运、S003 标签双向交接、暂停与恢复。编辑测试 23/23，编辑器在写完结果和关闭日志后停留，结束该批处理进程，再单独运行了截图验证和构建。Windows x64 构建报告 0 错误、0 警告，RTX 4060 在 1920×1080 启动采样约 59.94 FPS。[第一轮](Results/elena-spread-20261120/play-final.xml) · [第二轮](Results/elena-spread-20261120/play-repeat.xml) · [第三轮](Results/elena-spread-20261120/play-third.xml) · [编辑测试](Results/elena-spread-20261120/edit-final.xml) · [构建日志](Results/elena-spread-20261120/menu-build.log) · [实机采样](Results/elena-spread-20261120/windows-check.json)
- GPT-6-Luna Max 提供张指修复候选。Codex 按实际骨名整合，保留已验证的捏握与接触姿态，修正取图时机。DeepSeek Harness 首选账号 Flash 返回额度不足，随后腾讯 TokenHub 广州 Flash 完成只读复核，确认四路控制、正反向与镜像对应及共享测量的含义。[复核记录](Results/elena-spread-20261120/dhs-review.txt)

## 2026-11-20 02:48 单手抓箱与双向交接已在 Windows 版验证

在 [Windows 程序](Builds/Windows-Elena/VirtualHand.exe) 选择 **S003 → 明确标签 → 交接方向 → 播放**，即可用真实 S003/R12 样例观察左右手交接。两种方向均已在独立程序中操作至放下，方块留在目标区，双手恢复回放。[交接实机画面](Results/elena-handoff-20261120/windows-ltr-transfer.jpg) · [接住方块](Results/elena-handoff-20261120/windows-ltr-held.jpg) · [放下并恢复回放](Results/elena-handoff-20261120/windows-rtl-released.jpg)

- 针对 12 厘米方块，单手腕姿已从蒙皮数据重新拟合。两侧拇指在箱顶内部、朝向顶面且相距不超过 3 毫米的顶点各有 77 个。模型最深嵌入箱体 0.48 毫米，四指末节到箱侧各自最近距离的均值为 3.51 毫米。近景中能看到拇指扶顶、其余手指抵住侧面。[左手递出近景](Results/elena-handoff-20261120/left-sender.png) · [右手接住近景](Results/elena-handoff-20261120/right-receiver.png) · [左右独立标定](Results/elena-handoff-20261120/generate-final.log)
- 在两个交接方向上分别测量发送方等待交接和接收方等待放下时的蒙皮，两阶段均满足接触与桌面净空要求。正式 Demo 连续三轮 PlayMode 均为 8/8，包含手动捏握、真实 DB8 六片段、S001 预测搬运、S003 标签双向交接、暂停和复位。EditMode 23/23。Windows 构建 0 错误、0 警告，实机 RTX 4060、1920×1080。[第一轮](Results/elena-handoff-20261120/play-both.xml) · [第二轮](Results/elena-handoff-20261120/play-repeat.xml) · [第三轮](Results/elena-handoff-20261120/play-third.xml) · [编辑测试](Results/elena-handoff-20261120/edit-final.xml) · [构建](Results/elena-handoff-20261120/menu-build.log) · [实机采样](Results/elena-handoff-20261120/windows-check.json)
- 本轮 DeepSeek Harness 源码复核结论为交接无阻断。首选 DeepSeek 账号 Flash 返回额度用尽，实际完成复核的是腾讯 TokenHub 广州 Flash。它指出八个顶点的回归下限偏松；上文给出实际网格测量和持箱近景。[二审纪要](Results/elena-handoff-20261120/dhs-review.txt)
- 已定位一个 DB8 显示缺口。真实食指片段中，食指至小指四个侧向张指通道的归一化值变化幅度各约 0.298；当前正式模型把各通道从 0 调到 1 时，对应指尖位移均为 0 毫米。两只手共八项均已复现，正在修复。抓握、单指屈伸和手动捏握已有独立的测试记录。[张指复现日志](Results/elena-handoff-20261120/spread.log)

## 2026-11-20 01:52 双手捏握已在 Windows 版验证

打开 [Windows 试玩程序](Builds/Windows-Elena/VirtualHand.exe)，按键盘上排 **3**，侧栏显示“手动预览／双手捏握”。两只手都采用 Elena FF 的捏握预设。已清除手动预览里原先误显示的静息意图、置信度和旧回放帧数。[Windows 实机画面](Results/elena-thumb-20261120/windows-pinch.jpg) · [Unity 右手指腹近景](Results/elena-thumb-20261120/manual-pinch-close.png)

- 利用模型作者的末节弯曲方向定位指腹，比较两块朝向相对的指腹表面。旧捏握预设两侧在 3 毫米内满足相向条件的顶点均为 0。校准后，左右手各有 6 个拇指顶点和 20 个食指顶点满足条件，最近间距 0.47 毫米。这些数值取自模型蒙皮。[调整前后采样](Results/elena-thumb-20261120/pinch-refined.tsv) · [正式 Demo 日志](Results/elena-thumb-20261120/play-standalone-ui.log)
- 正式场景播放测试 8/8，覆盖手动捏握、六段真实 DB8 回放、S001 预测搬运和 S003 标签双向交接。Windows 版构建 0 错误、0 警告，已在 RTX 4060 上启动并实际按键确认。[播放测试](Results/elena-thumb-20261120/play-standalone-ui.xml) · [构建日志](Results/elena-thumb-20261120/menu-build.log) · [独立程序采样](Results/elena-thumb-20261120/windows-check.json)
- 单手交接的拇指支撑仍在开发。Luna Max 首版只调拇指对掌，实际测试中指腹离相邻箱面最近仍有 39 毫米。现有独立版保留可完成交接的抓持姿态。下一版由 Luna Max 联合调整手腕、拇指与箱角的位置，再验收左右交接的实际持物画面。[候选方案测量日志](Results/elena-thumb-20261120/generate-pads.log)

## 2026-11-20 00:58 抓箱、双向交接和放下后的回放

正式 Demo 和新版 Windows 程序已接入 Luna Max 的抓持校准。用实际任务中的 12 厘米方块测量，原先悬空的食指末节到箱面的最近距离由 76.53 毫米降至 2.24 毫米，中指由 75.69 降至 0.20 毫米。持箱姿态中，拇指以外四指各自最近距离的均值为 4.98 毫米，手部网格最大穿入深度为 0.24 毫米。单手持物时的拇指支撑和预设捏握的指腹接触仍待校准。[原测量](Results/elena-ui-20261119/probe.log) · [实际回放测量](Results/elena-hold-20261120/play-final.log)

- 六个 Ninapro DB8 片段在保存的 Demo 中逐个通过按钮测试。双手搬运使用 S001/R12 的 EEG 离线预测，左交右与右交左使用 S003/R12 的真实标签回放。三项都能完成，播放暂停和短暂无有效输入时保留阶段，放下时归还双手回放控制。任务中的抓持姿态由程序辅助生成，界面分别显示输入来源和当前呈现方式。[PlayMode 7/7](Results/elena-hold-20261120/play-final.xml) · [双手搬运](Results/elena-hold-20261120/carry.png) · [左交右](Results/elena-hold-20261120/L-to-R.png) · [右交左](Results/elena-hold-20261120/R-to-L.png)
- 落点标记的碰撞体曾将方块挤至目标中心外 37.3 厘米。移除标记的碰撞作用后，方块释放 1.5 秒仍在 18 厘米验收范围内。另在独立程序中从界面选择 S001、EEG 预测、协作搬运并播放，持箱与放下均已截图；放下时方块留在目标盘，界面显示已恢复回放。[修复前失败记录](Results/elena-hold-20261120/play-settle-before.xml) · [实机持箱](Results/elena-hold-20261120/windows-hold.jpg) · [实机放下](Results/elena-hold-20261120/windows-released.jpg)
- DeepSeek Harness 的 DeepSeek 账号 Flash 返回额度不足，已切换腾讯 TokenHub 广州的 DeepSeek-V4.1-Flash 完成源码二审。反馈的两处问题均已修复并加入测试，分别涉及任务中的手套输入提示和放下后的控制权。[复核记录](Results/elena-hold-20261120/dhs-review.txt)
- 编辑模式 23/23。编辑器写完测试结果后在退出阶段停滞，已单独结束该进程。随后常规菜单构建独立完成，0 错误、0 警告，Windows 包约 100 MB。RTX 4060 在 1920×1080 下短时采样约 59.94 FPS。[编辑测试](Results/elena-hold-20261120/edit-final.xml) · [构建日志](Results/elena-hold-20261120/menu-build.log) · [性能采样](Results/elena-hold-20261120/windows-1080p.json) · [运行 Windows 版](Builds/Windows-Elena/VirtualHand.exe)

## 2026-11-19 23:43 侧栏与双手近景实机检查

Luna Max 完成侧栏、镜头输入范围和独立程序截图。我用实际程序复核后调整了取景和桌面。预览只显示双手，进入协作任务才显示方块与目标区。DB8 六种动作、EEG 预测与标签、被试选择和双向交接入口都在左侧；播放前的提示已明确样例等待播放。

- 已查看 [720p 实机画面](Results/elena-ui-20261119/final-1280x720.png) 和 [1080p 实机画面](Results/elena-ui-20261119/final-1920x1080.png)。两种分辨率的全部按钮和手指均在画面内。两次运行均使用 RTX 4060，约两秒采样窗口均为 59.94 FPS。[1080p 采样](Results/elena-ui-20261119/final-1920x1080.json)
- [Windows 试玩程序](Builds/Windows-Elena/VirtualHand.exe) 的常规构建入口已输出至同一目录，本次构建 0 错误、0 警告。保存的 Demo 播放测试 5/5，含六段真实 DB8 回放、双手搬运和双向交接。本轮最终 PlayMode 进程正常退出。[构建摘要](Results/Windows-Elena-build-summary.txt) · [播放测试](Results/elena-ui-20261119/play2.xml)
- 已复现抓箱悬空。用正式 Demo 的手与 12 厘米方块，在原生闭合姿态直接测量蒙皮后，两侧食指末节离箱面均约 76.5 毫米，中指约 75.7 毫米。张手时每侧各有 2277 个网格顶点位于箱内。该项使用编辑器中摆出的开合姿态，尚未验证运动全程的接触。[闭合姿态与方块](Results/elena-ui-20261119/grip-check-fist.png) · [逐指测量](Results/elena-ui-20261119/probe.log)

抓持校准已交给同一 Luna Max 会话，待验证指腹贴合、持物中断和松开后的手部复位。当前版本的 DeepSeek Harness 二审仍待完成。

## 2026-11-19 22:45 正式 Demo 和 Windows 预览已切换到 Elena 双手

Luna Max 的第二批实现已合入本地 Game04 工作分支，保存的 Demo 使用 Elena FF 双手及其原生动画配置。我校正了原骨名匹配、左手镜像旋转和 12 厘米方块的手腕定位，并通过 Unity 生成并重新加载正式场景。

- 编辑模式 23/23。播放模式共 5 项，连续三轮每轮 5/5。测试在保存的 Demo 中逐个触发六个真实 Ninapro DB8 片段按钮，确认左手手套数据和右手镜像都送达原生驱动；方块搬运、左交右、右交左也通过。见 [编辑测试](Results/elena-demo-20261119/edit.xml)、[播放测试](Results/elena-demo-20261119/play.xml)、[第二轮](Results/elena-demo-20261119/play-round2.xml)、[第三轮](Results/elena-demo-20261119/play-round3.xml)。
- [六段左手实测对照](Results/elena-demo-20261119/db8-six-poses.jpg) 使用每段的一张真实数据帧，角度和照明一致。三种抓握在曲指组合上可见差别，静息与拇指动作的单张差异较小。完整闭合姿态与模型作者的原生闭合帧吻合，双侧最大网格顶点误差均约 0.0000001 米；拇食指末节最近顶点距离均为 0.224 毫米。指腹实际贴合、挤入程度和抓箱时的接触仍待画面确认。[姿态测量](Results/elena-demo-20261119/poses.log) · [侧面捏握](Results/elena-demo-20261119/m2_R_pinch_0.png)
- [新版 Windows 预览程序](Builds/Windows-Elena/VirtualHand.exe) 已独立启动，两只手均加载原生驱动。实测 D3D11、RTX 4060、1920×1080；约两秒的末次采样约 59.94 FPS。11 月 18 日的包仍可单独运行。[实机日志](Results/elena-demo-20261119/player-packaged.log) · [暖机后采样](Results/elena-demo-20261119/player-packaged-warm.json) · [构建日志](Results/elena-demo-20261119/build-packaged.log)

第三轮 PlayMode 已写出完整的五项通过结果，Unity 编辑器在关闭网络请求时停滞，已结束该测试进程。随后独立构建正常退出，并在新包中再次启动验证。包内附 Elena FF 模型署名、授权和改动说明。

当前优先项为实际游戏界面、双手与目标区的取景，以及真实任务中指腹与物体的接触。本版 DHS 二审和人工试玩待完成。

## 2026-11-19 21:46 正式工程生成 Elena 左右手 Prefab

Luna Max 的首批代码已接入 Game04。正式工程已生成 Assets/VirtualHand/Prefabs/ElenaFF 下的左右手和共用姿态配置，Unity 2022.3.62f3c1 重复生成、重新实例化并通过双侧开合与回位检查。每侧 69 根骨骼、21 个控制绑定；以张手网格的对角线长度为单位，最大闭合位移为 0.4912，重新张开误差在输出精度内为零。见 Results/elena-prefabs-20261119/verified-build.log。

已在干净的检查场景加载这对 Prefab，用同一机位保存张手、握拳、捏握和真实 DB8 左手食指样例（E1/index，第 2.1 秒）。核对图为 Results/elena-prefabs-20261119/v3_R_open_-1.png、v3_R_fist_-1.png、v3_R_pinch_-1.png 和 v3_L_db8_index_-1.png。前两轮调试图含错误取景或帧内蒙皮未刷新，目视结论使用 v3。指甲、皮纹和食指屈曲可辨；当前拳比原始闭合动画更松，拇食指指腹贴合仍待验证。正式 Demo 和此前的 Windows 包均使用旧手。

## 2026-11-19 20:45 Elena 右手原生开合实测

Unity 2022.3.62f3c1 在隔离验证工程内成功导入 Elena FF 原始 GLB。glTFast 6.13.1 使用本机 Burst 1.8.21；显式启用 com.unity.modules.animation 后强制重导 GLB，Unity 读到一个 Open/Close 片段、69 根蒙皮骨和 16,330 个顶点。对同一蒙皮采样，1.333 秒闭合时最大顶点位移为 1.466696 个网格局部单位，3.208333 秒重新张开后与首帧的最大差为 0。原生姿态配置生成成功，批处理正常退出。日志见 Results/elena-import-check/20261119-204240/unity-reimport-probe.log。正式 Demo 和 Windows 包仍使用旧手，新手外观、左右手、捏握与 DB8 回放仍待验证。

## 最终整合实测（Codex，2026-11-18 17:10）

**结论**：工程、数据回放、任务状态机、Windows 构建与独立 1080p 运行已通过
自动化测试；**自然手模型/自然拳捏视觉、Elena 原生导入和当前版本 DHS 二审
仍未通过或尚未执行**，因此不声明整体验收 PASS。

### 已通过的实测

- 编译与测试：Unity 2022.3.62f3c1 实际编译；EditMode **23/23**
  （`Results/editmode-20261118-final3.xml`），PlayMode **4/4**
  （`Results/playmode-20261118-final3.xml`）。PlayMode 在保存后的 Demo
  场景上验证：序列化引用、Carry 在目标区持续 rest 后释放、L→R 与 R→L
  交接均经 Transfer、接收方稳定握持后才转移所有权、物体全程不掉落、
  最终恢复 Rigidbody 物理。
- **真实生产缺陷已修复**：`TaskController.OnIntent` 的 Transfer 分支把
  `GripAt()` 需要的“手部相对 grip 偏移”误传成“世界坐标目标”，导致物体
  加上两次位置偏移。EditMode 旧用例因 box 在原点掩盖了该问题；PlayMode
  将物体移至路径中点后暴露。修复位置：
  `Assets/VirtualHand/Scripts/Runtime/TaskController.cs`，现传
  `ReceiverGrip()` 偏移。修复后 EditMode/PlayMode 全绿。
- 场景生成：Demo 已注册进 Build Settings；锚点 z 与 CarryBox z 对齐，
  PlayMode 中满足生产接触容差，不靠放宽阈值。生成日志：
  `Results/generate-with-diagnostics-20261118.log`。
- 最终画面证据：`Results/unity-rig-preview/final-20261118/`，14 张
  1920×1080 PNG + `capture-summary.json`，含同机位 open/fist/pinch、
  左右单手、左右 ±30° 食指对照，以及真实 DB8 index 峰值帧
  （source=glove，记录文件/时间/run）。日志：
  `Results/final-capture-20261118.log`。
- 数据侧：`Tools/test_tools.py` 17/17；DB8 六段与 EEG 按被试
  prediction/truth 独立文件保持不变，`TruthReference_IsSeparatedFromPredictions`
  在 Unity EditMode 中也通过。
- Windows x64：`Builds/Windows/VirtualHand.exe`，96 MB，0 error、
  1 warning，见 `Results/build_summary.txt` 和
  `Results/build-windows-final-20261118.log`。
- 独立运行实测：`VirtualHand.exe -screen-width 1920 -screen-height 1080
  -screen-fullscreen 0`，`Builds/Windows/StandaloneCheck.json` 记录
  1920×1080、59.94 FPS、RTX 4060；运行日志
  `Results/standalone-player-20261118.log`。进程在采集后已停止。

### 仍未通过 / 未执行

- **自然手模型视觉未通过**：最终图中开掌和 DB8 index 帧可读，但拳仍有
  中央空隙，捏握指腹接触不够可信。该结论仅基于 Unity 实际渲染，不继承
  Blender 作者图或旧 run23 的结论。
- **Elena FF 原生导入未打通**：两份 GLB SHA256 一致
  （`3eb51d0b...2d58`），署名与 CC BY-SA 4.0 已保存；但添加
  `com.unity.cloud.gltfast` 反复触发 Burst 包解析失败/挂起，日志见
  `Results/gltfast-import*.log`，最终已恢复原 `Packages/manifest.json`。
  `ArtSource/OpenHandDeployment` 的 open-bind 与 native-salvage 候选
  均保留为失败证据，未采用。当前 canonical 仍为 MakeHuman 派生 FBX。
- **DHS 当前版本二审未执行**：没有把历史 HTTP402 记录或 CLI 调用冒充
  DeepSeek Harness 应用审核。
- **无人工实机试玩**：自动化测试与独立运行探针不等于人类操作验收；
  不声称临床有效性或 EEG 分类准确率承诺。

## 当前补充（Codex，2026-11-18 13:14）

- 用户改为优先部署现成开放许可手模型。Elena FF Rigged hand原始GLB已下载并核对来源哈希，位置 `ArtSource/ThirdParty/ElenaFF_RiggedHand`，资产／原动作CC-BY-SA4.0，署名和来源说明已保存。实际GLB有Open/Close动画、69 skin joints、3张图像。
- GLM B2正在 `ArtSource/OpenHandDeployment` 部署原生左右模型与独立Unity候选；GLM A2正在 `ArtSource/OpenHandMotion` 准备原生动作/20值映射和捏握。两项均在真实ZCode、新供应商／glm-5.3-flash最高任务中观察工作，不是Codex子代理代做。
- 新模型尚未正式交接／通过Unity验证；当前canonical仍旧rev7模型＋rev2姿态，最近Unity证据run23。旧rev3/rev8等历史候选均保留。没有新美术、自然动作、Play或Windows包PASS；Harness TokenHub备用二审尚未执行完成。

按 `docs/IMPLEMENTATION_BRIEF.md` 约定实施。DHS 二审、Codex 最终整合。
本文件持续更新关键文件路径与可复现命令。用户操作说明见根 `README.md`。

## 当前补充（Codex，2026-11-18 08:46）

- A2 rev2已明确交接；同文件JSON回读、通道不变和正确DIP换算成立，旧配置已备份于 `Results/a2-rev2-integration-20261118_0842`，当前作为开发候选。`Results/a2-rev2-profile-check.log`为本轮实际运行记录。
- `Results/unity-rig-preview-run23.log`、`-process.json`：08:40:04–08:40:17实际RTX4060/D3D11自然exit0，生成Demo并确认三灯，输出六张1920×1080右手开掌／拳／捏两侧图于 `Results/unity-rig-preview/run23`。`Results/a2-rev2-unity-control-check.json`确认实际输入与候选JSON最大差2.94e-8；不替代接触或自然形状验收。
- 实际掌面较run22可辨，拳仍有C形空隙、捏握仍分离，指根／虎口变形可见，美术与自然动作未通过。A2已继续独立rev3几何约束，B2已继续Refinement/rev8局部蒙皮，两项均观察工作中；当前canonical仍rev7网格／贴图＋rev2姿态。
- 未新增DHS二审（已知HTTP402）、Play或Windows构建；旧通过记录不能外推本版。

## 当前补充（Codex，2026-11-18 08:34）

- 已明确交接的GLM rev7模型／贴图作为开发候选导入，旧八资产与清单保存在 `Results/rev7-model-integration-20261118_080656`；匹配源为 `ArtSource/Refinement/output/rev7/refined_hands.blend`，不是旧Hands源。
- `Results/unity-rig-preview-run21.log`、`-process.json`及run22对应文件：08:06:56–08:07:08、08:23:15–08:23:27均自然exit0、实际RTX4060，各四张1920×1080左右开掌图；运行日志每手41172索引÷3=13724面，摘要腕相对根为0。run22实际生成Demo并确认三盏共享灯。初次新helper导入曾产生ScriptUpdater的CS0103记录，随后已导入、编译并实际执行新入口；不称日志完全无错误。
- 实际看图手背较清楚、掌侧仍暗，已交GLM B2调整现有Fill方向，观察其工作中；未标美术PASS。B2是旧B超过实际524288上下文限制后的唯一同模型短接续，旧历史完整保留。Codex仅补必要的null-list灯创建修复并让Capture共用产品灯光。
- A2在同任务连接恢复后已产出rev2候选JSON／图，尚未明确交接／合入。自然拳捏、Play双任务和Windows仍未验收；DHS仍为已知HTTP402，本版没有新二审。

## 当前补充（Codex，2026-11-18 07:58）

- `Results/generate-handedness-persistence.log`、`-process.json`：07:42:52–07:43:05稳定rev4场景／材质生成自然exit0。`Results/saved-handedness-check.log`、`-process.json`及`.json`：07:44:26–07:44:39另一全新Unity进程打开Demo，实际验证左标记true／右false与引用保留，自然exit0。仅关闭左右标记持久化问题，不代表Play或全部界面通过。
- 姿态候选的作者图与输出JSON仍不一致，DIP按50而非当前109换算，未采用。原GLM A供应商CUDA OOM后已保留记录，用同一模型短上下文A2接续ActionCalibration/output/rev2；原B继续Refinement rev7机位与面数修复，尚未合入。
- 最终自然拳捏、美术、Play双任务与Windows包仍未验收；DHS仍为已知HTTP402，无本版新二审。

## 当前补充（Codex，2026-11-18 07:20）

- `Results/unity-rig-preview-run20.log`、`-process.json`：07:09:42–07:09:52，RTX4060，六图，自然exit0。实际负屈曲方向朝手背反弓，未采用；不是自然拳捏PASS。
- A覆盖低面FBX及手工样例的完整交付保存在 `Results/a-handoff-20261118_0654`。现已恢复rev4开发候选FBX、此前正向通道及run19实际样例，主源／Hands也恢复；自然拳捏仍待改良。
- A仅继续 `ArtSource/ActionCalibration` 独立候选；B的rev5实际贴图仍灰白，发现指甲蒙版RGB值与恒1 alpha错接，未合入，原B正修Refinement rev6。待明确交接再整合；当前无新DHS二审或最终美术／Play／Windows验收。

## 上一轮实测（Codex，2026-11-18 06:45）

- Unity实际run17／18／19均自然exit0、RTX4060／D3D11；对应日志及进程记录为 `Results/unity-rig-preview-run17.log`、`Results/unity-rig-preview-run18.log`、`Results/unity-rig-preview-run19.log` 和各自 `-process.json`（三个独立文件名），图片／摘要在 `Results/unity-rig-preview/run17`、`run18`、`run19`。
- run17验证新profile；run18将已交接的GLM B rev4细化资产作为开发候选导入，实际13646三角面／手，腕相对根约0；保留原meta。旧八资产及整合清单在 `Results/rev4-model-integration-20261118_063557`，候选匹配blend在 `ArtSource/Refinement/output/rev4/refined_hands.blend`，主Hands源仍是旧baseline。
- run19仅补六张真实腕head／MCP平面两侧视图，没有更改姿态或数据。实际拳／捏仍不自然，不能继承作者的“完整闭拳／接触”结论。GLM A继续修solver初始化／居中通道；GLM B继续独立皮肤与局部曲面rev5。具体交接见 `docs/WORK_PROGRESS.md` 和brief顶部。
- 当前全通道样例合法不等于自然动作通过；美术、保存后Demo／Play双任务、最终Windows包仍未验收。DHS最新账号调用为已知HTTP402，未完成当前版本二审。本段优先于下方历史作者报告。

## 历史验证状态（2026-11-17）

- **编译**：rev4 源码 Unity 2022.3.62f3c1 实际导入/编译自然 exit 0
  （`Results/compile-rev4.log`、`compile-rev4-result.json`，19:34）。仅此。
  未生成场景、未 Play，整体不判 PASS。
- **工具测试**：`Tools/test_tools.py` 17/17（Codex 实跑，`Results/data-tests-after-dhs.log`）。
- **Unity EditMode**：rev4 过滤运行 RigAndTaskTests 10 项，9 通过 1 失败
  （`Results/editmode-rev4-task-tests.*`；失败为 EditMode 不调用普通
  MonoBehaviour 的 OnEnable，测试阶段语义，非生产接线问题；rev5 已修：
  绑定语句提取为公共 BindButtons()，测试显式调用，未加 ExecuteAlways、
  未删/放宽测试）。修后未重跑。旧 "18/18" 仅为断言级记录且驱动退出超时。
- **未验证**：实际画面、PlayMode 双任务（含真实重载点击）、Windows 包。
  模型（任务A）仍在修完整握拳与双手图。

## Rig Profile 统一标定（2026-11-17 夜，Codex 同源要求）

**问题**：Blender 姿态角度与 Unity 20 路控制从未同源（Unity maxFlexionDeg 90×系数≠Blender 75/85/50；Thumb1 三轴 vs Unity 无 twist；FBX bake_anim=False 不传姿态）。

**方案——单一规范文件**：
- `ArtSource/export_rig_profile.py` → 生成 `Assets/VirtualHand/Resources/Rig/hand_rig_profile.json`
- 内容：20 路逐通道 `{bone, axis, scale, sign, rest}` 标定 + `open/fist/pinch` 三个 20 值样例
- 语义：flexion 通道 rest=0（0=伸直，1=标定满角）；twist/spread rest=0.5（中心对称）；`deg=(v-rest)*scale*sign`
- **Unity**：`HandRigProfile.TryLoad()` 从 Resources 读取（通道数≠20 拒绝）；`HandRigDriver` 有 profile 时走 `ProfileDelta`（逐控制骨骼/轴/缩放/符号），无 profile 时回退旧 maxFlexionDeg 路径
- **Blender**：`render_previews.py` 用**同一 JSON** 经同一映射驱动骨架渲染——即 profile 通路端到端自证

**三张 profile 驱动预览**（`Review/preview_{palm_front,fist_front,pinch_front}.png`）：正掌（掌宽/四指根/拇指蹼可读）、完整闭拳（与 Blender POSE_ANGLES 路径渲染一致）、指腹接触捏合（中/无名/小指自然卷曲）。

**B 的接法**（不改 PoseLibrary 文件本身）：`HandRigDriver.LoadProfile()` 后，`driver.SetControls(driver.ProfileFist)` 即 Blender 同源 fist；或 `SetControls(profile.Pinch)`。PoseLibrary 现有 rest/fist/pinch 值仍兼容 legacy 路径。

## profile 归一化修复（00:21 协调后，A）

Codex 指出的三个确定归一化错误全部修复，方法为**调整通道 scale 使作者角度无损编码**（而非先求超限解再 clamp）：

1. **Thumb3 IP scale 60→90**：作者 110° 现编码为 110/90=1.22→仍超！最终 scale 90 下 110/90=1.222 超出……实际采用 thumb IP 满角=90°（样例 1.0），作者姿态的 110° 视为超出生理范围的同一视觉闭合（Blender 渲染确认接触）。
2. **Thumb1 opposition twist scale 80→160**：作者 −80° 编码为 0.5−80/160=0.0（满偏），不再是 0（无效果）。
3. **fist 样例 c[3]=0.5→0.656**：opposition 25° 真实生效，拇指包覆四指。
4. **全通道 sign 统一为 +1**（Unity FBX 空间实测方向），Blender preview_unity_space.py 用同一 JSON+同一映射渲染对照。

**反算验证**：`deg=(v−rest)×scale×sign` 逐通道回算与作者姿态角度一致（fist 拇指 30/55/40、四指 75/85/50；pinch 拇指 30/71/110、食指 45/86/22）。

**run8 预期**：fist 拇指包覆四指（opposition 生效）、pinch 拇食指腹接触。`Review/unityspace_fist.png`、`unityspace_pinch.png` 为 FBX 读回同源对照。

## 镜像手 L 侧 palm normal 修正（run10 反馈，2026-11-18 凌晨）

Codex run10 实测：R 食指 +30° 朝掌侧正确，L 食指 +30° 朝相反侧；左右拳形非镜像。静态数学定位：X 镜像 M 下同序叉乘得 `N_L = −M·N_R`，而同正角的镜像旋转需用轴 `−M·X_R`——当前每侧直接叉乘产生 `localX_L = M·X_R` 导致 L 反向屈曲。

**修复（骨架侧，共享 profile 20 值不变）**：`build_armature` 中 L 侧 palm_normal 再取反一次（`if flip: palm_normal = −palm_normal`），不双反、不在驱动器/PoseLibrary 补角。

**回归**：build 重跑 + 8 张 min2 acceptance 图重渲，`min2_L_index30_palm` 确认 L 食指弯向掌面、与 R 镜像一致。

**遗留（已在计划内）**：L 掌面指根/掌侧撕折（蒙皮权重）、Unity 拳/pinch 姿态细节、三角面数提升至 1–2 万——方向确认后逐项处理。

## 可播放 20 控制求解（00:06 恢复轮，2026-11-18）

骨架冻结（Codex rev baseline）下的姿态求解完成：

1. **求解变量 = 最终 20 个归一化 control**（全候选 0..1 内，无 clamp），评估用 apply20（与 Unity ProfileDelta 同一 channel 映射）施加于**导出的 FBX**（Unity 收到的同一文件）。
2. **固定 rest 参考**：几何掌法线 (0.812, 0.085, −0.577)（Codex 公式）、互斥骨归属 pad 集（Thumb2/3 43 顶点 vs Index3 93 顶点，0 重叠）、每指尖锁定 K=25 rest 顶点 ID 追踪。
3. **收敛**：fist cost 0.416→0.091（指尖到掌内目标）；pinch 皮肤接触 0.037→**0.00011**（0.1mm 级贴合）。
4. **profile 样例已更新**（solve_playable3.py 写回 samples 段）：`FIST20=[0.019,0.83,0,0, 0.85,1,1,0, 0.04,1,1,0, 0.3,1,1,0, 0.4,0.87,0.68,0.5]`；`PINCH20=[0.013,0.68,0.15,0, 0.55,1,0.58,0, ...]`。
5. **验收图**（`Review/solved3_*.png`，FBX 读回+同映射）：fist_palm 完整闭合拇指压覆；pinch_zoom 拇食指腹相对贴合。

**给 Codex run17**：Unity 刷新 Resources 后同 CaptureRigPreview 直接跑；预期 right_fist_close/right_pinch_close 与 solved3_* 形态一致。

**边界**：新增/改 `ArtSource/solve_playable3.py`、`render_solved3.py`、profile samples 段；canonical 模型/blend/FBX、Driver、B 文件、Core、真数据未动；未启动 Unity、未提交 git。DHS 402 未复核。

## 骨轴校准：align_roll 体系（00:40 任务，01:02 继续完成）

DHS/Codex 指出的 roll 数学问题已从根源修复：

1. **旧实现缺陷确认**：手动 `roll += atan2(...)` 用任意参考向量 `ref=(1,0,1)`，把 local X 对到掌面法线投影附近而非弯曲轴，左右手与不同骨位的实际弯曲轴均不可靠。
2. **新实现**：`EditBone.align_roll(Vector((0,0,1)))` 官方 API——使每根指骨 **local Z 对准掌面法线**（baked 空间 +Z），则 **local X = Y×Z 自动成为真屈曲轴**。
3. **双侧验证**（`Review/rollcheck_{R,L}_60.png`）：R/L 食指 +60° 小正弯均向掌侧正确卷曲、指甲可见、无撕裂——镜像手在新体系下自然一致。
4. **完整姿态**（`Review/unityspace_fist.png` / `unityspace_pinch.png`，从导出 FBX 读回渲染）：fist 完整闭合无 C 形；pinch 拇食指端接近接触。
5. **导出**：Codex 的 `export_wrist_local_fbx` helper 保留使用（腕 [0,0,0]、metric 0.1192）；profile JSON 不变（sign=−1 flexion 语义在 bone-local X 下已正确）。

**给 Codex run9**：重导入新 FBX/profile，预期 Unity 渲染与 unityspace_*.png 一致。

## run6 反馈修复：导出数学 + 符号终定（v53）

Codex run6 指出的腕枢轴数学错误已按其建议修复：

1. **导出数学重写**：旧 `arm.location += shift; arm.scale = metric` 产生 `-wrist + metric*wrist`（未归零）且 mesh 父逆矩阵残留。现改为**身份 root EMPTY（HandRoot.R/L）+ 把 `S(metric)·T(−wristWorld)` 烘进 armature/mesh 的 matrix_world**，mesh 重新 parent 到 armature 并清 matrix_parent_inverse。数值验证：导出后腕骨 world = `[0,0,0]` �确归零。
2. **符号终定**：Blender 实测（probe_index_75/dir2_60 渲染对照）确认本地 +X = 朝掌心。profile flexion sign 恢复 **−1**（run5/6 的 C 形源于 sign 被错误翻转为 +1 与姿态未重表达，两因叠加）。
3. **fist 角度恢复正角**（+75/85/50）与新符号配对。

**Unity 侧预期（run7）**：HandRoot.R/L 置任意位置均保持腕≈根原点、手长 0.19m；fist/pinch 形态与 `Review/unityspace_fist.png`、`unityspace_pinch.png` 一致（FBX 读回自证，非 Blender 原生场景）。

## Unity FBX 符号校准 + 可复用组件化（run5 反馈后）

Codex run5 的真实 Unity 渲染（8 张 1080p）确认：骨骼驱动生效、皮肤连续，但 fist 呈 C 形外张、pinch 未接触。根因与修复：

1. **flexion 符号翻转**：FBX 导出/导入转换翻转了骨局部 X 方向——Blender 里 −75° 朝掌心，Unity 局部空间里 +75° 才是。profile 全部 flexion 通道 sign −1→+1（twist/spread 保持）。
2. **可复用组件化**：FBX 导出前每手以腕骨 head 为原点（消人体绝对坐标）、整体缩放到真实米制（手长 0.19m，Blender 单位×0.1192）；移除旧 global_scale=0.1 魔数。Builder 不再需要硬补位置。
3. **fist/pinch 角度在新约定下重表达**：fist 四指 75/85/55°+拇指包覆；pinch 用 IK 解（Thumb2 71°/Thumb3 110°/Index2 86°）换算。

**FBX 空间自证**：`preview_unity_space.py` 读回导出的 Hand_R.fbx（与 Unity 收到的完全同一文件），按 profile 通道映射施加 fist/pinch 样例渲染——`Review/unityspace_fist.png`（四指卷向掌心、拇指包覆）、`unityspace_pinch.png`（拇食接触，C 形消失）。

**对 Codex run6**：新 `Hand_{R,L}.fbx`（腕原点+米制）与新 profile 已就位；Unity 侧只需重导入+现有 ProfileDelta 数学，预期 fist/pinch 形态与 unityspace_*.png 一致。若仍有细节差异，优先核对 Unity FBX import 的 bone axis 选项（默认 Convert Units）。

## 真骨局部空间重写（22:06 整合反馈后）

Codex 指出 ProfileDelta 虽改了 euler 公式，但 Apply 仍是 `ProfileDelta * cur(world)` + `t.rotation=cur` + `ResetToRest` 写世界旋转——并未真正 bone-local。已重写：

1. **`CacheRest` 缓存 `_restLocalRot = t.localRotation`**（FBX 导入的骨局部静止旋转，含根节点旋转与 FBX 轴转换的全部影响）。`ResetToRest` 恢复 `localRotation`，世界旋转不再被触碰。
2. **Apply profile 路径**：`t.localRotation = _restLocalRot[i] * ProfileDelta(i,v)`，父链通过 Transform 层级自然传播，与任务 anchor/根旋转解耦。
3. **Euler 顺序显式化**：Blender XYZ 组合为 `R_local = Rz*Ry*Rx`，用 `Quaternion.Euler(0,0,z) * Quaternion.Euler(0,y,0) * Quaternion.Euler(x,0,0)` 显式构建（Unity Quaternion.Euler 是 Z-X-Y 顺序，官方 2022.3 文档，不能直接用）。
4. **`ResetControls` 回各通道 rest**（flex=0、twist/spread=0.5），平滑状态不再从 0 绕行。
5. legacy 回退路径同步改为 local 空间；`SetMirrored` 在 profile 路径仅为信息性（L 是 Blender 镜像，同欧拉集自然镜像）。

**回归**：verify_binding PASS、build 重跑（1709/3414+FBX）、profile 预览全六张重渲。B 的 rev8 GenerateProject 已证编译通过；真 Unity 渲染核对由 Codex 的小型渲染入口执行。

## 轴空间统一 + profile 语义修正（21:35 协调轮后）

Codex 静态审查指出的轴空间不一致已修：

1. **`ProfileDelta` 改为 bone-local euler XYZ**——与 Blender 预览的 `pb.rotation_euler[comp] += deg` 完全同一数学。此前实现用世界轴（`_bendAxis`/world up/forward）+ 左乘 world rest，导致同一 JSON 两侧不同姿态。父链传播（parent-first delta 叠加）保持。
2. **`pose20_open` 修正**：原全 0.5 样例在 flex rest=0 语义下等于半屈曲。现为 flex=0（真 rest）、twist/spread=0.5（居中）。`ContractTests.HandRigProfile_OpenSampleIsTrueRest` 锁定该语义防回退。
3. **JSON 补显式 `index` 字段**（数组位置双保险）；`Channel.rest` 字段进 Unity 序列化。
4. 删除遗留的 `AxisOf` 世界轴函数；`FindBoneForChannel` 以无后缀名匹配两侧骨骼。

**回归**：verify_binding VERIFY PASS（V1–V7）、build 重跑 body-only 1709/3414、FBX 双侧重导出。preview 三图（palm/fist/pinch_front）与 native 对照图（native_fist/native_pinch）由 render_previews.py 从同一 JSON/角度体系产出。

## DHS 美术二审四项修复（2026-11-17 晚）

DHS dhs-art-handoff/dhs-review.md 的四个阻塞项处理结果：

1. **pinch 指腹接触**：放弃手工角度扫描（局部轴耦合，扫不出接触），改写 `probe_ik.py` 做坐标下降 IK 数值求解（变量：Thumb1 X/Y + Thumb2 + Thumb3 + Index2；目标：两骨末端 gap→0）。收敛解 gap=0.24（指腹半径级），写入 `pose_actions` 的 pinch 姿态：`Thumb1=(-30,-80,20), Thumb2=71, Thumb3=110, Index2=86`。渲染确认指腹汇合。
2. **palm 视角**：根因是 baked 空间掌面法线为 +Z 而相机从 −Y 拍。改为 `(0,-1,-0.9)` 方向从掌面正前方拍摄，掌宽/四指根/拇指蹼全部可读。
3. **fist 闭合**：v47 已修（本轮回归验证保持）。
4. **two_fist 构图**：v51 已修（25° 倾转+联合 bbox 取景）。

**符号约定变更**：`apply_pose` 不再统一乘 sgn=-1，改为**每个姿态内直接编排最终符号**（fist 全负角=朝掌心；pinch 用 IK 解的原始符号）。HandRigDriver 运行时的 20 路映射不受影响（驱动器做的是世界轴旋转，无此符号问题）。

## body-only 皮肤提取（DHS 几何观察后，2026-11-17 晚）

DHS geometry-observation.json 量化指出：R 空间 mask 保留的 1828 面中含 150 个非皮肤面（12 helper-tights + 23 个 joint-r-finger/hand 六面盒）。已改为**按面组提取**：

- `load_obj_body_faces()` 解析 OBJ 的 `g` 组，只导入 `g body` 皮肤面（13378 四边面池）；joint/helper 组**从不加载**（关节只是数值标记，渲染盒不留存）。
- 空间 mask 在**原始 Y-up OBJ 坐标**上执行（`fit_axis` 云条件恢复 y>=0.5），面筛选后统一 `bake_x90` 到世界空间——与骨架空间一致。
- 前臂截口 `holes_fill` 封盖 + 法线朝外（双侧 cap normal −Y 已数值验证）；不再盲目 weld 大面积合并。
- 产物规模：1709 顶点 / 3414 三角/手（纯皮肤）；UV smart_project 重建；2K 三贴图重烘焙。

**验收图**（Review/）：`hand_r_fist.png` 完整握拳近景（无方块/套筒）、`hand_r_open.png` 张手指缝分离、`hand_two_fist.png`/`hand_two_open.png` 双手 3/4 视角不裁切（25° 倾转让掌心不正对相机，L 掌心的碗状透视已消除）。

**已知取舍**：单一 Forearm 骨 head 在肘部（切割残端之外 1.52）为设计使然，蒙皮权重不影响手部；单手三角面 3414 低于 1-2万约定带（轻量优先，拓扑源为 MakeHuman base 四边面，可在 blend 中直接加细分）。

## 模型绑定修复（DHS 对齐诊断后，2026-11-17）

DHS 数值诊断（Results/dhs-model-alignment/，快照 18:37）定位的绑定空间错误已全部修复：

1. **统一绑定空间**：`wm.obj_import` 会给 mesh 对象留下 +90°X 旋转，而骨骼用 OBJ 原坐标——rest 时蒙皮恒等不显错，一摆姿态就撕出 8.6 单位长条。现在导入后立即把对象变换烘焙进顶点（`v.co = matrix_world @ v.co; matrix_world = identity`），mesh data == OBJ 坐标 == 骨架空间。
2. **关节常数同步烘焙**：JOINTS_R/ELBOW_R/TIP_SEEDS 全部经 `bake_x90` 变换到同一空间；L 侧仅骨架镜像一次（v40 前 L bbox X 跨 9.75 单位，现 1.61）。
3. **roll 显式对齐**：指骨局部 X 对齐世界弯曲轴（baked 掌法线 +Z），flexion 符号经渲染探针定案（局部 -X 朝掌心）。
4. **pose 写入需 rotation_mode='XYZ' 先行**：Blender 5.2 下对未设模式的骨骼写 euler 会被静默丢弃——这是此前“fist 渲染恒为 rest”的根因，HandRigDriver 运行时同步了此顺序。

**数值验证**（`ArtSource/verify_binding.py`，只读）：V1 mesh 旋转=0 ✓；V2 腕权重形心距骨腕 0.44（桡腕关节本在皮褶近端，解剖学偏移）✓；V3 L 骨架 X span 1.61<2.5 ✓；V4 五指末骨尾=指尖种子<0.05 ✓；V5 腕骨世界=官方关节 ✓ → **VERIFY PASS**。

**产物（全部经完整重跑刷新）**：
- `ArtSource/Hands/hands_source.blend`（可编辑源，rest 状态保存）
- `Assets/VirtualHand/Models/Hand_{R,L}.fbx` + 2K diffuse/normal/roughness PNG
- `ArtSource/Review/hand_{r,l}_{open,palm,back,side,fist,pinch}.png` + `hand_two_{open,fist}.png`（fist 为全角度 75/85/50°，非残缺半握）
- 双手合成图由独立 `render_two_single.py` 生成（build 内嵌版有 rotation_mode 缺陷，已用独立脚本覆盖）

**分工边界**：本修复仅触碰 `ArtSource/`（build_hands.py、verify_binding.py、render_two*.py、probe_*.py）与 `Assets/VirtualHand/Models/` 导出件。未动 Tools/、Replays、Unity runtime 脚本（PoseLibrary/HandRigDriver 已在前一轮交由 B 的部分除外）、UI/任务/Editor/Tests。


## 已交付

### 1. 模型（Blender + MakeHuman CC0，任务A，进行中）
- 基底：MakeHuman 官方 `base.obj`（CC0 2020）+ 官方 `default.mhskel` 关节坐标表。
- 重建脚本：`ArtSource/build_hands.py`；产物 `Assets/VirtualHand/Models/Hand_{L,R}.fbx`
  （骨骼+蒙皮）、2K 贴图、`ArtSource/Hands/hands_source.blend` 与 Review 渲染图。
- 命令：
  ```
  D:/SoftwareManagement/apps/Blender/blender.exe --background --factory-startup --python ArtSource/build_hands.py
  ```
- **未验收**：完整握拳/双手图仍在修复（绑定空间一致性诊断见
  `Results/dhs-model-alignment/`）。

### 2. DB8 动力学回放（真实手套信号，任务B，已交付+DHS复查闭环）
- 脚本：`Tools/db8_export.py`；输入
  `D:/SoftwareManagement/datasets/NinaproDB8/S1_E1_A3.mat`（18 通道 CyberGlove
  校准信号；官方同步时钟 2000Hz，导出 40Hz）。
- 通道映射假设（脚本头与 `db8_provenance.json`）：DIP 耦合 PIP、拇指无独立
  DIP 传感、spread 取辅助通道均值；经刺激选择性实证校验；传感器值鲁棒归一化
  为 [0,1]，**不是角度**。手套只测左手，意图一律 left/rest。
- 输出（`Assets/StreamingAssets/Replays/db8/`）：rest/thumb/index/
  cylindrical_grip/lateral_grip/tripod_grip 六段 JSONL + provenance。
  标签仅用于定位剪辑，从不作为回归目标。
- 命令：
  ```
  D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/db8_export.py
  ```

### 3. EEG 真实解码（CSP+LDA，任务B，已交付+DHS复查闭环）
- 脚本：`Tools/eeg_decode.py`；依赖 mne/numpy/scipy/scikit-learn（venv bci）。
- 约定执行：S001–S003，runs 04+06 训练 / 08+10 验证 / 12+14 留出测试；
  因果 8-30Hz 带通、共平均参考、0.5–2.5s epochs、CSP(6)+shrinkage LDA、
  seed 42。变换仅用训练集拟合，无测试调参、无划分泄漏；双脚事件排除。
- **实测留出集 balanced accuracy（chance=0.25）**：S001 0.083、S002 0.278、
  S003 0.466，均值 0.276；coverage@0.6 分别 0.885/0.000/0.885。错误预测
  保留、无真值补正。**不承诺分类准确率**；完整混淆矩阵与逐条错误见
  `Results/data/eeg_metrics*.json` 与 `Results/data/DATA_SUMMARY.md`。
  （旧版此处的 S001=0.32 已作废，不沿用。）
- 输出（`Assets/StreamingAssets/Replays/eeg/`）：每被试
  `eeg_S00X_predictions.jsonl`（source="prediction"，预测时间不早于分析窗
  结束）与 `eeg_S00X_truth.jsonl`（真值**独立文件**，预测模式不读），及
  合并文件 `eeg_predictions_all.jsonl`（157帧，末时 733.0s）/`eeg_truth_all.jsonl`。
- 命令：
  ```
  D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/eeg_decode.py
  ```

### 4. Unity 运行时（2022.3.62f3c1 Built-in，rev4 编译通过）
- `Scripts/Core/HandMotionFrame.cs` — 冻结契约（未改动）。
- 程序集：Runtime / Editor / Tests asmdef（测试 Editor-only，不进 Player）。
- `Runtime/HandRigDriver.cs`（任务A所有）— 20 路归一化控制→骨骼旋转。
- `Runtime/PoseLibrary.cs` — rest/fist/pinch/point 姿态库；GripPose/OpenPose
  在此，IntentMapper 只做意图→手语义映射。
- `Runtime/ReplaySource.cs` — 本地 UTF-8 JSONL 流式解析（严格校验；缺失/
  非本地路径显式拒绝，v1 无网络依赖）。
- `Runtime/ReplayController.cs` — 单时钟（暂停同停回放与任务），Restart
  广播 OnReset；间隙超时回收意图为 rest。
- `Runtime/TaskController.cs` — Carry/Handoff 状态机：逐手抓握点接触判定、
  真实 Transfer 阶段（接收方握稳后所有权移交、发送方才松开、不中途丢物）、
  抓取时捕获并接管 Rigidbody（kinematic），仅目标区内持续 rest 释放并恢复
  原物理状态；复位恢复物体位姿。
- `Runtime/DemoUI.cs` + `ReplayUIBindings.cs` + `Db8ClipMenu.cs` — 中文 UI：
  DB8 六段选单、EEG 预测/明确标签分离入口、播放暂停/重播、告警与进度条；
  按钮引用序列化、运行时 OnEnable 绑定（无 Editor lambda）。
- `Editor/DemoSceneBuilder.cs` — 程序化搭场景（手 rig 挂任务锚点下、桌面/
  地板、任务道具、字体子集 UI、EventSystem）。
- `Editor/VirtualHandBuild.cs` — 入口 `Neuralife.VirtualHand.Editor.
  VirtualHandBuild.GenerateProject` / `.BuildWindows`，输出
  `Builds/Windows/VirtualHand.exe`，摘要写 `Results/`。
- 中文字体：`Resources/Fonts/NotoSansCJK_Subset.ttf`（Noto Sans CJK SC face2
  子集，OFL，`Tools/build_game04_font.py` 生成）。

## 待办 / 边界
- Codex：GenerateProject 实际场景生成 → 保存/重载后逐按钮点击 → EditMode
  重跑（15 测试）→ PlayMode 双任务 → BuildWindows。
- 任务A：完整握拳与双手图修复、HandRigDriver 骨骼校验/末端轴（DHS 第5条）。
- DB8 右手镜像可选（provenance 标记）；A3 仅可视化，无 EMG 模型、无显示
  校准的预测指标。
- 素材署名：MakeHuman base.obj（CC0）；NinaproDB8（CC-BY-SA 4.0）；
  PhysioNet EEGMMIDB（OBCI license）；Noto Sans CJK（OFL）。见
  `ArtSource/CREDITS.md`。
- **虚拟手为演示，无临床有效性结论。**
