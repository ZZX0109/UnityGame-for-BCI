# IK-style numeric solve for pinch contact (thumb pad meets index pad).
import bpy, os, math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]

BONES = ["Thumb1.R", "Thumb2.R", "Thumb3.R", "Index1.R", "Index2.R"]
REST = {"Thumb1.R": (30, 10, 20), "Thumb2.R": (95, 0, 0), "Thumb3.R": (70, 0, 0),
        "Index1.R": (45, 0, 0), "Index2.R": (38, 0, 0)}


def set_pose(x):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    # x = [T1x, T1y, T2x, T3x, I2x]; other components zero
    layout = {"Thumb1.R": (x[0], x[1], 20), "Thumb2.R": (x[2], 0, 0),
              "Thumb3.R": (x[3], 0, 0), "Index1.R": (REST["Index1.R"][0], 0, 0),
              "Index2.R": (x[4], 0, 0)}
    for n, a in layout.items():
        pb = arm.pose.bones[n]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(a[0]), math.radians(a[1]), math.radians(a[2]))
    bpy.context.view_layer.update()
    return bpy.context.evaluated_depsgraph_get()


def tip_gap(deps):
    itip = arm.matrix_world @ Vector(arm.pose.bones["Index3.R"].tail)
    ttip = arm.matrix_world @ Vector(arm.pose.bones["Thumb3.R"].tail)
    return (itip - ttip).length


# coordinate descent on 5 params: T1x, T1y(twist), T2x, T3x, I2x
x = [30, 10, 95, 70, 38]
step = [10, 15, 12, 10, 8]
deps = set_pose(x)
best = tip_gap(deps)
for it in range(6):
    improved = False
    for pi in range(5):
        for delta in (step[pi], -step[pi]):
            xr = x[:]
            xr[pi] += delta
            xr[pi] = max(-120, min(120, xr[pi]))
            deps = set_pose(xr)
            g = tip_gap(deps)
            if g < best - 1e-4:
                best = g
                x = xr
                improved = True
    print("iter", it, "gap", round(best, 3), "x", x)
    if not improved:
        step = [s / 2 for s in step]
        if max(step) < 1:
            break
print("SOLVED gap", round(best, 3), "angles", x)
print("IK DONE")
