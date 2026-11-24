"""GLM candidate task: baseline crossing re-measurement on the FROZEN source.

Read-only run: applies the two seated poses in memory, never saves the blend.
Reproduces the run-04 BVH + triangle-SAT method to re-confirm the 284/342
event baselines and add render-equivalent (subsurf level 1) numbers.
"""
from __future__ import annotations

import hashlib
import json
import math
import traceback
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

CAND_DIR = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\GLMResourceStyling\seated_pants_candidate_20261122")
SOURCE = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\generated\neutral_adult_rev2_20261120\neutral_adult_avatar.blend")
EXPECTED_SHA256 = "5b78b94ce0a6a0fe3122d47659a5a3e9604597fa4f8d2219e3c019e9ad5d34e0"
PAIR_NAMES = (
    ("Human", "Human.cortu_cargo_pants", "body_pants"),
    ("Human.toigo_fisherman_sweater", "Human.cortu_cargo_pants", "sweater_pants"),
)
EPS = 1e-6
ROOT_DROP_M = 0.35
POSES = ((-80.0, 70.0), (-80.0, 80.0))

BODY_NAME = "Human"
PANTS_NAME = "Human.cortu_cargo_pants"


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def evaluated_mesh(name, depsgraph):
    obj = bpy.data.objects.get(name)
    if obj is None or obj.type != "MESH":
        raise RuntimeError(f"expected mesh object missing: {name}")
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
    pa = [point.dot(axis) for point in a]
    pb = [point.dot(axis) for point in b]
    return max(pa) < min(pb) - EPS or max(pb) < min(pa) - EPS


def triangles_intersect(a, b):
    ea = [a[1] - a[0], a[2] - a[1], a[0] - a[2]]
    eb = [b[1] - b[0], b[2] - b[1], b[0] - b[2]]
    na = ea[0].cross(ea[1])
    nb = eb[0].cross(eb[1])
    axes = [na, nb]
    axes.extend(edge.cross(na) for edge in ea)
    axes.extend(edge.cross(nb) for edge in eb)
    axes.extend(x.cross(y) for x in ea for y in eb)
    return not any(axis_separates(axis, a, b) for axis in axes)


def plane_distances(triangle, points):
    normal = (triangle[1] - triangle[0]).cross(triangle[2] - triangle[0])
    if normal.length_squared < 1e-18:
        return [0.0] * len(points)
    normal.normalize()
    return [(point - triangle[0]).dot(normal) for point in points]


def analyze_pair(name_a, name_b, band):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    a_obj, a_eval, a_mesh, av, at = evaluated_mesh(name_a, depsgraph)
    b_obj, b_eval, b_mesh, bv, bt = evaluated_mesh(name_b, depsgraph)
    try:
        a_tree = BVHTree.FromPolygons(av, at, epsilon=0.0, all_triangles=True)
        b_tree = BVHTree.FromPolygons(bv, bt, epsilon=0.0, all_triangles=True)
        broadphase = a_tree.overlap(b_tree)
        counts = {"crossing": 0, "touch_or_coplanar": 0, "separated_sat": 0}
        pairs = []
        unique_a, unique_b = set(), set()
        for ai, bi in broadphase:
            tri_a = [av[index] for index in at[ai]]
            tri_b = [bv[index] for index in bt[bi]]
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
            if band[0] <= mean[2] <= band[1]:
                unique_a.add(ai)
                unique_b.add(bi)
                pairs.append({"triangleA": ai, "triangleB": bi,
                              "meanWorldXYZ_m": [round(v, 6) for v in mean]})
        return {
            "meshA": {"name": a_obj.name, "vertices": len(av), "triangles": len(at)},
            "meshB": {"name": b_obj.name, "vertices": len(bv), "triangles": len(bt)},
            "bvhBroadphasePairs": len(broadphase),
            "satClassificationCounts": counts,
            "crossingPairCountInBand": len(pairs),
            "uniqueTrianglesAInBand": len(unique_a),
            "uniqueTrianglesBInBand": len(unique_b),
            "crossingPairsInBand": pairs,
        }
    finally:
        a_eval.to_mesh_clear()
        b_eval.to_mesh_clear()


