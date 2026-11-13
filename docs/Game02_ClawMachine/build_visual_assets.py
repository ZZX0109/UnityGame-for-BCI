"""Original deterministic tiny PBR-style textures, Python standard library only."""
from pathlib import Path
import hashlib,json,math,random,struct,zlib,argparse
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'Assets/ClawMachine/Textures'
def png(width,height,rgba):
 def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
 raw=b''.join(b'\0'+bytes(rgba[y*width*4:(y+1)*width*4]) for y in range(height))
 return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b'')
def outputs():
 result={}
 for kind in ('IvoryMicrograin','SilverBrush','FabricWeave','FabricNormal'):
  pixels=[];rng=random.Random(20261113)
  for y in range(64):
   for x in range(64):
    if kind=='FabricNormal':
     nx=0.10*math.sin(x*math.pi/2);ny=0.10*math.sin(y*math.pi/2);nz=math.sqrt(1-nx*nx-ny*ny)
     pixels.extend([int((nx+1)*127.5),int((ny+1)*127.5),int((nz+1)*127.5),255]);continue
    noise=rng.uniform(-1,1)
    value=246+noise*5 if kind=='IvoryMicrograin' else 230+10*math.sin(y*1.8)+noise*3 if kind=='SilverBrush' else 232+9*((x%4<2) != (y%4<2))+noise*7
    value=max(0,min(255,int(value)));pixels.extend([value,value,value,255])
  result[kind+'.png']=png(64,64,pixels)
 return result
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args();BASE.mkdir(parents=True,exist_ok=True)
 generated=outputs()
 for name,data in generated.items():
  p=BASE/name
  if args.check:
   if not p.exists() or p.read_bytes()!=data:raise SystemExit('Visual texture mismatch: '+str(p))
  else:p.write_bytes(data)
 manifest={name:hashlib.sha256(data).hexdigest() for name,data in generated.items()}
 p=BASE/'OriginalTextureManifest.json'
 if not args.check:p.write_text(json.dumps(manifest,indent=2)+'\n')
 elif json.loads(p.read_text())!=manifest:raise SystemExit('Visual manifest mismatch')
 print('[OK] Four original 64x64 texturemaps reproducible; no third-party image assets.')
if __name__=='__main__':main()
