# Game04 VirtualHand — 实施进度说明（GLM-5.3-flash）

按 `docs/IMPLEMENTATION_BRIEF.md` 约定实施。DHS 二审、Codex 最终整合。
本文件持续更新关键文件路径与可复现命令。

## 已交付

### 1. 模型（Blender + MakeHuman CC0）
- 基底：MakeHuman 官方 `base.obj`（CC0 2020, Data Collection AB）+ 官方 `default.mhskel` 关节坐标表（权威腕/掌/指关节位置，非猜测）。
- 重建脚本：`ArtSource/build_hands.py`（一键：切割前臂→bmesh 补腕口→官方坐标骨架→自动蒙皮→2K 烘焙→姿态动作→渲染→FBX）。
- 产物：
  - `Assets/VirtualHand/Models/Hand_R.fbx`、`Hand_L.fbx`（含骨骼+蒙皮网格）
  - `Assets/VirtualHand/Models/Hand_{R,L}_{diffuse,normal,roughness}.png`（2K）
  - `ArtSource/Hands/hands_source.blend`（可编辑源）
  - `ArtSource/Review/hand_{r,l}_{open,palm,back,side,fist,pinch}.png` + `hand_two_{open,fist}.png`
- 蒙皮：程序化距离衰减顶点组（每顶点最近 3 骨）+ Armature 修改器（headless 下 ops ARMATURE_AUTO 不可靠）。
- 姿态：世界轴增量旋转（每帧重置 rest 后绕世界 X 弯曲），不依赖 FBX 骨骼 roll 约定；Unity 端 HandRigDriver 同一方法。
- 命令：
  ```
  D:/SoftwareManagement/apps/Blender/blender.exe --background --factory-startup --python ArtSource/build_hands.py
  ```
- 三角面数约 3.8k/手（在约定 10-20k 下方，轻量；如需加密可调 `fit_axis` 半径与细分参数）。

### 2. DB8 动力学回放（真实手套信号）
- 脚本：`Tools/db8_export.py`
- 输入：`D:/SoftwareManagement/datasets/NinaproDB8/S1_E1_A3.mat`（18 通道 Cyberglove II 校准信号，100Hz 采样）
- 通道映射假设（记录于脚本头与 provenance）：DIP 与 PIP 耦合、拇指 DIP 无独立传感、spread 取辅助通道均值；传感器值鲁棒归一化为 [0,1]，不是角度。
- 输出（`Assets/StreamingAssets/Replays/db8/`）：rest/thumb/index/cylindrical_grip/lateral_grip/tripod_grip 六剪辑 JSONL + `db8_provenance.json`。标签仅用于切剪辑，回归目标为手套信号本身。
- 命令：
  ```
  D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/db8_export.py --mat D:/SoftwareManagement/datasets/NinaproDB8/S1_E1_A3.mat --outdir Assets/StreamingAssets/Replays --fps 30
  ```

### 3. EEG 真实解码（CSP+LDA）
- 脚本：`Tools/eeg_decode.py`，依赖：mne/numpy/scipy/scikit-learn（venv bci 均已具备）。
- 按约定：S001-S003，runs 04+06 训练 / 08+10 验证 / 12+14 �出测试；因果 8-30Hz 带通、共平均参考、0.5-2.5s epochs、CSP(6)+shrinkage LDA、seed 42。变换仅用训练集拟合，无测试调参、无泄漏切分。
- 产出（`Assets/StreamingAssets/Replays/eeg/`）：`eeg_metrics.json` + 每人 `eeg_S00X_predictions.jsonl`（source="prediction"，首帧 t=窗长即预测不早于分析窗结束）与 `eeg_S00X_truth.jsonl`（真值单独文件，预测模式不读）。错误预测保留。
- S001 测试集平衡准确率 0.32（原始 EEG 正常水平，约定不设最低准确率门槛）。
- 命令：
  ```
  D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/eeg_decode.py --data D:/SoftwareManagement/datasets/eegmmidb/1.0.0 --subjects S001,S002,S003 --outdir Assets/StreamingAssets/Replays
  ```

### 4. Unity 运行时（2022.3.62f3c1 Built-in）
- `Assets/VirtualHand/Scripts/Core/HandMotionFrame.cs` — 冻结契约（未改动）。
- `Runtime/HandRigDriver.cs` — 20 路归一化控制→骨骼旋转（拇指块 CMC/MCP/IP/opposition，其余 MCP/PIP/DIP/spread），非有限数值与长度错误拒绝。
- `Runtime/PoseLibrary.cs` — rest/fist/pinch/point 姿态库 + 意图→手映射。
- `Runtime/ReplaySource.cs` — JSONL 流式解析（严格校验：duration>0、intent/source 枚举、[0,1] 值域、20 长度数组；拒绝行可见计数）。
- `Runtime/ReplayController.cs` — �一时钟（暂停同时停回放与任务），间隙超时回收意图为 rest，重置清空姿态/时钟/来源。
- `Runtime/TaskController.cs` — Carry/Handoff 状态机：接触后抓握闩锁跨 rest/dropout，单对象所有权（kinematic 切换），仅指定阶段释放。
- `Runtime/DemoUI.cs` — 中文状态（模式/意图/置信/来源/主体|run），空格=播放暂停、R=重置、1-5=手动姿态。
- `Editor/VirtualHandBuild.cs` — 入口 `Neuralife.VirtualHand.Editor.VirtualHandBuild.GenerateProject` / `.BuildWindows`，输出 `Builds/Windows/VirtualHand.exe`，摘要写 `Results/`。
- `Editor/DemoSceneBuilder.cs` — 程序化搭场景（FBX 实例化+贴图绑定+灯光+UI+相机）。

## 待办 / 边界
- FBX 导入 Unity 后需 Codex 侧构建验证（不启动竞争 Editor）。
- DB8 右手为左手镜像（`db8_provenance.json` 注明），A3 仅可视化，无 EMG 模型、无显示校准的预测指标。
- 素材署名：MakeHuman base.obj（CC0）；NinaproDB8（CC-BY-SA 4.0）；PhysioNet EEGMMIDB（OBCI license）。
