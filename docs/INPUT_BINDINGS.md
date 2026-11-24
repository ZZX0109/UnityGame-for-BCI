# 键位配置 (INPUT_BINDINGS)

自定义键位的动作列表、默认布局、重绑定与存盘机制，以及与三个游戏输入的共存规则。

实现：**Unity Input System 1.7.0**（规范第七部分指定）。

---

## 1. 动作列表

规范第 7.1 条要求的动作，全部已定义于 `Interaction/HandActionDefinitions.cs` 的
`HandAction` 枚举：

| 动作 | 枚举值 | 左手 | 右手 | 双手 |
|---|---|---|---|---|
| Open 张开 | `Open` | ✅ | ✅ | ✅ |
| Fist 握拳 | `Fist` | ✅ | ✅ | ✅ |
| Pinch 捏取 | `Pinch` | ✅ | ✅ | ✅ |
| Point 指向 | `Point` | ✅ | ✅ | ✅ |
| Release 松开 | `Release` | ✅ | ✅ | ✅ |
| Index Flex | `IndexFlex` | ✅ | ✅ | ✅ |
| Middle Flex | `MiddleFlex` | ✅ | ✅ | ✅ |
| Ring Flex | `RingFlex` | ✅ | ✅ | ✅ |
| Little Flex | `LittleFlex` | ✅ | ✅ | ✅ |
| Thumb Flex | `ThumbFlex` | ✅ | ✅ | ✅ |

另有三个命令动作：

| 动作 | 枚举值 | 说明 |
|---|---|---|
| 切换外观 | `ToggleAppearance` | 写实 ↔ 蓝色半透明 |
| 教学开始 | `GuidanceStart` | 显示教学引导手 |
| 教学结束 | `GuidanceEnd` | 隐藏教学引导手 |

**左右手独立绑定。** `HandActionBinding.side` 决定该绑定驱动哪只手，
同一动作可以分别给左右手绑定不同按键。

---

## 2. 默认键位

沿用原 DemoUI 的物理键位，已有肌肉记忆继续有效：

### 2.1 左手

| 按键 | 动作 |
|---|---|
| `1` | 张开 |
| `2` | 握拳 |
| `3` | 捏取 |
| `4` | 指向 |
| `5` | 松开 |

### 2.2 右手

| 按键 | 动作 |
|---|---|
| `6` | 张开 |
| `7` | 握拳 |
| `8` | 捏取 |
| `9` | 指向 |
| `0` | 松开 |

### 2.3 命令

| 按键 | 动作 |
|---|---|
| `B` | 切换外观 |
| `G` | 教学开始 |
| `H` | 教学结束 |

默认布局由 `HandActionBindingProfile.CreateDefault()` 生成。

---

## 3. 配置行为（规范 7.2）

玩家可在设置界面配置的项，对应 `HandActionBinding` 的字段：

| 配置项 | 字段 | 说明 |
|---|---|---|
| 绑定的动作 | `action` | `HandAction` 枚举 |
| 控制左手还是右手 | `side` | `SeatedHandSide` |
| 使用哪个键位 | `bindingPath` | Input System 控制路径，如 `<Keyboard>/1` |
| 按下触发还是持续按住 | `trigger` | `OnPress` / `WhileHeld` / `OnRelease` |
| 松开后是否恢复自然静息 | `restoreRestOnRelease` | |
| 动作切换速度 | `transitionSeconds` | 0.02–2.0 秒 |
| 是否允许多个手指组合 | `allowCombination` | 关闭时该动作先清空其他手指 |

### 3.1 保存 / 重新加载 / 恢复默认

使用 **Input System 官方的 Binding Overrides JSON**，不自己维护按键扫描器：

```csharp
var profile = /* 你的 HandActionBindingProfile 资产 */;

profile.SaveOverrides();                          // 保存
profile.LoadOverrides(profile.Asset);             // 重新加载
profile.ResetToDefaults();                        // 恢复默认
```

**存储位置（独立键，不覆盖游戏设置）：**

```
PlayerPrefs key: "Neuralife.VirtualHand.BindingOverrides.v1"
```

Windows 上落在 `HKCU\Software\<公司名>\<产品名>` 注册表项。

### 3.2 为什么 id 必须确定性生成

Binding Overrides JSON **通过 id 引用动作与绑定**。如果每次运行都随机生成 id，
保存的覆盖在下次启动时就找不到目标，重绑定会静默失效。

因此本实现：

- 不通过 `AddAction` / `AddBinding` 构建（那会生成随机 id）
- 而是**生成 `.inputactions` JSON 字符串**，用 `StableId(seed)`（MD5 派生的确定性 GUID）固定每个动作与绑定的 id
- 再交给 `InputActionAsset.FromJson()` 解析

