# 当前验证状态 · 2026-11-13

本分支同时包含抓娃娃机的布偶、机柜、灯光、界面与截图采集代码更新；最新说明见
`docs/Game02_ClawMachine/PRESENTATION_20261113.md`。下文八关与存档说明适用于同步的推箱子主线。

本轮保留 Editor 退出 Play 修复、艺术馆视觉和原推动/撤销/碰撞规则，重构第二至第八关地图。
这是供本机视觉及功能测试的源码候选版。当前环境没有 Unity Editor，未运行编译、Test Runner、Windows 构建或 GUI。
历史记录位于 `History/`；旧轮次的通过陈述不能作为本轮运行证据。

| 检查 | 本轮结果 | 证据 |
| --- | --- | --- |
| Python 工具与地图回归 | 合并主线与抓娃娃机后 55/55 通过 | `Integration-Optimization-20261113.txt` |
| 独立关卡求解 | 8/8 可解；最少推动 1/7/22/12/4/10/5/32；机制断言及完整走路/推动回放通过 | 同上；`LevelDesign/solutions.json` |
| 资源完整性 | GUID、meta、Importer、JSON、关卡目录和构建场景检查通过 | `ToolChecks-LevelRedesign-20261113.txt` |
| 字体覆盖 | 880 码点、271,036 字节；当前运行时与目录文字覆盖通过 | 字体目录 license / manifest |
| 差异格式 | 完整差异 `git diff --check` 通过 | `ToolChecks-LevelRedesign-20261113.txt` |
| Unity EditMode | 已扩充八关最少推动与地图修订存档用例；本环境未运行 | `Tests/EditMode/` |
| Unity PlayMode | 已修订旧地图相关交互用例，新增独立 VisualCapture；本环境未运行 | `Tests/PlayMode/` |
| 八关完整行走回归 | 新增 `LevelWalkthroughTests`，通过碰撞、瞄准、推动动画与真实完成路径通关；Unity 未运行 | `Tests/PlayMode/LevelWalkthroughTests.cs` |
| 菜单布局预览与 HUD 要点 | 从实际关卡绘制墙体/走道轮廓，HUD 复用目录说明；实际渲染待验收 | `Scripts/UI/LevelLayoutGraphic.cs` 与 `GameUIController.cs` |
| Unity 编译、字体/贴图导入、Windows 构建 | 未运行 | 没有生成虚假 XML/通过声明 |
| GUI、音色音量、鼠标手感、帧时间/GC | 待本机验收 | `ManualTest-20261112.md` 与视觉说明 |

## 已完成的修改

实验室房间、模块箱、感应台、材质/照明、菜单预览、HUD/组件、微动效和音效的实现见
`LabVisualChanges-20261112.md` 与 `GalleryVisualChanges-20261112.md`。推箱状态机与原碰撞体规则保留。
八关机制与地图版本隔离见 `LevelRedesign-20261113.md`；源地图 SVG 图册不作为 Unity 渲染证据。
本轮进一步增加关卡布局缩略图、HUD 机制说明及八关完整行走测试，详见 `DevelopmentOptimization-20261113.md`。
新增回归覆盖音量设置、材质所有权、Player Shader 引用、装饰/物理隔离、重复归位反馈和预览隔离。
上一轮的暂停/协程事务/静态死格修复及路程统计继续保留。

## 最新敞顶调整

按要求移除天花、加高墙板、顶梁及悬吊灯槽；灯光/反射保留，墙体碰撞及八关规则未改。新增八关可见几何边界回归用例，尚未在 Unity 执行。艺术馆暖色配色保留。

## 本轮未做

EEG/UDP/康复实验数据接入、URP迁移、烘焙GI、外部照片级资产和完整后处理链未实施。
八关 stable ID 与旧解锁保留；重做关卡 revision 2 的旧完成/最佳成绩失效。UI 画面质量、实际字号、字体动态导入及运行时行为仍需本机验收。
