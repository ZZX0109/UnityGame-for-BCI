# Game04 VirtualHand

基于真实数据的虚拟手演示： Ninapro DB8 数据手套姿态回放、PhysioNet EEGMMIDB
离线运动想象解码回放、双手协作搬运/交接任务。Unity 2022.3.62f3c1 Built-in，
Windows x64。

> 联络与分工见 `docs/IMPLEMENTATION_BRIEF.md`；整体计划见
> `docs/VIRTUAL_HAND_PLAN.md`；数据管线细节与复现命令见 `Tools/README.md`；
> 素材与数据署名见 `ArtSource/CREDITS.md`。本文件只是操作说明，不复制上述内容。

## Windows 试玩

第一视角 VR 的工程分工、已知穿模和患者坐姿接入步骤见 [VR 接入计划](docs/FIRST_PERSON_VR_PLAN.md)。已经启用 XR 的 Unity 宿主可将 `Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab` 挂在追踪空间下面。该预制件读取两只手柄的握持位姿和抓握量，无追踪时隐藏对应手；启用 XR 的责任在宿主。每只腕的 `wristPositionOffset` 和 `wristRotationOffsetEuler` 可单独标定。正式游戏接入与佩戴验收进度见 [开发结果](RESULTS.md)。

打开 [VirtualHand.exe](Builds/Windows-Elena/VirtualHand.exe)。启动后会载入圆柱抓握样例。可以在左侧选择六段 DB8 动作之一，再点“播放 / 暂停”。右键拖动模型区域可旋转镜头，滚轮调整距离。左手来自手套数据，右手显示镜像。

当前 Demo 使用 Elena FF 的双手模型。协作搬运和双向交接已通过真实数据回放测试。若要试玩搬运，选择 S001、点“EEG 预测”和“协作搬运”，再播放。侧栏会标出手部辅助握持；放下方块后恢复输入回放。实机截图和最新验证见 [开发结果](RESULTS.md)。11 月 18 日的运行版本保存在 `Builds/Windows`。

## 快速上手

```text
1. Unity Hub 打开本工程（2022.3.62f3c1，Built-in 渲染管线）。
2. 菜单 Neuralife → VirtualHand → Generate Project Assets
   （= Neuralife.VirtualHand.Editor.VirtualHandBuild.GenerateProject），
   程序化生成 Demo 场景（双手、桌面、道具、UI、EventSystem）。
3. 按 Play：
   - 启动时自动载入 DB8 圆柱抓握片段；
   - 左侧按钮：DB8 姿态回放 / EEG 预测回放 / 明确标签回放 / 播放暂停 / 重播，
     以及 协作搬运、交接(左→右)、交接(右→左)；
   - DB8 区域列出全部六段已导出片段（rest / thumb / index /
     cylindrical_grip / lateral_grip / tripod_grip），逐条点选；
   - 顶部状态行显示 模式/意图/置信度/来源/被试|run，下方橙色行为告警
     （非法行数、加载错误），进度条为回放位置。
4. 键盘：空格=播放/暂停，R=重播，1–5=手动姿态（rest/fist/pinch/point/混合）。
   鼠标右键拖动旋转视角，滚轮缩放，与 UI 不冲突。
5. 构建：菜单 Neuralife → VirtualHand → Build Windows x64，输出
   Builds/Windows-Elena/VirtualHand.exe（Player 下回放数据取自 StreamingAssets）。
```

## 数据回放说明

- **DB8 姿态回放**（source=glove）：Ninapro DB8 S1 的 18 通道 CyberGlove
  校准信号映射为 20 路归一化手部控制（非角度）。手套只测了**左手**，因此
  所有片段意图均为 left/rest；片段由 stimulus 标签定位，标签从不作为回归
  目标。A3 仅作可视化。
- **EEG 预测回放**（source=prediction）：S001–S003 离线 CSP(6)+shrinkage
  LDA 的**真实预测**，含保留的错误预测；预测时间不早于其分析窗结束。
  **预测模式绝不读取真值文件**——真值在独立的 `*_truth.jsonl`
  （source=labels），仅通过“明确标签回放”入口显式载入，用于对照。
- **意图四类**：rest/left/right/both，驱动双手开合与任务状态机。任务沿
  预设辅助路径运动（用户点按钮选择任务，EEG 只提供意图类别，不做到达
  位置解码）。Carry：双手接近→抓取→沿路径→目标区内持续 rest 才放下。
  Handoff：发送方抓取→路径中点进入传递→接收方握稳后所有权移交、发送方
  才松开→接收方继续放置。抓持跨 rest/低置信度/中断保持。
- 暂停同时停止回放与任务运动；重播/切换模式清空姿态、持物、时钟与来源。

## 数据复现

数据管线（下载、解码、DB8 导出、单元测试）的依赖与命令见
`Tools/README.md`；字体子集生成见 `Tools/build_game04_font.py` 头部说明。
回放数据已随工程提交在 `Assets/StreamingAssets/Replays/`，无需重跑即可
使用。

## 当前限制

- 正式 Demo 使用 Elena FF 原生开合和左右手捏握预设。按数字键 3 可预览双手捏握。单手交接时，拇指扶住箱顶，其余手指托住侧面。[捏握近景](Results/elena-thumb-20261120/manual-pinch-close.png)
- 六段 DB8 手套回放均驱动食指至小指的屈伸和侧向张开。模型支持逐指侧展；这批样例将三个指间传感器的均值映射到四指，右手显示左手记录的镜像。
- EEG 离线解码逐被试测试集 balanced accuracy：S001 0.083、S002 0.278、
  S003 0.466（chance=0.25，均值 0.276；seed 42，固定划分，错误预测保留，
  无真值补正）。**不承诺任何数据分类准确率**，数字仅供参考，详见
  `Results/data/DATA_SUMMARY.md`。
- DB8 右手为左手镜像，来源见 `db8_provenance.json`。传感器值经过归一化，手指间距为虚拟模型上的测量值。测试范围、实机截图与 DeepSeek Harness 二审记录见 [开发结果](RESULTS.md)。
