# 输入预检 — 七角色清单（2026-11-20，静态审阅）

状态：**STATIC REVIEW ONLY**。本文仅记录静态文件核对与源码审阅结论，不含任何运行 PASS（构建/加载均未执行）。资料来源：Luna 1 `Results/vr-check-20261120-2000/luna-assets/report.md` + `inputs.json`（下称 L1），本会话仅做磁盘复核与 MPFB2 源码审阅。

## 执行归因

- 实际模型：glm-5.3-flash / reasoning_effort=max（ZCode 本机会话）
- 本轮可证明活动窗口：起 2026-11-20 20:32:34 +0800（date 命令记录），止见文末 `ACTIVITY_LOG_20261120.md`
- 上一轮工时更正：原报 0.5h 无活动日志支撑，**改为未测**；可证明证据仅为检查点文件 mtime 20:06:00（20:05 派发后），可归档工具活动 ≤2 分钟，实际思考等待时长不可证明。等待 Luna 期间不计工时。

## 七角色清单

基网要求：五个已获取 .mhclo 均标 `hm08`（L1）；MPFB 2.0.18 basemesh 与 hm08 兼容（MPFB2 官方示例同源加载）。

| # | 角色 | 选定资产 | 作者/许可（官方页原文） | 描述符及依赖 | SHA-256 | 状态 |
|---|---|---|---|---|---|---|
| 1 | skin | `skins02_cc0\skins\toigo_light_skin_male_bronze\toigo_light_skin_male_bronze.mhmat` | MargaretToigo / CC0（skins02 包页） | diffuseTexture `young_lightskinned_male_diffuse_Bronze.png` **已核实存在于同目录**；litsphere/phong 为全局引用（见下） | `4dbc8cd5…584b7`（本会话 20:0x 审计算得，见上轮检查点） | ✅ 就绪 |
| 2 | eyes | — | makehuman_system / CC0（包页） | 官方仅 system 主包含 `low-poly/high-poly.mhclo`；eye-materials01/02 包为材质非网格（L1 核实） | — | ⛔ **阻塞**（唯一阻塞项） |
| 3 | eyebrows | `garments-cc0\eyebrows\mindfront_eyebrows_01\mindfront_eyebrows_01.mhclo` | Mindfront / CC0 | OBJ+MHMAT 齐全，无外部纹理（L1：结构 PASS） | `1e62989d…1757f`（L1） | ✅ 就绪 |
| 4 | hair | `garments-cc0\hair\toigo_blunt_bob\toigo_blunt_bob.mhclo` | MargaretToigo / CC0 | OBJ+MHMAT+3 纹理齐全（L1：结构 PASS） | `252f6b2e…4e1`（L1） | ✅ 就绪 |
| 5 | shirt | `garments-cc0\clothes\toigo_fisherman_sweater\toigo_fisherman_sweater.mhclo` | MargaretToigo / CC0 | OBJ+MHMAT+2 纹理齐全（L1：结构 PASS） | `b454960b…d8999`（L1） | ✅ 就绪 |
| 6 | trousers | `garments-cc0\clothes\cortu_cargo_pants\cortu_cargo_pants.mhclo` | Cortu / CC0 | OBJ+MHMAT+2 纹理齐全；`litsphereTexture data/litspheres/lit_matte.png` 缺失（影响见下） | `5bc0c728…fada`（L1） | 🟡 就绪（litsphere 静态判定不阻塞） |
| 7 | shoes | `garments-cc0\clothes\toigo_mj_cloth_shoes\toigo_mj_cloth_shoes.mhclo` | MargaretToigo / CC0 | OBJ+MHMAT+3 纹理齐全（L1：结构 PASS） | `6e81c1d6…05e`（L1） | ✅ 就绪 |

