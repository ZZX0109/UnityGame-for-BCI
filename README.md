# FirstPersonSokoban · 第一人称三维推箱子

**2026-11-12 · 界面与稳定性修复候选版 · 8 关 · v2 存档兼容**

Unity **2022.3.62f3c1**（中国版 2022.3 LTS），目标 Windows x64，使用旧 Input Manager。
本轮已更新源码；尚未在当前环境运行 Unity 编译、Test Runner、Windows 构建或 GUI 游玩。
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

失焦自动暂停，返回后手动继续。设置支持灵敏度、FOV、反转 Y、舒适模式和全屏。
行走距离统计实际水平位移，包含推箱跟随，不包含撤销/Teleport 的瞬移。
菜单显示关卡完成状态与最佳推动数；底部按钮进入最新已解锁关卡。

## 本轮修改

- 主菜单采用统一深蓝灰与青绿色视觉，调整标题层级、关卡状态、进度条和滚动条。
- HUD 增加计时、目标进度；暂停、设置、结算卡片与内部控件整体自适应。
- 中文优先使用随包字体 Sokoban UI Sans（Noto Sans CJK SC 的 OFL 子集），保留系统字体回退。
- 释放箱体高亮与预览的运行时材质；删除闲置动画实现；相机高度参数实际生效。
- 统一路程统计，强化动画中途退出/重置测试，设置页 ESC 返回层级修正。
- 停用旧 Python 资产生成器，Unity Editor 成为唯一资产生成入口。
- 新增只读资源完整性检查、真实 Unity 测试和 Windows 打包入口；历史验证记录归档。

八关与推箱规则保留。最少推动数为 **1 / 1 / 3 / 3 / 4 / 6 / 3 / 9**。
当前范围是键鼠推箱子；EEG、UDP 和康复实验数据模块未包含在本轮中。

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
