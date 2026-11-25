# Game03 虚拟手验证 · 2026-11-25

Unity 2022.3.62f3c1。在 Windows 独立副本运行 Unity Test Runner，两组均为零跳过。逐项结果和运行时间见 [test-results.json](test-results.json)。

| 测试 | 结果 | 覆盖范围 |
| --- | --- | --- |
| EditMode | 185 / 185 | 双腕实际模型校准、基于关节位置的弯曲与张指、两指靠拢、缺失数据与回放契约等 |
| PlayMode | 75 / 75 | 模拟追踪中断与恢复、实测指尖距离输入、完整 Demo 回放、双手持物和交接、第一人称过街角色接线 |
| Windows 独立程序 | S01-T1，1280 × 720 | [第一人称街景截图](S01-T1.png)。正常启动默认使用键鼠。 |

在项目根目录运行同版本 Unity，用 `-batchmode -projectPath . -runTests -testPlatform EditMode -testFilter "Neuralife.VirtualHand;StreetTrackedHandsOrientationTests" -testResults edit.xml` 复测编辑器项目。运行时组使用 `-testPlatform PlayMode -testFilter "Neuralife.VirtualHand;StreetCrossing.Tests.PlayMode.FullBodyStreetCrossingTests;SimulatedTrackingPlayModeTests" -testResults play.xml`。

PICO 手部同步测试应用和过街游戏分别使用 `Neuralife.XR.Editor.PicoProject.BuildHandLabAndroid` 和 `Neuralife.XR.Editor.PicoProject.BuildAndroid` 构建。可在同一台头显分别安装 `com.neuralife.handsync.vr` 和 `com.neuralife.street.vr`。手部面板分别显示左右手的输入来源。光学手势使用张开、握拳、捏合和指向四类。手部关节数据中断时停止报告该侧手势。

先前构建已安装到 PICO 4 Pro，手部测试应用通过冷启动检查。头显随后休眠并断开 USB；佩戴光学追踪仍待实测。真机检查应在手部追踪开启时分别对左右手张开、握拳、捏合、指向，核对两侧面板响应，然后让单手移出视野再返回，确认掉追踪时没有误触发，另一侧继续响应。在过街场景面对可检查的标志，右手先张开后捏合一次，核对只触发一次交互。

开发分工：本机 ZCode `glm-5.3-flash`（会话 `sess_9a02527f-67c7-44c9-ab59-ed898f3e04fc`）完成独立手部测试场景、两平台构建入口、指节弯曲与张指映射及相应回归测试。三名 `gpt-6-luna`，推理强度 `max`，分别负责腕部与抓握校准、回放数据核查、Android 工具链与打包验证。本地 Codex 整合、修正丢失追踪后的交互和捏合识别、运行回归和截图。Chrome 规划客户端本轮不可用。

## 最终 Android 测试包

对应源码提交 `8ee03eb436320c095510acb9de03597d9fba6b36`。两个包均通过 APK v2 签名校验，使用测试证书。包内包含 ARM64 IL2CPP，目标 Android API 35，最低 API 29。已核对合并后的 AndroidManifest 含 `com.picovr.permission.HAND_TRACKING` 及 `handtracking=1`。

| 文件 | 大小（字节） | SHA-256 |
| --- | ---: | --- |
| Neuralife-HandSyncLab-PICO.apk | 86722483 | `c93fbf0a29f66625561b9285c987bbae5b18c27be59264305d36c941a7ae84d1` |
| Neuralife-street-PICO.apk | 86719707 | `4747f0e3debdd4ade000ae51a8311e400331ee8443e198b77bfaa62d056d0b30` |

最终打包完成后 USB 和无线 ADB 均无可用设备。这两个文件已交付电脑桌面，头显内仍为先前安装的构建。
