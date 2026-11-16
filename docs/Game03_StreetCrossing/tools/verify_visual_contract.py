"""Read-only approved four-lane contract against an explicit pre-change Git baseline.

This is a source/configuration gate, not C# compilation or a Unity physics test.
No fetch, reset, checkout, generation or file writes are performed.
The historical two-lane visual-only contract is available with --visual-only.
"""
from __future__ import annotations

import argparse
from decimal import Decimal
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
BASELINE = "2bf5aacb64380015248670811c81158185ae66a6"
GAME = "Assets/StreetCrossing/"
WORLD = GAME + "Scripts/Presentation/StreetWorld.cs"
SIGNALS = GAME + "Scripts/Traffic/TrafficSignalView.cs"
DETAILS = GAME + "Scripts/Presentation/StreetVisualDetails.cs"
LAYOUT = GAME + "Scripts/Core/StreetLayout.cs"
SCENARIO_SOURCE = GAME + "Scripts/Core/StreetScenarioDefinition.cs"
APPROVED_SCENARIOS = {
    "S01": {"maximumVehicles": 4, "initialVehiclesPerStraightLane": 1, "walk": 16, "flash": 7},
    "S02": {"maximumVehicles": 10, "initialVehiclesPerStraightLane": 2, "walk": 18, "flash": 7},
    "S03": {"maximumVehicles": 8, "initialVehiclesPerStraightLane": 1, "walk": 20, "flash": 8},
}
PROFILE_REF = re.compile(
    r"^  visualProfile: \{fileID: 11400000, guid: [0-9a-f]{32}, type: 2\}\n", re.M
)
CS_TOKEN = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.S)
NUMBER = re.compile(r"(?<![\w.])(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[fFdDmM]?(?![\w.])")


def source_code(text: str, hide_strings: bool = False) -> str:
    """Blank comments (and optionally literals) while retaining line/offset layout."""
    def blank(match):
        token = match.group()
        if token.startswith("//") or token.startswith("/*") or hide_strings:
            return re.sub(r"[^\n]", " ", token)
        return token
    return CS_TOKEN.sub(blank, text)


def canonical(expression: str) -> str:
    """Ignore whitespace and equivalent C# decimal spelling, e.g. .5f / 0.5f."""
    expression = source_code(expression)
    expression = NUMBER.sub(lambda m: format(Decimal(m.group().rstrip("fFdDmM")).normalize(), "f"), expression)
    return re.sub(r"\s+", "", expression)


def split_arguments(arguments: str) -> list[str]:
    masked = source_code(arguments, hide_strings=True)
    depth, start, result = 0, 0, []
    for i, char in enumerate(masked):
        if char in "([{":
            depth += 1
        elif char in ")]}":
            depth -= 1
        elif char == "," and depth == 0:
            result.append(arguments[start:i].strip()); start = i + 1
    result.append(arguments[start:].strip())
    return result


def calls(text: str, names: tuple[str, ...]):
    code = source_code(text)
    masked = source_code(text, hide_strings=True)
    for match in re.finditer(r"\b(" + "|".join(map(re.escape, names)) + r")\s*\(", masked):
        depth, end = 1, match.end()
        while end < len(masked) and depth:
            if masked[end] == "(": depth += 1
            elif masked[end] == ")": depth -= 1
            end += 1
        if depth:
            raise ValueError("Unbalanced " + match.group(1) + " call.")
        yield match.group(1), split_arguments(code[match.end():end - 1])


def assignment(text: str, name: str) -> str:
    match = re.search(r"\b" + re.escape(name) + r"\s*=\s*([^;]+);", source_code(text))
    if match is None:
        raise ValueError("Missing observation target assignment: " + name)
    return canonical(match.group(1))


def s03_blocker(text: str) -> str:
    matches = [args for _, args in calls(text, ("Box",))
               if args and args[0] == '"Opaque bus information shelter - changes visibility"']
    if len(matches) != 1:
        raise ValueError("Expected exactly one deliberate S03 opaque shelter.")
    args = matches[0]
    if len(args) > 4 and args[4] != "true":
        raise ValueError("S03 opaque shelter collider was disabled.")
    return canonical(",".join(args))


