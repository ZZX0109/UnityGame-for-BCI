# 安装说明 (INSTALLATION)

把虚拟手模块安装到任意 Unity 游戏工程的步骤、依赖与回滚方法。

---

## 1. 环境要求

| 项目 | 要求 |
|---|---|
| Unity | **2022.3.x**（本项目验证于 **2022.3.62f3c1** 中国版） |
| 渲染管线 | **Built-in Render Pipeline** |
| 目标平台 | Windows x64（PCVR / 桌面）；Android 一体机为后续路线 |
| 输入处理 | **Both**（Input System + Legacy Input Manager） |
| 可选 | OpenXR 1.11.0 + XR Management 4.4.1（仅 VR 追踪需要） |

**不支持：** URP / HDRP、Unity 2021 及以下、Unity 2023 及以上（未验证）。

---

## 2. 依赖包

在 `Packages/manifest.json` 的 `dependencies` 中加入：

```json
{
  "com.unity.cloud.gltfast": "file:com.unity.cloud.gltfast-6.13.1.tgz",
  "com.unity.inputsystem": "1.7.0",
  "com.unity.xr.management": "4.4.1",
  "com.unity.xr.openxr": "1.11.0"
}
```

| 包 | 必需性 | 说明 |
|---|---|---|
| `com.unity.cloud.gltfast` | **必需** | 用于导入 Elena FF GLB。本地 tgz 已随包提供，不要换成其他版本的 glTF 加载器。 |
| `com.unity.inputsystem` | **必需** | 自定义键位功能。若缺失，模块仍可编译（`#if ENABLE_INPUT_SYSTEM` 保护），但键位重绑定不可用。 |
| `com.unity.xr.management` | 可选 | 仅 VR 追踪需要。桌面/键盘模式不需要。 |
| `com.unity.xr.openxr` | 可选 | 同上。 |
| `com.unity.xr.hands` | **当前不可得** | 见 `XR_TRACKING.md`。Unity 中国版注册表无 1.4.2。 |

> ⚠️ **不要把 XR 依赖提前加进三个正式游戏分支。** 规范第 2.4 条明确要求。

### 2.1 输入处理器设置

`ProjectSettings/ProjectSettings.asset` 中：

```yaml
activeInputHandler: 2      # 0=Legacy  1=Input System  2=Both
```

**必须设为 2（Both）**，否则原有三个游戏的 Legacy 输入会失效。

---

## 3. 安装步骤

### 3.1 使用 UnityPackage（推荐）

1. 关闭 Unity 编辑器。
2. 双击 `Builds/FirstPersonHands/FirstPersonHands.unitypackage`，或在 Unity 中
   **Assets → Import Package → Custom Package…** 选择该文件。
3. 全部勾选，点击 **Import**。
4. 确认依赖包已按第 2 节加入 `manifest.json`。glTFast 的 tgz 随包提供，
   需复制到 `Packages/` 目录后再改 manifest。
5. 打开 Unity，等待导入与编译完成。
6. 确认 Console **无编译错误**。

### 3.2 手动复制（备用）

直接复制整个 `Assets/VirtualHand/` 目录到目标工程，并另行处理依赖包。

---

## 4. 验证安装成功

依次检查：

1. **Console 无错误。** 允许存在的警告：glTFast 关于材质属性的提示。
2. **Project 窗口** 中 `Assets/VirtualHand/` 下五个子目录齐全：
   `Models/ Prefabs/ Materials/ Shaders/ Scripts/`，以及 `Scenes/ Tests/`。
3. **`Prefabs/ElenaFF/FirstPersonHands.prefab` 无 Missing Script。**
   在 Inspector 中展开根节点，`VirtualHandHost`、`ControllerHandPair`、
   `HandAppearanceController`、`HandInputRouter` 应全部正常显示。
4. **材质不是粉色。** 选中 `Materials/Generated/Elena_Skin_ElenaHand_R.mat`，
   应显示 `glTF/PbrMetallicRoughness`，而不是 `Hidden/InternalErrorShader`。
