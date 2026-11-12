from pathlib import Path
import hashlib

ROOT = Path(__file__).resolve().parents[1]
ASSET_ROOT = ROOT / "Assets" / "FirstPersonSokoban"
UNITY_TAG = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"


def rel(path):
    return path.relative_to(ROOT).as_posix()


def guid(path):
    return hashlib.md5(rel(path).encode("utf-8")).hexdigest()


def write(path, content):
    if path.exists():
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def rgb(value):
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def color(value, alpha=1.0):
    r, g, b = rgb(value)
    return f"{{r: {r:.6f}, g: {g:.6f}, b: {b:.6f}, a: {alpha:.6f}}}"


def material(name, base, metallic, smoothness, emission=None):
    path = ASSET_ROOT / "Resources" / "Materials" / f"{name}.mat"
    keywords = ["_EMISSION"] if emission else []
    glow = color(emission) if emission else color("000000")
    body = f"""{UNITY_TAG}--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {name}
  m_Shader: {{fileID: 46, guid: 0000000000000000f000000000000000, type: 0}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: {keywords if keywords else '[]'}
  m_InvalidKeywords: []
  m_LightmapFlags: {4 if emission else 5}
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_LockedProperties: 
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs: []
    m_Ints: []
    m_Floats:
    - _Metallic: {metallic}
    - _Glossiness: {smoothness}
    m_Colors:
    - _Color: {color(base)}
    - _EmissionColor: {glow}
  m_BuildTextureStacks: []
"""
    write(path, body)


class PrefabBuilder:
    def __init__(self):
        self.counter = 100000

    def fid(self):
        self.counter += 10
        return self.counter

    def node(self, name, position=(0, 0, 0), scale=(1, 1, 1), tag="Untagged"):
        return {
            "name": name, "tag": tag, "position": position, "scale": scale,
            "go": self.fid(), "transform": self.fid(), "components": [], "docs": [], "children": []
        }

    def component(self, node, class_id, class_name, body):
        fid = self.fid()
        node["components"].append((class_id, fid))
        node["docs"].append(f"--- !u!{class_id} &{fid}\n{class_name}:\n  m_GameObject: {{fileID: {node['go']}}}\n{body}")
        return fid

    def mono(self, node, script_relative, extra=""):
        script = ROOT / script_relative
        body = f"  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {{fileID: 11500000, guid: {guid(script)}, type: 3}}\n  m_Name: \n  m_EditorClassIdentifier: \n{extra}"
        self.component(node, 114, "MonoBehaviour", body)

    def cube(self, parent, name, position, scale, material_name, collider=False):
        node = self.node(name, position, scale)
        parent["children"].append(node)
        mat = ASSET_ROOT / "Resources" / "Materials" / f"{material_name}.mat"
        self.component(node, 33, "MeshFilter", "  m_Enabled: 1\n  m_Mesh: {fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}\n")
        renderer = f"""  m_Enabled: 1
  m_CastShadows: 1
  m_ReceiveShadows: 1
  m_DynamicOccludee: 1
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 1
  m_RayTracingMode: 2
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: 2100000, guid: {guid(mat)}, type: 2}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ScaleInLightmap: 1
"""
        self.component(node, 23, "MeshRenderer", renderer)
        if collider:
            self.box_collider(node, (0, 0, 0), (1, 1, 1))
        return node

    def box_collider(self, node, center, size):
        body = f"""  m_Material: {{fileID: 0}}
  m_IsTrigger: 0
  m_Enabled: 1
  serializedVersion: 2
  m_Size: {vector(size)}
  m_Center: {vector(center)}
"""
        self.component(node, 65, "BoxCollider", body)

    def emit(self, node, father=0):
        components = "\n".join(f"  - component: {{fileID: {fid}}}" for _, fid in [(4, node["transform"])] + node["components"])
        go = f"""--- !u!1 &{node['go']}
GameObject:
  serializedVersion: 6
  m_Component:
{components}
  m_Layer: 0
  m_Name: {node['name']}
  m_TagString: {node['tag']}
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
"""
        children = "\n".join(f"  - {{fileID: {child['transform']}}}" for child in node["children"])
        if not children:
            children = " []"
        else:
            children = "\n" + children
        transform = f"""--- !u!4 &{node['transform']}
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {node['go']}}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {vector(node['position'])}
  m_LocalScale: {vector(node['scale'])}
  m_ConstrainProportionsScale: 0
  m_Children:{children}
  m_Father: {{fileID: {father}}}
"""
        docs = [go, transform] + node["docs"]
        for child in node["children"]:
            docs.extend(self.emit(child, node["transform"]))
        return docs

    def save(self, root, name):
        content = UNITY_TAG + "".join(self.emit(root))
        write(ASSET_ROOT / "Resources" / "Prefabs" / f"{name}.prefab", content)


