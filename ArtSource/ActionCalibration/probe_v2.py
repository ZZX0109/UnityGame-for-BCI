# Probe: verify 20-control drive bends the fingers, and determine the true
# geometric PALM normal via a small positive-flexion motion (fingertips curl
# toward the palm side). Prints sign of old meta-plane normal vs palm side.
import bpy, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "output", "rev2", "probe")
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(HERE, "input", "Hand_R.fbx"))
arm = bpy.data.objects["Armature.R"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
w = bpy.data.worlds.new("W"); w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.85, 0.85, 0.87, 1)
sc.world = w
lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 3.0
lo = bpy.data.objects.new("sun", lt); bpy.context.collection.objects.link(lo)
lo.rotation_euler = (math.radians(55), 0, math.radians(35))
lt2 = bpy.data.lights.new("fill", 'SUN'); lt2.energy = 1.5
lo2 = bpy.data.objects.new("fill", lt2); bpy.context.collection.objects.link(lo2)
lo2.rotation_euler = (math.radians(120), 0, math.radians(-140))
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

B = arm.pose.bones
FINGERS = ("Index", "Middle", "Ring", "Little")


def zero_all():
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)


def set_x(bone, deg):
    e = B[bone].rotation_euler
    e.x += math.radians(deg)


def tips_mean():
    pts = []
    for f in FINGERS:
        pb = B[f + "3.R"]
        pts.append(arm.matrix_world @ pb.tail)
    return sum(pts[1:], pts[0]) / len(pts)


def framebbox():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    return [ev.matrix_world @ Vector(c) for c in ev.bound_box]


def render(tag, direction, zoom=1.25):
    pts = framebbox()
    focus = sum(pts, Vector()) / 8.0
    span = max(max(p.x for p in pts) - min(p.x for p in pts),
               max(p.y for p in pts) - min(p.y for p in pts),
               max(p.z for p in pts) - min(p.z for p in pts))
    cam = bpy.data.cameras.new("c" + tag); cam.type = 'ORTHO'
    cam.ortho_scale = span * zoom
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = focus + d * 5
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.join(OUT, tag + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)


def meta_normal():
    iw = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    lw = (arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    return (iw.cross(lw)).normalized()


# --- step 1: open pose render ---
zero_all()
bpy.context.view_layer.update()
t0 = tips_mean()
render("probe_open", meta_normal())

# --- step 2: small positive flex on all MCP/PIP/DIP X ---
zero_all()
for f in FINGERS:
    set_x(f + "1.R", 18)
    set_x(f + "2.R", 20)
    set_x(f + "3.R", 15)
bpy.context.view_layer.update()
t1 = tips_mean()
render("probe_smallflex", meta_normal())

# --- step 3: geometric palm side from curl displacement ---
disp = (t1 - t0)
n_meta = meta_normal()
s = 1.0 if disp.dot(n_meta) > 0 else -1.0
print("PALMSIGN", s, "DISP", tuple(round(v, 4) for v in disp), "NMIN", tuple(round(v, 4) for v in n_meta))
palm = n_meta * s
dorsal = -palm

# --- step 4: full curl seen from both sides ---
zero_all()
for f in FINGERS:
    set_x(f + "1.R", 70)
    set_x(f + "2.R", 80)
    set_x(f + "3.R", 50)
bpy.context.view_layer.update()
render("probe_fullcurl_palmview", palm)
render("probe_fullcurl_dorsalview", dorsal)

print("PROBE DONE")
