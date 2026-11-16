# 娃娃机优化交付 · 2026-11-16

当前分支：`codex/optimize-game02`。本轮实现已完成，交付为源码候选版；Unity原生运行、截图、Windows游戏包及性能仍待许可环境验收。

## 实现

- 保留C01礼盒、C02兔子、C03偏心狐狸，布局、seed、尝试次数和物理参数保持。
- 滚轮仅主观察区变焦，侧栏、HUD、设置与画中画不抢滚动；空抓/滑落即时反馈。
- 闭合无持物进入Returning，先升高再回Home，直接结算；滑落仍走原运输/释放/物理稳定判奖，不重复计次。
- C01投影与XZ辅助、C03重心提示及接触/稳定进度均为只读；设置独立保存并恢复原暂停状态。
- 面板节点池复用并按动作语义恢复焦点，避免Grab/Help/Retry复用同一节点后误触其他动作。

公共修复：文本工具显式UTF-8、受校验文本固定LF，二进制保留字节哈希；原有正式宿主和其他游戏入口未覆盖。娃娃机/过马路分支普通Build Settings首场景已指向各自游戏，推箱子场景保留。新增中文使用原授权字体源2.004重建，字体源SHA与已有manifest相同。

## 本轮实际验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| Python工具回归 | 57/57通过 | `Evidence-Optimization-20261116/SourceChecks/04.log` |
| GUID/meta/importer/构建场景、八关求解、字体覆盖 | 通过 | `Evidence-Optimization-20261116/SourceChecks/` |
| 当前游戏运行源：Editor/Player条件编译、测试源码、Editor工具 | 托管编译通过 | `Evidence-Optimization-20261116/Managed/` |
| 实际纯托管规则 | 9/9通过 | `Evidence-Optimization-20261116/pure-rules.log` |
| Unity Test Runner / 真实物理与UI | **NOT RUN** | `Evidence-Optimization-20261116/UnityAttempt/` |
| Windows EXE/ZIP | **NOT BUILT** | Editor在许可证检查退出，没有伪造Player包 |
| 四分辨率实际截图、10分钟GPU性能、20次重开资源趋势 | **NOT RUN** | 没有将源地图/离线模型或工具测试当实机证据 |

托管检查使用已安装Unity的真实程序集、Roslyn和Mono，没有Physics/UI桩；不执行原生生命周期，也不等同Unity编译导入或Test Runner。`summary.json`记录源指纹，`Managed/source-sha256.json`记录当前C#源码哈希。

## 继续实机验收

先在Unity Hub激活有效的2022.3.62f3c1许可，然后关闭同项目Editor实例，在工程目录运行：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_game_delivery.py --game claw
python Tools/run_visual_capture.py --game claw --expect-size 1920x1080
```

完整交付脚本只在真实测试XML全部通过后构建Windows包；截图脚本需可见Game View与图形设备。依次以1280×720、1920×1080、2560×1440、3840×2160采集并人工检查字体、按钮、关键信息与遮挡。终点街景画面不含结果Canvas时以manifest为准。

性能验收使用实际Player确认RTX4060为渲染GPU，1080p高档预热后连续10分钟，目标平均≥60FPS、P95≤20ms；并执行20次重开/切换，记录内存、对象数和GC趋势。当前没有测量数据，不标PASS。
