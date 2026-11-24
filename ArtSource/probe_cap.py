import bpy, os
import numpy as np
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
for side in ("R", "L"):
    o = bpy.data.objects["Hand." + side]
    me = o.data
    # find vertices near the wrist-cut plane (forearm end): furthest from index MCP toward elbow
    # the cut ring is the set of verts at min Y (baked: forearm extends -Y)
    ys = np.array([v.co.y for v in me.vertices])
    ymin = ys.min()
    ring_ids = [i for i, v in enumerate(me.vertices) if v.co.y < ymin + 0.02]
    print(side, "cut-ring verts", len(ring_ids), "at y~", round(float(ymin), 2))
    # count faces fully in ring (the cap)
    cap = sum(1 for p in me.polygons if all(vi in ring_ids for vi in p.vertices))
    print(side, "cap faces", cap)
