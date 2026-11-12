"""Read-only compatibility alias. Never recreate existing asset GUIDs from names.

Restore tracked .meta files from version control. Import new assets in Unity.
"""
from validate_project import main


if __name__ == "__main__":
    raise SystemExit(main())
