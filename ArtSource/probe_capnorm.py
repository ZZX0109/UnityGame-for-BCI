import bpy, os
import numpy as np
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
for side in ("R", "L"):
    o = bpy.data.objects["Hand." + side]
    me = o.data
    ys = np.array([v.co.y for v in me.vertices])
    ymin = ys.min()
    ring_ids = set(i for i, v in enumerate(me.vertices) if v.co.y < ymin + 0.02)
    # cap face normal (world, with object transform ~identity)
    for p in me.polygons:
        if all(vi in ring_ids for vi in p.vertices):
            print(side, "cap normal y", round(float(p.normal.y), 3), "(should point -Y, away from hand)")
            break
