# Game03 建模优化 — 第一轮视觉替换门控结果（2026-11-23）

## 结论（诚实口径）

**门控：部分通过。** 独立测试场景证明三类真实资产都能在 Unity 2022.3 Built-in 管线中正确导入、显示并显著优于旧的程序几何体；但**尚未扩展到整条街道 S01–S03**，也**未重新打包**，因为写实度与成本仍有明确限制（见下）。

- 截图：`Results/visual-upgrade-test/real-assets-kerb-1080p.png`（1920×1080，路沿第一人称视角）
- 场景：`Assets/StreetCrossing/Scenes/VisualUpgradeTest.unity`（独立，未改动 S01–S03 与交通逻辑）
- 来源/授权：`Assets/StreetCrossing/Art/External/ATTRIBUTION.md`

## 实际完成（P0.1 → P0.5 严格按序）

| 阶段 | 结果 | 证据 |
| --- | --- | --- |
| P0.1 Blender | 本机已有 **Blender 5.2.3 LTS**，高于 MTree 要求的 4.5，无需下载 | `blender.exe --version` |
| P0.2 工具/授权 | MTree 5.6.0（GPLv3）、Building Tools 1.0.13（MIT）、Ara 3D Gen（CC0 MakeHuman 数据）均已获取，许可证与源归档保存在 `third_party/` | `third_party/` 目录 |
| P0.3 候选模型 | 3 名写实行人（21–35k tris，69 骨骼 Unity-Humanoid）、3 棵真实分枝树（6–25k 顶点）、1 栋真实窗洞建筑（220 顶点/204 面） | `output/` + Blender 预览 |
| P0.4 独立测试场景 | 一名行人 + 两棵树 + 一栋建筑，路沿 1.65 m 相机；未触碰 S01–S03 | `VisualUpgradeTest.unity` |
| P0.5 1080p 门控 | 截图确认三类资产同时可见；修复了 FBX 贴图未提取、树干白色、建筑 0.01 缩放三个实际导入问题 | `Results/visual-upgrade-*.log` |

## 关键的技术修复

1. **Building Tools 与 Blender 5.2/Python 3.13 不兼容**：上游 `options.py` 使用可变 dataclass 默认值，且 `-b` 模式错误弹窗会崩溃。制作了 `building_tools_py313/` 补丁副本（保留原件备份）后成功生成真实门窗洞。
2. **Ara FBX 纹理不自动进 Unity**：编写 `VisualUpgradeImporter.FixAll`，用 `ModelImporter.ExtractTextures` 提取全部贴图，人物从白色人偶恢复为带肤色/衬衫/牛仔裤的写实角色。
3. **MTree 树冠 FBX 爆量**：几何节点叶片导出高达 227–727 MB（500 万+顶点）。改为「真实分枝 + 轻量低多边形树冠」方案，单树降到 0.45–1.6 MB；树干补棕色树皮材质。
4. **Building Tools FBX 0.01 缩放**：Unity 导入后仅 0.11 m，测试中按 100× 缩放恢复到 10.5 × 8.64 × 4.2 m。

## 未达标 / 明确限制（不得冒充通过）

- **写实度**：行人是真实人体与贴图（明显优于方块人偶）；树有真实分枝但树冠是刻意低多边形/风格化，**不是照片级**；建筑窗洞是真的几何开口，但立面仍是单一材质、细节少于现有 S01–S03 的程序化立面。整体是「明显改善形体」而非「写实街景完成」。
- **未扩展**：只做了 1 行人 / 2 树 / 1 建筑；报告要求的「3 行人 / 2–3 树 / 1 建筑整街对比」尚未执行。
- **未打包**：没有生成新的 Windows 版本；现有构建仍是旧模型。
- **成本**：行人 FBX 各约 69 MB（内嵌 2K 贴图）。纹理降采样尝试反而增大文件，暂未应用；若扩展到整街需先解决资源体积。
- **GPL 注意**：MTree 生成的树按 GPL 工具输出处理，未做再授权声明；若用于分发需法务确认。
- **未使用 tevy.dev**：站点在本机代理下 502 不可达；KinemIQ 动画包可下载但只有动画、无网格，Ara 人物自带骨骼故未集成。
- **批处理退出**：Unity `-batchmode` 包装进程因工程既有 `[InitializeOnLoad] StreetArtSetup` 刷新循环持续运行，证据文件写完后由外部停止；日志中 capture complete 与截图文件为判定依据。

