"""Compare actual scene physics/task bindings with the pre-presentation git revision.

Numeric local YAML IDs may change when authoring meshes. Resolve them to scene
paths/component kinds so this catches physics changes without mistaking IDs for
behaviour. This is a source invariant check, not Unity/PhysX acceptance.
"""
import argparse
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCENE = 'Assets/ClawMachine/Scenes/ClawMachineScene.unity'
PHYSICS = {54, 65, 135, 136}


def snapshot(text, script_guids):
    blocks = {int(key): (int(kind), body) for kind, key, body in
              re.findall(r'^--- !u!(\d+) &(\d+)\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}
    names, transforms, go_transforms = {}, {}, {}
    for key, (kind, body) in blocks.items():
        if kind == 1:
            name = re.search(r'^  m_Name: (.*)$', body, re.M).group(1)
            names[key] = json.loads(name) if name.startswith('"') else name
        elif kind == 4:
            go = int(re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1))
            parent = int(re.search(r'm_Father: \{fileID: (\d+)\}', body).group(1))
            transforms[key] = (go, parent, body)
            go_transforms[go] = key

    def path(tid):
        go, parent, _ = transforms[tid]
        peers = [key for key, (other, above, _) in transforms.items()
                 if above == parent and names[other] == names[go]]
        return (path(parent) + '/' if parent else '') + names[go] + '[' + str(peers.index(tid)) + ']'

    def identity(key):
        if key == 0:
            return '0'
        kind, body = blocks[key]
        if kind == 1:
            return path(go_transforms[key]) + ':GameObject'
        if kind == 4:
            return path(key) + ':Transform'
        go = int(re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1))
        script = re.search(r'm_Script: \{fileID: \d+, guid: ([a-f0-9]+)', body)
        return path(go_transforms[go]) + ':' + str(kind) + (':' + script.group(1) if script else '')

    result = {}
    for key, (kind, body) in blocks.items():
        script = re.search(r'm_Script: \{fileID: \d+, guid: ([a-f0-9]+)', body)
        if kind not in PHYSICS and not (kind == 114 and script and script.group(1) in script_guids):
            continue
        resolved = re.sub(r'\{fileID: (\d+)\}', lambda m: '{object: ' + identity(int(m.group(1))) + '}', body)
        go = int(re.search(r'm_GameObject: \{fileID: (\d+)\}', body).group(1))
        lineage = []
        tid = go_transforms[go]
        while tid:
            _, parent, transform = transforms[tid]
            lineage.append((path(tid), re.findall(r'^  m_Local(?:Position|Rotation|Scale): .+$', transform, re.M)))
            tid = parent
        result[identity(key)] = {'fields': resolved, 'transforms': lineage}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-ref', default='3f709374c6a69354e842f6c7048a10139a074094')
    args = parser.parse_args()
    scripts = ROOT / 'Assets/ClawMachine/Scripts'
    script_guids = set()
    for folder in ('Core', 'Drivers', 'Gripper', 'Prize'):
        for meta in (scripts / folder).glob('*.cs.meta'):
            script_guids.add(re.search(r'^guid: ([a-f0-9]+)$', meta.read_text(), re.M).group(1))
    baseline = subprocess.run(['git', 'show', args.baseline_ref + ':' + SCENE], cwd=ROOT,
                              check=True, capture_output=True, text=True).stdout
    before = snapshot(baseline, script_guids)
    after = snapshot((ROOT / SCENE).read_text(), script_guids)
    changed = sorted(key for key in before.keys() | after.keys() if before.get(key) != after.get(key))
    if changed:
        raise SystemExit('Physics/task source differs:\n' + '\n'.join(changed))
    print(f'[OK] {len(before)} physical/task components and ancestor poses unchanged versus {args.baseline_ref}.')
    print('NOT VERIFIED: actual Unity compilation, collision dynamics or graphics.')


if __name__ == '__main__':
    main()
