"""为 Assets 下缺少 .meta 的资源生成稳定 GUID（幂等，不覆盖已有 .meta）。

用法：python Tools/ensure_meta.py
"""
import hashlib
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"

CS_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEXT_META = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

DEFAULT_META = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def main():
    existing = set()
    for meta in ASSETS.rglob("*.meta"):
        match = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8"))
        if match:
            existing.add(match.group(1))

    created = []
    for path in sorted(ASSETS.rglob("*")):
        if not path.is_file() or path.suffix == ".meta":
            continue
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            continue
        seed = path.relative_to(ROOT).as_posix()
        digest = hashlib.md5(seed.encode("utf-8")).hexdigest()
        guid = digest
        suffix = 0
        while guid in existing:
            suffix += 1
            guid = hashlib.md5(f"{seed}#{suffix}".encode("utf-8")).hexdigest()
        existing.add(guid)

        if path.suffix == ".cs":
            template = CS_META
        elif path.suffix in (".txt", ".json", ".asmdef", ".md"):
            template = TEXT_META
        else:
            template = DEFAULT_META
        meta.write_text(template.format(guid=guid), encoding="utf-8")
        created.append(meta.relative_to(ROOT).as_posix())

    print(f"新增 {len(created)} 个 .meta，Assets 下共 {len(existing)} 个唯一 GUID")
    for item in created:
        print("  " + item)


if __name__ == "__main__":
    main()
