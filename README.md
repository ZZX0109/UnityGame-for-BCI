# 第一人称过马路开发分支

当前分支：`game03/street-crossing-fps`。本轮按代码审查要求补齐三个任务配置、核心交互和 Canvas/TextMeshPro UI；推箱子主线资源同步保留。

使用 **Unity 2022.3.62f3c1 中国版** 打开项目，打开 `Assets/StreetCrossing/Scenes/StreetCrossingScene.unity` 并按 Play。各任务从本游戏菜单选择；运行细节以该游戏 docs 目录的最新交付说明为准。

真实测试与 Windows 打包入口：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_game_delivery.py --game street
```

该流程要求可用图形设备、有效 Editor 授权和对应 Windows Build Support；未实际运行的编译、Unity 测试、Windows 游玩与录屏不能视为通过。详见 [统一验证流程](Validation/GameDelivery.md)。

---

以下为保留的推箱子工程说明；测试本分支新增游戏请使用上方场景。

# FirstPersonSokoban · 第一人称三维推箱子

**2026-11-12 · 认知训练实验室视觉候选版 · 8 关 · v2 存档兼容**

Unity **2022.3.62f3c1**（中国版 2022.3 LTS），目标 Windows x64，使用旧 Input Manager。
本轮统一了实验室场景、训练模块、感应台、UI 和声音反馈；尚未在当前环境运行 Unity 编译、Test Runner、Windows 构建或 GUI 游玩。
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

- 将原灰盒房间改为完整训练室：墙板、踢脚线、观察窗、服务门、显示屏和通风口；顶部敞开，不生成天花板、顶梁或悬吊灯槽。
- 使用中性白灰/石墨灰、低对比细纹理与粗糙度变化；冷白主光、暖色补光和单次室内反射捕获。
- 木箱改为倒角训练模块：深色护角、抓手凹槽、四面编号、小面积状态灯与选中边框。
- 目标格改为低矮感应台：金属圈、感应环、对齐刻度，真实归位时一次柔和反馈。
- 菜单缩小标题、加入真实 3D 设备静态预览；HUD 将计时/推动/目标集中右上，交互提示放底部。
- 统一主/次/危险按钮状态，加入短淡入；舒适模式减少动画，不做扫描线、频闪或慢动作。
- 新增可调音量的点击/推动/归位/通关/警告/撤销/重置音效；环境音可选且默认关闭。
- 装饰不参与碰撞或推箱规则；所有自建材质、纹理、Mesh、声音及预览纹理有明确释放路径。
- 保留 Editor 中退出 Play 的修复。完整变更与视觉验收见 [实验室视觉说明](Validation/LabVisualChanges-20261112.md)。

保留 Built-in Standard 渲染管线，未迁移 URP，未声称已烘焙 GI 或完成实机性能优化。
材质纹理和声音由代码确定性生成，无需下载额外资源包。
`Resources/Materials/Lab*Template.mat` 和小型粗糙度贴图用于保留 Player 构建中的运行时 Shader 组合，不能按“未引用资源”删除。
八关、v2 成绩和推箱规则保留。最少推动数仍为 **1 / 1 / 3 / 3 / 4 / 6 / 3 / 9**。
当前范围是键鼠空间规划训练游戏；EEG、UDP 和康复实验数据模块未包含在本轮中。

## 验证与打包

Python 工具要求 Python 3.10+，常规验证只使用标准库：

```bash
python Tools/validate_project.py
python Tools/validate_levels.py
python Tools/build_ui_font.py --check
python -m unittest discover -s Tools/tests -v
```

实际 Unity 测试可在 Editor 的 Test Runner 中运行。安装 Windows Build Support 后，
在关闭该工程的 Editor 实例后执行以下命令（将路径替换为你的实际安装位置）：

```powershell
python Tools/run_delivery.py --unity "D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe"
```

该命令串行执行 EditMode、PlayMode、C# 关卡验证和 Windows 构建，测试失败或没有有效 XML 时停止。
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
