# GLM · 开源全身人物外观适配（2026-11-20）

负责人：实际 ZCode glm-5.3-flash，最高推理。用户要求每批协作包含 GLM。

工作区 C:/Users/Ait/Desktop/Neuralife/Unity/Game04-VirtualHand。读取 docs/FIRST_PERSON_VR_PLAN.md 及本文件。仅写 ArtSource/Avatar/GLMResourceStyling/。现有正式游戏、Assets、下载缓存、其他 ArtSource 子目录和主计划由 Codex 或其他 Luna 负责，禁止改写或清理。不要打开 Unity 以免占用测试工程。

目标：使用 MPFB2/MakeHuman 的现成人体、自然皮肤、短发、眉毛和日常服装，制作具体可执行的外观配置，替代当前硬壳头发和单色皮肤；不能自造球形躯干或硬壳头发作为最终模型。

已有资源：ArtSource/_download/avatar-resources/mpfb2_v2.0.18/mpfb2-2.0.18/ 为已下载官方源码，API 示例在 script_samples；源码 GPLv3、随附图形资产 CC0。系统资产和 skins02 包由 Codex 下载中，请只读检查 manifest.json 和解压目录。若未完成，可参考 https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html 及 skins02.html 的官方预览并先完成配置，不重复下载相同包。下载完成后补充实际路径。当前测试图在 Results/vr-check-20261120-1546/elbows-down/。

当前 Luna AG 正写 ArtSource/Avatar/build_mpfb_avatar.py，负责完整模型与服装导出；不要抢写或重做该导出器。Luna AH 做 Unity 网格分区，AI 做蓝色材质交付，也不要接管。

请交付：

1. appearance_presets.json：至少两套通用成人的真实比例、自然肤色、短发/眉毛/眼睛、朴素便服组合。每项列出真实资产名、相对路径（存在后填写）、原作者与许可、粗糙度和色泽建议，清楚区分已解析路径与待下载。不把默认外观当作患者测量。
2. style_preview.py：接收 AG 生成的 .blend 路径及外观配置，采用已有材质/纹理进行非破坏的副本调整、自然工作室光照和头部近景/上半身图输出；只写本目录。缺少材质或资产时明确失败，不能用程序生成假素材替代，不修改原始 .blend。
3. HANDOFF.md：实际完成内容、配置对应资源、渲染图片（若可运行）、哪些尚未验证。面部为通用成人，不采集真实患者人脸。优先短小、可用的实现，不做冗长审计。

无需等待确认，直接在上述独立目录开发；最终集成与 Unity/PICO 验收由 Codex 完成。
