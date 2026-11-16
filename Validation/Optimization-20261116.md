# 推箱子优化交付 · 2026-11-16

当前分支：`codex/optimize-game01`。本轮实现已完成，交付为源码候选版；Unity原生运行、截图、Windows游戏包及性能仍待许可环境验收。

## 实现

- 保留八关及原地图、解锁顺序、推动容差和地图revision存档。
- 增加本次会话继续本关，保留棋盘、撤销栈、位置、视角与计时；菜单中断半次推动会回滚逻辑、时间与路程。
- 菜单换关、重开及退出保护未完成进度；R保留双按确认。全通文案按当前revision实际完成数判断。
- 增加各关本次会话首次机制说明、可关闭推向/站位辅助及首次死锁解释。辅助开关为本次会话选项。
- 死锁搜索增加2ms软预算并复用容器；撤销/暂停/切关取消过期结果，达到状态上限保持Unknown。

公共修复：文本工具显式UTF-8、受校验文本固定LF，二进制保留字节哈希；原有正式宿主和其他游戏入口未覆盖。娃娃机/过马路分支普通Build Settings首场景已指向各自游戏，推箱子场景保留。新增中文使用原授权字体源2.004重建，字体源SHA与已有manifest相同。

## 本轮实际验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| Python工具回归 | 38/38通过 | `Evidence-Optimization-20261116/SourceChecks/04.log` |
| GUID/meta/importer/构建场景、八关求解、字体覆盖 | 通过 | `Evidence-Optimization-20261116/SourceChecks/` |
| 当前游戏运行源：Editor/Player条件编译、测试源码、Editor工具 | 托管编译通过 | `Evidence-Optimization-20261116/Managed/` |
| 实际纯托管规则 | 19/19通过 | `Evidence-Optimization-20261116/pure-rules.log` |
| Unity Test Runner / 真实物理与UI | **NOT RUN** | `Evidence-Optimization-20261116/UnityAttempt/` |
| Windows EXE/ZIP | **NOT BUILT** | Editor在许可证检查退出，没有伪造Player包 |
| 四分辨率实际截图、10分钟GPU性能、20次重开资源趋势 | **NOT RUN** | 没有将源地图/离线模型或工具测试当实机证据 |

托管检查使用已安装Unity的真实程序集、Roslyn和Mono，没有Physics/UI桩；不执行原生生命周期，也不等同Unity编译导入或Test Runner。`summary.json`记录源指纹，`Managed/source-sha256.json`记录当前C#源码哈希。

## 继续实机验收

先在Unity Hub激活有效的2022.3.62f3c1许可，然后关闭同项目Editor实例，在工程目录运行：

```powershell
$env:UNITY_EDITOR_PATH = 'D:/SoftwareManagement/Unity/2022.3.62f3c1/Editor/Unity.exe'
python Tools/run_source_checks.py
python Tools/run_delivery.py
python Tools/run_visual_capture.py --game sokoban --expect-size 1920x1080
```

完整交付脚本只在真实测试XML全部通过后构建Windows包；截图脚本需可见Game View与图形设备。依次以1280×720、1920×1080、2560×1440、3840×2160采集并人工检查字体、按钮、关键信息与遮挡。终点街景画面不含结果Canvas时以manifest为准。

性能验收使用实际Player确认RTX4060为渲染GPU，1080p高档预热后连续10分钟，目标平均≥60FPS、P95≤20ms；并执行20次重开/切换，记录内存、对象数和GC趋势。当前没有测量数据，不标PASS。
