# Neuralife PICO XR

Unity 2022.3，Windows PCVR / Android PICO 4、4 Ultra、Neo3。官方 PICO OpenXR 插件锁定 `3aa3e62bff41df618529eeb60ff02c29a515dafe`（包内版本 1.4.1；官网下载包标记 1.4.0）。PICO 系统最低版本按官方插件要求使用 5.13.0 或更高版本。三款游戏各自保留同版本的嵌入包，可单独克隆工程。

## 使用

首次导入后执行 `Neuralife → PICO XR → Configure project`。直接点 Play 用键鼠测试；只有选择 `Neuralife → PICO XR → Play in VR` 才会启动本次头显预览。停止后再次点 Play 自动回到桌面模式。普通桌面包仍默认键鼠；加 `--pico-vr` 可请求头显模式。专用 PCVR / APK 构建默认启用 VR，不需要启动参数。

Windows 先连接 PICO 串流并选择可用的 OpenXR runtime，再启动专用 PCVR EXE。Android 须为同一 Unity 安装 Android Build Support、SDK、NDK 和 OpenJDK，执行 `Build Android PICO`，开发测试 APK 使用本机开发签名；商店签名和上架不属于本工具。

安装一体机包：在头显开启开发者模式，USB 连接电脑并在头显内允许调试。确认 `adb devices` 显示设备后执行 `adb install -r "Neuralife-sokoban-PICO.apk"`；娃娃机和过马路分别替换为 `Neuralife-claw-PICO.apk` 和 `Neuralife-street-PICO.apk`。三款可并存。

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Packages/com.neuralife.pico-xr/Tools/run_pico.py all
python Packages/com.neuralife.pico-xr/Tools/run_pico.py android
```

先保存并关闭同一工程的编辑器。工具依次运行真实 Unity 配置、EditMode / PlayMode 回归及 Windows 构建；日志、XML 和结果写入新建的 `BuildOutput/PICO-Validation-*`。`android` 会检查模块并使用新输入后端；切回桌面流程会恢复 Both。工具的 `--sdk-mirror` 仅用于有网络限制的本地官方 Git 缓存，必须匹配上述 commit，不改换 SDK。

## 操作

- 右手射线指向菜单，扳机按下并松开点击；按住拖动，右摇杆上下滚动。
- 左摇杆移动；推箱子/过马路以头部水平方向为参考，娃娃机以机柜坐标为参考。
- 推箱子/过马路右摇杆左右每次转向 30°，松回中心后才能再次转向；按下右摇杆回正。娃娃机保持固定站位，用真实头部移动观察。
- 右扳机：推箱子推动 / 娃娃机抓取或继续 / 过马路查看标牌。菜单点击不会同时触发该动作。
- A：推箱子撤销。B：重置确认。X：暂停/返回。Y：娃娃机辅助画面。
- 摘下头显、失焦或失去追踪会停止输入并暂停；恢复后手动继续。按键要先松开、摇杆要先回中，才能再次接受命令。

头显姿态由追踪数据接管，桌面 FOV 和鼠标灵敏度不参与 VR 相机。移动速度限制为 1.2 m/s，无快跑和跨路瞬移。碰到虚拟墙且头部进入墙内时遮黑；退回可站立区域恢复。原有规则、撤销、交通判定和存档不变。

## 验收边界

配置、编译、模拟输入和桌面回归不能证明 PICO 真机通过。须另外记录双目 / 6DoF、左右手柄、中文菜单、全部关卡、断连摘戴与恢复、边界，以及至少 10 分钟目标刷新率和舒适性。构建清单的 `headsetAcceptance` 默认 `NOT_TESTED`，不得用历史桌面成绩替代。

官方依据：[PICO SDK](https://github.com/Pico-Developer/PICO-Unity-OpenXR-SDK)、[Unity OpenXR 配置](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.11/manual/project-configuration.html)。
