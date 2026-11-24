import bpy, os, math
import numpy as np
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]
me = o.data

V = np.array([list(v.co) for v in me.vertices])

def wsum():
    """deform all vertices with current pose using weights, report extreme offsets"""
    # use depsgraph evaluated mesh vs rest
    return None

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
angles = {"Index1.R": 75, "Index2.R": 85, "Index3.R": 50}
for n, a in angles.items():
    pb = arm.pose.bones[n]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(a), 0, 0)
bpy.context.view_layer.update()

dg = bpy.context.evaluated_depsgraph_get()
ev = o.evaluated_get(dg)
evm = ev.to_mesh()
EV = np.array([list(v.co) for v in evm.vertices])
ev.to_mesh_clear()

rest_world = np.array([ (o.matrix_world @ Vector(v.co))[:] for v in me.vertices ])
disp = np.linalg.norm(EV - rest_world, axis=1)
worst = np.argsort(-disp)[:8]
print("WORST DISPLACEMENTS:")
for i in worst:
    print("  v%d co=%s disp=%.2f" % (i, [round(float(c),2) for c in V[i]], disp[i]))

# fingertip region vertex (nearest to Index3 tail rest world)
tip = np.array([-4.56, 1.58, 3.12])
ti = int(np.argmin(np.linalg.norm(V - tip, axis=1)))
print("TIP rest", [round(float(c),2) for c in V[ti]], "deformed", [round(float(c),2) for c in EV[ti]])
print("PROBE3 DONE")
