import bpy, os, math, json
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath("../Assets/VirtualHand/Models/Hand_L.fbx"))
arm = bpy.data.objects["Armature.L"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
w = bpy.data.worlds.new("W"); w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.85, 0.85, 0.87, 1)
sc.world = w
lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 3.5
lo = bpy.data.objects.new("sun", lt); bpy.context.collection.objects.link(lo)
lo.rotation_euler = (math.radians(55), 0, math.radians(35))
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
with open(os.path.abspath("../Assets/VirtualHand/Resources/Rig/hand_rig_profile.json"), encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]
fist = profile["samples"]["fist"]

def render(tag):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    focus = sum(pts, Vector()) / 8.0
    cam = bpy.data.cameras.new("c"+tag); cam.type='ORTHO'
    cam.ortho_scale = 0.30
    co = bpy.data.objects.new("c"+tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector((0.2, -0.5, 0.85)).normalized()
    co.location = focus + d * 3
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/L_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)

def apply(mode):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels):
        pb = arm.pose.bones.get(ch["bone"] + ".L")
        if pb is None: continue
        deg = (fist[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        comp = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        e = pb.rotation_euler
        e[comp] += math.radians(deg if mode == "direct" else -deg if ch["axis"] == "X" else deg)
    bpy.context.view_layer.update()

apply("direct")
render("direct")
apply("negX")
render("negX")
print("L2 DONE")
