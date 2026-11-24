# PICO 启动黑屏修复（2026-11-18）

用户在头显启动 StreetCrossing 后黑屏。连接设备后，PICO OS 为 5.13.7。

## 原因与修改

原 APK 的设备日志在 16:18:38 报 `CapsuleCollider`、`SphereCollider` 不存在，随后 `StreetWorld.Shape -> Tree -> Build -> StreetGameManager.Awake` 抛出空引用异常。`RebuildScenario` 在世界构建之后才创建玩家相机，因此异常使启动停在没有相机的状态。OpenXR 已正常启动，不能用构建成功代替设备运行验收。

新增 `Assets/StreetCrossing/link.xml`，保留动态 `GameObject.CreatePrimitive` 依赖的 MeshFilter、MeshRenderer 及四种基本碰撞组件。保留其他引擎裁剪。此问题与 Unity 的 [CreatePrimitive 运行时裁剪说明](https://docs.unity.cn/2022.2/Documentation/ScriptReference/GameObject.CreatePrimitive.html) 一致。

## 本轮验收

1. 原头显日志复现上述缺失组件和启动异常。
2. 原生 Android IL2CPP / ARM64 构建成功，APK 签名、VR manifest 检查通过：`BuildOutput/PICO-Validation-20261118T083224557875/summary.json`。
3. 生成的 `UnityClassRegistration.cpp` 包含 CapsuleCollider、SphereCollider 的原生注册。
4. `adb install -r` 覆盖安装成功；冷启动成功。新进程日志未再出现 Unity Error 或上述空引用异常，已生成 S01 SessionOpened 日志。
5. 设备实际双眼截图显示道路、游戏菜单及手柄射线，启动显示已验证。完整玩法、交互及其他游戏的头显验收不在本条通过范围内。
6. 头显安装的 `base.apk` 与桌面交付 APK 的 SHA256 一致：`3d6553a7f19eee3d5f4f28b22cdac5c94d063cf9b718252300f3a568bf3102ba`。

设备日志与截图：工作区根目录 `BuildOutput/PICO-Street-Blackscreen-20261118/`，含 `device-before.log`、`device-after.log`、`device-after.png`、`Logs/`、`delivery.json`。

新 APK：`BuildOutput/PICO-Android-20261118T083233600/Neuralife-street-PICO.apk`。已同步桌面 `Neuralife-PICO4-最新APK-20261118-1606` 中的过马路文件、校验清单和安装说明。旧构建留作追溯，其他两个 APK 未改动。

退出方法：短按右手柄主页键返回系统；长按用于回正，不作为退出步骤。本轮已通过 ADB 关闭旧的黑屏进程并返回系统，随后安装、启动修复版本。

## 开始按钮真机跟进（16:37）

用户反馈无法开始后，设备回传画面显示 PICO 系统安全边界窗口。提示用户完成实际活动范围的边界确认、用右手柄食指扳机点击后，16:37:26 真机事件日志记录 `AttemptStarted`，任务 `S01-T1`、配置 0、attempt 1；随后设备截图显示菜单关闭并进入道路任务 HUD。未通过 ADB 注入游戏点击或自动开始，也未修改射线或按钮逻辑。证据为根目录 `BuildOutput/PICO-Street-Blackscreen-20261118/device-ui-latest.png` 和 `Logs/Logs/S01-20261118-083347-b157092a.jsonl`。确认进入任务；完整过街玩法仍未完成验收。