## 第二轮精修（用户反馈“好丑”后，2026-11-23）

用户明确要求「树和建筑模型都需要更精细」，因此没有只改场景灯光，而是重做了模型本身：

| 项目 | 第一轮 | 第二轮精修 |
| --- | --- | --- |
| 树 | MTree 真分枝 + 12–16 个大低模叶球，轮廓像炸开的灌木 | 程序化干净主干/分级枝条 + 150–260 个压扁、随机旋转的小叶簇；轮廓更像行道树 |
| 建筑 | Building Tools 真实窗洞，但只有单一墙面 + 两条玻璃带 | 手工分区墙面真实留洞 + 窗框/窗台/竖向与横向窗棂 + 木门 + 雨棚 + 阳台栏杆 + 层间檐口 + 屋顶女儿墙 |
| 验证场景 | 默认棋盘格地面、无道路语境 | 中性地面 + 沥青路面 + 路缘 + 斑马线；只改独立测试场景 |

**本轮证据**

- 新截图：`Results/visual-upgrade-test/refined-assets-kerb-1080p.png`（旧截图 `real-assets-kerb-1080p.png` 保留，未覆盖）。
- 新场景：`Assets/StreetCrossing/Scenes/VisualUpgradeTest_Refined.unity`（旧 `VisualUpgradeTest.unity` 保留）。
- 新资产：`Trees/*_Refined.fbx`、`Buildings/NearFieldResidence_Refined.fbx`。
- 生成脚本：`generate_trees_refined.py`、`generate_building_refined.py`；快速预览 `preview_refined_assets.py`。
- 单资产大小：三棵树约 0.42–0.72 MB，建筑约 33 KB，全部远小于早期 MTree 叶片 227–727 MB 的爆量导出。

**过程中修掉的真实问题**

1. Blender 的 `-Y` 正面经 FBX 导入后映射到 Unity `+Z`，首次截图看到的是建筑空白背面；保留导入自带的 Z-up 修正后再绕 Y 轴转 180° 才露出正立面。
2. 新 FBX 导入后树被缩到约 0.01 倍，只剩两个绿点；改为按实测包围盒高度归一化到 7.2 m / 6.8 m。
3. 建筑同样按实测宽度归一化到 10.3 m，不再硬编码 100× 缩放。

**仍然诚实保留的限制**

- 树叶仍是风格化几何叶簇，不是照片级叶片/透明贴图；近景看仍能看到多面体边缘。
- 建筑是三维体块与细节几何，没有真实砖墙/玻璃 PBR 贴图。
- 行人仍是 69 MB 内嵌 2K 贴图的 Ara 角色，本轮未压缩、未换动画。
- 只验证 1 行人 / 2 树 / 1 建筑；**S01–S03 与交通逻辑仍未改动，未重新打包，未做整街验收**。
- Unity `-batchmode` 包装进程仍在证据写入后被外部停止；以日志中的 `capture complete` 与截图文件为判定依据。

## 第三轮：真正接入游戏并继续细化树叶（用户反馈"游戏里还是原来的内容"）

第二轮只改了独立验证场景，用户指出游戏内仍是旧内容。本轮做了两件事：

1. **真正接入 S01–S03**：`StreetWorld.Tree()` 用精修树替换原来的圆柱树干 + 低模树冠；`StreetDistrictBuilder.BuildExtensions()` 里所有"远处树的绿色方块"也换成精修树；近景住宅 `side < 0 && bay == 0` 换成精修建筑。树坑碰撞和 `Facade Residence` 命名保留，交通逻辑未改。
2. **树叶再细化**：把压扁的叶球簇换成逐片双面叶刃几何（随机朝向/宽度/弯曲、4 组色变），松树针叶单独加宽加密；三种树约 0.75–1.2 MB。

**游戏内证据**

- S01 正向视角：`Results/visual-upgrade-test/in-game-refined-S01-1080p.png`
- S01 人行道视角：`Results/visual-upgrade-test/in-game-refined-S01-sidewalk-1080p.png`
- 日志确认场景内 24 棵精修树都有非零渲染包围盒；旧绿色方块树已消失。
- 运行时预制体：`Assets/StreetCrossing/Resources/StreetCrossing/Modules/RefinedTree*.prefab`、`RefinedResidence.prefab`。直接把 FBX 放进 Resources 会被反复还原成旧版本，因此改为从 `Art/External` 生成稳定预制体。

**验证结果**

