# 导入模型使用指南（CC0 写实街景资产）

## 来源与授权

全部模型来自 [Poly Haven Hidden Alley 合集](https://polyhaven.com/collections/hidden_alley)，**CC0** 授权——可安全提交到公开 GitHub、可用于商业游戏成品。

## 为什么用 FBX 而不是 glTF（重要）

**Unity 2022.3 没有内置 glTF 导入器**，本项目 `Packages/manifest.json` 也未安装 glTFast/UnityGLTF。`.gltf` 文件会被 Unity 当作未知资产（DefaultAsset），`Resources.Load<GameObject>` 恒返回 null，模型**永远静默回退**。因此本轮改为 Poly Haven 官方 **FBX** 导出（Unity 原生支持）。

FBX 的 Unity 默认导入设置对本项目已经是安全的：`addColliders=0`、`importCameras=0`、`importLights=0`（场景碰撞与光照由代码统一管理）。

## 已就绪资产（32 个文件，约 15 MB）

### 模型 `Resources/StreetCrossing/Imported/Models/`（8 × FBX，1.33 MB）

| 文件 | 用途 | 替换目标 |
|---|---|---|
| `metal_trash_can.fbx` | 锈蚀金属垃圾桶 | `Litter bin` + lid + opening |
| `water_manhole_cover.fbx` | 铸铁井盖（WATER 浮雕） | 程序化井盖 |
| `utility_box_01.fbx` / `utility_box_02.fbx` | 配电箱 | 新增路缘设施 |
| `rollershutter_door.fbx` | 卷帘门 | 新增商铺立面 |
| `rollershutter_window_01.fbx` | 卷帘窗 | 新增商铺立面 |
| `old_tyre.fbx` | 旧轮胎 | 路边装饰 |
| `security_light.fbx` | 工业安防灯 | 新增建筑墙面灯 |

### 贴图 `Resources/StreetCrossing/Imported/Textures/`（24 × 1K JPG，13.8 MB）

每个模型三张：`<name>_diff.jpg`（albedo）、`<name>_nor.jpg`（法线）、`<name>_rough.jpg`（粗糙度）。

## 运行时如何工作（无需任何手工步骤即可生效）

`StreetAssetLibrary.SpawnImported(parent, assetName, localPosition, targetHeight)`：

1. `Resources.Load<GameObject>` 加载 FBX，失败则 `Debug.LogWarning` 并返回 `null` → 调用方走程序化回退
2. 实例化到父节点，**高度归一化**到 `targetHeight`（等比缩放）
3. **从 Resources 贴图在运行时构建 Standard PBR 材质**（albedo + normal + 固定中等光泽），每个资产共享一份材质（利于合批与释放）
4. **落座**：把模型底面精确放在 `localPosition.y`，水平居中到 `localPosition`
5. 由调用方 `AddVisualLod` 加入视距剔除

已接入 3 处：垃圾桶、消防栓、路灯。当前若模型缺失，会静默回退到上一轮的程序化实现——**不会中断场景构建或测试**。

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

## 仍待补下载（代码已就位）

| 模型 | 面数 | 说明 |
|---|---|---|
| `fire_hydrant` | 86,316 | 高面数，建议 Unity 内做 LOD 或有条件启用 |
| `street_lamp_01` | 30,610 | 同上 |
| `modular_fire_escape` | 11,322 | 建筑侧面立体层次 |

下载：从 [Poly Haven 模型页](https://polyhaven.com/models) 取 **FBX 1k** 放 `Models/`，三张 1k JPG 放 `Textures/`（命名 `<name>_diff.jpg` / `_nor.jpg` / `_rough.jpg`），再跑一次上面的菜单项即可，无需改代码。

## 已放弃的方案（记录以免重走）

- **glTF/GLB**：Unity 2022.3 无导入器，需新增 glTFast 包依赖（有项目风险），已改用 FBX。
- **手写 .meta 文件指定 GUID**：模型/贴图全部按 Resources 路径引用，不需要 GUID，手写 meta 反而有格式风险。Unity 首次导入自动生成。
- **手写 .mat 资产**：需手写 Unity YAML 且要配合贴图 GUID，易错；改为运行时用 `Shader.Find("Standard")` 构建，零 YAML 风险。
