# FirstPersonSokoban · 第一人称三维推箱子

**2026-11-13 · 暖色艺术馆、八关机制与布局预览候选版 · 保留旧解锁进度**

Unity **2022.3.62f3c1**（中国版 2022.3 LTS），目标 Windows x64，使用旧 Input Manager。
本轮保留暖色艺术馆风格，并重构第二至第八关的实际墙体与箱子路线；尚未在当前环境运行 Unity 编译、Test Runner、Windows 构建或 GUI 游玩。
实际验证边界见 [当前状态](Validation/CurrentStatus.md)。

## 开始测试

1. 更新本仓库，在 Unity Hub 使用 **2022.3.62f3c1** 打开工程根目录。
2. 等待资源导入，打开 `Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity`，按 Play。
3. 本轮资源已提交，正常更新无需重新生成 Prefab。仅在资源确实缺失时使用菜单
   `First Person Sokoban → Setup Missing Game Assets`。
4. 按 [本轮测试清单](Validation/ManualTest-20261112.md) 检查。请先记录 Console 的编译错误，再进行游玩。

仓库不包含已验证的新 Windows exe。旧压缩包不会自动获得本轮修改；请用本轮源码运行或重新构建。

## 操作

| 按键 | 作用 |
| --- | --- |
| WASD / 鼠标 | 自由行走 / 转动视角 |
| E | 对准相邻箱子正面，推动一格 |
| Z | 撤销上次推动，恢复玩家位置与视角 |
| R | 连按两次确认重置，避免误触 |
| ESC | 暂停 / 继续；设置页中先返回上一层 |

失焦自动暂停，返回后手动继续。设置支持灵敏度、FOV、反转 Y、舒适模式、全屏、声音音量和环境音（默认关闭）。
行走距离统计实际水平位移，包含推箱跟随，不包含撤销/Teleport 的瞬移。
菜单显示关卡完成状态与最佳推动数；底部按钮进入最新已解锁关卡。

## 本轮修改

- 使用奶油白墙面、暖色水磨石地面、装框抽象画与陶土色训练模块；顶部敞开。
- 保留低对比材质细节、自然主光、柔和补光与单次反射捕获；资源的实际导入和光照仍待 Unity 检查。
- 模块保留四面编号、选中边框与归位反馈；目标格为低矮方形地面标识。
- 关卡卡片直接从实际地图绘制墙体与走道缩略图，帮助辨认不同房间结构；不显示箱位、目标或解法。
- HUD 增加当前关卡要点，复用目录中的机制说明；不改变地图、解锁顺序或提示规则。
- 菜单缩小标题、加入真实 3D 设备静态预览；HUD 将计时/推动/目标集中右上，交互提示放底部。
- 统一主/次/危险按钮状态，加入短淡入；舒适模式减少动画，不做扫描线、频闪或慢动作。
- 新增可调音量的点击/推动/归位/通关/警告/撤销/重置音效；环境音可选且默认关闭。
- 装饰不参与碰撞或推箱规则；所有自建材质、纹理、Mesh、声音及预览纹理有明确释放路径。
- 保留 Editor 中退出 Play 的修复。完整变更与视觉验收见 [实验室视觉说明](Validation/LabVisualChanges-20261112.md)。

保留 Built-in Standard 渲染管线，未迁移 URP，未声称已烘焙 GI 或完成实机性能优化。
材质纹理和声音由代码确定性生成，无需下载额外资源包。
`Resources/Materials/Lab*Template.mat` 和小型粗糙度贴图用于保留 Player 构建中的运行时 Shader 组合，不能按“未引用资源”删除。
基础推动、撤销与碰撞规则保留。当前最少推动数为 **1 / 7 / 22 / 12 / 4 / 10 / 5 / 32**。
逐关机制、准确俯视图与可回放自动解法见 [八关重构说明](Validation/LevelRedesign-20261113.md) 和
[源地图图册](Validation/LevelDesign/index.html)。图册由地图文本生成，**不是 Unity 实机截图**。

存档格式版本为 3：稳定关卡 ID 保持不变，第一关继续兼容旧成绩；第二至第八关地图 revision 升为 2，
旧图的最佳推数、最佳时间和完成标记不再套用到新图。旧解锁数保留，已解锁玩家可直接复测新地图。
历史 `FPSokoban.v2.*` 命名空间继续保存解锁数，新图成绩写入带 `.r2` 的独立键。
当前范围是键鼠空间规划训练游戏；EEG、UDP 和康复实验数据模块未包含在本轮中。

## 验证与打包

Python 工具要求 Python 3.10+，常规验证只使用标准库：

```bash
python Tools/validate_project.py
python Tools/validate_levels.py --mechanics --report Validation/LevelDesign
python Tools/build_ui_font.py --check
python -m unittest discover -s Tools/tests -v
```

实际 Unity 测试可在 Editor 的 Test Runner 中运行。新增 `LevelWalkthroughTests` 覆盖八关真实场景的完整路线：
通过 CharacterController 行走、射线选箱、推动动画与完成存档，不使用传送或直接修改棋盘来通关。
这八项测试已经编写，尚未在本环境执行；离线回放仅验证路线合法，不能代替 Unity 测试。
安装 Windows Build Support 后，
在关闭该工程的 Editor 实例后执行以下命令（将路径替换为你的实际安装位置）：

```powershell
python Tools/run_delivery.py --unity "D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe"
```

该命令串行执行 EditMode、PlayMode、C# 关卡验证和 Windows 构建，测试失败或没有有效 XML 时停止。
截图捕获是独立流程，见 [真实画面捕获说明](Validation/VisualCapture.md)。未执行捕获时不得把地图图册当作实机画面。
结果位于新建的 `BuildOutput/Delivery-*`：完整 ZIP、SHA-256、XML/日志和源文件指纹。
`--tests-only` 只运行测试。Editor 菜单 `First Person Sokoban → Delivery → Validate Levels and Build Windows`
可直接构建，但没有运行 Test Runner，因此清单明确记录 `unityTests=not_run`。

解压 Windows ZIP 后，exe、`FirstPersonSokoban_Data/`、`UnityPlayer.dll` 必须保持同目录。
字体授权随包放在 `ThirdPartyNotices/`。构建成功之后仍需 Windows 实机验收。

CI 默认运行静态检查、工具测试、字体覆盖和八关求解。完整 Unity CI 需要配置专用 Windows
self-hosted runner（标签 `unity-2022.3.62f3c1`）、精确版本 Editor 及许可、Windows Build Support、
环境变量 `UNITY_EDITOR_PATH`，然后设置仓库变量 `UNITY_CI_ENABLED=true`。
未配置时状态摘要明确为 Unity 测试/构建 **NOT VERIFIED**；当前未替用户启用或配置这些条件。

## 文件说明

- `Assets/FirstPersonSokoban/`：脚本、资源、场景与 Unity 测试。
- `Tools/`：关卡/资源检查、字体重建、测试与打包入口。
- `Validation/CurrentStatus.md`：当前验证边界。
- `Validation/History/`：历史陈述，缺少原始附件时不作为本轮通过证据。
- `Resources/Fonts/LICENSE.txt`：完整字体许可；重建方式见字体目录说明。

不要删除 `.meta`、程序集定义、`packages-lock.json` 或常规 ProjectSettings。