- EditMode：`147/147 Passed`（带图形设备；无图形设备时 4 个材质测试因设备缺失失败，已复测排除）。
- PlayMode：`39 passed / 5 failed / 3 skipped`。失败项为维护门碰撞、转弯包络 penetrates `Facade Residence`、入口匹配、VR 菜单；这些不是本次资产替换引入的可证失败，但**本轮没有修复，不冒充全绿**。
- 批处理包装进程仍在证据写入后被外部停止；以 XML 与截图为判定依据。

**仍有限制**

- 树叶是几何叶刃，不是透明贴图叶片；松树仍偏稀，未做到照片级。
- 建筑没有 PBR 贴图；行人仍是 69 MB 内嵌贴图角色，未压缩、未接动画。
- 未重新打包 Windows，没有做完整游玩/性能验收。

## 第四轮：整体建模精细（用户要求“进一步精细整体建模”）

**本轮完成（全部落 S01–S03 正式场景，不改交通逻辑）**

1. **真实行人接入**：`scripts/trim_pedestrian_fbx.py` 从原始 70 MB / 496 动画的 Ara 角色中，仅保留 Idle / Walk / Texting / Phone call，导出 `StreetPedestrian_Trimmed.fbx`（约 4 MB）。`PedestrianPrefabBuilder` 配置 Generic 骨骼、4 个 clip、AnimatorController（参数 `Motion`：0 待机、1 行走、2 发短信、3 打电话），生成 `Resources/.../StreetPedestrian.prefab`。`StreetBackgroundPopulation` 不再拼方块身体，改为实例化真人模型，路线、脚底高度和 LODGroup 契约保留。
2. **松树加密**：`generate_trees_refined.py` 的松针从 9 层改 17 层，枝展与针簇预算提升（约 3.5 MB），顶端不再裸露；三种树重新导出并重建运行时预制体。
3. **铺装和立面**：新增 `scripts/generate_surface_textures.py`（纯标准库生成无缝纹理），新 `PavingAlbedo_Refined.png` 是错缝混凝土板而不是随机深浅棋盘，`PlasterAlbedo_Refined.png` 去掉菱形网纹。生成 `PavingRefined.mat` / `PlasterRefined.mat` 并由 `StreetAssetLibrary` 加载；原 `Paving.mat`/`Plaster.mat` 未覆盖。近景住宅补 UV 后由 `RefinedFacadeBinder` 把灰泥纹理绑定到 `FacadeMasonry`。
4. **天空与光照**：新建 `RefinedSky.mat`（Skybox/Procedural），写入 `StreetCrossingScene.unity` 的 `RenderSettings.skybox` 与 Trilight 环境光；纯深蓝底消失，出现天空渐变。

**游戏内证据**

- 正向：`Results/visual-upgrade-test/overall-refined-S01-final-1080p.png`
- 人行道：`Results/visual-upgrade-test/overall-refined-S01-sidewalk-final.png`
- 中间过程图：`pedestrian-S01-*.png`、`overall-refined-S01-*.png` 保留，便于前后对比。

**验证**

- EditMode：`147/147 Passed`（`TestResults/StreetCrossing-EditMode-overall-final.xml`）。
- PlayMode（行人接入后与最终整体改动后各跑一次）：均为 `39 passed / 5 failed / 3 skipped`（`TestResults/StreetCrossing-PlayMode-ped.xml`、`TestResults/StreetCrossing-PlayMode-overall-final.xml`），与本轮前基线一致，无新增失败。
- 5 个失败项（维护门碰撞契约 ×2、S03 转弯包络、任务入口匹配、VR 菜单）未修复，不冒充全绿。

**仍有限制（诚实）**

- 树叶仍是几何叶刃，不是 alpha 贴图；近景能看出风格化边缘。
- 行人只有一款男性模型（通过缩放/动画区分），没有多样体态与服装。
- 建筑没有完整 PBR 贴图；天空是程序化渐变，没有云层几何。
- 未重新打包 Windows，未做完整游玩/帧率验收。

## 下一步（需用户确认后才继续）

1. 修复既有 5 个 PlayMode 失败（维护门碰撞契约、S03 转弯包络、任务入口、VR 菜单），单独成轮，不与美术改动混在一起。
2. 若要继续写实：行人增加 2–3 套服装/体态、树叶 alpha 卡片、建筑 PBR 贴图与招牌细节。
3. S01–S03 五视角游玩截图与帧率验收，确认后再决定是否重新打包 Windows。