def signal_envelopes(text: str) -> dict[str, tuple[str, str]]:
    result = {}
    for name, args in calls(text, ("Part", "Body")):
        label_index = 1 if name == "Part" else 2
        collider_index = label_index + 4
        if len(args) <= collider_index or args[collider_index] != "true":
            continue
        label = args[label_index].strip('"')
        if label in result:
            raise ValueError("Duplicate collidable signal component: " + label)
        result[label] = (canonical(args[label_index + 1]), canonical(args[label_index + 2]))
    expected = {"Signal pole", "Pedestrian housing", "Vehicle pole", "Vehicle housing"}
    if set(result) != expected:
        raise ValueError("Collidable signal component set changed: " + repr(sorted(result)))
    code = canonical(text)
    for loop in ("for(intside=0;side<2;side++)", "for(intlane=0;lane<2;lane++)"):
        if loop not in code:
            raise ValueError("Cannot establish the original two-per-side signal construction loop.")
    return result


def check_visual_only(text: str, label: str):
    code = source_code(text, hide_strings=True)
    if re.search(r"\b(?:\w*Collider|Rigidbody|CreatePrimitive)\b", code):
        raise ValueError(label + " contains a physics/collider/primitive API; review its observation impact.")


def visual_factory(text: str) -> str:
    code = source_code(text)
    masked = source_code(text, hide_strings=True)
    start = re.search(r"\bRenderer\s+VisualRounded\s*\([^)]*\)\s*\{", masked)
    if start is None:
        raise ValueError("Missing auditable VisualRounded factory.")
    depth, end = 1, start.end()
    while end < len(masked) and depth:
        if masked[end] == "{": depth += 1
        elif masked[end] == "}": depth -= 1
        end += 1
    if depth:
        raise ValueError("Unbalanced VisualRounded factory.")
    return code[start.end():end - 1]


def git_text(root: Path, commit: str, path: str) -> str:
    run = subprocess.run(["git", "show", commit + ":" + path], cwd=root, capture_output=True, text=True, encoding="utf-8")
    if run.returncode:
        raise ValueError("Baseline file is unavailable locally: " + path)
    return run.stdout


def scenario_number(text: str, name: str) -> Decimal:
    match = re.search(r"^  " + re.escape(name) + r": ([^\n]+)$", text, re.M)
    if not match:
        raise ValueError("Missing scenario field " + name)
    return Decimal(match.group(1))


def without_fields(text: str, names: set[str]) -> str:
    text = PROFILE_REF.sub("", text)
    return re.sub(r"^  (?:" + "|".join(map(re.escape, sorted(names))) + r"): [^\n]*\n", "", text, flags=re.M)


def phase_rows(text: str):
    return [(int(phase), Decimal(duration), int(vehicle), int(pedestrian)) for phase, duration, vehicle, pedestrian in
            re.findall(r"  - phase: (\d+)\n    duration: ([^\n]+)\n    vehicleEntry: ([01])\n    pedestrianEntry: ([01])", text)]


def check_four_lane_scenario(before: str, current: str, before_phase: str, current_phase: str, sid: str):
    allowed = {"roadHalfWidth", "maximumVehicles", "initialVehiclesPerStraightLane", "walkingSpeed", "comfortableSpeed",
               "fastWalkingSpeed", "playerSpawn", "destination", "title", "description"}
    if without_fields(before, allowed) != without_fields(current, allowed):
        raise ValueError(sid + " changed a scenario field outside the approved four-lane scope (seed/traffic dynamics must remain stable).")
    expected = dict(APPROVED_SCENARIOS[sid], roadHalfWidth=6.6, walkingSpeed=2.5, comfortableSpeed=2.0, fastWalkingSpeed=3.4)
    for field, value in expected.items():
        if field in ("walk", "flash"):
            continue
        if scenario_number(current, field) != Decimal(str(value)):
            raise ValueError(sid + " does not match the approved " + field)
    for field, z in (("playerSpawn", "-11.3"), ("destination", "10.8")):
        match = re.search(r"^  " + field + r": \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}$", current, re.M)
        expected_y = Decimal("0.02") if field == "playerSpawn" else Decimal(0)
        if not match or tuple(map(Decimal, match.groups())) != (Decimal(0), expected_y, Decimal(z)):
            raise ValueError(sid + " " + field + " does not match the approved sidewalk offset.")
    for field in ("title", "description"):
        if not re.search(r"^  " + field + r": \S", current, re.M):
            raise ValueError(sid + " has no readable " + field)
    old_rows, rows = phase_rows(before_phase), phase_rows(current_phase)
    if len(rows) != 6 or len(old_rows) != 6 or [row[0] for row in rows] != list(range(6)):
        raise ValueError(sid + " requires all six ordered traffic phases.")
    for old, row in zip(old_rows, rows):
        if row[2:] != old[2:]:
            raise ValueError(sid + " phase admission flags changed.")
        expected_duration = Decimal(expected["walk"] if row[0] == 3 else expected["flash"]) if row[0] in (3, 4) else old[1]
        if row[1] != expected_duration:
            raise ValueError(sid + " phase duration is outside the approved crossing window.")
    def without_walk_durations(text):
        return re.sub(r"(  - phase: [34]\n    duration:) [^\n]+", r"\1 APPROVED", text)
    if without_walk_durations(before_phase) != without_walk_durations(current_phase):
        raise ValueError(sid + " phase configuration changed beyond the approved Walk/Flashing durations.")