`StableId` 的 seed 形如 `action/Open/Left`、`binding/Open/Left@<Keyboard>/1`。

### 3.3 重绑定 API

```csharp
// 返回被顶掉的那个绑定的 key，或 null（无冲突）
string replaced = profile.Rebind("Open/Left", "<Keyboard>/q");
```

重绑定后 `bindingPath` 对应的 id 会变化，因此 `Rebind` 会重建 asset 并立即保存。

---

## 4. 与游戏输入的冲突处理（规范 7.3）

### 4.1 默认不占用游戏核心键位

`HandActionBindingProfile.DefaultReservedPaths()` 内置了三个游戏的核心键位，
新手创建 profile 时应把该列表赋给 `reservedPaths`：

| 游戏 | 保留键位 |
|---|---|
| Game01 推箱子 | `W` `A` `S` `D` `E` `Z` `R` |
| Game02 抓娃娃机 | `Space` 方向键 `F` |
| Game03 过马路 | `Escape` `Tab` `Shift` |

默认布局使用的 `1-0` `B` `G` `H` **与以上保留键位无重叠**。

### 4.2 冲突检测

```csharp
List<string> problems = profile.DetectConflicts();
// 例："“<Keyboard>/e” 已被游戏占用，动作 Open 需要重新绑定"
// 例："“<Keyboard>/1” 同时绑定到 Open/Left 和 Fist/Left"
```

检测两类冲突：

1. **与游戏保留键位重叠** → 报告并提示重新绑定
2. **profile 内部重复绑定** → 报告，防止一次按键错误触发两个操作

**检测只报告，不静默接管。** 这是规范第 7.3 条的要求。

### 4.3 菜单获得焦点时屏蔽

`InputActionHandProvider.suppressWhileUiFocused`（默认 **开**）在
`EventSystem.current.currentSelectedGameObject != null` 时：

- 停止驱动手部
- 把当前姿态平滑回落到静息

旧入口 `KeyboardHandPoseProvider` 同样如此（默认 **开**），
保证"菜单有焦点时不触发游戏交互"，且**持有的按键不会持续驱动手部**。

宿主也可显式设置：

```csharp
provider.UiHasFocus = true;   // 菜单打开时
```

### 4.4 与 Legacy Input Manager 共存

| 关注点 | 处理 |
|---|---|
| 模块如何读输入 | **只通过 Input Actions 读取**，不轮询旧 Input Manager |
| 会不会抢游戏按键 | 不会。模块不调用 `Input.GetKey` 之外的任何屏蔽手段，也不禁用其他输入 |
| 项目设置 | `activeInputHandler: 2`（Both） |
| 回归风险 | 需要实际重跑三个游戏确认输入未退化 —— **当前未执行，见 `TEST_REPORT.md`** |

**编译期证据：** 已经验证在有/无 `ENABLE_INPUT_SYSTEM` 两种宏组合下均 0 错误，
因此 Input System 存在与否都不会让工程编译失败。

---

## 5. 两个入口的关系

| 入口 | 组件 | 用途 | 是否支持重绑定 |
|---|---|---|---|
| **新（推荐）** | `InputActionHandProvider` | 自定义键位、存盘、冲突检测 | ✅ |
| **旧（兼容）** | `KeyboardHandPoseProvider` | 保持原 DemoUI 固定键位行为 | ❌（Inspector 里固定 KeyCode） |

两者都实现 `IHandPoseProvider`，都通过 `HandInputRouter` 路由，
**同一只手同一帧只允许一个 provider 写入**。

建议：新工程只用 `InputActionHandProvider`；已有依赖固定键位的工程可同时保留，
但不要让两个 provider 同时驱动同一只手。

---

## 6. 尚未验证的部分

以下为规范第 10.3 条要求的测试，**当前未执行**（需要 PlayMode）：

| 测试项 | 状态 |
|---|---|
| 重绑定成功 | ❌ 未运行 |
| 保存后重启保留 | ❌ 未运行 |
| 恢复默认键位 | ❌ 未运行 |
| 与游戏输入冲突时给出提示 | ❌ 未运行（`DetectConflicts` 逻辑已实现） |
| UI 有焦点时不触发游戏交互 | ❌ 未运行（逻辑已实现） |
| 键位切换不会触发旧动作 | ❌ 未运行 |
| 左右手绑定互不干扰 | ❌ 未运行（结构上按 `side` 分离） |

代码已实现，但**没有运行测试前不得标记为通过**。
