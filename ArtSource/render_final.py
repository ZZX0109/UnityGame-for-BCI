# Final run13 previews: profile-driven, camera on TRUE palm normal, contact marker.
import bpy, os, math, json
import numpy as np
from mathutils import Vector, Matrix

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath("../Assets/VirtualHand/Models/Hand_R.fbx"))
arm = bpy.data.objects["Armature.R"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene

with open(os.path.abspath("../Assets/VirtualHand/Resources/Rig/hand_rig_profile.json"), encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

def apply20(values):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels):
        pb = arm.pose.bones.get(ch["bone"] + ".R")
        if pb is None: continue
        deg = (values[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        e = pb.rotation_euler
        comp = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        e[comp] += math.radians(deg)
    bpy.context.view_layer.update()

def setup_env():
    w = bpy.data.worlds.new("W"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.85, 0.85, 0.87, 1)
    sc.world = w
    lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 3.5
    lo = bpy.data.objects.new("sun", lt)
    bpy.context.collection.objects.link(lo)
    lo.rotation_euler = (math.radians(55), 0, math.radians(35))

def render_ortho(tag, focus, normal, span):
    cam = bpy.data.cameras.new("c" + tag); cam.type = 'ORTHO'
    cam.ortho_scale = span * 1.3
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(normal).normalized()
    co.location = focus + d * 5
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/run13_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)

setup_env()
deps = bpy.context.evaluated_depsgraph_get()

# palm normal from posed geometry per Codex/DHS formula
def get_normal():
    iw = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    lw = (arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    return (iw.cross(lw)).normalized()

# fist
apply20(profile["samples"]["fist"])
bpy.context.view_layer.update()
n = get_normal()
deps = bpy.context.evaluated_depsgraph_get()
ev = o.evaluated_get(deps)
pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
focus = sum(pts, Vector()) / 8.0
span = max(max(p.x for p in pts) - min(p.x for p in pts),
           max(p.y for p in pts) - min(p.y for p in pts),
           max(p.z for p in pts) - min(p.z for p in pts))
render_ortho("fist_palm", focus, n, span)
render_ortho("fist_side", focus, n.cross(Vector((0, 1, 0))), span)

# pinch
apply20(profile["samples"]["pinch"])
bpy.context.view_layer.update()
# contact marker at midpoint between thumb/index tips
deps = bpy.context.evaluated_depsgraph_get()
itip = arm.matrix_world @ Vector(arm.pose.bones["Index3.R"].tail)
ttip = arm.matrix_world @ Vector(arm.pose.bones["Thumb3.R"].tail)
contact = (itip + ttip) / 2
mk = bpy.data.meshes.new("mk")
mk_obj = bpy.data.objects.new("mk", mk)
bpy.context.collection.objects.link(mk_obj)
bpy.ops.object.mode_set(mode="OBJECT")
import bmesh
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.008)
bm.to_mesh(mk); bm.free()
mk_obj.location = contact
n = get_normal()
print("PINCH CONTACT GAP", round((itip - ttip).length, 4))
render_ortho("pinch_palm_contact", contact, n, 0.35)
render_ortho("pinch_side_contact", contact, n.cross(Vector((0, 1, 0))), 0.35)
bpy.data.objects.remove(mk_obj)
print("RUN13 PREVIEWS DONE")
