"""Re-export the clean rest blend through the production exporter; no rebuild."""
import importlib.util
import json
from pathlib import Path
import bpy

here = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("build_hands", here / "build_hands.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
bpy.ops.wm.open_mainfile(filepath=str(here / "Hands" / "hands_source.blend"))
results = [module.export_wrist_local_fbx(side) for side in ("R", "L")]
out = here.parent / "Results" / "wrist-export-integration.json"
out.write_text(json.dumps({"scope": "Blender export transforms only; Unity verification remains required", "exports": results}, indent=2) + "\n", encoding="utf-8", newline="\n")
