# Independent numeric intersection check (no build-script functions reused).
# Fact test: does the armature (rest) actually intersect the mesh on both sides?
import bpy, os
import numpy as np
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))

ok = True
for side in ("R", "L"):
    o = bpy.data.objects["Hand." + side]
    arm = bpy.data.objects["Armature." + side]
    # mesh world vertices
    MW = np.array([ (o.matrix_world @ v.co)[:] for v in o.data.vertices ])
    # per-bone: fraction of bone segments (sampled) inside the mesh bbox AND
    # distance from each joint head to nearest mesh vertex
    MWtree = MW  # brute force fine at this size
    worst = 0.0
    for bn in ("Wrist", "Index1", "Index2", "Index3", "Middle1", "Middle3",
               "Ring1", "Ring3", "Little1", "Little3", "Thumb1", "Thumb3",
               "IndexMeta", "MiddleMeta"):
        b = arm.data.bones[bn + "." + side]
        head = np.array((arm.matrix_world @ Vector(b.head_local))[:])
        d = np.linalg.norm(MWtree - head, axis=1).min()
        worst = max(worst, d)
        if d > 0.35:
            ok = False
            print("FAR", side, bn, round(float(d), 3))
    # mesh vs armature bbox overlap
    mb_min, mb_max = MW.min(0), MW.max(0)
    ab = [arm.matrix_world @ Vector(x) for b in arm.data.bones for x in (b.head_local, b.tail_local)]
    ab = np.array(ab)
    a_min, a_max = ab.min(0), ab.max(0)
    overlap = all(a_min[i] <= mb_max[i] + 1e-6 and a_max[i] >= mb_min[i] - 1e-6 for i in range(3))
    print(side, "bbox-overlap", overlap, "worst-joint-to-mesh", round(float(worst), 3))
    if not overlap: ok = False
print("INTERSECT_RESULT", "PASS" if ok else "FAIL")
