"""Independent build entries must not silently include the retained old game."""
from pathlib import Path
import tempfile
import unittest

from test_delivery_tools import fixture, guid
from validate_project import GAME_ENTRY_SCENES, check_project


LEGACY_SCENE = "Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity"


def add_game_scene(root, scene):
    path = root / scene
    path.parent.mkdir(parents=True)
    path.write_text("%YAML 1.1\n", encoding="utf-8", newline="\n")
    for asset in (path.parent.parent, path.parent, path):
        folder = "folderAsset: yes\n" if asset.is_dir() else ""
        Path(str(asset) + ".meta").write_text(
            "fileFormatVersion: 2\nguid: " + guid(asset.relative_to(root).as_posix()) +
            "\n" + folder + "DefaultImporter:\n  externalObjects: {}\n",
            encoding="utf-8", newline="\n")


def set_build_scenes(root, entries):
    text = "EditorBuildSettings:\n  m_Scenes:\n"
    for scene, enabled in entries:
        text += f"  - enabled: {enabled}\n    path: {scene}\n    guid: {guid(scene)}\n"
    (root / "ProjectSettings/EditorBuildSettings.asset").write_text(text, encoding="utf-8", newline="\n")


class BuildSceneIsolationChecks(unittest.TestCase):
    def each_game(self, check):
        for scene in GAME_ENTRY_SCENES.values():
            with self.subTest(scene=scene), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture(root)
                add_game_scene(root, scene)
                check(root, scene)

    def test_current_entry_passes_with_legacy_assets_preserved(self):
        def check(root, scene):
            set_build_scenes(root, [(scene, 1)])
            self.assertEqual([], check_project(root))
            self.assertTrue((root / LEGACY_SCENE).is_file())
        self.each_game(check)

    def test_enabling_legacy_scene_alongside_current_entry_is_rejected(self):
        def check(root, scene):
            set_build_scenes(root, [(scene, 1), (LEGACY_SCENE, 1)])
            self.assertTrue(any("must enable only" in error for error in check_project(root)))
        self.each_game(check)

    def test_only_legacy_or_disabled_current_entry_cannot_pass(self):
        def check(root, scene):
            for entries in ([(LEGACY_SCENE, 1)], [(scene, 0)]):
                with self.subTest(entries=entries):
                    set_build_scenes(root, entries)
                    self.assertTrue(any("must enable only" in error for error in check_project(root)))
        self.each_game(check)

    def test_disabled_legacy_entry_is_optional_but_still_guid_checked(self):
        def check(root, scene):
            set_build_scenes(root, [(scene, 1), (LEGACY_SCENE, 0)])
            self.assertEqual([], check_project(root))
            settings = root / "ProjectSettings/EditorBuildSettings.asset"
            settings.write_text(settings.read_text(encoding="utf-8").replace(guid(LEGACY_SCENE), "0" * 32),
                                encoding="utf-8", newline="\n")
            self.assertTrue(any("GUID/path mismatch" in error for error in check_project(root)))
        self.each_game(check)

    def test_retained_legacy_assets_still_receive_integrity_checks(self):
        def check(root, scene):
            set_build_scenes(root, [(scene, 1)])
            box = root / "Assets/FirstPersonSokoban/Resources/Prefabs/Box.prefab"
            Path(str(box) + ".meta").unlink()
            self.assertTrue(any("Missing meta" in error and "FirstPersonSokoban" in error for error in check_project(root)))
        self.each_game(check)

    def test_sokoban_only_project_keeps_its_original_entry_requirement(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixture(root)
            self.assertEqual([], check_project(root))
            set_build_scenes(root, [(LEGACY_SCENE, 0)])
            self.assertTrue(any("PuzzleScene must be enabled" in error for error in check_project(root)))


if __name__ == "__main__":
    unittest.main()
