# GLMResourceStyling 交付说明（2026-11-20）

负责人：ZCode glm-5.3-flash。工作范围仅本目录（`ArtSource/Avatar/GLMResourceStyling/`）。

## 1. 实际完成内容

| 文件 | 说明 |
| --- | --- |
| `appearance_presets.json` | 两套通用成人外观配置（`casual_female_A` / `casual_male_A`）：真实比例基线、自然皮肤、短发、眉毛/眼睛/睫毛/牙齿/舌头、朴素便装+鞋。全部为 MPFB2/MakeHuman 现成资产，无自造模型。 |
| `style_preview.py` | Blender 无头渲染脚本（Blender 5.2.3 LTS，`D:/SoftwareManagement/apps/Blender/blender.exe` 已验证在位）：加载 AG 的 .blend → 副本方式调材质（只调已有 Principled BSDF 粗糙度/色泽，不加工程生成贴图）→ 三点式棚拍布光 → 输出头部近景 + 上半身两张 PNG + 调整后副本 `restyled_copy.blend` + `applied.json`（资产→对象映射）。原始 .blend 不回写。 |
| `HANDOFF.md` | 本文件。 |

## 2. 配置对应资源（资产名均经只读核对）

- **资产名来源**：官方素材包预览页 `makehuman_system_assets.html` / `skins02.html`（见任务单链接），以及 MPFB2 源码 `script_samples/05、06、10` 中硬编码的真实文件名（`young_caucasian_female.mhmat`、`low-poly.mhclo`、`eyebrow001.mhclo`、`teeth_base.mhclo` 等）。
- **许可**：系统包 90+ 项全部 CC0、作者 makehuman_system；skins02 全部 CC0（女款预设皮肤 `young_caucasian_female` 来自系统包；男款预设皮肤 `toigo_light_skin_male_freckles` 作者 MargaretToigo，CC0）。MPFB2 源码 GPLv3、随附图形资产 CC0（`LICENSE.ASSETS.md`）。
- **材质约定**：与 AG 导出示例（`script_samples/10_complete_character_export_fbx.py`）一致，统一 `GAMEENGINE` 材质，便于后续 Unity FBX 导入；粗粝度/色泽建议写在 JSON 各资产项内。
- **本地缓存现状（交付时点）**：`ArtSource/_download/avatar-resources/manifest.json` 已登记 mpfb2（SHA-256 本地记录在案）；`makehuman_system_assets_cc0.zip` 仍在下载（`.partial`），skins02 未出现，二者的解压目录尚不存在。JSON 中这些项标 `status: pending_download`，`relative_path` 为按官方包清单推断的预期位置，解压后需实际解析确认。

## 3. 渲染图片

**暂无。** 两个前置条件当时均未满足：

1. 素材包未下载完成（见上）；
2. AG 的 `build_mpfb_avatar.py` 全身 .blend 尚未产出，`style_preview.py` 没有可用的输入文件。

脚本就绪后的运行方式：

```bash
"D:/SoftwareManagement/apps/Blender/blender.exe" -b -P \
  ArtSource/Avatar/GLMResourceStyling/style_preview.py -- \
  --blend  <AG 产出的 .blend> \
  --config ArtSource/Avatar/GLMResourceStyling/appearance_presets.json \
  --preset casual_female_A
```

输出默认写入本目录 `Results/<preset>_<时间戳>/`。任一资产或材质缺失时脚本以退出码 2 显式失败并列出缺失清单，不用假素材兜底（符合任务约束）。

## 4. 尚未验证项（如实声明）

- **JSON 中 `pending_download` 项的 relative_path 未在磁盘验证**——仅与官方清单核对过资产名；解压后若文件名有出入，以实际为准并回填路径。
- **style_preview.py 未跑通端到端渲染**——语法/JSON 已自检通过，Blender 可执行与版本已验证，但因缺少输入 .blend，材质匹配、布光、出图均未实测。
- **对象匹配假设**：脚本依赖 MPFB2 写入的 `asset_source` / `object_type` 自定义属性与 `<human>.<asset_name>` 命名约定（源码 `humanservice.py:530` 附近已确认该约定）；若 AG 的导出器改变命名，需同步调整 `find_character_object`。
- **朝向假设**：按 MakeHuman/MPFB 默认脸朝 -Y 取景；若 AG 模型朝向不同，头部/上半身相机会取到背面，改 `main()` 中两个相机 Y 值符号即可。
- skins02 男款皮肤 `toigo_light_skin_male_freckles` 的 .mhmat 实际文件名未在磁盘核对（官方页面列出资产名，文件名按包内惯例推断）。

## 5. 边界确认

- 仅写入本目录；未打开 Unity，未改动正式游戏、Assets、其他 ArtSource 子目录、下载缓存（只读）与主计划文档。
- 未抢写 `build_mpfb_avatar.py`（Luna AG）、Unity 网格分区（Luna AH）、蓝色材质（Luna AI）。
- 面部/外观为通用成人合成，未采集任何真实患者人脸或人体测量；JSON 中已写入禁止混淆为患者数据的约束。

## 6. 交接给 Codex（最终集成时）

1. 素材包解压后回填 JSON 中 `pending_download` 路径，并跑一次 `style_preview.py` 验证；
2. AG .blend 产出后按第 3 节命令渲染验收图；
3. 若 Unity 侧需要 Cycles 之外的表现，皮肤材质可改 `MAKESKIN`（`script_samples/05`）重出。
