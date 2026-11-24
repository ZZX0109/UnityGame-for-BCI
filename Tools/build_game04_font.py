# -*- coding: utf-8 -*-
"""Generate the Game04 OFL Chinese UI font subset.

Source: NotoSansCJK-Regular-2.004.ttc, face index 2 = "Noto Sans CJK SC"
(per docs/IMPLEMENTATION_BRIEF.md). Output:
  Assets/VirtualHand/Resources/Fonts/NotoSansCJK_Subset.ttf  (+ .license.txt)

The subset covers every character used in the demo UI strings plus ASCII,
so all Chinese labels render with the proper font instead of LegacyRuntime.
Characters not in the subset fall back at runtime; re-run this script after
adding new UI strings.

License: Noto Sans CJK is SIL Open Font License 1.1; the subset keeps the
same license (see .license.txt). This game is not the Game03 project; the
Game03 font pipeline is only a documented reference.

Usage:
  D:/SoftwareManagement/venv/bci/Scripts/python.exe Tools/build_game04_font.py
"""

import os
import string
import re
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

SRC = "C:/Users/Ait/AppData/Local/Temp/NotoSansCJK-Regular-2.004.ttc"
FACE_INDEX = 2  # Noto Sans CJK SC
OUT_DIR = os.path.join("Assets", "VirtualHand", "Resources", "Fonts")
OUT_TTF = os.path.join(OUT_DIR, "NotoSansCJK_Subset.ttf")
OUT_LIC = os.path.join(OUT_DIR, "NotoSansCJK_Subset.license.txt")

# every literal UI string rendered by DemoSceneBuilder/DemoUI/TaskController
UI_STRINGS = [
    "Game04 虚拟手演示",
    "模式: %s 意图: 静息 左手 右手 双手 来源: EEG预测 真实标签 数据手套 手动",
    "任务: 协作搬运 交接 左→右",
    "DB8 姿态回放 EEG 预测回放 明确标签回放 播放/暂停 重播",
    "rest thumb index cylindrical_grip lateral_grip tripod_grip",
    "已加载 已拒绝 行非法帧 加载失败: 回放目录不存在: 回放文件不存在: ",
    "v1 仅支持本地 JSONL 文件: 回放路径为空",
    "阶段: 接近 抓取 抬起 跟随 传递 放置 完成 闲置",
    "0123456789.:/|()+-→_ ",
    "虚拟手动作回放 协作任务 回放来源 播放控制 拇指屈伸 食指屈伸 圆柱抓握 侧捏 三指捏 姿态",
    "载入 目录 被试 请选择 分别点击按钮 当前 手套 左手镜像 独立显示 空隙 失效 有效 时间",
    "： 暂停 原始 片段 预测 标注 等待 开始 单手 双手 数据 已载入 帧（共载入）",
    string.ascii_letters,
    "，。（）",
]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    text = "".join(UI_STRINGS) + string.printable
    # Collect literal UI text so new controller labels keep their own glyphs.
    scripts = Path("Assets/VirtualHand/Scripts")
    for name in ("Editor/DemoSceneBuilder.cs", "Runtime/DemoUI.cs", "Runtime/ReplayUIBindings.cs",
                 "Runtime/SeatedHandSettingsPresenter.cs", "Runtime/SeatedWristNeutralCapture.cs", "Editor/SeatedHandSettingsPanelBuilder.cs"):
        source = (scripts / name).read_text(encoding="utf-8-sig")
        for label in re.findall(r'"([^"\n]*)"', source):
            text += "".join(c for c in label if ord(c) > 127)
    chars = sorted(set(text) - set("\n\r\t\x0b\x0c"))
    print(f"subset: {len(chars)} unique characters")

    # pyftsubset CLI (programmatic main()) is the robust path for TTC/CFF;
    # it handles face selection, cmap closure and CFF table surgery itself.
    args = [
        SRC,
        f"--font-number={FACE_INDEX}",
        f"--text={ ''.join(chars) }",
        f"--output-file={OUT_TTF}",
        "--name-IDs=*", "--name-legacy=True",
        "--layout-features=*",
        "--notdef-outline",
        "--drop-tables=DSIG",
    ]
    subset.main(args)

    # verify the saved font actually contains our Chinese glyphs
    check = TTFont(OUT_TTF, lazy=True)
    cmap = check.getBestCmap()
    missing = [c for c in chars if ord(c) > 127 and ord(c) not in cmap]
    if missing:
        print("WARNING missing glyphs:", "".join(missing))
    else:
        print("all CJK glyphs present")
    print("glyphs in font:", len(cmap))

    with open(OUT_LIC, "w", encoding="utf-8") as f:
        f.write(
            "Noto Sans CJK SC (subset of NotoSansCJK-Regular-2.004.ttc, face 2)\n"
            "Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved\n"
            "Font Name 'Noto'.\n\nThis Font Software is licensed under the SIL\n"
            "Open Font License, Version 1.1 <https://scripts.sil.org/OFL>.\n"
            "Subset generated for Game04-VirtualHand UI text only.\n")
    print("wrote", OUT_TTF)


if __name__ == "__main__":
    main()
