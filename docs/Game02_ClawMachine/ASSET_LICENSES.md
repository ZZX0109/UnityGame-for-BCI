# C01 资源来源与许可

| 资源 | 来源与用途 | 许可 / 可分发状态 |
|---|---|---|
| 机柜、导轨、滑车、吊线、三指、控台、出口、礼盒、街机背景 | 本次原创圆角/椭球网格模块、生成器与内置基础形状组成，独立 Prefab 和复合碰撞；无外部模型 | 本项目原创几何布局，可随项目源文件分发；内置网格由 Unity 引擎提供 |
| 2026-11-13 布偶轮廓、狐狸卷尾、刺绣拼布、机柜饰面与店铺陈列 | 本仓库 `ClawMeshModule` 与 `generate_prototype.py` 原创程序网格及布局；无下载模型、照片或贴图 | 随项目源代码分发；保留简化碰撞，不宣称 Blender 精细建模或软体物理 |
| 颜色、玻璃、金属、摩擦及四张微表面纹理 | 本次原创材质数值、64×64 塑料微粒/拉丝/织物底色及织物法线，使用项目现有内置 Standard Shader | 可随项目分发；不包含外购 Shader 或贴图 |
| 十种机械阶段提示音与电机层 | `ClawGameManager` 使用不同频率、谐波与衰减包络在本地生成 | 本次原创合成音；无下载录音或采样 |
| `Assets/ClawMachine/Fonts/ClawUISans.otf` | Noto Sans CJK SC Regular 2.004，原 TTC 简体中文 face index 2，经字符子集和家族名重命名 | SIL Open Font License 1.1，可再分发完整衍生字体；完整版权/OFL 位于同目录 LICENSE.txt |

字体原作者版权、上游地址与来源 SHA256 保存在 `Fonts/LICENSE.txt` 与 `Fonts/GlyphCoverage.json`。衍生名为 **Claw UI Sans**，保留原授权元数据；没有改动 Sokoban 的字体。

仅在维护字体子集时需要 fontTools 4.x 和原版已授权源字体。项目打开、播放、构建以及普通覆盖检查无需安装系统字体或 fontTools。

```bash
python docs/Game02_ClawMachine/build_ui_font.py --check
python docs/Game02_ClawMachine/build_ui_font.py --source /path/to/NotoSansCJK-Regular.ttc
```

共享 TMP 位图 Shader 和 Settings 位于 `Assets/BCIShared/TMP`，由两个游戏共用；Shader 为本轮原创兼容实现，Settings 引用固定 TMP 3.0.6 的包脚本。TMP 包使用 Unity 官方许可，不复制任何付费字型或采样。

纹理可用 `python docs/Game02_ClawMachine/build_visual_assets.py --check` 验证原始字节；网格配方见 `Models/GeometryRecipes.json`，每个实例持有并清理自己的 Mesh。

没有使用 Asset Store 付费资源、网络图片、下载音乐或需要另购的 PBR 资源。实际 Unity 编译/视觉/音频效果未在本环境执行验证。