def set_pose(armature, thigh_deg, calf_deg):
    sagittal_axis = Vector((1.0, 0.0, 0.0))
    for side in ("l", "r"):
        for name, degrees in ((f"thigh_{side}", thigh_deg), (f"calf_{side}", calf_deg)):
            pose = armature.pose.bones[name]
            local_axis = pose.bone.matrix_local.to_3x3().transposed() @ sagittal_axis
            local_axis.normalize()
            pose.rotation_mode = "QUATERNION"
            pose.rotation_quaternion = Quaternion(local_axis, math.radians(degrees))


def main():
    if Path(bpy.data.filepath).resolve() != SOURCE.resolve():
        raise RuntimeError(f"unexpected loaded source: {bpy.data.filepath}")
    before = sha256(SOURCE)
    if before != EXPECTED_SHA256:
        raise RuntimeError(f"frozen input SHA mismatch: {before}")
    armature = bpy.data.objects.get("Human.rig")
    if armature is None:
        raise RuntimeError("Human.rig missing")
    pants = bpy.data.objects[PANTS_NAME]
    sweater = bpy.data.objects["Human.toigo_fisherman_sweater"]
    all_mods = list(pants.modifiers) + list(sweater.modifiers)
    names = [f"{kind}_{side}" for kind in ("thigh", "calf") for side in ("l", "r")]
    baseline_pose = {n: (armature.pose.bones[n].rotation_mode,
                         armature.pose.bones[n].rotation_quaternion.copy())
                     for n in names}
    baseline_loc = armature.location.copy()

    out = {"context": bpy.app.version_string, "scenarios": {}}
    try:
        # Render-equivalent evaluation: raise viewport subsurf to render level 1
        # for the pants (and sweater) so the evaluated topology matches RENDER.
        for mod in all_mods:
            if mod.type == "SUBSURF":
                mod.levels = mod.render_levels

        for thigh, calf in POSES:
            key = f"thigh{int(thigh)}_calf{int(calf)}"
            armature.location = baseline_loc.copy()
            armature.location.z -= ROOT_DROP_M
            set_pose(armature, thigh, calf)
            bpy.context.view_layer.update()
            bands = {"body_pants": [0.0, 1.5], "sweater_pants": [0.47, 0.77]}
            out["scenarios"][key] = {}
            for name_a, name_b, label in PAIR_NAMES:
                detail = analyze_pair(name_a, name_b, bands[label])
                detail.pop("crossingPairsInBand")
                out["scenarios"][key][label] = detail
    finally:
        # Read-only pass: nothing is saved, so in-memory modifier/pose state
        # does not need restoring; only guard against accidental writes.
        armature.location = baseline_loc
        for n, (mode, quat) in baseline_pose.items():
            pose = armature.pose.bones[n]
            pose.rotation_mode = mode
            pose.rotation_quaternion = quat
        bpy.context.view_layer.update()

    after = sha256(SOURCE)
    if after != EXPECTED_SHA256:
        raise RuntimeError(f"frozen input changed: {after}")
    out["sourceSha256"] = after
    out["savedBlend"] = False
    out["evalLevels"] = "SUBSURF viewport raised to render level 1 (render-equivalent topology); FabricClearance active; same BVH+SAT as run-04"
    target = CAND_DIR / "evidence"
    target.mkdir(parents=True, exist_ok=True)
    (target / "baseline_render_equivalent.json").write_text(
        json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary = {k: {lbl: v["crossingPairCountInBand"] for lbl, v in sc.items()}
               for k, sc in out["scenarios"].items()}
    print("GLM_BASELINE " + json.dumps(summary, sort_keys=True))


try:
    main()
except Exception:
    traceback.print_exc()
    raise