所有绝对路径根：`C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\_download\avatar-resources\`。L1 的 30 文件 SHA 复核 30/30 由 Luna 1 完成，本会话抽查目录结构与 .mhmat 内容与 L1 记载一致，未重算全部 SHA。

## 皮肤 .mhmat 贴图核实（本会话磁盘复核）

`toigo_light_skin_male_bronze` 目录内容：`…Bronze.mhmat`、`…Bronze.thumb`、`young_lightskinned_male_diffuse_Bronze.png`（三件齐全）。mhmat 正文（本会话读取）：

- `diffuseTexture young_lightskinned_male_diffuse_Bronze.png` → **存在**，同目录相对引用可解析。
- `shaderParam litsphereTexture data/litspheres/lit_standard_skin.png` → 全局根引用，**磁盘缺失**（同裤子类问题）。
- `shader data/shaders/glsl/phong` → 全局根引用，同样缺失。

### litsphere/phong 全局引用缺失的静态影响（MPFB2 2.0.18 源码审阅）

- `entities/material/makeskinmaterial.py:270`：`if key.endswith("Texture") and self._settings[key] and not "litsphere" in key:` —— **MakeSkin 材质显式跳过 litsphere 键**，不尝试加载该文件。衣物（含裤子）按 `build_mpfb_avatar.py` 走 `material_type='MAKESKIN'` → 静态判定：`lit_matte.png` 缺失**不阻塞**。
- 皮肤走 `ENHANCED_SSS`（`enhancedskinmaterial.py`）：全源码 litsphere 检索仅命中 `mhmatkeys.py`（键定义）、`mhmaterial.py`（通用解析）、`makeskinmaterial.py`（跳过逻辑）、`makeskinpanel.py`（UI），增强皮肤材质构建不消费 litsphere/phong → 静态判定：**不阻塞**。
- 以上为源码级推断，**运行时加载仍未测**（本轮禁止运行构建）。

## 眼球缺项（唯一阻塞）

- 本地无任何 `eyes/*.mhclo`；system 主包无效 .partial 禁用；官方 eye-materials 包为材质非网格（L1 已核实类型）。
- 影响面：`build_mpfb_avatar.py` 的 `REQUIRED_ROLES` 含 eyes，`resolve_assets` 在任何角色缺失时整体退出（exit 2，无占位）→ **eyes 未解决前构建不可运行**。
- 解决路径（需用户/上游决策，任一）：a) 授权从官方主站新取 system 包；b) 授权从无效 .partial 尝试恢复；c) 授权从 MPFB2 测试数据外来源补充。此前未授权前保持阻塞。

## 完整命令模板（**未运行**；eyes 解锁并确认后由后续轮次执行）

```bash
cd "C:/Users/Ait/Desktop/Neuralife/Unity/Game04-VirtualHand/ArtSource"
"D:/SoftwareManagement/apps/Blender/blender.exe" -b --python Avatar/build_mpfb_avatar.py -- \
  --assets-root "ArtSource/_download/avatar-resources" \
  --skin "ArtSource/_download/avatar-resources/skins02_cc0/skins/toigo_light_skin_male_bronze/toigo_light_skin_male_bronze.mhmat" \
  --eyes "<待补：本地不存在任何 eyes .mhclo>" \
  --eyebrows "ArtSource/_download/avatar-resources/garments-cc0/eyebrows/mindfront_eyebrows_01/mindfront_eyebrows_01.mhclo" \
  --hair "ArtSource/_download/avatar-resources/garments-cc0/hair/toigo_blunt_bob/toigo_blunt_bob.mhclo" \
  --shirt "ArtSource/_download/avatar-resources/garments-cc0/clothes/toigo_fisherman_sweater/toigo_fisherman_sweater.mhclo" \
  --trousers "ArtSource/_download/avatar-resources/garments-cc0/clothes/cortu_cargo_pants/cortu_cargo_pants.mhclo" \
  --shoes "ArtSource/_download/avatar-resources/garments-cc0/clothes/toigo_mj_cloth_shoes/toigo_mj_cloth_shoes.mhclo"
```

说明：`--assets-root` 在 eyes 缺失时其实可省略显式六件（自动发现仅对 skin 生效），显式写出是为把经核对的文件钉死；`<待补>` 占位必须在执行前替换为真实文件，否则脚本按设计 exit 2。

## 本轮活动起止（可证明）

- 起：2026-11-20 20:32:34 +0800（date 命令，本会话第一条工具输出）
- 止：见 `ACTIVITY_LOG_20261120.md`（写毕后 stat 取证）
- 工时：见同上；等待与不可证明时段一律不计、不估。
