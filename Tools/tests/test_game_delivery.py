"""Guard against false-positive Unity evidence. XML fixtures are temporary test data."""
import json
from contextlib import redirect_stderr, redirect_stdout
import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import run_game_delivery as delivery


def xml(namespace="ClawMachine.Tests.PlayMode", categories=("Acceptance_C01", "Acceptance_C02", "Acceptance_C03")):
    cases = "".join('<test-case fullname="' + namespace + '.Case' + str(i) + '" result="Passed">'
                    '<properties><property name="category" value="' + category + '"/></properties></test-case>'
                    for i, category in enumerate(categories))
    return '<test-run result="Passed" total="' + str(len(categories)) + '" passed="' + str(len(categories)) + '" failed="0" skipped="0"><test-suite>' + cases + '</test-suite></test-run>'


class GameEvidenceChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.xml = self.root / "fixture.xml"

    def tearDown(self):
        self.temp.cleanup()

    def write(self, text):
        self.xml.write_text(text, encoding="utf-8")
        return self.xml

    def test_all_actual_scoped_scenario_cases_required(self):
        result = delivery.read_scoped_tests(self.write(xml()), "ClawMachine.Tests", delivery.GAMES["claw"]["categories"])
        self.assertEqual(3, result["passed"])
        self.assertEqual(3, len(result["cases"]))

    def test_optional_capture_cases_cannot_count_as_delivery_evidence(self):
        with self.assertRaisesRegex(ValueError, "Optional VisualCapture"):
            delivery.read_scoped_tests(self.write(xml(categories=("VisualCapture",))), "ClawMachine.Tests")

    def test_passed_root_without_actual_cases_is_rejected(self):
        path = self.write('<test-run result="Passed" total="3" passed="3"/>')
        with self.assertRaisesRegex(ValueError, "case count"):
            delivery.read_scoped_tests(path, "ClawMachine.Tests")

    def test_other_game_cannot_satisfy_scope(self):
        with self.assertRaisesRegex(ValueError, "unscoped"):
            delivery.read_scoped_tests(self.write(xml("StreetCrossing.Tests.PlayMode")), "ClawMachine.Tests")

    def test_only_c01_cannot_satisfy_three_challenge_acceptance(self):
        with self.assertRaisesRegex(ValueError, "Acceptance_C02"):
            delivery.read_scoped_tests(self.write(xml(categories=("Acceptance_C01",))), "ClawMachine.Tests", delivery.GAMES["claw"]["categories"])

    def test_inherited_suite_category_is_accepted(self):
        text = '<test-run result="Passed" total="1" passed="1"><test-suite><properties><property name="Category" value="Acceptance_C01"/></properties><test-case fullname="ClawMachine.Tests.PlayMode.Success" result="Passed"/></test-suite></test-run>'
        result = delivery.read_scoped_tests(self.write(text), "ClawMachine.Tests", ["Acceptance_C01"])
        self.assertIn("Acceptance_C01", result["categories"])

    def test_case_failure_cannot_hide_behind_passed_summary(self):
        text = xml().replace('result="Passed"><properties>', 'result="Failed"><properties>', 1)
        with self.assertRaisesRegex(ValueError, "non-passing"):
            delivery.read_scoped_tests(self.write(text), "ClawMachine.Tests")

    def test_editor_version_must_be_actual_not_only_requested(self):
        log = self.root / "editor.log"
        log.write_text("Initialize engine version: 2022.3.62f3c1 (revision)\n")
        delivery.verify_editor(self.write(xml()), log, "2022.3.62f3c1")
        log.write_text("Initialize engine version: 2022.3.62f3 (revision)\n")
        with self.assertRaisesRegex(ValueError, "Exact Editor"):
            delivery.verify_editor(self.xml, log, "2022.3.62f3c1")

    def test_wrong_xml_editor_overrides_plausible_log(self):
        text = xml().replace("<test-suite>", '<environment unity-version="2022.3.62f3"/><test-suite>', 1)
        log = self.root / "editor.log"
        log.write_text("Initialize engine version: 2022.3.62f3c1 (revision)\n")
        with self.assertRaisesRegex(ValueError, "differs"):
            delivery.verify_editor(self.write(text), log, "2022.3.62f3c1")

    def test_exe_alone_is_not_a_windows_delivery(self):
        (self.root / "ClawMachine.exe").write_bytes(b"fixture")
        with self.assertRaisesRegex(ValueError, "incomplete"):
            delivery.require_windows_payload(self.root, "ClawMachine")

    def test_empty_runtime_files_cannot_pass_payload_gate(self):
        (self.root / "ClawMachine.exe").touch()
        (self.root / "UnityPlayer.dll").touch()
        (self.root / "ClawMachine_Data").mkdir()
        with self.assertRaisesRegex(ValueError, "nonempty"):
            delivery.require_windows_payload(self.root, "ClawMachine")

    def test_missing_editor_records_blocked_and_creates_no_unity_evidence(self):
        (self.root / "ProjectSettings").mkdir()
        (self.root / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f3c1\n")
        game = delivery.GAMES["claw"]
        for path in (game["scene"], game["static"]):
            file = self.root / path
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text("" if file.suffix == ".unity" else "print('static fixture only')\n")
        output = self.root / "BuildOutput/test"
        with patch.object(delivery, "ROOT", self.root), patch.object(delivery, "check_project", return_value=[]), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            code = delivery.main(["--game", "claw", "--unity", str(self.root / "missing-editor"), "--commit", "fixture-only", "--output", str(output)])
        self.assertEqual(1, code)
        evidence = output / "Evidence"
        result = json.loads((evidence / "game-validation-summary.json").read_text())
        self.assertEqual("blocked", result["status"])
        self.assertEqual("not_run", result["build"])
        self.assertEqual(64, len(result["sourceBeforeImport"]))
        self.assertEqual([], list(output.rglob("*.xml")))
        self.assertEqual([], list(output.rglob("*.zip")))


if __name__ == "__main__":
    unittest.main()
