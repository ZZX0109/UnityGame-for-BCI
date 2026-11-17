"""Build/check this project's licensed Claw UI font.

The original inherited Sokoban font tool is preserved with the archived source.
"""
from pathlib import Path
import runpy
import sys

if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    runpy.run_path(str(Path(__file__).resolve().parents[1] /
                       "docs/Game02_ClawMachine/build_ui_font.py"), run_name="__main__")
