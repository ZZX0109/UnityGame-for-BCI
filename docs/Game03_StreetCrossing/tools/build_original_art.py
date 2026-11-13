"""Generate original reusable metre-scale meshes, modules and tileable Standard materials. No third-party art."""
from pathlib import Path
import math, random, uuid
from PIL import Image
ROOT=Path(__file__).resolve().parents[3]; GAME=ROOT/'Assets/StreetCrossing'; ART=GAME/'Art'; RES=GAME/'Resources/StreetCrossing/Modules'
ART.mkdir(exist_ok=True);RES.mkdir(parents=True,exist_ok=True)
def guid(p):return uuid.uuid5(uuid.NAMESPACE_URL,'StreetCrossing-v1/'+p.relative_to(ROOT).as_posix()).hex
class Mesh:
 def __init__(self):self.v=[];self.f=[]
 def box(self,c,s):
  x,y,z=c;a,b,d=[q/2 for q in s];n=len(self.v)
  self.v += [(x+u*a,y+w*b,z+k*d) for u,w,k in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
  self.f += [tuple(n+i for i in face) for face in [(1,4,3,2),(5,6,7,8),(1,5,8,4),(2,3,7,6),(4,8,7,3),(1,2,6,5)]]
 def loft(self,sections):
  n=len(self.v)
  for z,width,bottom,top,roof_width in sections:self.v.extend([(-width,bottom,z),(width,bottom,z),(roof_width,top,z),(-roof_width,top,z)])
  self.f.append(tuple(n+i for i in (4,3,2,1)))
  for i in range(len(sections)-1):
   for j in range(4):self.f.append((n+i*4+j+1,n+i*4+(j+1)%4+1,n+(i+1)*4+(j+1)%4+1,n+(i+1)*4+j+1))
  self.f.append(tuple(n+(len(sections)-1)*4+i for i in (1,2,3,4)))
 def wheel(self,r=.34,width=.2):
  count=24;n=len(self.v)
  for x,rad in [(-width/2,r*.86),(-width*.35,r),(width*.35,r),(width/2,r*.86)]:
   for j in range(count):self.v.append((x,math.cos(j*2*math.pi/count)*rad,math.sin(j*2*math.pi/count)*rad))
  for ring in range(3):
   for j in range(count):self.f.append((n+ring*count+j+1,n+ring*count+(j+1)%count+1,n+(ring+1)*count+(j+1)%count+1,n+(ring+1)*count+j+1))
  self.f += [tuple(n+j+1 for j in reversed(range(count))),tuple(n+3*count+j+1 for j in range(count))]
 def save(self,name):
  p=ART/(name+'.obj');out=['# Original Street Crossing module; metres; authored generator included','o '+name]
  out += ['v %.6f %.6f %.6f'%v for v in self.v]
  out += ['vt %.6f %.6f'%(v[0]*.35+v[2]*.2,v[1]*.35+v[2]*.2) for v in self.v]
  out += ['s 1']
  out += ['f '+' '.join(f'{i}/{i}' for i in f) for f in self.f];p.write_text('\n'.join(out)+'\n');return p
meshes={}
for kind,L,W,H in [('Sedan',4.2,1.75,1.5),('Hatchback',3.7,1.65,1.42),('Van',4.8,1.9,2.02)]:
 m=Mesh();m.loft([(-L/2,W*.38,.35,.68,W*.43),(-L*.38,W*.49,.3,.86,W*.47),(L*.3,W*.49,.3,.84,W*.46),(L/2,W*.37,.36,.62,W*.4)]);meshes[kind+'Body']=m.save(kind+'Body')
 cabin=Mesh();cabin.loft([(-L*.28,W*.44,.81,H-.12,W*.36),(-L*.16,W*.46,.81,H,W*.36),(L*.16,W*.45,.81,H-.04,W*.34),(L*.28,W*.42,.81,1.03,W*.37)]);meshes[kind+'Glass']=cabin.save(kind+'Glass')
 roof=Mesh();roof.box((0,H-.01,-L*.04),(W*.7,.09,L*.31));meshes[kind+'Roof']=roof.save(kind+'Roof')
wheel=Mesh();wheel.wheel();meshes['Wheel']=wheel.save('Wheel');hub=Mesh();hub.wheel(.19,.025);meshes['Hub']=hub.save('Hub')
walls=Mesh();walls.box((0,3.6,0),(10.3,7.2,4));meshes['ResidentialWalls']=walls.save('ResidentialWalls')
trim=Mesh();glass=Mesh()
trim.box((0,7.15,0),(10.65,.24,4.28));trim.box((0,.22,0),(10.5,.38,4.12))
for row in range(3):
 for col in [-1,0,1]:
  x=col*2.9;y=1.8+row*2.05
  trim.box((x,y,-2.08),(1.7,1.4,.18));glass.box((x,y,-2.19),(1.43,1.15,.06));trim.box((x,y,-2.24),(.07,1.2,.05));trim.box((x,y-.73,-2.19),(1.85,.12,.37))
  if row>0:
   trim.box((x,y-.85,-2.55),(1.95,.12,.7));trim.box((x,y-.35,-2.9),(1.95,.07,.07))
   for edge in [-1,1]:trim.box((x+edge*.89,y-.58,-2.9),(.06,.58,.06))
trim.box((4,.99,-2.09),(1.03,1.98,.2));meshes['ResidentialTrim']=trim.save('ResidentialTrim');meshes['ResidentialGlass']=glass.save('ResidentialGlass')
rng=random.Random(3113)
for name,base,roughness in [('Asphalt',(90,90,81),.85),('Paving',(197,186,159),.72),('Plaster',(221,207,179),.65)]:
 size=128;image=Image.new('RGB',(size,size));pixels=image.load();height=[]
 for y in range(size):
  line=[]
  for x in range(size):
   noise=rng.randrange(-11,12)
   if name=='Paving' and (x%32<2 or (y+(16 if x//32%2 else 0))%32<2):noise-=30
   if name=='Plaster' and y%32==0:noise-=10
   pixels[x,y]=tuple(max(0,min(255,v+noise)) for v in base);line.append(noise)
  height.append(line)
 image.save(ART/(name+'Albedo.png'))
 normal=Image.new('RGB',(size,size));n=normal.load()
 for y in range(size):
  for x in range(size):
   dx=(height[y][(x+1)%size]-height[y][(x-1)%size])*.22;dy=(height[(y+1)%size][x]-height[(y-1)%size][x])*.22
   n[x,y]=(int(max(0,min(255,128-dx))),int(max(0,min(255,128-dy))),252)
 normal.save(ART/(name+'Normal.png'))
 def texture_ref(tex):return '{fileID: 2800000, guid: '+guid(ART/tex)+', type: 3}'
 text='''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_Name: NAME
  m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}
  m_ValidKeywords: [_NORMALMAP]
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_CustomRenderQueue: -1
  stringTagMap: {}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: ALBEDO
        m_Scale: {x: 4, y: 4}
        m_Offset: {x: 0, y: 0}
    - _BumpMap:
        m_Texture: NORMAL
        m_Scale: {x: 4, y: 4}
        m_Offset: {x: 0, y: 0}
    m_Ints: []
    m_Floats:
    - _Metallic: 0.03
    - _Glossiness: GLOSS
    - _BumpScale: 0.32
    m_Colors:
    - _Color: {r: 1, g: 1, b: 1, a: 1}
    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}
'''.replace('NAME',name).replace('ALBEDO',texture_ref(name+'Albedo.png')).replace('NORMAL',texture_ref(name+'Normal.png')).replace('GLOSS',str(1-roughness))
 (RES/(name+'.mat')).write_text(text)
def material(name,color,metal=0,gloss=.25):
 text=(GAME/'Materials/StreetSurface.mat').read_text().replace('m_Name: StreetSurface','m_Name: '+name)
 import re
 text=re.sub(r'- _Color: .*', '- _Color: {r: %.4f, g: %.4f, b: %.4f, a: 1}'%color,text)
 text=re.sub(r'- _Metallic: .*','- _Metallic: '+str(metal),text);text=re.sub(r'- _Glossiness: .*','- _Glossiness: '+str(gloss),text)
 p=RES/(name+'.mat');p.write_text(text);return p
paint=material('VehiclePaint',(.52,.34,.25),.5,.62);glazing=material('Glazing',(.28,.36,.35),.22,.78);rubber=material('TyreRubber',(.075,.083,.073),0,.16);metal=material('WheelAlloy',(.53,.55,.51),.75,.5);frame=material('FacadeTrim',(.72,.64,.50),.02,.22)
def prefab(name,children):
 p=RES/(name+'.prefab');out=['%YAML 1.1','%TAG !u! tag:unity3d.com,2011:']
 def go(gid,tid,n,components=[]):
  out.extend([f'--- !u!1 &{gid}','GameObject:','  serializedVersion: 6','  m_Component:',f'  - component: {{fileID: {tid}}}']+[f'  - component: {{fileID: {c}}}' for c in components]+['  m_Layer: 0','  m_Name: '+n,'  m_TagString: Untagged','  m_IsActive: 1'])
 go(100010,100020,name)
 out.extend(['--- !u!4 &100020','Transform:','  m_GameObject: {fileID: 100010}','  serializedVersion: 2','  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}','  m_LocalPosition: {x: 0, y: 0, z: 0}','  m_LocalScale: {x: 1, y: 1, z: 1}','  m_Children:']+[f'  - {{fileID: {200020+i*100}}}' for i in range(len(children))]+['  m_Father: {fileID: 0}'])
 for i,(label,mesh,mat,position,scale) in enumerate(children):
  gid=200010+i*100;tid=gid+10;mf=gid+20;mr=gid+30;go(gid,tid,label,[mf,mr])
  out.extend([f'--- !u!4 &{tid}','Transform:',f'  m_GameObject: {{fileID: {gid}}}','  serializedVersion: 2','  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}','  m_LocalPosition: {x: %s, y: %s, z: %s}'%position,'  m_LocalScale: {x: %s, y: %s, z: %s}'%scale,'  m_Children: []','  m_Father: {fileID: 100020}',f'--- !u!33 &{mf}','MeshFilter:',f'  m_GameObject: {{fileID: {gid}}}','  m_Mesh: {fileID: 4300000, guid: '+guid(mesh)+', type: 3}',f'--- !u!23 &{mr}','MeshRenderer:',f'  m_GameObject: {{fileID: {gid}}}','  m_Enabled: 1','  m_CastShadows: 1','  m_ReceiveShadows: 1','  m_Materials:','  - {fileID: 2100000, guid: '+guid(mat)+', type: 2}','  m_SortingLayerID: 0','  m_SortingOrder: 0'])
 p.write_text('\n'.join(out)+'\n')
for kind,L in [('Sedan',4.2),('Hatchback',3.7),('Van',4.8)]:
 children=[('Painted body',meshes[kind+'Body'],paint,(0,0,0),(1,1,1)),('Cabin',meshes[kind+'Roof'],paint,(0,0,0),(1,1,1)),('Glazed cabin',meshes[kind+'Glass'],glazing,(0,0,0),(1,1,1))]
 for side in [-1,1]:
  for axle in [-1,1]:
   x=side*(.84 if kind!='Van' else .94);z=axle*L*.3
   children += [('Tyre',meshes['Wheel'],rubber,(x,.35,z),(1,1,1)),('Wheel alloy',meshes['Hub'],metal,(x+side*.12,.35,z),(1,1,1))]
 prefab(kind+'Module',children)
prefab('ResidentialModule',[('Plaster masonry',meshes['ResidentialWalls'],RES/'Plaster.mat',(0,0,0),(1,1,1)),('Facade sills balconies and frames',meshes['ResidentialTrim'],frame,(0,0,0),(1,1,1)),('Recessed glazing',meshes['ResidentialGlass'],glazing,(0,0,0),(1,1,1))])
print('Generated original modular OBJ meshes, four prefabs and tileable albedo/normal materials.')
