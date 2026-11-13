"""Read-only three-scenario/source integrity. Does NOT compile C# or execute Unity."""
from pathlib import Path
import json,re,subprocess,sys
ROOT=Path(__file__).resolve().parents[3];GAME=ROOT/'Assets/StreetCrossing'
def main():
 errors=[]
 for command in ([sys.executable,str(ROOT/'Tools/validate_project.py')],[sys.executable,str(Path(__file__).with_name('build_street_font.py')),'--check']):
  if subprocess.run(command,cwd=ROOT).returncode:errors.append('Dependent integrity check failed.')
 scenarios={}
 for sid in ['S01','S02','S03']:
  text=(GAME/'Scenarios'/f'{sid}.asset').read_text();table=(GAME/'Scenarios'/f'{sid}PhaseTable.asset').read_text()
  def number(name):return float(re.search(r'^  '+re.escape(name)+r': ([0-9.]+)$',text,re.M).group(1))
  times=[float(v) for v in re.findall(r'^    duration: ([0-9.]+)$',table,re.M)]
  if len(times)!=6 or min(times)<=0 or times[3]<number('roadHalfWidth')*2/number('comfortableSpeed')+2:errors.append(sid+' has no configured comfortable-speed Walk window.')
  if f'  scenarioId: {sid}' not in text:errors.append('Stable id mismatch: '+sid)
  if number('requireLeftObservation')!=1 or number('requireRightObservation')!=1:errors.append(sid+' acceptance baseline requires both observations.')
  scenarios[sid]={'maximum':number('maximumVehicles'),'interval':number('maximumSpawnInterval'),'turning':number('turningTraffic'),'occlusion':number('kerbOcclusion')}
 if not scenarios['S02']['maximum']>scenarios['S01']['maximum'] or not scenarios['S02']['interval']<scenarios['S01']['interval']:errors.append('S02 must have meaningfully denser traffic.')
 if scenarios['S03']['turning']!=1 or scenarios['S03']['occlusion']!=1:errors.append('S03 needs real turns and kerb occlusion.')
 for path in (GAME/'Scripts').rglob('*.cs'):
  text=path.read_text()
  if 'FirstPersonSokoban' in text or 'ClawMachine' in text or 'RuntimeInitializeOnLoadMethod' in text:errors.append('Cross-game/global bootstrap dependency: '+str(path))
  if path.parent.name=='UI' and ('OnGUI' in text or 'GUILayout.' in text):errors.append('IMGUI remains: '+str(path))
  stripped=re.sub(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'','',text,flags=re.S)
  stack=[];pairs={')':'(',']':'[','}':'{'}
  for c in stripped:
   if c in '([{':stack.append(c)
   elif c in ')]}':
    if not stack or stack.pop()!=pairs[c]:errors.append('Delimiter mismatch: '+str(path));break
  if stack:errors.append('Unclosed delimiter: '+str(path))
 refs=json.loads((GAME/'Scripts/StreetCrossing.Runtime.asmdef').read_text())['references']
 if set(refs)!={'Unity.TextMeshPro','Unity.ugui'}:errors.append('Unexpected runtime assembly dependency.')
 manifest=json.loads((ROOT/'Packages/manifest.json').read_text());lock=json.loads((ROOT/'Packages/packages-lock.json').read_text())
 if manifest['dependencies'].get('com.unity.textmeshpro')!='3.0.6' or lock['dependencies']['com.unity.textmeshpro']['version']!='3.0.6':errors.append('TMP must be pinned consistently at3.0.6.')
 settings=list((ROOT/'Assets').rglob('TMP Settings.asset'))
 if len(settings)!=1 or settings[0].relative_to(ROOT).as_posix()!='Assets/BCIShared/TMP/Resources/TMP Settings.asset':errors.append('Exactly one shared canonical TMP Settings asset is required.')
 for module in ['Sedan','Hatchback','Van','Residential']:
  if not (GAME/'Resources/StreetCrossing/Modules'/f'{module}Module.prefab').is_file():errors.append('Missing original module '+module)
 for name in ['Asphalt','Paving','Plaster']:
  for suffix in ['Albedo','Normal']:
   if not (GAME/'Art'/f'{name}{suffix}.png').is_file():errors.append('Missing original texture '+name+suffix)
 entry=(ROOT/'ProjectSettings/EditorBuildSettings.asset').read_text()
 if entry.index('FirstPersonSokoban/Scenes/PuzzleScene.unity')>entry.index('StreetCrossing/Scenes/StreetCrossingScene.unity'):errors.append('Existing default scene order changed.')
 for error in errors:print('[FAIL]',error)
 if errors:return 1
 print('[OK] S01/S02/S03 data/windows, explicit regions, isolated Canvas/TMP source, reusable art and canonical resources checks passed.')
 print('Unity compilation/import, NUnit execution, actual rendering, playthrough, Windows build and video remain UNVERIFIED.')
 return 0
if __name__=='__main__':raise SystemExit(main())
