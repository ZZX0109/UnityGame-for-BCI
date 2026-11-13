# 真实 Unity 画面采集

此入口仅采集画面，供人工审查。截图生成、Blender 离线资产预览、地图布局图均不表示玩法或视觉验收通过。

使用已安装并授权的 **Unity 2022.3.62f3c1**，保持图形设备和可见的 Game View。先关闭同工程已打开的 Editor，避免项目锁；命令会打开有界面的 Editor。不要切去 Scene View 或最小化。首次资源导入后，在 Game View 选择目标尺寸；可先运行一次，再按该尺寸重新采集。

```powershell
$env:UNITY_EDITOR_PATH = 'D:/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_visual_capture.py --game sokoban
# 两个开发分支分别使用 --game claw 和 --game street
# 已设置精确 Game View 尺寸时，可校验而不缩放：
python Tools/run_visual_capture.py --game claw --expect-size 1920x1080
```

输出必须是 BuildOutput 内的新空目录。脚本不加 `-batchmode` 或 `-nographics`：整帧抓取需要实际渲染，`WaitForEndOfFrame` 不用于无界面批处理。普通交付测试用 `!VisualCapture` 类别排除采集测试；采集命令只执行 `VisualCaptureTests` 和 `VisualCapture` 类别，不能用其他通过的测试代替。

| 游戏 | 场景 | 必需画面 |
| --- | --- | --- |
| 推箱子 | L01–L08 | 每关初始视角与俯视画面，共16张 |
| 抓娃娃机 | C01–C03 | 每关正面、左侧、右侧及PIP，共12张 |
| 过街 | S01–S03 | 每关起点、路沿观察、斑马线，共9张 |

流程保存原始 NUnit XML、Editor 日志、捕获清单、源指纹、实际分辨率和每张 PNG 的 SHA256。缺少任一场景/视角、损坏图片、错误尺寸、Null renderer、失败/跳过/空测试或源文件变化均失败。不会生成替代图片，也不会对证据图缩放。无 Editor 时只写 `blocked`，没有截图或 XML。

采集时可暂停场景以稳定构图，相关状态须记入游戏清单；这些画面不证明完整游玩成功。俯视图可由实际 Unity 相机单独渲染，不包含 HUD 时须注明。完整 Game View 画面应包含 Canvas 与PIP。

人工仍需逐图检查字体、HUD遮挡、机柜和奖品可见性、信号可读性、近景材质、三个街区身份及错误材质。另行录制完整游玩、记录性能和操作舒适度后，才能更新对应验收状态。当前开发环境未安装 Unity Editor，真实画面采集未运行。
