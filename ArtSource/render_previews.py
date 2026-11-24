# Render the three preview stills by applying the *profile sample* 20-values
# (open / fist / pinch) through the same per-channel mapping Unity will use.
# This proves the profile path end-to-end inside Blender before Unity ever runs.
import bpy, os, json, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE = os.path.normpath(os.path.join(
    HERE, "..", "Assets", "VirtualHand", "Resources", "Rig", "hand_rig_profile.json"))
BLEND = os.path.join(HERE, "Hands", "hands_source.blend")

with open(PROFILE, encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]
samples = profile["samples"]

# build-module functions (apply_pose/POSE_ANGLES) for native-path renders
g = {"__file__": os.path.abspath("build_hands.py")}
_src = open("build_hands.py", encoding="utf-8").read()
_src = _src.replace(chr(10) + "main()" + chr(10), chr(10))
exec(compile(_src, "build_hands.py", "exec"), g)
bpy.ops.wm.open_mainfile(filepath=BLEND)
g["pose_actions"](bpy.data.objects["Armature.R"], "R")
g["pose_actions"](bpy.data.objects["Armature.L"], "L")
sc = bpy.context.scene


def apply_sample(values):
    """Apply a 20-value control vector through the profile channel mapping."""
    for side in ("R", "L"):
        arm = bpy.data.objects[f"Armature.{side}"]
        for pb in arm.pose.bones:
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (0, 0, 0)
        for ci, ch in enumerate(channels):
            pb = arm.pose.bones.get(ch["bone"] + "." + side)
            if pb is None:
                continue
            rest = ch.get("rest", 0.0)
            deg = (values[ci] - rest) * ch["scale"] * ch["sign"]
            e = pb.rotation_euler
            comp = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
            e[comp] += math.radians(deg)
            pb.rotation_euler = e
    bpy.context.view_layer.update()


def render(tag, focus, direction, dist):
    cam = bpy.data.cameras.new("c" + tag); cam.lens = 85
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = Vector(focus) + d * dist
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/preview_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("PREVIEW", tag)


# only R hand for close-ups: hide L
bpy.data.objects["Hand.L"].hide_render = True
bpy.data.objects["Armature.L"].hide_render = True

# palm-front (true palm normal is +Z in baked space)
apply_sample(samples["open"])
render("palm_front", (-4.5, -2.0, 2.0), (0, 0.35, 1.0), 8.0)
apply_sample(samples["fist"])
render("fist_front", (-4.5, -2.0, 2.2), (0, 0.45, 1.0), 8.0)
apply_sample(samples["pinch"])
render("pinch_front", (-4.3, -2.0, 2.4), (0.25, 0.55, 0.8), 7.0)
# native-path verification: direct POSE_ANGLES euler, same as build renders
for pose in ("fist", "pinch"):
    g["apply_pose"](bpy.data.objects["Armature.R"], "R", pose)
    render("native_" + pose, (-4.5, -2.0, 2.2), (0.2, 0.5, -0.85), 8.0)
    if pose == "pinch":  # palm-side close-up so the pad contact is visible
        render("native_pinch_palm", (-4.2, -2.0, 2.3), (0.25, 0.5, 0.85), 6.5)
    g["clear_pose"](bpy.data.objects["Armature.R"])
print("THREE PREVIEWS DONE")
