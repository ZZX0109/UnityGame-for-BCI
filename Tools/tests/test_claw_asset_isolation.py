"""An isolated Claw project must load without any imported Sokoban assets."""
from pathlib import Path
import tempfile
import unittest

from test_build_scene_isolation import add_game_scene, set_build_scenes
from test_delivery_tools import fixture, guid
from validate_project import check_project


CLAW_SCENE = "Assets/ClawMachine/Scenes/ClawMachineScene.unity"


class ClawAssetIsolationChecks(unittest.TestCase):
    def isolated_project(self, root):
        fixture(root)
        add_game_scene(root, CLAW_SCENE)
        # Model a reversible archive outside Assets, not a deleted source tree.
        legacy = root / "Assets/FirstPersonSokoban"
        legacy.rename(root / "ArchivedSokoban")
        Path(str(legacy) + ".meta").rename(root / "ArchivedSokoban.meta")
        set_build_scenes(root, [(CLAW_SCENE, 1)])

    def test_claw_requires_no_sokoban_catalog_prefabs_or_scene(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.isolated_project(root)
            self.assertEqual([], check_project(root))
            self.assertTrue((root / "ArchivedSokoban/Scenes/PuzzleScene.unity").is_file())
            self.assertFalse((root / "Assets/FirstPersonSokoban").exists())

    def test_archived_assets_cannot_satisfy_a_live_scene_reference(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.isolated_project(root)
            scene = root / CLAW_SCENE
            scene.write_text("guid: " + guid("Assets/FirstPersonSokoban/Resources/Prefabs/Box.prefab"),
                             encoding="utf-8", newline="\n")
            self.assertTrue(any("Unresolved GUID" in error for error in check_project(root)))

    def test_missing_claw_scene_blocks_the_isolated_build(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.isolated_project(root)
            (root / CLAW_SCENE).rename(root / "missing-scene.unity")
            self.assertTrue(any("Missing required file: " + CLAW_SCENE == error
                                for error in check_project(root)))


if __name__ == "__main__":
    unittest.main()
