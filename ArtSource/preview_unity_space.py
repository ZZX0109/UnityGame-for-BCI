# Render fist/pinch previews by importing the EXPORTED FBX — this is the exact
# geometry+armature Unity receives, so flexion signs/axes are validated in situ.
import bpy, os, math
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath("../Assets/VirtualHand/Models/Hand_R.fbx"))
arm = bpy.data.objects.get("Armature.R")
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
print("FBX arm", arm.name, "mesh", o.name)

# apply fist via profile angles in FBX-local space (same math as Unity ProfileDelta)
import json
with open(os.path.abspath("../Assets/VirtualHand/Resources/Rig/hand_rig_profile.json"), encoding="utf-8") as f:
    profile = json.load(f)

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

def apply_sample(values):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(profile["channels"]):
        pb = arm.pose.bones.get(ch["bone"] + ".R")
        if pb is None: continue
        deg = (values[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        e = pb.rotation_euler
        comp = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        e[comp] += math.radians(deg)
    bpy.context.view_layer.update()

sc = bpy.context.scene
def render(tag, direction, dist):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    focus = sum(pts, Vector()) / 8.0
    cam = bpy.data.cameras.new("c"+tag); cam.lens = 60
    co = bpy.data.objects.new("c"+tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = focus + d * dist
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"../ArtSource/Review/unityspace_{tag}.png")
    if False: pass
    sc.render.filepath = os.path.abspath("Review/unityspace_" + tag + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)

# lights
w = bpy.data.worlds.new("W"); w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.9, 0.9, 0.9, 1)
sc.world = w
lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 3
lo = bpy.data.objects.new("sun", lt); bpy.context.collection.objects.link(lo)
lo.rotation_euler = (math.radians(60), 0, math.radians(30))

apply_sample(profile["samples"]["fist"])
render("fist", (0.3, -1, 0.3), 2.2)
apply_sample(profile["samples"]["pinch"])
render("pinch", (0.25, -0.6, 0.8), 2.0)
print("UNITY-SPACE PREVIEWS DONE")