def vector(value):
    return "{x: %s, y: %s, z: %s}" % tuple(value)


def make_floor():
    b = PrefabBuilder()
    root = b.node("Floor")
    b.box_collider(root, (0, -0.12, 0), (2, 0.24, 2))
    b.cube(root, "Tile", (0, -0.1, 0), (1.96, 0.2, 1.96), "Floor")
    b.save(root, "Floor")


def make_wall():
    b = PrefabBuilder()
    root = b.node("Wall")
    b.cube(root, "Wall Body", (0, 1.4, 0), (2, 2.8, 2), "Wall", True)
    b.cube(root, "Top Cap", (0, 2.82, 0), (2.035, 0.08, 2.035), "WallTrim")
    b.cube(root, "Vertical Inlay", (0, 1.35, 1.003), (0.045, 2.3, 0.016), "WallTrim")
    b.save(root, "Wall")


def make_box():
    b = PrefabBuilder()
    root = b.node("Box")
    b.box_collider(root, (0, 0.63, 0), (1.28, 1.26, 1.28))
    b.mono(root, "Assets/FirstPersonSokoban/Scripts/Interaction/BoxView.cs")
    b.cube(root, "Body", (0, 0.63, 0), (1.22, 1.22, 1.22), "Box")
    b.cube(root, "Bottom Frame", (0, 0.07, 0), (1.32, 0.12, 1.32), "BoxFrame")
    b.cube(root, "Top Frame", (0, 1.19, 0), (1.32, 0.12, 1.32), "BoxFrame")
    for x in (-0.57, 0.57):
        for z in (-0.57, 0.57):
            b.cube(root, f"Corner {x} {z}", (x, 0.63, z), (0.11, 1.22, 0.11), "BoxFrame")
    b.save(root, "Box")


def make_goal():
    b = PrefabBuilder()
    root = b.node("Goal")
    b.mono(root, "Assets/FirstPersonSokoban/Scripts/Interaction/GoalView.cs")
    b.cube(root, "Plate", (0, 0.014, 0), (1.52, 0.025, 1.52), "GoalBase")
    b.cube(root, "Glow North", (0, 0.035, 0.76), (1.6, 0.04, 0.065), "Goal")
    b.cube(root, "Glow South", (0, 0.035, -0.76), (1.6, 0.04, 0.065), "Goal")
    b.cube(root, "Glow East", (0.76, 0.035, 0), (0.065, 0.04, 1.6), "Goal")
    b.cube(root, "Glow West", (-0.76, 0.035, 0), (0.065, 0.04, 1.6), "Goal")
    b.save(root, "Goal")


def make_player():
    b = PrefabBuilder()
    root = b.node("Player")
    character = """  m_Material: {fileID: 0}
  m_IsTrigger: 0
  m_Enabled: 1
  serializedVersion: 3
  m_Height: 1.8
  m_Radius: 0.32
  m_SlopeLimit: 45
  m_StepOffset: 0.35
  m_SkinWidth: 0.04
  m_MinMoveDistance: 0
  m_Center: {x: 0, y: 0.9, z: 0}
"""
    b.component(root, 143, "CharacterController", character)
    b.mono(root, "Assets/FirstPersonSokoban/Scripts/Player/FirstPersonController.cs",
           "  moveSpeed: 3.5\n  mouseSensitivity: 2.2\n  gravity: -22\n  cameraHeight: 1.65\n")
    b.mono(root, "Assets/FirstPersonSokoban/Scripts/Interaction/BoxInteractor.cs", "  interactionDistance: 2.5\n")
    camera = b.node("First Person Camera", (0, 1.65, 0), tag="MainCamera")
    root["children"].append(camera)
    camera_body = f"""  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 2
  m_BackGroundColor: {color('789097')}
  m_NormalizedViewPortRect:
    serializedVersion: 2
    x: 0
    y: 0
    width: 1
    height: 1
  near clip plane: 0.05
  far clip plane: 100
  field of view: 75
  orthographic: 0
  orthographic size: 5
  m_Depth: -1
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingPath: -1
  m_TargetTexture: {{fileID: 0}}
  m_TargetDisplay: 0
  m_TargetEye: 3
  m_HDR: 1
  m_AllowMSAA: 1
  m_AllowDynamicResolution: 0
  m_OcclusionCulling: 1
"""
    b.component(camera, 20, "Camera", camera_body)
    b.component(camera, 81, "AudioListener", "  m_Enabled: 1\n")
    b.save(root, "Player")


