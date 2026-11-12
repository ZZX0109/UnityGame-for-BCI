"""Read-only Street S01 integrity checks. This does not compile C# or execute Unity."""
from pathlib import Path
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
GAME = ROOT / 'Assets/StreetCrossing'

def main():
    errors = []
    for command in ([sys.executable, str(ROOT / 'Tools/validate_project.py')], [sys.executable, str(Path(__file__).with_name('build_street_font.py')), '--check']):
        result = subprocess.run(command, cwd=ROOT)
        if result.returncode: errors.append('Dependent integrity check failed: ' + command[1])
    scenario = (GAME / 'Scenarios/S01.asset').read_text()
    table = (GAME / 'Scenarios/S01PhaseTable.asset').read_text()
    def number(name):
        match = re.search(r'^  ' + re.escape(name) + r': ([0-9.]+)$', scenario, re.M)
        if not match: raise ValueError('Missing scenario number ' + name)
        return float(match.group(1))
    durations = [float(v) for v in re.findall(r'^    duration: ([0-9.]+)$', table, re.M)]
    if len(durations) != 6 or any(v <= 0 for v in durations): errors.append('Six positive phase durations are required.')
    elif durations[3] < 2 * number('roadHalfWidth') / number('comfortableSpeed') + 2: errors.append('Insufficient comfortable-speed Walk window.')
    if number('maximumVehicles') != 2: errors.append('S01 acceptance baseline must use two vehicles.')
    for path in sorted((GAME / 'Scripts').rglob('*.cs')):
        text = path.read_text()
        if 'FirstPersonSokoban' in text or 'ClawMachine' in text or 'RuntimeInitializeOnLoadMethod' in text:
            errors.append('Unwanted dependency/global bootstrap: ' + str(path.relative_to(ROOT)))
        # Strip comments/strings before checking delimiters; still not a language compiler.
        stripped = re.sub(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', '', text, flags=re.S)
        stack=[]; pairs={')':'(',']':'[','}':'{'}
        for char in stripped:
            if char in '([{': stack.append(char)
            elif char in ')]}':
                if not stack or stack.pop()!=pairs[char]: errors.append('Delimiter mismatch: '+str(path.relative_to(ROOT))); break
        if stack: errors.append('Unclosed delimiter: '+str(path.relative_to(ROOT)))
    runtime = json.loads((GAME / 'Scripts/StreetCrossing.Runtime.asmdef').read_text())
    if runtime['references']: errors.append('Runtime should have no shared-game assembly dependencies.')
    settings=(ROOT/'ProjectSettings/EditorBuildSettings.asset').read_text()
    if settings.index('FirstPersonSokoban/Scenes/PuzzleScene.unity') > settings.index('StreetCrossing/Scenes/StreetCrossingScene.unity'):
        errors.append('Existing default PuzzleScene order changed.')
    for error in errors: print('[FAIL]',error)
    if errors:return 1
    print('[OK] S01 serialized safety window, isolated runtime, C# delimiters and preserved scene entry checks passed.')
    print('Unity compilation, NUnit execution, rendering, playthrough and Windows build remain UNVERIFIED.')
    return 0

if __name__=='__main__': raise SystemExit(main())
