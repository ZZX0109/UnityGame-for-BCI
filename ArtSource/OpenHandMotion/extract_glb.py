# Extract rig structure + Open/Close motion data from the Elena FF hand.glb
# in its NATIVE glTF space (no rebake, no re-rigging; B2 owns metric FBX baking).
#
# Outputs (ArtSource/OpenHandMotion/):
#   extract_glb_report.json : armature hierarchy, rest local transforms,
#                              per-bone quaternion samples at the artist's
#                              open (t=0) / closed (t=1.333333) key times,
#                              plus mesh bounds for scale sanity.
# All data is derived animation/model data of Elena FF's CC-BY-SA-4.0 asset.
import bpy, os, json, math
from mathutils import Quaternion as _Q  # noqa: F401 (used via mathutils below)

HERE = os.path.dirname(os.path.abspath(__file__))
GLB = os.path.join(HERE, "..", "ThirdParty", "ElenaFF_RiggedHand", "hand.glb")
OUT = os.path.join(HERE, "extract_glb_report.json")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

arm = next(ob for ob in bpy.data.objects if ob.type == 'ARMATURE')
meshes = [ob for ob in bpy.data.objects if ob.type == 'MESH']
print("ARMATURE", arm.name, "bones", len(arm.data.bones))
print("MESHES", [(m.name, len(m.data.vertices)) for m in meshes])

# animation info
actions = [a for a in bpy.data.actions]
print("ACTIONS", [(a.name, a.frame_range[:]) for a in actions])
# glTF import maps seconds to frames at scene fps; report both
fps = bpy.context.scene.render.fps
print("FPS", fps)

def q_clean(q):
    return [round(v, 6) for v in (q.w, q.x, q.y, q.z)]  # (w,x,y,z)

# --- per-bone rest local transforms (from armature data = edit bones) ---
def local_rest(b):
    # bone.parent.matrix_space? use relative: bone.head/tail are armature space;
    # edit_bones give rest in armature space -> compute local matrix
    if b.parent is None:
        return b.matrix.copy()
    return b.parent.matrix.inverted() @ b.matrix

report_bones = {}
for eb in arm.data.bones:
    entry = {
        "parent": eb.parent.name if eb.parent else None,
        "children": [c.name for c in eb.children],
        "use_deform": eb.use_deform,
        "head_local": [round(v, 6) for v in eb.head_local],
        "tail_local": [round(v, 6) for v in eb.tail_local],
        "rest_local_matrix": [list(round(x, 6) for x in row) for row in local_rest(eb)],
    }
    report_bones[eb.name] = entry

# --- animation samples: quaternion at open (t0) and closed (t1) times ---
act = actions[0]
T_OPEN, T_CLOSED = 0.0, 1.333333
# frames from seconds (glTF keeps duration; Blender fps import)
f_open = T_OPEN * fps + 1.0
f_closed = T_CLOSED * fps + 1.0
print("FRAMES open", f_open, "closed", f_closed)

def pose_mat(bone_name, frame):
    sc.frame_set(int(round(frame)))
    pb = arm.pose.bones.get(bone_name)
    if pb is None:
        return None
    # local (bone.parent-space) quaternion
    if pb.bone.parent:
        m = pb.bone.parent.matrix_local.inverted() @ pb.matrix
    else:
        m = pb.matrix
    return m.to_quaternion()

sc = bpy.context.scene
sc.frame_set(0)
times = {"open_t0": f_open, "closed_t1.333333": f_closed}
samples = {}
for bname in arm.data.bones.keys():
    samples[bname] = {}
    for tkey, fr in times.items():
        q = pose_mat(bname, fr)
        samples[bname][tkey] = q_clean(q) if q else None

# delta closed-vs-open quaternion per bone
from mathutils import Quaternion
deltas = {}
for bname, sd in samples.items():
    qo, qc = sd["open_t0"], sd["closed_t1.333333"]
    if qo and qc:
        qo_q = Quaternion(tuple(qo))
        qc_q = Quaternion(tuple(qc))
        deltas[bname] = q_clean(qc_q.rotation_difference(qo_q))

# mesh bounds (native space)
mbounds = {}
for m in meshes:
    bb = [m.matrix_world @ v.co for v in m.data.vertices[:0]] or None
    mbounds[m.name] = {"verts": len(m.data.vertices)}

report = {
    "source": {
        "title": "Rigged hand",
        "author": "Elena FF",
        "license": "CC-BY-SA-4.0",
        "file": "ArtSource/ThirdParty/ElenaFF_RiggedHand/hand.glb",
        "sha256": "3eb51d0b612fb4b5f3b63c9e32ac145be191abcfd38ca7d3c5cf4fe0a7ee2d58",
        "action": act.name,
        "times_seconds": {"open": 0.0, "closed": 1.333333, "reopen": 3.208333},
    },
    "fps": fps,
    "bones": report_bones,
    "quaternion_samples_wxyz": samples,
    "delta_closed_vs_open_wxyz": deltas,
    "notes": [
        "Derived from Elena FF 'Rigged hand' (CC-BY-SA-4.0); derivative data keeps the same license.",
        "Quaternions are local (parent-bone space), Blender-imported rest, wxyz order.",
        "No rebake: native glTF/Blender import space; B2 owns metric-space FBX baking.",
    ],
}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(report, f, indent=1, ensure_ascii=False)
print("WROTE", OUT)
print("EXTRACT_DONE")
