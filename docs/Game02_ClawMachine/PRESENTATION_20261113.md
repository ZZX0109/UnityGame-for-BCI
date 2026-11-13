# 抓娃娃机：2026-11-13 场景与观察画面改进

本次交付为可复现的源码、场景和资源改进。尚未获得真实 Unity 运行截图、实测物理结果或性能数据，不能据此认定画面验收通过。

## 已写入的改进

- 兔子与狐狸使用有肩部收窄的布偶躯干；兔子增加绣鼻、吻部、腮红、腹部拼布及缝线；狐狸增加卷尾、尾尖、胸部拼布、鼻、耳内色块和小爪。狐狸现在与兔子一样使用织物纹理及低强度法线。
- 新增的面部和服装细节放在 `VisualRoot - noncontact character details`，没有新增碰撞体。原有头/耳碰撞节点继续保留，躯干尺寸、质量与偏心配重不变；视觉仍为刚性布偶，没有实现柔体变形。
- 机柜主要外壳改用圆角网格，增加可见金属脚、嵌入式控台、黄铜细边、螺丝与侧面通风槽。新增结构为视觉装饰。
- 内部灯带材质开启 `_EMISSION`；增加两盏无阴影的低强度补光。一个 128 分辨率反射探针在场景就绪后捕获一次，不逐帧重绘；禁用时清理拥有的对象，无图形设备时不生成捕获。
- C01/C02/C03 使用薄荷、玫瑰、琥珀三套机柜装饰色，通过 `MaterialPropertyBlock` 切换，不改目标、seed 或物体位置。
- 店铺背景增加完整的展示机外框、玻璃、陈列物、后墙层板及地板分缝。
- 游戏进行时默认全宽主画面和简洁任务卡，可展开完整操作；菜单、设置、暂停继续保留滚动的大按钮。PIP 移到左下，独立使用 30°视野和略高的抓取区观察点，仍保持 4:3。UI 上的右键不会开始旋转镜头。

以上全部通过生成器同步到 `ClawMachineScene.unity`、独立 Prefab 和 `GeneratedAssets.json`。场景目前仍是独立的序列化快照，不依赖修改独立 Prefab 自动传播；后续应继续从生成器修改并重新生成。

## 规则与物理约束核对

基准为 `3f709374c6a69354e842f6c7048a10139a074094`。

`verify_presentation_physics.py` 将 YAML 数字对象 ID 解析为节点/组件身份，比较真实场景中所有 Rigidbody、Collider、Core/Drivers/Gripper/Prize 组件字段及祖先位姿。56 个物理/任务组件与基准一致。三份 challenge、profile、workspace、driver、抓取状态机、输入坐标轴均未更改。

该检查证明源数据没有改变上述约束，不替代 Unity 导入、物理仿真或抓取测试。

## 本地检查

```bash
python docs/Game02_ClawMachine/verify_presentation_physics.py
python docs/Game02_ClawMachine/validate_prototype.py
python Tools/validate_project.py
python -m unittest discover -s Tools/tests -v
git diff --check
```

上述检查通过；同步最新 main 后工具测试 55 项通过。新增/扩展的 Unity 网格、视口、简洁面板和 PIP 测试已写入，尚未执行。

## 必须补齐的运行验收

用项目指定的 Unity Editor 2022.3.62f3c1 导入并执行 EditMode、PlayMode 与 Acceptance；逐一确认 C01–C03 正面、左侧、右侧和 PIP 的目标/爪尖可辨认，玻璃不遮挡，简洁与完整面板均可操作。检查一次正常投放和 C03 偏心滑落，验证灯光、反射探针、网格、PIP 与 UI 的启停不累积资源。真实截图与测试输出应由新的视觉捕获流程生成，未经人工审图不能标为视觉验收完成。
