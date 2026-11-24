"""Measure the edited candidate with the same render-equivalent BVH+SAT method.

Region check: every crossing event's mean world point is compared against the
run-04 recorded posed world positions of the approved mask union (primary +
one-ring) for that pose; events farther than REGION_RADIUS from every mask
point are counted as outside-region.
"""
from __future__ import annotations

import json
import math
import sys
import traceback
from pathlib import Path

import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

CAND_DIR = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\GLMResourceStyling\seated_pants_candidate_20261122")
MASK_JSON = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\BuildOutput\Game04-seated-pants-repair-target-20261122\run-04\repair-influence-mask.json")
PAIR_NAMES = (
    ("Human", "Human.cortu_cargo_pants", "body_pants"),
    ("Human.toigo_fisherman_sweater", "Human.cortu_cargo_pants", "sweater_pants"),
)
EPS = 1e-6
ROOT_DROP_M = 0.35
POSES = ((-80.0, 70.0), (-80.0, 80.0))
REGION_RADIUS = 0.14  # generous radius around approved mask points (m)


def evaluated_mesh(name, depsgraph):
    obj = bpy.data.objects.get(name)
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    vertices = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    mesh.calc_loop_triangles()
    triangles = [tuple(tri.vertices) for tri in mesh.loop_triangles]
    return obj, evaluated, mesh, vertices, triangles


def axis_separates(axis, a, b):
    if axis.length_squared < 1e-18:
        return False
    axis.normalize()
    pa = [p.dot(axis) for p in a]
    pb = [p.dot(axis) for p in b]
    return max(pa) < min(pb) - EPS or max(pb) < min(pa) - EPS


def triangles_intersect(a, b):
    ea = [a[1] - a[0], a[2] - a[1], a[0] - a[2]]
    eb = [b[1] - b[0], b[2] - b[1], b[0] - b[2]]
    na = ea[0].cross(ea[1])
    nb = eb[0].cross(eb[1])
    axes = [na, nb]
    axes.extend(e.cross(na) for e in ea)
    axes.extend(e.cross(nb) for e in eb)
    axes.extend(x.cross(y) for x in ea for y in eb)
    return not any(axis_separates(axis, a, b) for axis in axes)


def plane_distances(triangle, points):
    normal = (triangle[1] - triangle[0]).cross(triangle[2] - triangle[0])
    if normal.length_squared < 1e-18:
        return [0.0] * len(points)
    normal.normalize()
    return [(p - triangle[0]).dot(normal) for p in points]


def analyze_pair(name_a, name_b, band, mask_arr):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    a_obj, a_eval, a_mesh, av, at = evaluated_mesh(name_a, depsgraph)
    b_obj, b_eval, b_mesh, bv, bt = evaluated_mesh(name_b, depsgraph)
    try:
        a_tree = BVHTree.FromPolygons(av, at, epsilon=0.0, all_triangles=True)
        b_tree = BVHTree.FromPolygons(bv, bt, epsilon=0.0, all_triangles=True)
        broadphase = a_tree.overlap(b_tree)
        counts = {"crossing": 0, "touch_or_coplanar": 0, "separated_sat": 0}
        events, outside = [], 0
        for ai, bi in broadphase:
            tri_a = [av[i] for i in at[ai]]
            tri_b = [bv[i] for i in bt[bi]]
            if not triangles_intersect(tri_a, tri_b):
                counts["separated_sat"] += 1
                continue
            da = plane_distances(tri_a, tri_b)
            db = plane_distances(tri_b, tri_a)
            crossing = min(da) < -EPS and max(da) > EPS and min(db) < -EPS and max(db) > EPS
            counts["crossing" if crossing else "touch_or_coplanar"] += 1
            if not crossing:
                continue
            ca = [sum(p[k] for p in tri_a) / 3 for k in range(3)]
            cb = [sum(p[k] for p in tri_b) / 3 for k in range(3)]
            mean = [(ca[k] + cb[k]) / 2 for k in range(3)]
            if not (band[0] <= mean[2] <= band[1]):
                continue
            is_out = True
            if mask_arr is not None and len(mask_arr):
                dists = np.linalg.norm(mask_arr - np.array(mean), axis=1)
                is_out = bool(dists.min() > REGION_RADIUS)
            if is_out:
                outside += 1
            events.append({"tA": ai, "tB": bi,
                           "mean": [round(v, 5) for v in mean],
                           "outsideRegion": is_out})
        return {
            "meshA": {"vertices": len(av), "triangles": len(at)},
            "meshB": {"vertices": len(bv), "triangles": len(bt)},
            "broadphase": len(broadphase),
            "counts": counts,
            "crossingPairCountInBand": len(events),
            "outsideRegionCount": outside,
            "events": events,
        }
    finally:
        a_eval.to_mesh_clear()
        b_eval.to_mesh_clear()


def set_pose(armature, thigh_deg, calf_deg):
    sagittal = Vector((1.0, 0.0, 0.0))
    for side in ("l", "r"):
        for name, degrees in ((f"thigh_{side}", thigh_deg), (f"calf_{side}", calf_deg)):
            pose = armature.pose.bones[name]
            local = pose.bone.matrix_local.to_3x3().transposed() @ sagittal
            local.normalize()
            pose.rotation_mode = "QUATERNION"
            pose.rotation_quaternion = Quaternion(local, math.radians(degrees))


def main():
    data = json.loads(MASK_JSON.read_text(encoding="utf-8"))
    armature = bpy.data.objects["Human.rig"]
    baseline_loc = armature.location.copy()

    # mask union posed world points per pose (from run-04 records)
    mask_by_calf = {}
    for sc in data["poseScenarios"]:
        pts = [r["posedWorldMeters"] for r in sc["maskVertexRecords"]]
        mask_by_calf[int(sc["pose"]["calfDegrees"])] = np.array(pts, dtype=float)

    out = {"context": bpy.app.version_string, "scenarios": {},
           "regionRadiusMeters": REGION_RADIUS,
           "regionSource": "run-04 repair-influence-mask.json posedWorldMeters (mask union per pose)"}
    for thigh, calf in POSES:
        key = f"thigh{int(thigh)}_calf{int(calf)}"
        armature.location = baseline_loc.copy()
        armature.location.z -= ROOT_DROP_M
        set_pose(armature, thigh, calf)
        bpy.context.view_layer.update()
        mask_arr = mask_by_calf[int(calf)]
        bands = {"body_pants": [0.0, 1.5], "sweater_pants": [0.47, 0.77]}
        out["scenarios"][key] = {}
        for name_a, name_b, label in PAIR_NAMES:
            detail = analyze_pair(name_a, name_b, bands[label], mask_arr)
            events = detail.pop("events")
            detail["uniqueTrianglesA"] = len({e["tA"] for e in events})
            detail["uniqueTrianglesB"] = len({e["tB"] for e in events})
            out["scenarios"][key][label] = detail
            evdir = CAND_DIR / "events" / key
            evdir.mkdir(parents=True, exist_ok=True)
            (evdir / f"{label}.json").write_text(
                json.dumps(events, ensure_ascii=False, indent=1), encoding="utf-8")
        armature.location = baseline_loc.copy()

    (CAND_DIR / "evidence" / "candidate_render_equivalent.json").write_text(
        json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary = {k: {lbl: {"events": v["crossingPairCountInBand"],
                         "outsideRegion": v["outsideRegionCount"]}
                   for lbl, v in sc.items()}
               for k, sc in out["scenarios"].items()}
    print("GLM_CAND " + json.dumps(summary, sort_keys=True))


try:
    main()
except Exception:
    traceback.print_exc()
    sys.exit(1)