def make_scene():
    b = PrefabBuilder()
    root = b.node("FirstPersonSokoban · Bootstrap")
    b.mono(root, "Assets/FirstPersonSokoban/Scripts/Core/GameBootstrap.cs")
    settings = f"""{UNITY_TAG}--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {{fileID: 0}}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 10
  m_Fog: 0
  m_AmbientSkyColor: {color('8CA6AD')}
  m_AmbientEquatorColor: {color('718A91')}
  m_AmbientGroundColor: {color('45575C')}
  m_AmbientIntensity: 0.85
  m_AmbientMode: 3
  m_SkyboxMaterial: {{fileID: 0}}
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_GIWorkflowMode: 1
  m_LightmapsMode: 1
  m_LightingSettings: {{fileID: 0}}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
  m_NavMeshData: {{fileID: 0}}
"""
    scene_path = ASSET_ROOT / "Scenes" / "PuzzleScene.unity"
    write(scene_path, settings + "".join(b.emit(root)))
    build = f"""{UNITY_TAG}--- !u!1045 &1
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes:
  - enabled: 1
    path: Assets/FirstPersonSokoban/Scenes/PuzzleScene.unity
    guid: {guid(scene_path)}
  m_configObjects: {{}}
"""
    write(ROOT / "ProjectSettings" / "EditorBuildSettings.asset", build)


def write_metas():
    directories = [ASSET_ROOT] + [p for p in ASSET_ROOT.rglob("*") if p.is_dir()]
    for folder in directories:
        meta = Path(str(folder) + ".meta")
        if meta.exists():
            continue
        write(meta, f"""fileFormatVersion: 2
guid: {guid(folder)}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""")
    for asset in [p for p in ASSET_ROOT.rglob("*") if p.is_file() and p.suffix != ".meta"]:
        meta = Path(str(asset) + ".meta")
        if meta.exists():
            continue
        suffix = asset.suffix
        if suffix == ".cs":
            importer = """MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        elif suffix in (".txt", ".json"):
            importer = """TextScriptImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        elif suffix == ".asmdef":
            importer = """AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        elif suffix == ".mat":
            importer = """NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 2100000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        elif suffix == ".prefab":
            importer = """PrefabImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        else:
            importer = """DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        write(meta, f"fileFormatVersion: 2\nguid: {guid(asset)}\n{importer}")


def main():
    materials = {
        "Floor": ("AAB9BA", 0.08, 0.5, None),
        "Wall": ("C5CFCE", 0.05, 0.42, None),
        "WallTrim": ("526B70", 0.5, 0.5, None),
        "Box": ("BD7545", 0.25, 0.38, None),
        "BoxFrame": ("3A4547", 0.72, 0.48, None),
        "BoxComplete": ("48B89F", 0.35, 0.42, "1D7566"),
        "BoxDenied": ("C75E50", 0.2, 0.35, "7A211C"),
        "GoalBase": ("24494B", 0.4, 0.52, None),
        "Goal": ("3CAF9E", 0.3, 0.48, "176B5E"),
        "GoalComplete": ("8CF7CE", 0.25, 0.42, "49D9A6"),
    }
    for name, values in materials.items():
        material(name, *values)
    make_floor()
    make_wall()
    make_box()
    make_goal()
    make_player()
    make_scene()
    write_metas()
    print("Serialized scene, 5 prefabs, 10 materials, build settings and stable .meta files generated.")


if __name__ == "__main__":
    main()
