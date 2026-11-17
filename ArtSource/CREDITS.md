# ArtSource 资产与许可

## MakeHuman base.obj + default.mhskel
- 来源: https://github.com/makehumancommunity/makehuman (master, 2026-11 拉取)
- 许可: CC0 1.0 (2020-09 由 Data Collection AB 明确发布至公有领域；正文见 base.obj 文件头)
- 用途: 手部网格基底 + 官方关节坐标表（wrist/metacarpal/finger1-5 全套）
- 本目录 `_download/makehuman/` 保存原始下载件；`_download/makehuman/base_mir.mhclo` 为镜像下载副本（同 CC0）

## 自建资产
- `Hands/hands_source.blend`: 可编辑 Blender 源（骨架/蒙皮/姿态动作）
- `Assets/VirtualHand/Models/Hand_{R,L}.fbx`: Unity 导入件（无需 Blender）
- `Assets/VirtualHand/Models/Hand_*_diffuse|normal|roughness.png`: 2K 烘焙贴图
- 全部按约定保存在本目录（Assets 之外）与 Models 目录，未打包原始下载数据。

## 外部数据（不随仓库打包）
- Ninapro DB8 S1_E1_A3.mat — CC-BY-SA 4.0，缓存于 D:/SoftwareManagement/datasets/NinaproDB8
- PhysioNet EEGMMIDB S001-S003 — OBCI license，缓存于 D:/SoftwareManagement/datasets/eegmmidb
