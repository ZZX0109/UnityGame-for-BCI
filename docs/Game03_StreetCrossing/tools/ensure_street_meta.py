"""Create only missing metadata inside StreetCrossing; existing GUIDs are preserved."""
from pathlib import Path
import uuid
ROOT=Path(__file__).resolve().parents[3];GAME=ROOT/'Assets/StreetCrossing'
for p in [GAME]+sorted(GAME.rglob('*')):
 if p.suffix=='.meta':continue
 meta=Path(str(p)+'.meta')
 if meta.exists():continue
 rel=p.relative_to(ROOT).as_posix();g=uuid.uuid5(uuid.NAMESPACE_URL,'StreetCrossing-v1/'+rel).hex
 text='fileFormatVersion: 2\nguid: '+g+'\n'
 if p.is_dir():text+='folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
 elif p.suffix=='.cs':text+='MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
 elif p.suffix=='.obj':
  text+='''ModelImporter:
  serializedVersion: 22200
  internalIDToNameTable:
  - first:
      43: 4300000
    second: NAME
  externalObjects: {}
  materials:
    materialImportMode: 0
  meshes:
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    importCameras: 0
    importLights: 0
    useFileUnits: 1
    fileIdsGeneration: 2
    weldVertices: 1
    generateSecondaryUV: 0
    indexFormat: 0
  tangentSpace:
    normalSmoothAngle: 55
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
  importAnimation: 0
'''.replace('NAME',p.stem)
 elif p.suffix=='.png':
  normal='Normal' in p.stem
  text+='''TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 12
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: SRGB
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: NORMAL
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  textureFormat: 1
  maxTextureSize: 512
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 4
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 0
  compressionQuality: 75
  alphaSource: 0
  textureType: TYPE
  textureShape: 1
  platformSettings: []
'''.replace('SRGB',str(int(not normal))).replace('NORMAL',str(int(normal))).replace('TYPE','1' if normal else '0')
 else:
  imp={'.asmdef':'AssemblyDefinitionImporter','.prefab':'PrefabImporter','.mat':'NativeFormatImporter','.asset':'NativeFormatImporter','.txt':'TextScriptImporter','.json':'TextScriptImporter','.shader':'ShaderImporter'}.get(p.suffix,'DefaultImporter')
  text+=imp+':\n  externalObjects: {}\n'
  if p.suffix in ('.mat','.asset'):text+='  mainObjectFileID: '+('2100000' if p.suffix=='.mat' else '11400000')+'\n'
 text+='  userData:\n  assetBundleName:\n  assetBundleVariant:\n';meta.write_text(text)
print('Street metadata present; existing GUIDs retained.')
