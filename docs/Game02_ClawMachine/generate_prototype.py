"""Deterministic offline authoring of the checked-in C01 scene/prefabs.

Run from any directory with Python 3. No Unity, downloaded assets or dependencies.
The manifest protects hand-edited generated assets: an ordinary run refuses to
overwrite changed bytes. --force is an explicit authoring operation, --check is
read only. Existing .meta GUIDs are preserved and verified, never regenerated.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import re

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'Assets/ClawMachine'
MANIFEST = Path(__file__).with_name('GeneratedAssets.json')
HEADER = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
outputs = {}


def guid(path):
    return hashlib.sha256(('claw-machine-c01-v1:' + path).encode()).hexdigest()[:32]


def asset(path):
    return 'Assets/ClawMachine/' + path


def ref(identifier):
    return '{fileID: %d}' % identifier


def external(path, identifier=11500000, kind=3):
    return '{fileID: %d, guid: %s, type: %d}' % (identifier, guid(asset(path)), kind)


def vec(value):
    return '{' + ', '.join('%s: %.7g' % (axis, number) for axis, number in zip('xyzw', value)) + '}'


def quat(euler):
    # Unity Euler order Z-X-Y: qY * qX * qZ.
    x, y, z = [math.radians(v) * .5 for v in euler]
    cx, cy, cz, sx, sy, sz = math.cos(x), math.cos(y), math.cos(z), math.sin(x), math.sin(y), math.sin(z)
    return (cy*sx*cz+sy*cx*sz, sy*cx*cz-cy*sx*sz, cy*cx*sz-sy*sx*cz, cy*cx*cz+sy*sx*sz)


def output(path, text):
    outputs[asset(path)] = '\n'.join(line.rstrip() for line in text.splitlines())+'\n'


class Scene:
    def __init__(self):
        self.next = 1000
        self.nodes = []
        self.components = []

    def allocate(self):
        self.next += 1
        return self.next

    def node(self, name, parent=None, position=(0,0,0), scale=(1,1,1), euler=(0,0,0)):
        n = dict(id=self.allocate(), t=self.allocate(), name=name, parent=parent, position=position, scale=scale, rotation=quat(euler), children=[], components=[])
        self.nodes.append(n)
        if parent: parent['children'].append(n)
        return n

    def component(self, node, type_id, name, fields):
        c = dict(id=self.allocate(), node=node, type=type_id, name=name, fields=fields)
        node['components'].append(c)
        self.components.append(c)
        return c

    def mono(self, node, script, fields=''):
        return self.component(node, 114, 'MonoBehaviour', '  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: '+external('Scripts/'+script+'.cs')+'\n  m_Name: \n  m_EditorClassIdentifier: \n'+fields)

    def box(self, node, size=(1,1,1), center=(0,0,0), trigger=False, enabled=True):
        return self.component(node, 65, 'BoxCollider', '  m_Material: '+external('Materials/PrizeFriction.physicMaterial',13400000,2)+'\n  m_IsTrigger: %d\n  m_Enabled: %d\n  serializedVersion: 2\n  m_Size: %s\n  m_Center: %s\n' % (trigger,enabled,vec(size),vec(center)))

    def mesh(self, name, parent, position, scale, material, mesh=10202, collide=False, euler=(0,0,0)):
        n=self.node(name,parent,position,scale,euler)
        self.component(n,33,'MeshFilter','  m_Mesh: {fileID: %d, guid: 0000000000000000e000000000000000, type: 0}\n' % mesh)
        self.component(n,23,'MeshRenderer','  m_Enabled: 1\n  m_CastShadows: 1\n  m_ReceiveShadows: 1\n  m_DynamicOccludee: 1\n  m_LightProbeUsage: 1\n  m_ReflectionProbeUsage: 1\n  m_RenderingLayerMask: 1\n  m_Materials:\n  - '+external('Materials/'+material+'.mat',2100000,2)+'\n  m_ScaleInLightmap: 1\n')
        if collide: self.box(n)
        return n

    def body(self,node,kinematic=False):
        return self.component(node,54,'Rigidbody','  serializedVersion: 4\n  m_Mass: 0.3\n  m_Drag: 0.05\n  m_AngularDrag: 0.2\n  m_CenterOfMass: {x: 0, y: 0, z: 0}\n  m_InertiaTensor: {x: 1, y: 1, z: 1}\n  m_InertiaRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_UseGravity: %d\n  m_IsKinematic: %d\n  m_Interpolate: 1\n  m_Constraints: 0\n  m_CollisionDetection: %d\n' % (not kinematic,kinematic,0 if kinematic else 2))

    def render(self,subset=None):
        nodes=self.nodes if subset is None else subset
        selected={n['id'] for n in nodes}
        text=HEADER
        for n in nodes:
            text+='--- !u!1 &%d\nGameObject:\n  serializedVersion: 6\n  m_Component:\n  - component: %s\n' % (n['id'],ref(n['t']))
            text+=''.join('  - component: %s\n' % ref(c['id']) for c in n['components'])
            text+='  m_Layer: 0\n  m_Name: %s\n  m_TagString: Untagged\n  m_IsActive: 1\n' % json.dumps(n['name'],ensure_ascii=False)
            text+='--- !u!4 &%d\nTransform:\n  m_GameObject: %s\n  serializedVersion: 2\n  m_LocalRotation: %s\n  m_LocalPosition: %s\n  m_LocalScale: %s\n  m_ConstrainProportionsScale: 0\n' % (n['t'],ref(n['id']),vec(n['rotation']),vec(n['position']),vec(n['scale']))
            text+='  m_Children:' + ('\n'+''.join('  - %s\n'%ref(c['t']) for c in n['children'] if c['id'] in selected) if n['children'] else ' []\n')
            text+='  m_Father: %s\n'%ref(n['parent']['t'] if n['parent'] and n['parent']['id'] in selected else 0)
        for c in self.components:
            if c['node']['id'] not in selected: continue
            text+='--- !u!%d &%d\n%s:\n  m_GameObject: %s\n%s' % (c['type'],c['id'],c['name'],ref(c['node']['id']),c['fields'])
        return text


def field(name, obj):
    return '  '+name+': '+ref(obj['id'])+'\n'


def transform_field(name, node):
    return '  '+name+': '+ref(node['t'])+'\n'


def descendants(node):
    return [node]+[n for child in node['children'] for n in descendants(child)]


def build():
    materials={'Ivory':((.91,.88,.80,1),.15,.37), 'Silver':((.65,.68,.67,1),.8,.62),
        'Graphite':((.17,.20,.20,1),.6,.38), 'Mint':((.37,.68,.55,1),.08,.35),
        'Ribbon':((.94,.82,.48,1),.1,.32), 'Floor':((.74,.65,.53,1),.02,.25),
        'Backdrop':((.84,.78,.66,1),.03,.3), 'Light':((.96,.97,.91,1),.1,.55),
        'Rose':((.73,.45,.39,1),.08,.3), 'Glass':((.69,.90,.85,.055),.1,.65)}
    for name,(color,metal,smooth) in materials.items():
        glass=name=='Glass'
        floats={'_Metallic':metal,'_Glossiness':smooth,'_Mode':3 if glass else 0,'_SrcBlend':5 if glass else 1,'_DstBlend':10 if glass else 0,'_ZWrite':0 if glass else 1}
        output('Materials/'+name+'.mat',HEADER+'--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_Name: '+name+'\n  m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}\n  m_ValidKeywords: '+('[_ALPHAPREMULTIPLY_ON]' if glass else '[]')+'\n  m_InvalidKeywords: []\n  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 1\n  m_CustomRenderQueue: '+('3000' if glass else '-1')+'\n  stringTagMap: '+('{RenderType: Transparent}' if glass else '{}')+'\n  m_SavedProperties:\n    serializedVersion: 3\n    m_TexEnvs: []\n    m_Ints: []\n    m_Floats:\n'+''.join('    - %s: %s\n'%p for p in floats.items())+'    m_Colors:\n    - _Color: {r: %s, g: %s, b: %s, a: %s}\n    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}\n'%color)
    output('Materials/PrizeFriction.physicMaterial',HEADER+'--- !u!134 &13400000\nPhysicMaterial:\n  m_Name: PrizeFriction\n  dynamicFriction: 0.6\n  staticFriction: 0.7\n  bounciness: 0\n  frictionCombine: 0\n  bounceCombine: 2\n')
    output('Challenges/C01Profile.asset',HEADER+'--- !u!114 &11400000\nMonoBehaviour:\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_Script: '+external('Scripts/Prize/PrizeProfile.cs')+'\n  m_Name: C01Profile\n  m_EditorClassIdentifier: \n  mass: 0.3\n  centerOfMass: {x: 0, y: 0, z: 0}\n  maxCenterOffset: 0.05\n  contactTolerance: 0.035\n  verticalTolerance: 0.045\n  breakForce: 24\n  stableSpeed: 0.08\n  stableDwell: 0.3\n  settleTimeout: 2.5\n')
    output('Challenges/C01.asset',HEADER+'--- !u!114 &11400000\nMonoBehaviour:\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_Script: '+external('Scripts/Core/ClawChallengeDefinition.cs')+'\n  m_Name: C01\n  m_EditorClassIdentifier: \n  challengeId: C01\n  title: "C01 · 三轴定位教学"\n  prizeId: mint-parcel\n  targetId: mint-parcel\n  seed: 20261112\n  attemptLimit: 5\n  spawnPosition: {x: 0.08, y: 0.92, z: 0.08}\n  spawnEuler: {x: 0, y: 0, z: 0}\n  cameraPolicy: FrontLeftRight\n  successRule: ReleasedTargetFullyInsideChuteAndStable\n  graspProfile: '+external('Challenges/C01Profile.asset',11400000,2)+'\n')
    s=Scene()
    root=s.node('Claw Machine - C01 simulator')
    cabinet=s.node('VisualCabinet',root)
    s.mesh('Rear plinth',cabinet,(0,.39,.26),(1.36,.76,.68),'Ivory',collide=True)
    s.mesh('Front right panel',cabinet,(.28,.40,-.58),(.72,.74,.12),'Ivory',collide=True)
    s.mesh('Left plinth rail',cabinet,(-.63,.4,-.3),(.10,.74,.52),'Ivory',collide=True)
    for x in (-.66,.66):
        for z in (-.58,.58):
            s.mesh('Corner frame',cabinet,(x,1.40,z),(.055,1.28,.055),'Ivory',collide=True)
        s.mesh('Top side rail',cabinet,(x,2.02,0),(.07,.065,1.20),'Ivory',collide=True)
        glass=s.mesh('Low reflection side glass',cabinet,(x,1.40,0),(.008,1.12,1.08),'Glass',collide=True)
        s.mesh('Gantry Z guide',cabinet,(x*.82,1.96,0),(.035,.035,1.08),'Silver')
    s.mesh('Rear glass',cabinet,(0,1.4,.58),(1.24,1.15,.008),'Glass',collide=True)
    s.mesh('Front glass',cabinet,(0,1.43,-.58),(1.24,1.10,.008),'Glass',collide=True)
    s.mesh('Marquee - no ceiling mesh',cabinet,(0,2.14,-.58),(1.42,.23,.10),'Ivory')
    s.mesh('Mint marquee stripe',cabinet,(0,2.14,-.638),(.82,.065,.012),'Mint')
    for z in (-.58,.58):
        s.mesh('Top cross rail',cabinet,(0,2.02,z),(1.36,.065,.07),'Silver')
    for x in (-.625,.625):
        s.mesh('Interior light bar',cabinet,(x,1.42,.535),(.018,1.06,.018),'Light')
    # Floor is three solids around an actual open hole x[-.58,-.10], z[-.52,-.10].
    bay=s.node('PrizeBay - real chute cutout',root)
    s.mesh('Bay rear floor',bay,(0,.77,.225),(1.24,.06,.65),'Ivory',collide=True)
    s.mesh('Bay front right floor',bay,(.26,.77,-.325),(.72,.06,.45),'Ivory',collide=True)
    s.mesh('Bay front left strip',bay,(-.60,.77,-.325),(.04,.06,.45),'Ivory',collide=True)
    s.mesh('Bay front edge strip',bay,(-.34,.77,-.535),(.48,.06,.03),'Ivory',collide=True)
    # Four-sided lower chute; no collider crosses its falling path.
    chuteRoot=s.node('PrizeChute',root)
    s.mesh('Collection tray',chuteRoot,(-.34,.245,-.31),(.48,.03,.42),'Mint',collide=True)
    for x in (-.59,-.09): s.mesh('Chute side wall',chuteRoot,(x,.50,-.31),(.02,.48,.44),'Silver',collide=True)
    for z in (-.53,-.09): s.mesh('Chute inspection window' if z<-.3 else 'Chute back wall',chuteRoot,(-.34,.50,z),(.48,.48,.02),'Glass' if z<-.3 else 'Silver',collide=True)
    scoreNode=s.node('Settlement trigger - full prize dwell',chuteRoot,(-.34,.47,-.31))
    scoreBox=s.box(scoreNode,(.46,.48,.40),trigger=True)
    detector=s.mono(scoreNode,'Prize/PrizeChuteDetector',field('settlementVolume',scoreBox))
    drop=s.node('Drop pose',root,(-.34,1.65,-.31))
    s.mesh('Control console',cabinet,(.22,.84,-.72),(.85,.08,.25),'Ivory',collide=True)
    s.mesh('Grab button',cabinet,(.37,.90,-.73),(.055,.022,.055),'Rose',mesh=10206)
    s.mesh('Joystick stem',cabinet,(.02,.91,-.73),(.015,.055,.015),'Graphite',mesh=10206)
    s.mesh('Joystick ball',cabinet,(.02,.97,-.73),(.05,.05,.05),'Mint',mesh=10207)
    gantryRoot=s.node('GantryRoot',root)
    bridge=s.mesh('X bridge',gantryRoot,(0,1.92,0),(1.08,.055,.06),'Silver')
    trolley=s.mesh('XZ trolley',gantryRoot,(0,1.92,0),(.20,.10,.16),'Graphite')
    cable=s.mesh('Y cable',gantryRoot,(0,1.86,0),(.012,.06,.012),'Graphite',mesh=10206)
    tool=s.node('Tool - grip origin',gantryRoot,(0,1.65,0))
    body=s.body(tool,True)
    s.mesh('Gripper hub',tool,(0,.17,0),(.13,.065,.13),'Silver',mesh=10206)
    fingers=[];pads=[]
    for i in range(3):
        angle=math.radians(90+i*120)
        finger=s.node('Finger %d'%i,tool,(math.cos(angle)*.18,.07,math.sin(angle)*.18))
        s.mesh('Silver finger',finger,(0,0,0),(.022,.18,.022),'Silver',mesh=10208)
        s.mesh('Contact pad',finger,(0,-.07,0),(.033,.045,.033),'Mint',mesh=10207)
        fingers.append(finger); pads.append(s.node('Contact probe',finger,(0,-.07,0)))
    grip=s.mono(tool,'Gripper/GripperController',field('toolBody',body)+'  fingers:\n'+''.join('  - '+ref(n['t'])+'\n' for n in fingers)+'  contactPads:\n'+''.join('  - '+ref(n['t'])+'\n' for n in pads))
    workspace=s.box(root,(.86,.74,.68),(0,1.28,0),trigger=True,enabled=False)
    driver=s.mono(root,'Drivers/GantryDriver',transform_field('machineRoot',root)+field('workspaceVolume',workspace)+field('toolBody',body)+field('gripper',grip)+transform_field('bridge',bridge)+transform_field('trolley',trolley)+transform_field('cable',cable)+'  railHeight: 1.92\n  acceleration: 1.5\n  tolerance: 0.005\n')
    prize=s.node('Target - mint parcel',root,(.08,.92,.08))
    prizeBody=s.body(prize)
    prizeCollider=s.box(prize,(.22,.24,.20))
    s.mesh('Rigid parcel',prize,(0,0,0),(.22,.24,.20),'Mint')
    s.mesh('Ribbon X',prize,(0,.122,0),(.224,.009,.032),'Ribbon')
    s.mesh('Ribbon Z',prize,(0,.124,0),(.033,.009,.204),'Ribbon')
    s.mesh('Front ribbon',prize,(0,0,-.102),(.033,.244,.008),'Ribbon')
    prizePhysics=s.mono(prize,'Prize/PrizePhysics',field('body',prizeBody)+field('mainCollider',prizeCollider)+transform_field('machineRoot',root))
    cameras=s.node('CameraRig',root)
    look=s.node('Cabinet look target',cameras,(0,1.13,0))
    cameraComps=[]
    for i,(name,pos) in enumerate([('Front',(0,1.52,-3.45)),('Left',(-3.45,1.52,0)),('Right',(3.45,1.52,0))]):
        target=(0,1.13,0); dx,dy,dz=[target[j]-pos[j] for j in range(3)]
        yaw=math.degrees(math.atan2(dx,dz)); pitch=-math.degrees(math.atan2(dy,math.hypot(dx,dz)))
        cam=s.node(name+' camera',cameras,pos,euler=(pitch,yaw,0))
        c=s.component(cam,20,'Camera','  m_Enabled: %d\n  serializedVersion: 2\n  m_ClearFlags: 2\n  m_BackGroundColor: {r: 0.84, g: 0.79, b: 0.69, a: 1}\n  m_projectionMatrixMode: 1\n  m_GateFitMode: 2\n  m_FOVAxisMode: 0\n  m_SensorSize: {x: 36, y: 24}\n  m_LensShift: {x: 0, y: 0}\n  m_FocalLength: 50\n  m_NormalizedViewPortRect:\n    serializedVersion: 2\n    x: 0\n    y: 0\n    width: 0.747\n    height: 1\n  near clip plane: 0.03\n  far clip plane: 30\n  field of view: 42\n  orthographic: 0\n  orthographic size: 5\n  m_Depth: 0\n  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n  m_RenderingPath: -1\n  m_TargetTexture: {fileID: 0}\n  m_TargetDisplay: 0\n  m_TargetEye: 3\n  m_HDR: 0\n  m_AllowMSAA: 1\n  m_AllowDynamicResolution: 0\n  m_ForceIntoRT: 0\n  m_OcclusionCulling: 1\n'% (i==0))
        cameraComps.append(c)
    # One listener for every view, located at the machine instead of one per camera.
    s.component(cameras,81,'AudioListener','  m_Enabled: 1\n')
    cameraController=s.mono(cameras,'Camera/CraneCameraController','  views:\n'+''.join('  - '+ref(c['id'])+'\n' for c in cameraComps)+transform_field('lookTarget',look))
    managers=s.node('Managers',root)
    inputController=s.mono(managers,'Input/ClawInputController')
    task=s.mono(managers,'Core/GraspTaskController',field('gantry',driver)+field('gripper',grip)+field('prize',prizePhysics)+field('chute',detector)+'  challenge: '+external('Challenges/C01.asset',11400000,2)+'\n'+transform_field('dropPose',drop)+'  aimSpeed: 0.35\n  lowerSpeed: 0.25\n  liftSpeed: 0.32\n  closeDuration: 0.5\n')
    audio=s.component(managers,82,'AudioSource','  m_Enabled: 1\n  serializedVersion: 4\n  OutputAudioMixerGroup: {fileID: 0}\n  m_audioClip: {fileID: 0}\n  m_PlayOnAwake: 0\n  m_Volume: 1\n  m_Pitch: 1\n  Loop: 0\n  Mute: 0\n  Spatialize: 0\n  SpatializePostEffects: 0\n  Priority: 128\n  DopplerLevel: 0\n  MinDistance: 1\n  MaxDistance: 10\n  Pan2D: 0\n  rolloffMode: 0\n  BypassEffects: 0\n  BypassListenerEffects: 0\n  BypassReverbZones: 0\n')
    manager=s.mono(managers,'Core/ClawGameManager',field('task',task)+field('keyboard',inputController)+field('cameras',cameraController)+field('audioSource',audio))
    s.mono(managers,'UI/ClawGameUI',field('game',manager)+'  uiFont: '+external('Fonts/ClawUISans.otf',12800000,3)+'\n')
    # Snapshot prefabs are self-contained, references stay typed and local.
    output('Prefabs/Machine/ClawMachine.prefab',s.render(descendants(root)))
    for name,node in [('Cabinet',cabinet),('Gantry',gantryRoot),('PrizeChute',chuteRoot),('PrizeBay',bay),('XBridge',bridge),('Trolley',trolley),('Cable',cable),('Gripper',tool)]:
        output('Prefabs/Machine/'+name+'.prefab',s.render(descendants(node)))
    # Prize prefab is an art/physics template; scene's PrizePhysics provides its machine-frame binding.
    prizeNodes=descendants(prize)
    saved=prizePhysics['node']; prize['components'].remove(prizePhysics)
    s.components.remove(prizePhysics)
    oldPos=prize['position']; prize['position']=(0,0,0)
    output('Prefabs/Prize/MintParcel.prefab',s.render(prizeNodes))
    prize['position']=oldPos; prize['components'].append(prizePhysics); s.components.append(prizePhysics)
    room=s.node('ArcadeShell - open top')
    s.mesh('Warm gallery floor',room,(0,-.075,0),(10,.1,10),'Floor',collide=True)
    s.mesh('Rear gallery wall',room,(0,1.8,2.7),(10,3.8,.12),'Backdrop')
    for x in (-2.0,2.0):
        s.mesh('Distant arcade plinth',room,(x,.6,1.8),(.8,1.2,.7),'Rose' if x<0 else 'Mint')
        s.mesh('Distant arcade display',room,(x,1.4,1.8),(.8,.5,.7),'Ivory')
    light=s.node('Soft key light',room,(0,3,-2),euler=(38,-28,0))
    s.component(light,108,'Light','  m_Enabled: 1\n  serializedVersion: 10\n  m_Type: 1\n  m_Shape: 0\n  m_Color: {r: 1, g: 0.94, b: 0.82, a: 1}\n  m_Intensity: 1.05\n  m_Range: 10\n  m_SpotAngle: 30\n  m_InnerSpotAngle: 21.8\n  m_CookieSize: 10\n  m_Shadows:\n    m_Type: 2\n    m_Resolution: -1\n    m_CustomResolution: -1\n    m_Strength: 0.65\n    m_Bias: 0.05\n    m_NormalBias: 0.4\n    m_NearPlane: 0.2\n  m_Cookie: {fileID: 0}\n  m_DrawHalo: 0\n  m_Flare: {fileID: 0}\n  m_RenderMode: 0\n  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n  m_RenderingLayerMask: 1\n  m_Lightmapping: 4\n  m_BounceIntensity: 1\n  m_ColorTemperature: 5500\n  m_UseColorTemperature: 0\n')
    settings='--- !u!29 &1\nOcclusionCullingSettings:\n  serializedVersion: 2\n  m_OcclusionBakeSettings:\n    smallestOccluder: 5\n    smallestHole: 0.25\n    backfaceThreshold: 100\n  m_SceneGUID: 00000000000000000000000000000000\n  m_OcclusionCullingData: {fileID: 0}\n--- !u!104 &2\nRenderSettings:\n  serializedVersion: 9\n  m_Fog: 0\n  m_AmbientSkyColor: {r: 0.78, g: 0.75, b: 0.67, a: 1}\n  m_AmbientEquatorColor: {r: 0.65, g: 0.64, b: 0.58, a: 1}\n  m_AmbientGroundColor: {r: 0.38, g: 0.37, b: 0.33, a: 1}\n  m_AmbientIntensity: 0.85\n  m_AmbientMode: 3\n  m_SkyboxMaterial: {fileID: 0}\n--- !u!157 &3\nLightmapSettings:\n  serializedVersion: 12\n  m_GIWorkflowMode: 1\n  m_LightmapsMode: 1\n  m_LightingSettings: {fileID: 0}\n--- !u!196 &4\nNavMeshSettings:\n  serializedVersion: 2\n  m_NavMeshData: {fileID: 0}\n'
    output('Scenes/ClawMachineScene.unity',HEADER+settings+s.render()[len(HEADER):])
    assemblies={
        'Scripts/ClawMachine.Runtime.asmdef':dict(name='ClawMachine.Runtime',rootNamespace='ClawMachine',references=[],autoReferenced=True),
        'Editor/ClawMachine.Editor.asmdef':dict(name='ClawMachine.Editor',rootNamespace='ClawMachine.Editor',references=['ClawMachine.Runtime'],includePlatforms=['Editor']),
        'Tests/EditMode/ClawMachine.EditModeTests.asmdef':dict(name='ClawMachine.EditModeTests',references=['ClawMachine.Runtime','UnityEngine.TestRunner','UnityEditor.TestRunner'],includePlatforms=['Editor'],overrideReferences=True,precompiledReferences=['nunit.framework.dll'],autoReferenced=False,defineConstraints=['UNITY_INCLUDE_TESTS']),
        'Tests/PlayMode/ClawMachine.PlayModeTests.asmdef':dict(name='ClawMachine.PlayModeTests',references=['ClawMachine.Runtime','UnityEngine.TestRunner','UnityEditor.TestRunner'],overrideReferences=True,precompiledReferences=['nunit.framework.dll'],autoReferenced=False,defineConstraints=['UNITY_INCLUDE_TESTS'])}
    for path,data in assemblies.items():output(path,json.dumps(data,indent=2)+'\n')


def meta(path):
    rel=path.relative_to(ROOT).as_posix()
    text='fileFormatVersion: 2\nguid: '+guid(rel)+'\n'
    if path.is_dir():return text+'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    importer={'.cs':'MonoImporter','.asmdef':'AssemblyDefinitionImporter','.json':'TextScriptImporter','.txt':'TextScriptImporter','.mat':'NativeFormatImporter','.asset':'NativeFormatImporter','.physicMaterial':'NativeFormatImporter','.prefab':'PrefabImporter','.otf':'TrueTypeFontImporter'}.get(path.suffix,'DefaultImporter')
    text+=importer+':\n  externalObjects: {}\n'
    if path.suffix=='.cs':text+='  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
    if path.suffix in ('.asset','.mat','.physicMaterial'):text+='  mainObjectFileID: '+str({'.asset':11400000,'.mat':2100000,'.physicMaterial':13400000}[path.suffix])+'\n'
    if path.suffix=='.otf':text+='  serializedVersion: 4\n  fontSize: 24\n  forceTextureCase: -2\n  includeFontData: 1\n  fontName: Claw UI Sans\n  fontNames:\n  - Claw UI Sans\n  fallbackFontReferences: []\n  fontRenderingMode: 0\n  ascentCalculationMode: 1\n'
    return text+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n'


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--force',action='store_true');parser.add_argument('--check',action='store_true');args=parser.parse_args()
    build();previous=json.loads(MANIFEST.read_text()) if MANIFEST.exists() else {}
    changed=[]
    for rel,content in outputs.items():
        p=ROOT/rel; actual=p.read_text() if p.exists() else None
        if args.check:
            if actual!=content:changed.append(rel)
        elif actual!=content:
            if actual is not None and hashlib.sha256(actual.encode()).hexdigest()!=previous.get(rel) and not args.force:
                raise SystemExit('Refusing to overwrite edited asset: '+rel+'; inspect it, then use --force if intended.')
            p.parent.mkdir(parents=True,exist_ok=True);p.write_text(content)
    for p in [BASE]+sorted(BASE.rglob('*')):
        if p.suffix=='.meta':continue
        mp=Path(str(p)+'.meta')
        if mp.exists():
            existing=re.search(r'^guid: ([0-9a-f]{32})$',mp.read_text(),re.M)
            if not existing or existing.group(1)!=guid(p.relative_to(ROOT).as_posix()):raise SystemExit('Unexpected existing GUID; preserved '+str(mp))
        elif args.check:changed.append(mp.relative_to(ROOT).as_posix())
        else:mp.write_text('\n'.join(line.rstrip() for line in meta(p).splitlines())+'\n')
    if args.check:
        if changed:raise SystemExit('Generated assets differ: '+', '.join(changed))
        print('C01 generated scene, prefab, materials, profiles and .meta: exact deterministic match.')
    else:
        MANIFEST.write_text(json.dumps({p:hashlib.sha256(c.encode()).hexdigest() for p,c in sorted(outputs.items())},indent=2)+'\n')
        print('Authored',len(outputs),'assets; existing GUIDs preserved.')


if __name__=='__main__':main()