def check_four_lane_sources(layout: str, scenario: str):
    code = canonical(layout)
    for name, value in {"StraightLaneCount": "4", "LaneWidth": "3.3", "RoadHalfWidth": "6.6", "SideRoadHalfWidth": "3.3",
                        "SidewalkOffset": "3.3", "SpawnZ": "-11.3", "DestinationZ": "10.8"}.items():
        if name + "=" + value + ";" not in code:
            raise ValueError("StreetLayout does not declare the approved " + name)
    if "return(index*2-(StraightLaneCount-1))*halfLaneWidth;" not in code:
        raise ValueError("Four lane centres must be generated symmetrically from the approved lane width.")
    if "twoLaneZ+Mathf.Sign(twoLaneZ)*SidewalkOffset" not in code:
        raise ValueError("Sidewalk placement is missing its explicit outward offset.")
    code = canonical(scenario)
    for expression in ("StraightLaneCount=>StreetLayout.StraightLaneCount;", "LaneZ(intlane)=>StreetLayout.LaneZ(lane,laneOffset);",
                       "((lane<StraightLaneCount/2)==rightHandTraffic)?1:-1"):
        if expression not in code:
            raise ValueError("Scenario does not expose the approved four-lane coordinates/two-direction mapping.")


def check_four_lane_signals(text: str):
    code = canonical(text)
    for expression in ("newRenderer[StreetLayout.StraightLaneCount,3]", "for(intlane=0;lane<scenario.StraightLaneCount;lane++)",
                       "if(lane==0||lane==scenario.StraightLaneCount-1)", "(roadside-scenario.LaneZ(lane))*direction",
                       "lane<scenario.StraightLaneCount/2?-scenario.roadHalfWidth-0.5:scenario.roadHalfWidth+0.5"):
        if expression not in code:
            raise ValueError("Cannot establish four physical signal heads and two roadside supports.")
    envelopes = {}
    for _, args in calls(text, ("Body",)):
        if len(args) > 6 and args[6] == "true":
            envelopes[args[2].strip('"')] = (canonical(args[3]), canonical(args[4]))
    expected = {
        "Signal pole": ("newVector3(0,1.35,0)", "newVector3(0.08,2.7,0.08)"),
        "Pedestrian housing": ("newVector3(0,2.1,0)", "newVector3(0.8,0.95,0.2)"),
        "Vehicle pole": ("newVector3(0,2,0)", "newVector3(0.1,4,0.1)"),
        "Vehicle housing": ("newVector3(lateral,3.25,0)", "newVector3(0.65,1.9,0.32)"),
    }
    if envelopes != expected:
        raise ValueError("Approved four-lane signal collider envelopes changed.")
    guard = re.search(r"if\(lane==0\|\|lane==scenario\.StraightLaneCount-1\)\{([^}]+)\}", code)
    if not guard or '"Vehiclepole"' not in guard.group(1):
        raise ValueError("Vehicle support poles must be confined to the two roadside lane groups.")


