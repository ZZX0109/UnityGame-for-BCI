"""Shared evidence helpers and the Claw-only Windows delivery entry point.

Python standard library only. Requires the exact installed, licensed Editor and
Windows Build Support. Example (PowerShell):
python Tools/run_delivery.py --unity 'D:/Unity/2022.3.62f3c1/Editor/Unity.exe'

The Editor menu can build without tests; this command requires passing XMLs
before invoking the build. GUI acceptance is always recorded as not_run.
"""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET



def source_fingerprint(root):
    files = sorted((p for folder in ("Assets", "Packages", "ProjectSettings")
                    for p in (root / folder).rglob("*") if p.is_file()),
                   key=lambda p: p.relative_to(root).as_posix())
    content = "".join(p.relative_to(root).as_posix() + "\t" + hashlib.sha256(p.read_bytes()).hexdigest() + "\n"
                      for p in files)
    return hashlib.sha256(content.encode("utf-8")).hexdigest()


def read_test_result(path):
    result = ET.parse(path).getroot()
    if result.tag != "test-run":
        raise ValueError(f"{path.name}: expected NUnit test-run XML")
    total = int(result.get("total", "0"))
    passed = int(result.get("passed", "0"))
    failed = int(result.get("failed", "0"))
    skipped = int(result.get("skipped", "0"))
    inconclusive = int(result.get("inconclusive", "0"))
    if (result.get("result", "").lower() != "passed" or total <= 0 or passed != total
            or failed != 0 or skipped != 0 or inconclusive != 0):
        raise ValueError(f"{path.name}: all tests must pass (total={total}, passed={passed}, "
                         f"failed={failed}, skipped={skipped}, inconclusive={inconclusive})")
    return {"total": total, "passed": passed, "failed": failed,
            "skipped": skipped, "inconclusive": inconclusive}


def main(argv=None):
    """Compatibility entry point: test and build the Claw project only."""
    from run_game_delivery import main as run_claw_delivery
    return run_claw_delivery(["--game", "claw"] + (list(argv) if argv is not None else sys.argv[1:]))


if __name__ == "__main__":
    raise SystemExit(main())
