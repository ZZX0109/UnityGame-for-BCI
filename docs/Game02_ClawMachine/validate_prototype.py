"""Read-only, standard-library structural checks for the isolated C01 prototype.

These checks do not compile C#, simulate PhysX, render a screenshot or replace
the authored Unity tests. Run Python Tools/validate_project.py as well.
"""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'Assets/ClawMachine'
GUID = re.compile(r'\bguid:\s*([a-f0-9]{32})')


def check():
    errors=[]
    metas={}
    for path in (ROOT/'Assets').rglob('*.meta'):
        found=GUID.findall(path.read_text())
        if len(found)==1:metas[found[0]]=Path(str(path)[:-5])
    serialized=list(BASE.rglob('*.unity'))+list(BASE.rglob('*.prefab'))+list(BASE.rglob('*.asset'))+list(BASE.rglob('*.physicMaterial'))
    for path in serialized:
        text=path.read_text()
        ids=re.findall(r'^--- !u!\d+ &(\d+)',text,re.M)
        if len(ids)!=len(set(ids)):errors.append(f'{path.name}: duplicate object ID')
        for target in re.findall(r'\{fileID: (\d+)\}',text):
            if target!='0' and target not in ids:errors.append(f'{path.name}: unresolved local object {target}')
        for target in GUID.findall(text):
            if target.startswith('0000000000000000'):continue
            if target not in metas:errors.append(f'{path.name}: unresolved asset GUID {target}')
    # Confirm the physics floor's authored solids leave an actual falling path.
    scene=(BASE/'Scenes/ClawMachineScene.unity').read_text()
    blocks={int(i):body for i,body in re.findall(r'^--- !u!\d+ &(\d+)\n(.*?)(?=^--- !u!|\Z)',scene,re.M|re.S)}
    def v(body,key):
        m=re.search(re.escape(key)+r': \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',body)
        return tuple(float(x) for x in m.groups()) if m else None
    transforms={};names={}
    for identifier,body in blocks.items():
        if body.startswith('GameObject:'):
            goName=re.search(r'm_Name: (.*)',body).group(1)
            names[identifier]=json.loads(goName) if goName.startswith('"') else goName
        if body.startswith('Transform:'):
            go=int(re.search(r'm_GameObject: \{fileID: (\d+)\}',body).group(1))
            parent=int(re.search(r'm_Father: \{fileID: (\d+)\}',body).group(1))
            transforms[identifier]=(go,parent,v(body,'m_LocalPosition'),v(body,'m_LocalScale'))
    def pose(tid):
        go,parent,p,s=transforms[tid]
        if not parent:return p,s
        pp,ps=pose(parent)
        return tuple(pp[i]+p[i]*ps[i] for i in range(3)),tuple(s[i]*ps[i] for i in range(3))
    goTransforms={row[0]:tid for tid,row in transforms.items()}
    fallingMin=(-.45,.28,-.41);fallingMax=(-.23,1.78,-.21)
    for identifier,body in blocks.items():
        if not body.startswith('BoxCollider:') or 'm_Enabled: 0' in body or 'm_IsTrigger: 1' in body:continue
        go=int(re.search(r'm_GameObject: \{fileID: (\d+)\}',body).group(1))
        if names.get(go,'').startswith('Target'):continue
        p,scale=pose(goTransforms[go]);size=v(body,'m_Size');center=v(body,'m_Center')
        low=tuple(p[i]+(center[i]-size[i]/2)*scale[i] for i in range(3));high=tuple(p[i]+(center[i]+size[i]/2)*scale[i] for i in range(3))
        if all(low[i]<fallingMax[i]-1e-6 and high[i]>fallingMin[i]+1e-6 for i in range(3)):
            errors.append('Solid collider blocks central chute falling path: '+names.get(go,str(go)))
    for source in (BASE/'Scripts').rglob('*.cs'):
        text=source.read_text()
        for forbidden in ('FirstPersonSokoban.', 'UnityEditor', 'System.Net', 'DllImport', 'UnityEngine.Random', 'FindObjectOfType', 'GameObject.Find'):
            if forbidden in text:errors.append(f'{source.name}: unexpected runtime dependency {forbidden}')
    # The exact Editor and baseline renderer remain unchanged.
    if '2022.3.62f3c1' not in (ROOT/'ProjectSettings/ProjectVersion.txt').read_text():errors.append('Editor version changed')
    build=(ROOT/'ProjectSettings/EditorBuildSettings.asset').read_text()
    for path in ('Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity','Assets/ClawMachine/Scenes/ClawMachineScene.unity'):
        if path not in build:errors.append('Build Settings missing '+path)
    for command in ([sys.executable,str(Path(__file__).with_name('generate_prototype.py')),'--check'],[sys.executable,str(Path(__file__).with_name('build_ui_font.py')),'--check']):
        result=subprocess.run(command,cwd=ROOT,capture_output=True,text=True)
        print(result.stdout,end='')
        if result.returncode:errors.append(result.stderr.strip() or result.stdout.strip())
    if errors:
        for message in errors:print('[FAIL]',message)
        return 1
    print('[OK] C01 local object/asset references, open chute path, isolation, entry scene and font.')
    print('NOT VERIFIED: C# compilation, Unity tests, PhysX grip/drop, camera/UI rendering, Windows build.')
    return 0


if __name__=='__main__':raise SystemExit(check())
