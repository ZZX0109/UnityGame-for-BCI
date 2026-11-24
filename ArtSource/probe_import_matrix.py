import bpy, os
from mathutils import Vector
import numpy as np
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=os.path.abspath("_download/makehuman/base.obj"))
o = bpy.context.selected_objects[0]
M = o.matrix_world
print("IMPORT ROT deg", [round(a * 57.2958, 2) for a in M.to_euler()])
# raw first vertex vs transformed
v0 = o.data.vertices[0].co
w = M @ v0
print("RAW v0", [round(c, 3) for c in v0], "WORLD v0", [round(c, 3) for c in w])
# bounds in both spaces
raw = np.array([list(v.co) for v in o.data.vertices])
print("RAW bounds x", round(raw[:,0].min(),2), round(raw[:,0].max(),2),
      "y", round(raw[:,1].min(),2), round(raw[:,1].max(),2),
      "z", round(raw[:,2].min(),2), round(raw[:,2].max(),2))
# official joint (wrist) raw-obj coords:
j = (-4.321, 2.402, 1.773)
print("JOINT wrist OBJ-space", j)
# nearest raw vertex to joint in raw space:
d = np.linalg.norm(raw - np.array(j), axis=1)
i = int(np.argmin(d))
print("NEAREST raw vert to wrist joint", round(float(d[i]),3), "at", [round(c,3) for c in raw[i]])
