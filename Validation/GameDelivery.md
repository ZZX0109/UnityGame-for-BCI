# 两个游戏分支的真实 Unity 验证与 Windows 交付

此流程覆盖抓娃娃机 C01–C03 与第一人称过街 S01–S03。Python 静态检查、资源校验与脚本测试不等于 Unity 编译、物理运行、GUI 验收或 Windows 实测。

## 环境与执行

要求已安装并授权的 **Unity 2022.3.62f3c1 中国版**、可用的图形设备、对应 Windows Build Support、Python 3.10+。不自动下载 Editor、替换 Unity 版本、获取授权或连接任何机械硬件。流程使用 `-batchmode`，保留图形设备，不加 `-nographics`；Null renderer 无法证明 TMP、Canvas 或画中画正常，导致跳过的图形测试也不能算通过。

在对应分支项目根目录的 PowerShell 中，设置已安装 Editor 的路径后执行：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_game_delivery.py --game claw
# 过街分支改用：
python Tools/run_game_delivery.py --game street
```

仅执行测试可加 `--tests-only`。可用 `--output BuildOutput/MyFreshEvidence` 指定新的空输出目录，旧目录中的 XML、日志或构建不会被复用。

流程顺序为：源代码静态校验 → Editor 资源准备 → 本游戏 EditMode → 本游戏 PlayMode → 校验真实 XML 与实际 Editor 版本 → 确认测试前后源文件未变化 → Windows 构建 → 完整性检查与打包。任一条件不满足就停止。Editor 缺失会写入 `blocked`，不生成测试 XML 或 Windows ZIP。

## 不可跳过的证据要求

- NUnit `test-run` 的总数必须大于零，全部通过，不能含失败、跳过或不确定结果。
- 实际 `test-case` 必须与根计数一致，属于所选游戏的测试命名空间，不能用另一个游戏的绿色测试代替。
- PlayMode 必须包含本游戏三个场景的 `Acceptance_C01/C02/C03` 或 `Acceptance_S01/S02/S03` 类别；这些测试应驱动实际游戏流程，不能直接设置获奖/到达状态。
- XML 或原始日志必须能确认实际 Editor 是精确的中国版版本。请求路径名不能代替执行版本证据。
- Editor 资源准备完成后记录源指纹，测试和构建均不能再悄悄修改 Assets、Packages 或 ProjectSettings。
- Windows 交付需要非空的真实 PE EXE、UnityPlayer.dll，以及含 boot.config/globalgamemanagers 的完整 Data 目录。只有一个 EXE 不算交付。

成功输出包含完整 Windows 程序目录、实际 XML/日志、源指纹、提交号、逐文件 SHA256、字体许可、ZIP 与 SHA256SUMS.txt。测试脚本中的临时 XML 只是回归测试数据，不进入交付证据。

## CI

`.github/workflows/game-delivery.yml` 对两个游戏分支执行静态检查。若后续整合了两个游戏，工作流会按游戏分别运行。

真实 Unity 任务需要仓库变量 `UNITY_CI_ENABLED=true`，以及标签为 `self-hosted, Windows, X64, unity-2022.3.62f3c1` 的专用授权 runner，runner 环境中必须设置 `UNITY_EDITOR_PATH`。未配置时 Unity 任务显示跳过并明确写明“未验证”，不视为通过。PR 不在持久 Windows runner 上自动执行代码。

## 仍需真实实机完成

即使此脚本成功，Windows 人工游玩、画面与中文布局、视线可读性、操作舒适度、物理手感、性能和游玩视频仍单独标为 `not_run`。在目标 Windows 机器逐场景完整游玩，保存录屏和实际性能数据后才能据实更新验收表。

当前开发环境没有 Editor、C# 编译器或授权配置，无法在此环境完成上述真实 Unity/Windows 证据。交付脚本会如实保留这一阻塞，不制造“通过”的 XML、EXE 或视频。