5. **打开场景** `Assets/VirtualHand/Scenes/Showcase/HandShowcase.unity`，按 **Play**。
   应看到双手，Console 出现：
   ```
   [HandRigDriver] HandProportions applied to N bones on HandRig.L/R.
   ```
   （`N=0` 或该行不出现，表示手指比例是默认值，属正常。）

---

## 5. 在游戏中使用

最小接入（3 步）：

1. 把 `FirstPersonHands.prefab` 拖到玩家相机下，位置归零。
2. 在根节点上添加你的适配组件，实现 `IGameHandActionAdapter`。
3. 把它拖到 `VirtualHandHost.adapterBehaviour` 字段。

```csharp
public class MyGameHandAdapter : MonoBehaviour, IGameHandActionAdapter
{
    public void OnHandUpdate(HandTrackingFrame left, HandTrackingFrame right,
                             HandGestureEvent leftGesture, HandGestureEvent rightGesture)
    {
        // 只在这里决定"手势对应什么游戏操作"，
        // 不要在这里改手部骨骼或追踪数据。
    }
}
```

详细契约见 `GAME_INTEGRATION.md`。

---

## 6. 禁用与卸载

### 6.1 临时禁用（推荐，保留设置）

```csharp
var host = FindObjectOfType<VirtualHandHost>();
host.IsEnabled = false;      // 隐藏双手、挂起输入路由、交还控制权
```

游戏原有操作**立即恢复**，因为手部模块从不独占输入：
它只读取自己的 Input Actions，不屏蔽 Legacy Input Manager 的任何按键。

### 6.2 完全卸载

1. 关闭 Unity。
2. 删除 `Assets/VirtualHand/` 整个目录。
3. 如不再需要，从 `manifest.json` 移除 `com.unity.inputsystem`、`com.unity.xr.management`、
   `com.unity.xr.openxr`。**注意：若原游戏自身需要这些包，不要移除。**
4. 清除本模块的按键配置（可选，见下）。
5. 重新打开 Unity。

### 6.3 清除按键配置

本模块的键位覆盖保存在**独立的 PlayerPrefs 键**，不覆盖三个游戏的输入设置：

| 平台 | 位置 |
|---|---|
| Windows | 注册表 `HKCU\Software\<公司名>\<产品名>` 中键 `Neuralife.VirtualHand.BindingOverrides.v1` |

也可在运行时代码中清除：

```csharp
HandActionBindingProfile.ResetToDefaults();   // 或
PlayerPrefs.DeleteKey("Neuralife.VirtualHand.BindingOverrides.v1");
```

---

## 7. 常见问题

| 现象 | 原因 | 处理 |
|---|---|---|
| 材质是粉色 | Shader 未打进构建 / glTFast 缺失 | 确认 `Assets/VirtualHand/Shaders/AvatarBlueOutline.shader` 在 `Always Included Shaders`；确认 glTFast 已安装 |
| 手不可见 | `VirtualHandHost.IsEnabled = false` | 检查是否被代码禁用 |
| 按键无反应 | Input System 未启用 | 确认 `activeInputHandler: 2` |
| 按键无反应 | UI 有焦点 | 本模块在 UI 聚焦时主动屏蔽按键，属预期行为 |
| 手很长/很短 | 手指比例被改过 | 检查 `Resources/Rig/ElenaFF/HandProportions.asset`，全部为 1 即默认 |
| Console 报 `no HandProportions asset` | 资源不在 Resources 目录 | 确认路径为 `Assets/VirtualHand/Resources/Rig/ElenaFF/HandProportions.asset` |
| 左手显示成右手 | Prefab 的 `_isMirrored` 被改 | 左手应为 1，右手应为 0 |
| 批处理构建报 `another Unity instance is running` | 编辑器占用 `Temp/UnityLockfile` | 关闭编辑器后重试 |

---

## 8. 回滚

模块不修改游戏原有脚本、Prefab 或输入设置，因此回滚只需：

1. 删除 `Assets/VirtualHand/`。
2. 还原 `Packages/manifest.json` 与 `packages-lock.json`。
3. 还原 `ProjectSettings/ProjectSettings.asset` 中的 `activeInputHandler`（若原值不是 2）。

**模块不会修改三个正式游戏的任何分支或文件。**
