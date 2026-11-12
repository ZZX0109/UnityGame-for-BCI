# FirstPersonSokoban · 第一人称三维推箱子

版本：**P0/P1 修复版 2026-11-11（v2 存档 / 8 关）**
引擎：Unity **2022.3.62f3c1**（2022.3 LTS），目标平台 Windows x64，旧 Input Manager。
源码标识：`FirstPersonSokoban.Runtime` + `FirstPersonSokoban.Editor` 两个程序集。

---

## 1. 快速开始

- **直接玩**：解压 `FirstPersonSokoban-WindowsBuild-P0P1Fixed-20261111.zip`，双击 `FirstPersonSokoban.exe`。
  不要单独移动 exe，需与 `FirstPersonSokoban_Data/`、`UnityPlayer.dll` 保持同目录。
- **打开工程**：用 Unity 2022.3 LTS 打开本目录，加载
  `Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity` 后按 Play。
- **校验关卡**：菜单 `First Person Sokoban → Validate All Levels`。
- **补齐资源**（仅在资源缺失时）：`First Person Sokoban → Setup Missing Game Assets`。

## 2. 操作说明

| 按键 | 作用 |
| --- | --- |
| WASD | 自由行走 |
| 鼠标 | 转动视角（可在设置里调灵敏度、FOV、反转 Y） |
| E | 对准箱子并推动一格 |
| Z | 撤销上一次推动（同时恢复位置与视角） |
| R | 重置当前关（键盘需连按两次确认，避免误触） |
| ESC | 暂停 / 继续（失焦自动暂停，返回后需手动继续） |

推动规则：**网格状态为准**，玩家必须站在箱子相邻的格子且横向站正，箱子前方一格不能是墙或另一个箱子。
瞄准时箱体顶部会高亮、地面显示目标格预览；推不动时 HUD 会说明具体原因（未对准 / 太远 / 站位偏 / 前方是墙 / 前方有箱）。
局面被判定为死锁时会常驻提示，可用 Z 撤销或 R 重置。

设置入口：主菜单「设置」或暂停菜单「设置与舒适度」。含鼠标灵敏度、视野 FOV、反转 Y、
舒适模式（推动动画 0.55s 而非 0.38s）、全屏，以及「退出游戏」。

## 3. 关卡

8 关，最少推动数依次为 **1 / 1 / 3 / 3 / 4 / 6 / 3 / 9**。
前 3 关为教学，后续加入内墙转角、双箱顺序、相邻箱腾挪、狭口对齐与长线绕行。
关卡定义：`Assets/FirstPersonSokoban/Resources/Levels/Level01..08.txt`；
标题、副标题、初始朝向与稳定 ID：`Resources/LevelCatalog.json`。
新增关卡后请运行 `python Tools/validate_levels.py` 与 Editor 的 `Validate All Levels` 双重校验。

## 4. 最低运行环境

- Windows 10/11 x64；支持 DirectX 11 的显卡；键鼠。
- 建议 1920×1080 及以上；窄窗口会自动切换为上下排列的菜单布局。
- 无需联网、无需脑机接口 / EEG / UDP / 康复实验数据模块（项目范围不含这些）。
- 中文字体使用系统字体回退链（11 个中日韩候选，含 Microsoft YaHei / SimHei /
  PingFang SC / Noto Sans CJK SC 等），全部失败时退 Unity 内置字体；
  未随包附带第三方字体文件，无授权不明资源。

## 5. 验证状态（详见 `Validation/StaticValidation-2026-11-11.txt` 首页现状表与第十节）

| 项 | 结果 |
| --- | --- |
| 编译 | ✅ Unity 批处理退出码 0，无 `error CS`（`Validation/Logs/fix-import.log`） |
| EditMode | ✅ 33/33（`EditMode-fix-results.xml`） |
| PlayMode | ✅ 17/17（`PlayMode-fix-results.xml`） |
| 关卡可解性 | ✅ 8/8（`fix-validate-levels.log` + 独立 Python BFS 交叉验证） |
| Windows x64 构建 | ✅ `Build Finished, Result: Success`（`fix-build.log`） |

本轮（代码审查 P0/P1）修复：暂停恢复输入锁反转、贴墙死锁误判、推动动画协程与
状态事务、箱体视觉/碰撞边界统一、菜单布局两套参数、死锁搜索改分帧可取消、
辅助瞄准由 32° 收到 14°。逐文件修改清单与测试证据见验证文档第十节。

**未验证**：实机 GUI 人工通关、画面观感与鼠标手感、连续快速 E、动画中 Z/R/ESC 组合、
长时间运行稳定性、实机截图/录屏、不同分辨率下真实文字边界、Profiler 实测帧时间。
**未做**：音效、材质光照重做、授权字体包、macOS 构建、画质/帧率选项、8→12 关扩充。
构建成功不等于手感通过。

## 6. 关键文件哈希（SHA-256）

```
8be58f41da2f1bc5be476d8d66621c76b2d581a4af853186901890ccb63d7883  Assets/FirstPersonSokoban/Scripts/Core/PuzzleGameManager.cs
577ba3639e7aaa972289a44a6120f0982d2e73b011dddc1d62d92815d687946e  Assets/FirstPersonSokoban/Scripts/Core/DeadlockDetector.cs
79b9a89213e9284d7b0942e3dcee926d3d32269fe5bf3e6b6705d1be85c5feeb  Assets/FirstPersonSokoban/Scripts/Core/LevelSolver.cs
2241255ab69d94129a0f27e4af8162334ca56bf69fa9f5325a8ab06b9833dd2f  Assets/FirstPersonSokoban/Scripts/Interaction/BoxDimensions.cs
a767e91be8424f9dd2cad541c019e5ba7c5fa4522b8450964381716af44014fb  Assets/FirstPersonSokoban/Resources/LevelCatalog.json
957ceb6de3229d341fc57fce6463fb1c40699650435f5f1657e178e8ae240831  Validation/StaticValidation-2026-11-11.txt
059c313352892718258a120b87bdc6fc7c23ce67181084abe14f86f204f5dddb  BuildOutput/FirstPersonSokoban.exe
```

`BuildOutput/FirstPersonSokoban.exe` 是 Unity 的启动器模板，本轮内容变化集中在
`FirstPersonSokoban_Data/Managed/FirstPersonSokoban.Runtime.dll`（已核对新符号）。

## 7. 目录说明

```
Assets/FirstPersonSokoban/    运行时脚本、Editor 工具、场景/Prefab/材质/关卡、测试
ProjectSettings/              Unity 生成的完整项目配置（含 InputManager.asset）
Packages/manifest.json        包依赖
Validation/                   验证记录、Unity 日志、Test Runner 结果 XML
Tools/                        validate_levels.py（独立 BFS）、ensure_meta.py（GUID 补齐）、generate_serialized_assets.py
```
