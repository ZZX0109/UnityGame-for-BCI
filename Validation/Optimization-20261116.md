# 过马路优化交付 · 2026-11-16

当前分支：`codex/optimize-game03`。本轮实现已完成，交付为源码候选版；Unity原生运行、截图、Windows游戏包及性能仍待许可环境验收。

## 实现

- 保留S01/S02/S03原道路、横道、出生点、目的地、停车线、信号相位、车流seed及S03转弯/遮挡。
- 扩建约160m连续街景：住宅/公寓/便利店/咖啡店/药店/书店六门面、住宅/商业两型切角楼、侧墙屋顶、远景楼群。
- 补盲道、树池、排水、车架、垃圾桶、公交站、橱窗；新实体碰撞集中在世界几何层，装饰无碰撞。
- 三主题明确门面排列与设施间距，配置6/10/8名独立seed背景角色；只在人行道安全段活动，暂停同步。
- 完整车型池、仿真驱动轮转/刹车灯/转向灯、材质共享与LOD；碰撞/可见性NonAlloc满缓冲扩容重查，每模拟步集中同步。
- 普通查看、遮挡、清空、违规、瞬时危险与终态提示独立优先级/失效条件；E仅查看明确标识，观察超过10秒仅提醒、不改原评分。
- 实际输入驱动的五机位捕获入口每情境5张：起点、路沿左右、横道中段、目的地。终点隐藏结果Canvas时清单标记includesHUD=false。

公共修复：文本工具显式UTF-8、受校验文本固定LF，二进制保留字节哈希；原有正式宿主和其他游戏入口未覆盖。娃娃机/过马路分支普通Build Settings首场景已指向各自游戏，推箱子场景保留。新增中文使用原授权字体源2.004重建，字体源SHA与已有manifest相同。

## 本轮实际验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| Python工具回归 | 55/55通过 | `Evidence-Optimization-20261116/SourceChecks/04.log` |
| GUID/meta/importer/构建场景、八关求解、字体覆盖 | 通过 | `Evidence-Optimization-20261116/SourceChecks/` |
| 当前游戏运行源：Editor/Player条件编译、测试源码、Editor工具 | 托管编译通过 | `Evidence-Optimization-20261116/Managed/` |
| 实际纯托管规则 | 23/23通过 | `Evidence-Optimization-20261116/pure-rules.log` |
| Unity Test Runner / 真实物理与UI | **NOT RUN** | `Evidence-Optimization-20261116/UnityAttempt/` |
| Windows EXE/ZIP | **NOT BUILT** | Editor在许可证检查退出，没有伪造Player包 |
| 四分辨率实际截图、10分钟GPU性能、20次重开资源趋势 | **NOT RUN** | 没有将源地图/离线模型或工具测试当实机证据 |

托管检查使用已安装Unity的真实程序集、Roslyn和Mono，没有Physics/UI桩；不执行原生生命周期，也不等同Unity编译导入或Test Runner。`summary.json`记录源指纹，`Managed/source-sha256.json`记录当前C#源码哈希。

## 继续实机验收

先在Unity Hub激活有效的2022.3.62f3c1许可，然后关闭同项目Editor实例，在工程目录运行：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_game_delivery.py --game street
python Tools/run_visual_capture.py --game street --expect-size 1920x1080
```

完整交付脚本只在真实测试XML全部通过后构建Windows包；截图脚本需可见Game View与图形设备。依次以1280×720、1920×1080、2560×1440、3840×2160采集并人工检查字体、按钮、关键信息与遮挡。终点街景画面不含结果Canvas时以manifest为准。

性能验收使用实际Player确认RTX4060为渲染GPU，1080p高档预热后连续10分钟，目标平均≥60FPS、P95≤20ms；并执行20次重开/切换，记录内存、对象数和GC趋势。当前没有测量数据，不标PASS。
