"""Read-only comparison of street art changes against an explicit local Git baseline.

This is a source/configuration gate, not C# compilation or a Unity physics test.
No fetch, reset, checkout, generation or file writes are performed.
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
    run = subprocess.run(["git", "show", commit + ":" + path], cwd=root, capture_output=True, text=True)
    if run.returncode:
        raise ValueError("Baseline file is unavailable locally: " + path)
    return run.stdout


def verify(root: Path, baseline: str) -> list[str]:
    resolved = subprocess.run(["git", "rev-parse", "--verify", "--end-of-options", baseline + "^{commit}"],
                              cwd=root, capture_output=True, text=True)
    if resolved.returncode:
        raise ValueError("Baseline commit is unavailable locally. Provide an existing explicit commit; this verifier never fetches.")
    commit = resolved.stdout.strip()
    messages = ["Baseline " + commit]
    for sid in ("S01", "S02", "S03"):
        path = GAME + "Scenarios/" + sid + ".asset"
        before, current = git_text(root, commit, path), (root / path).read_text(encoding="utf-8")
        if len(PROFILE_REF.findall(current)) != 1:
            raise ValueError(sid + " must contain exactly one explicit, well-formed visualProfile reference.")
        if PROFILE_REF.sub("", before) != PROFILE_REF.sub("", current):
            raise ValueError(sid + " original scenario configuration changed beyond visualProfile.")
        phase = GAME + "Scenarios/" + sid + "PhaseTable.asset"
        if git_text(root, commit, phase) != (root / phase).read_text(encoding="utf-8"):
            raise ValueError(sid + " phase table changed.")
        messages.append(sid + ": original scenario configuration and phase table unchanged")
    before, current = git_text(root, commit, WORLD), (root / WORLD).read_text(encoding="utf-8")
    for target in ("LeftRoadTargets", "RightRoadTargets"):
        if assignment(before, target) != assignment(current, target):
            raise ValueError(target + " expression changed.")
    if s03_blocker(before) != s03_blocker(current):
        raise ValueError("S03 opaque shelter position/size/material/collider arguments changed.")
    messages.append("Road observation target expressions and original S03 opaque shelter unchanged")
    check_visual_only((root / DETAILS).read_text(encoding="utf-8"), "StreetVisualDetails")
    check_visual_only(visual_factory(current), "VisualRounded")
    messages.append("New detail builder and its mesh factory contain no collider/primitive APIs")
    before = git_text(root, commit, SIGNALS)
    current = (root / SIGNALS).read_text(encoding="utf-8")
    if signal_envelopes(before) != signal_envelopes(current):
        raise ValueError("Signal pole/housing local positions or collider envelope sizes changed.")
    code = canonical(current)
    if code.count("AddComponent<BoxCollider>()") != 1 or "if(collider)renderer.gameObject.AddComponent<BoxCollider>().size=size;" not in code:
        raise ValueError("Signal collider factory no longer has the reviewed size=original-size guard.")
    if "localScale" in source_code(current, hide_strings=True):
        raise ValueError("Signal transform scaling requires a fresh collider envelope review.")
    messages.append("Four original signal envelope definitions retained, each built twice (eight blockers)")
    return messages


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", default=BASELINE, help="Existing local Git commit/ref; default is the pre-refinement street commit.")
    args = parser.parse_args(argv)
    try:
        for message in verify(ROOT, args.baseline):
            print("[OK]", message)
    except (ValueError, OSError) as error:
        print("[FAIL]", error, file=sys.stderr)
        return 1
    print("Unity compilation, runtime colliders/visibility, rendering and playthrough remain UNVERIFIED.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
