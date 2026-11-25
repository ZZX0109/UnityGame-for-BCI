# 导入模型使用指南（CC0 写实街景资产）

## 来源与授权

全部模型来自 [Poly Haven Hidden Alley 合集](https://polyhaven.com/collections/hidden_alley)，**CC0** 授权——可安全提交到公开 GitHub、可用于商业游戏成品。

## 为什么用 FBX 而不是 glTF（重要）

**Unity 2022.3 没有内置 glTF 导入器**，本项目 `Packages/manifest.json` 也未安装 glTFast/UnityGLTF。`.gltf` 文件会被 Unity 当作未知资产（DefaultAsset），`Resources.Load<GameObject>` 恒返回 null，模型**永远静默回退**。因此本轮改为 Poly Haven 官方 **FBX** 导出（Unity 原生支持）。

FBX 的 Unity 默认导入设置对本项目已经是安全的：`addColliders=0`、`importCameras=0`、`importLights=0`（场景碰撞与光照由代码统一管理）。

## 已就绪资产（54 个文件，约 27.7 MB；另有 Unity 自动生成的 .meta 伴生文件不计）

### 模型 `Resources/StreetCrossing/Imported/Models/`（12 × FBX，约 4.3 MB）

| 文件 | 用途 | 替换目标 |
|---|---|---|
| `metal_trash_can.fbx` | 锈蚀金属垃圾桶 | `Litter bin` + lid + opening |
| `water_manhole_cover.fbx` | 铸铁井盖（WATER 浮雕） | 程序化井盖 |
| `utility_box_01.fbx` / `utility_box_02.fbx` | 配电箱 | 新增路缘设施 |
| `rollershutter_door.fbx` | 卷帘门 | 新增商铺立面 |
| `rollershutter_window_01.fbx` | 卷帘窗 | 新增商铺立面 |
| `old_tyre.fbx` | 旧轮胎 | 路边装饰 |
| `security_light.fbx` | 工业安防灯 | 新增建筑墙面灯 |
| `fire_hydrant.fbx` | 消防栓（clean-paint 变体；导入前已在 Blender 剔除同位重叠的 `*_aged` 变体，防 z-fighting） | 程序化消防栓 |
| `street_lamp_01.fbx` | 铸铁路灯（高 3.87 m，匹配代码目标高 3.9 m） | 程序化路灯 |
| `modular_fire_escape.fbx` | 模块化防火梯（7 个网格对象，2 个材质槽） | 预留建筑侧面立体层次 |
| `exterior_aircon_unit.fbx` | 外墙空调外机（2 个材质槽；主体+锈蚀格栅双材质） | 预留住宅/公寓上层立面挂点 |

### 贴图 `Resources/StreetCrossing/Imported/Textures/`（42 × 1K JPG，23.4 MB）

每个模型三张：`<name>_diff.jpg`（albedo）、`<name>_nor.jpg`（法线）、`<name>_rough.jpg`（粗糙度）。多槽位模型另有槽位前缀三联组：`<name>_01_*` / `<name>_02_*`（`modular_fire_escape`、`exterior_aircon_unit`）；`<name>_*` 基础三联即槽位 `_01` 的重命名副本。

## 运行时如何工作（无需任何手工步骤即可生效）

`StreetAssetLibrary.SpawnImported(parent, assetName, localPosition, targetHeight)`：

1. `Resources.Load<GameObject>` 加载 FBX，失败则 `Debug.LogWarning` 并返回 `null` → 调用方走程序化回退
2. 实例化到父节点，**高度归一化**到 `targetHeight`（等比缩放）
3. **从 Resources 贴图在运行时构建 Standard PBR 材质**（albedo + normal + 固定中等光泽），每个资产共享一份材质（利于合批与释放）
4. **落座**：把模型底面精确放在 `localPosition.y`，水平居中到 `localPosition`
5. 由调用方 `AddVisualLod` 加入视距剔除

已接入 12 处（12 个资产）：垃圾桶、消防栓、路灯、井盖、旧轮胎、配电箱×2、安防灯、卷帘窗（住宅立面）、卷帘门（Convenience 服务口）、空调外机（住宅立面上层）、防火梯（Apartment 立面）。当前若模型缺失，会静默回退到上一轮的程序化实现——**不会中断场景构建或测试**。

## 已知限制（运行时材质构建）

`StreetAssetLibrary.ApplyImportedMaterial` 从 Resources 贴图运行时构建 Standard PBR 材质，按资产+槽位缓存：

- 多槽位模型（`street_lamp_01` 原生 3 槽、`modular_fire_escape`/`exterior_aircon_unit` 原生 2 槽）**保留原槽数**：每槽按 FBX 原材质名后缀（`_01`/`_02`）解析槽位号，加载 `_{slot:00}_diff/_nor/_rough` 槽位前缀贴图三联组；该槽无专属贴图时回退基础材质，连基础贴图都没有的资产（如 aircon）保留 FBX 自带材质
- `street_lamp_01` 仅 `_01` 槽贴图：玻璃/灯泡槽暂回退本体材质（透明度仍缺失，待补 `_02`/`_03` 槽贴图）
- 灰度 rough 贴图无法拆出金属度通道，`_Metallic` 用固定值（有 rough 图 0.08，无则 0.12）
- 单槽资产行为与此前完全一致（一份共享材质覆盖）

## 一次性 Unity Editor 步骤

打开项目后执行菜单：**Street Crossing → Prepare imported CC0 street props**

它只做运行时代码无法完成的两件事：

- 把 `*_nor.jpg` 的导入类型设为 **NormalMap**（否则法线按 sRGB 采样，光照错误）
- 把所有贴图上限设为 **1024 px** 并启用压缩（PICO 性能）

不做的事：不碰网格、Prefab、场景几何；可重复执行。执行后 Console 会打印实际重配的贴图数量。

## 验证清单

1. **首次导入无错误**：Console 无编译错误（本项目已知的 3 个 CS0617 是外部编译环境伪报，Unity 内不会出现）
2. **模型已加载**：`StreetAssetLibrary.HasImportedAssets == true`
3. **视觉**：垃圾桶/消防栓/路灯为写实 FBX，贴图正常，无品红/纯白
4. **落座**：模型底部贴合地面，无悬空/陷地
5. **性能**：PICO 目标 ≥72 FPS；如不足，优先降低 `security_light`/`metal_trash_can` 的贴图尺寸

## 本轮新增资产（2026-11-24 下载完成）

上表 3 个待补模型已全部落地并通过校验（HTTP 200 + md5 + 字节数对 `files/{asset}` 清单逐一核对，`TOTAL_FAIL=0`）：

| 模型 | 作者（Poly Haven） | 面数 | 处理记录 |
|---|---|---|---|
| `fire_hydrant` | Gonçalo Felício | 86,316 | FBX 内含 clean/aged 两套同位几何体；已在 Blender 剔除 aged 套件后回导出（Y-up，高 0.80 m），顶点 43282→21641 |
| `street_lamp_01` | Josh Dean | 30,610 | 原样使用；实测尺寸 0.70×0.39×3.87 m，与代码目标高 3.9 m 吻合 |
| `modular_fire_escape` | Juniix | 11,322 | 原样使用；7 个网格对象、2 材质槽（`_01`/`_02`），已补 `_02` 槽贴图三联组，双槽各自成材质 |
| `exterior_aircon_unit` | Poly Haven | — | 原样使用；原始 FBX 为直接 Kaydara 二进制（非 zip），md5 `2c65dec41975985948e3e4ed8a9f3418`；2 材质槽（`_01`/`_02`），双槽贴图三联组已齐；上游另有 `_rusted_01/_rusted_02` 变体贴图未下载（运行时材质会覆盖，无影响） |

全部来自 [Hidden Alley 合集](https://polyhaven.com/collections/hidden_alley)，CC0。下载源清单与校验脚本产物在仓库外 `BuildOutput/polyhaven-dl/` 留档。后续扩充新资产时，代码侧 `GetImportedModel`/`SetImportedModel` 需各加一个 case 才能生效（`exterior_aircon_unit` 已于 2026-11-25 接入住宅立面上层挂点，运行时以 `DisableRustedVariant` 关闭 FBX 内置锈化孪生子树；`modular_fire_escape` 同日接入 Apartment 立面，归一化后按 6.4 m 高度整装落地）。

## 已放弃的方案（记录以免重走）

- **glTF/GLB**：Unity 2022.3 无导入器，需新增 glTFast 包依赖（有项目风险），已改用 FBX。
- **手写 .meta 文件指定 GUID**：模型/贴图全部按 Resources 路径引用，不需要 GUID，手写 meta 反而有格式风险。Unity 首次导入自动生成。
- **手写 .mat 资产**：需手写 Unity YAML 且要配合贴图 GUID，易错；改为运行时用 `Shader.Find("Standard")` 构建，零 YAML 风险。
