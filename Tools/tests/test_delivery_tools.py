"""Regression checks for missing assets, broken GUIDs and false-positive test evidence."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_delivery import read_test_result, source_fingerprint
from validate_levels import parse, solve
from validate_project import IMPORTERS, check_project


def guid(path):
    return hashlib.md5(path.encode("utf-8")).hexdigest()


def fixture(root):
    files = {
        "Packages/manifest.json": '{"dependencies": {}}',
        "Packages/packages-lock.json": '{"dependencies": {}}',
        "ProjectSettings/ProjectVersion.txt": "m_EditorVersion: 2022.3.62f3c1\n",
        "Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity": "%YAML 1.1\n",
        "Assets/FirstPersonSokoban/Resources/Levels/Level01.txt": "#####\n#PBG#\n#####",
        "Assets/FirstPersonSokoban/Resources/LevelCatalog.json": json.dumps({"levels": [
            {"resource": "Level01", "id": "first", "title": "First"}]}),
        "Assets/FirstPersonSokoban/Scripts/Runtime.asmdef": '{"name":"Runtime"}',
    }
    for name in ("Floor", "Wall", "Box", "Goal", "Player"):
        files[f"Assets/FirstPersonSokoban/Resources/Prefabs/{name}.prefab"] = "%YAML 1.1\n"
    scene = "Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity"
    files["ProjectSettings/EditorBuildSettings.asset"] = (
        f"EditorBuildSettings:\n  m_Scenes:\n  - enabled: 1\n    path: {scene}\n    guid: {guid(scene)}\n")
    for name, content in files.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    for path in sorted((root / "Assets").rglob("*")):
        importer = "DefaultImporter" if path.is_dir() else IMPORTERS[path.suffix]
        folder = "folderAsset: yes\n" if path.is_dir() else ""
        Path(str(path) + ".meta").write_text(
            f"fileFormatVersion: 2\nguid: {guid(path.relative_to(root).as_posix())}\n{folder}{importer}:\n  externalObjects: {{}}\n",
            encoding="utf-8")


class ProjectChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        fixture(self.root)
        self.prefab = self.root / "Assets/FirstPersonSokoban/Resources/Prefabs/Box.prefab"

    def tearDown(self):
        self.temp.cleanup()

    def test_complete_fixture_passes_without_writing_files(self):
        before = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        self.assertEqual([], check_project(self.root))
        after = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}
        self.assertEqual(before, after)

    def test_missing_prefab_fails(self):
        self.prefab.unlink()
        self.assertTrue(any("Missing required file" in e for e in check_project(self.root)))

    def test_missing_meta_fails(self):
        Path(str(self.prefab) + ".meta").unlink()
        self.assertTrue(any("Missing meta" in e for e in check_project(self.root)))

    def test_duplicate_guid_fails(self):
        meta = Path(str(self.prefab) + ".meta")
        other = self.root / "Assets/FirstPersonSokoban/Resources/Prefabs/Floor.prefab.meta"
        meta.write_text(other.read_text(encoding="utf-8"), encoding="utf-8")
        self.assertTrue(any("Duplicate GUID" in e for e in check_project(self.root)))

    def test_unresolved_reference_fails(self):
        self.prefab.write_text("guid: abcdefabcdefabcdefabcdefabcdefab", encoding="utf-8")
        self.assertTrue(any("Unresolved GUID" in e for e in check_project(self.root)))

    def test_wrong_asmdef_importer_fails(self):
        meta = self.root / "Assets/FirstPersonSokoban/Scripts/Runtime.asmdef.meta"
        meta.write_text(meta.read_text(encoding="utf-8").replace("AssemblyDefinitionImporter", "TextScriptImporter"), encoding="utf-8")
        self.assertTrue(any("Wrong importer" in e for e in check_project(self.root)))

    def test_catalog_missing_map_fails(self):
        path = self.root / "Assets/FirstPersonSokoban/Resources/LevelCatalog.json"
        path.write_text(path.read_text(encoding="utf-8").replace("Level01", "Level02"), encoding="utf-8")
        self.assertTrue(any("Missing catalog level" in e for e in check_project(self.root)))

    def test_build_scene_path_guid_mismatch_fails(self):
        path = self.root / "ProjectSettings/EditorBuildSettings.asset"
        text = path.read_text(encoding="utf-8")
        path.write_text(text.replace(guid("Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity"), "0" * 32), encoding="utf-8")
        self.assertTrue(any("GUID/path mismatch" in e for e in check_project(self.root)))

    def test_fingerprint_changes_with_source(self):
        before = source_fingerprint(self.root)
        self.prefab.write_text("changed source", encoding="utf-8")
        self.assertNotEqual(before, source_fingerprint(self.root))


class TestEvidenceChecks(unittest.TestCase):
    def result(self, text):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "results.xml"
            path.write_text(text, encoding="utf-8")
            return read_test_result(path)

    def test_zero_tests_does_not_count_as_passed(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Passed" total="0" passed="0" failed="0"/>')

    def test_failed_xml_rejected_even_when_process_was_successful(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Failed" total="3" passed="2" failed="1"/>')

    def test_passed_xml_reports_actual_count(self):
        self.assertEqual(33, self.result('<test-run result="Passed" total="33" passed="33" failed="0"/>')["passed"])

    def test_skipped_tests_rejected_even_when_run_says_passed(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Passed" total="33" passed="32" failed="0" skipped="1"/>')

    def test_inconclusive_tests_rejected_even_when_run_says_passed(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Passed" total="33" passed="32" failed="0" inconclusive="1"/>')

    def test_pass_count_must_equal_total_even_without_skip_attribute(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Passed" total="33" passed="32" failed="0"/>')

    def test_inconsistent_skip_counter_rejected(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Passed" total="33" passed="33" failed="0" skipped="1"/>')

    def test_inconsistent_inconclusive_counter_rejected(self):
        with self.assertRaises(ValueError):
            self.result('<test-run result="Passed" total="33" passed="33" failed="0" inconclusive="1"/>')

    def test_non_test_xml_rejected(self):
        with self.assertRaises(ValueError):
            self.result('<results result="Passed" total="33" passed="33" failed="0"/>')


class LevelParserChecks(unittest.TestCase):
    def map(self, text, callback):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Level01.txt"
            path.write_text(text, encoding="utf-8")
            return callback(path)

    def test_box_on_goal_and_player_on_goal_are_supported(self):
        result = self.map("#######\n#+B.*.#\n#.....#\n#######", parse)
        self.assertEqual(2, len(result[3]))
        self.assertEqual(2, len(result[4]))

    def test_invalid_symbol_rejected(self):
        with self.assertRaises(ValueError):
            self.map("#####\n#PBG#\n##?##", parse)

    def test_open_boundary_rejected(self):
        with self.assertRaises(ValueError):
            self.map("#####\n#PBG.\n#####", parse)

    def test_real_one_push_solution(self):
        self.assertEqual(1, self.map("#####\n#PBG#\n#####", solve)[0])


if __name__ == "__main__":
    unittest.main()