def verify(root: Path, baseline: str, visual_only: bool = False) -> list[str]:
    resolved = subprocess.run(["git", "rev-parse", "--verify", "--end-of-options", baseline + "^{commit}"],
                              cwd=root, capture_output=True, text=True, encoding="utf-8")
    if resolved.returncode:
        raise ValueError("Baseline commit is unavailable locally. Provide an existing explicit commit; this verifier never fetches.")
    commit = resolved.stdout.strip()
    messages = ["Baseline " + commit]
    for sid in ("S01", "S02", "S03"):
        path = GAME + "Scenarios/" + sid + ".asset"
        before, current = git_text(root, commit, path), (root / path).read_text(encoding="utf-8")
        if len(PROFILE_REF.findall(current)) != 1:
            raise ValueError(sid + " must contain exactly one explicit, well-formed visualProfile reference.")
        if visual_only and PROFILE_REF.sub("", before) != PROFILE_REF.sub("", current):
            raise ValueError(sid + " original scenario configuration changed beyond visualProfile.")
        phase = GAME + "Scenarios/" + sid + "PhaseTable.asset"
        old_phase, current_phase = git_text(root, commit, phase), (root / phase).read_text(encoding="utf-8")
        if visual_only and old_phase != current_phase:
            raise ValueError(sid + " phase table changed.")
        if not visual_only:
            check_four_lane_scenario(before, current, old_phase, current_phase, sid)
        messages.append(sid + (": original visual-only baseline unchanged" if visual_only else ": approved four-lane scenario/window; original seed and other traffic dynamics retained"))
    before, current = git_text(root, commit, WORLD), (root / WORLD).read_text(encoding="utf-8")
    if visual_only:
        for target in ("LeftRoadTargets", "RightRoadTargets"):
            if assignment(before, target) != assignment(current, target):
                raise ValueError(target + " expression changed.")
        if s03_blocker(before) != s03_blocker(current):
            raise ValueError("S03 opaque shelter position/size/material/collider arguments changed.")
        messages.append("Road observation targets and opaque shelter retain the visual-only baseline")
    else:
        check_four_lane_sources((root / LAYOUT).read_text(encoding="utf-8"), (root / SCENARIO_SOURCE).read_text(encoding="utf-8"))
        expected_target_z = "s.DirectionForLane(0)>0?s.LaneZ(0):s.LaneZ(s.StraightLaneCount-1)"
        if assignment(current, "leftApproachZ") != expected_target_z:
            raise ValueError("Road observation targets must track the approaching outer lanes, including mirrored driving sides.")
        for target, sign in (("LeftRoadTargets", -1), ("RightRoadTargets", 1)):
            z = "leftApproachZ" if sign == -1 else "-leftApproachZ"
            expected = "new[]{" + ",".join("newVector3(" + str(sign * distance) + ",1.1," + z + ")" for distance in (14, 20, 28)) + "}"
            if assignment(current, target) != expected:
                raise ValueError(target + " changed beyond its approved four-lane target coordinates.")
        shifted_blocker = s03_blocker(before).replace("newVector3(-2.7,1.1,-4.1)", "newVector3(-2.7,1.1,StreetLayout.SidewalkZ(-4.1))")
        if s03_blocker(current) != shifted_blocker:
            raise ValueError("S03 shelter must move outward with the sidewalk while retaining its size/material/collider.")
        messages.append("Four distinct lane centres, wider roadway and explicit outward sidewalk/observation offsets checked")
    check_visual_only((root / DETAILS).read_text(encoding="utf-8"), "StreetVisualDetails")
    check_visual_only(visual_factory(current), "VisualRounded")
    messages.append("New detail builder and its mesh factory contain no collider/primitive APIs")
    before = git_text(root, commit, SIGNALS)
    current = (root / SIGNALS).read_text(encoding="utf-8")
    if visual_only:
        if signal_envelopes(before) != signal_envelopes(current):
            raise ValueError("Signal pole/housing local positions or collider envelope sizes changed.")
    else:
        check_four_lane_signals(current)
    code = canonical(current)
    if code.count("AddComponent<BoxCollider>()") != 1 or "if(collider)renderer.gameObject.AddComponent<BoxCollider>().size=size;" not in code:
        raise ValueError("Signal collider factory no longer has the reviewed size=original-size guard.")
    if "localScale" in source_code(current, hide_strings=True):
        raise ValueError("Signal transform scaling requires a fresh collider envelope review.")
    messages.append("Original visual-only eight signal blockers retained" if visual_only else "Four lane-aligned overhead vehicle heads, two roadside supports and two pedestrian signals checked (10 collider envelopes)")
    return messages


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", default=BASELINE, help="Existing local Git commit/ref; default is the pre-refinement street commit.")
    parser.add_argument("--visual-only", action="store_true", help="Check the historical two-lane visual-only contract instead of the approved four-lane revision")
    args = parser.parse_args(argv)
    try:
        for message in verify(ROOT, args.baseline, args.visual_only):
            print("[OK]", message)
    except (ValueError, OSError) as error:
        print("[FAIL]", error, file=sys.stderr)
        return 1
    print("Unity compilation, runtime colliders/visibility, rendering and playthrough remain UNVERIFIED.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
