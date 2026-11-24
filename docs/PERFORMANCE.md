# 性能测量 (PERFORMANCE)

对应规范第 10.7 条。**本文件只记录真实测量到的数据，并把没能测的部分明确列出。**

测量脚本：`Assets/VirtualHand/Scripts/Editor/HandPerformanceProbe.cs`
原始输出：`Results/hand-performance.json`

```
Unity.exe -batchmode -projectPath <proj> \
  -executeMethod Neuralife.VirtualHand.Editor.HandPerformanceProbe.Run -logFile <log> -quit
```

---

## 1. 测量环境（真实）

| 项目 | 值 |
|---|---|
| Unity 版本 | **2022.3.62f3c1**（中国版） |
| 运行形态 | **Unity 编辑器**（不是构建后的播放器） |
| 操作系统平台 | WindowsEditor |
| 机器 | ASUS `System Product Name` |
| CPU | **AMD Ryzen 9 9950X 16-Core**（32 逻辑核） |
| 内存 | 63,162 MB |
| GPU | **NVIDIA GeForce RTX 4060** |
| 图形 API | **Direct3D11** |
| 测量时刻 (UTC) | 2026-11-24T08:21:31Z |

> ### ⚠️ 三条必须随这些数字一起披露的限制
>
> 1. **这是编辑器测量，不是设备帧时间测量。** GPU 帧耗时、头显刷新余量、双目渲染开销
>    全部**不在**本次测量范围内。
> 2. **本工程的 StandaloneWindows Burst AOT 处于关闭状态**（原因见 `KNOWN_LIMITATIONS.md` 第 5.5 节）。
>    因此编辑器计时**不代表 AOT 构建**的表现。
> 3. **没有头显设备**，规范 10.7 要求的"目标设备型号 + 实际刷新率下的帧率与帧时间"为
>    **`HARDWARE_NOT_TESTED`**。

---

## 2. 静态几何（已测量）

| 指标 | 单手数值 |
|---|---|
| SkinnedMeshRenderer 数量 | **1** |
| 顶点数 | **16,330** |
| 三角面数 | **28,994** |
| 子网格数 | **1** |
| BlendShape 数 | **0** |
| 骨骼数 | **69** |
| 材质槽数 | **1** |
| 场景内 Transform 总数 | **84** |
| 场景内 Renderer 总数 | **1** |

**双手推算：** 约 **57,988 三角面**、2 个 SkinnedMeshRenderer、2 个材质槽。

> **对 PICO 一体机的意义（工程推断，非实测）：** 近 5.8 万三角面的一双手对移动端一体机属于偏重，
> 但并非不可行。**未在任何头显上实测**，因此这只是需要后续验证的风险点，不是结论。
> 若需要降低，可选方向是减面或复用同一网格的 LOD——本轮**未做**任何减面。

---

## 3. CPU 成本（编辑器内测量，1000 次取均值）

| 指标 | 数值 |
|---|---|
| `SetControls` 单次（**开启**指间自穿插检测） | **0.00892 ms** |
| `SetControls` 单次（**关闭**检测） | **0.00667 ms** |
| 指间检测的额外开销 | **0.00224 ms** |
| 采样次数 | 2,000 次 / 每组 |

**如何解读：** 给一只手应用一次 20 路控制量约 **9 微秒**。双手即约 **18 微秒**。

对照规范给出的帧预算：

| 刷新率 | 一帧预算 | 双手姿态更新的占用 |
|---|---|---|
| 72 Hz | 13.9 ms | 约 **0.13%** |
| 90 Hz | 11.1 ms | 约 **0.16%** |

**结论：手部姿态更新的 CPU 成本相对于帧预算可以忽略。** 这个结论只在编辑器测量下成立，
但姿态更新是纯托管代码 + Transform 写入，与 GPU 无关，因此该结论相对稳健；
**真正的风险在渲染面数，不在 CPU 姿态计算。**

指间自穿插检测（规范 3.2）增加约 **0.0022 ms**。它是可关闭的
（`HandRigDriver.detectSelfIntersection`），如果目标设备吃紧可以直接关掉，
代价是失去该诊断。

---

## 4. 外观切换成本（已测量）

| 指标 | 数值 |
|---|---|
| 切换次数 | 200 次（100 个来回） |
| 单次平均 | **0.00095 ms** |
| 总耗时 | 0.095 ms |
| 切换后是否复用同一个材质实例 | **是** ✅ |
| 材质实例增长 | **无** ✅ |

**结论：** 写实 ↔ 蓝色切换是廉价操作，且**不会产生累积的材质实例**
（这是规范 10.4 关心的显存泄漏风险，此处以真实测量确认无泄漏）。

---

## 5. 未能测量的部分（**不得当作已通过**）

| 规范 10.7 要求项 | 状态 | 原因 |
|---|---|---|
| 目标设备型号 | 🚫 | 无头显。已记录的是**开发机**，不是目标设备 |
| 帧率与帧时间（设备实测） | 🚫 **HARDWARE_NOT_TESTED** | 需运行设备 |
| CPU / GPU 帧耗时（设备实测） | 🚫 | 上述 CPU 数值为编辑器微基准，GPU 未测 |
| 蓝色模式的额外渲染成本 | ⚠️ **部分** | 已测 CPU 侧切换开销；**GPU 侧渲染成本未测** |
| XR 手部追踪更新频率 | 🚫 | XR Hands 包不可得，无 XR 子系统 |
| 明显 GC 分配与突发掉帧 | ❌ 未测 | 需设备 + Profiler 长时运行 |
| 双目渲染开销 | 🚫 | 无设备 |

**规范要求「必须以实际设备刷新率和实测结果作为验收基础，而不是只设置目标帧率」——
因此上表中的所有 🚫 项都保持未通过状态，不以本文第 3 节的推算代替。**

---

## 6. 复现方式

```powershell
# 编辑器测量（不加 -nographics，否则 GPU 会显示为 Null Device）
Unity.exe -batchmode -projectPath <proj> `
  -executeMethod Neuralife.VirtualHand.Editor.HandPerformanceProbe.Run `
  -logFile <proj>\Logs\perf.log -quit

# 结果
<proj>\Results\hand-performance.json
```

脚本每次运行都会重新测量几何与 CPU 数值，并在 JSON 中如实写入
`measurementContext`、`burstAotState` 与 `finalDeviceFrameTimeMs: "HARDWARE_NOT_TESTED"`，
避免这些数字被误当成设备验收结果。
