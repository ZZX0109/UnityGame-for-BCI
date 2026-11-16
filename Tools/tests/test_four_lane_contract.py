"""The four-lane approval is bounded; it does not waive seed/dynamics/physics checks."""
import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "street_visual_contract", ROOT / "docs/Game03_StreetCrossing/tools/verify_visual_contract.py")
contract = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(contract)


def scenario(sid, approved):
    values = contract.APPROVED_SCENARIOS[sid]
    # Independent old/new fixtures: no Git checkout, asset generation, or
    # comparison of current source against itself is used to make these pass.
    fields = {
        "scenarioId": sid, "seed": {"S01": 3101, "S02": 3202, "S03": 3303}[sid],
        "minimumSpeed": 6, "maximumSpeed": 7, "queueGap": 2.5,
        "laneOffset": 1.65, "rightHandTraffic": 1,
        "roadHalfWidth": 6.6 if approved else 3.3,
        "maximumVehicles": values["maximumVehicles"] if approved else {"S01": 2, "S02": 10, "S03": 8}[sid],
        "initialVehiclesPerStraightLane": values["initialVehiclesPerStraightLane"],
        "walkingSpeed": 2.5 if approved else 1.35,
        "comfortableSpeed": 2.0 if approved else 1.05,
        "fastWalkingSpeed": 3.4 if approved else 1.85,
        "playerSpawn": "{x: 0, y: 0.02, z: -11.3}" if approved else "{x: 0, y: 0.02, z: -8}",
        "destination": "{x: 0, y: 0, z: 10.8}" if approved else "{x: 0, y: 0, z: 7.5}",
        "title": sid + " crossing", "description": "Observe both directions before entering.",
    }
    return "MonoBehaviour:\n" + "".join("  " + name + ": " + str(value) + "\n" for name, value in fields.items())


def phases(sid, approved):
    values = contract.APPROVED_SCENARIOS[sid]
    durations = [15, 3, 2, values["walk"] if approved else 12, values["flash"] if approved else 5, 2]
    text = "MonoBehaviour:\n  phases:\n"
    for phase, duration in enumerate(durations):
        text += f"  - phase: {phase}\n    duration: {duration}\n    vehicleEntry: {int(phase == 0)}\n    pedestrianEntry: {int(phase == 3)}\n"
    return text + "  clearanceWatchdogSeconds: 30\n"


class FourLaneContractChecks(unittest.TestCase):
    def verify_fixture(self, sid="S01", current=None, phase=None):
        contract.check_four_lane_scenario(scenario(sid, False), current or scenario(sid, True),
                                          phases(sid, False), phase or phases(sid, True), sid)

    def test_three_independently_specified_four_lane_revisions_pass(self):
        for sid in ("S01", "S02", "S03"):
            with self.subTest(sid=sid):
                self.verify_fixture(sid)

    def test_seed_or_vehicle_dynamics_cannot_change_under_layout_approval(self):
        for old, new in (("seed: 3101", "seed: 9999"), ("maximumSpeed: 7", "maximumSpeed: 9"), ("queueGap: 2.5", "queueGap: 1.0")):
            with self.subTest(field=old), self.assertRaises(ValueError):
                self.verify_fixture(current=scenario("S01", True).replace(old, new))

    def test_approved_fields_still_require_the_exact_accepted_values(self):
        for old, new in (("roadHalfWidth: 6.6", "roadHalfWidth: 3.3"), ("maximumVehicles: 4", "maximumVehicles: 2"),
                         ("walkingSpeed: 2.5", "walkingSpeed: 1.35"), ("z: -11.3", "z: -8"), ("z: 10.8", "z: 7.5")):
            with self.subTest(field=old), self.assertRaises(ValueError):
                self.verify_fixture(current=scenario("S01", True).replace(old, new))

    def test_phase_flags_and_other_phase_durations_remain_fixed(self):
        for old, new in (("duration: 15", "duration: 16"), ("phase: 3\n    duration: 16\n    vehicleEntry: 0", "phase: 3\n    duration: 16\n    vehicleEntry: 1"),
                         ("phase: 4\n    duration: 7", "phase: 4\n    duration: 1")):
            with self.subTest(field=old), self.assertRaises(ValueError):
                self.verify_fixture(phase=phases("S01", True).replace(old, new))

    def test_current_lane_source_and_signal_source_match_the_separate_contract(self):
        contract.check_four_lane_sources((ROOT / contract.LAYOUT).read_text(encoding="utf-8"),
                                         (ROOT / contract.SCENARIO_SOURCE).read_text(encoding="utf-8"))
        contract.check_four_lane_signals((ROOT / contract.SIGNALS).read_text(encoding="utf-8"))

    def test_two_heads_or_poles_in_every_lane_are_rejected(self):
        source = (ROOT / contract.SIGNALS).read_text(encoding="utf-8")
        for old, new in (("lane < scenario.StraightLaneCount", "lane < 2"),
                         ("lane == 0 || lane == scenario.StraightLaneCount - 1", "lane >= 0"),
                         ("new Vector3(lateral, 3.25f, 0)", "new Vector3(lateral, 1.25f, 0)")):
            with self.subTest(source=old), self.assertRaises(ValueError):
                contract.check_four_lane_signals(source.replace(old, new))


if __name__ == "__main__":
    unittest.main()
