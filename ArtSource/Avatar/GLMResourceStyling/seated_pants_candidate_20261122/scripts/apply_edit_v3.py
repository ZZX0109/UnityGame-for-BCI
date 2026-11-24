"""Candidate edit v3: LBS-inverse separation vectors.

At the seated pose, compute per-vertex required world separation, then map it
back to base-object space through the vertex's blended skinning matrix
(M = sum_g w_g * D_g, D_g = pose_g @ rest_g^-1 linear block), so the push
survives the armature rotation exactly. Three correction iterations; each
re-evaluates the posed clearance. Mask-only, topology preserved.
"""
from __future__ import annotations

import hashlib
import json
import math
import sys
import traceback
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

CAND_DIR = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\GLMResourceStyling\seated_pants_candidate_20261122")
MASK_JSON = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\BuildOutput\Game04-seated-pants-repair-target-20261122\run-04\repair-influence-mask.json")
CAND_BLEND = CAND_DIR / "neutral_adult_seated_pants_candidate.blend"
PANTS_NAME = "Human.cortu_cargo_pants"
BODY_NAME = "Human"

ROOT_DROP_M = 0.35
THIGH, CALF = -80.0, 70.0
TARGET_GAP = 0.006
BUFFER_GAP_SCALE = 0.5
ITERS = 3
SMOOTH_ITERS = 4
SMOOTH_FACTOR = 0.30
PROTECT_Z_MIN = 0.62


def set_pose(armature, thigh_deg, calf_deg):
    sagittal = Vector((1.0, 0.0, 0.0))
    for side in ("l", "r"):
        for name, degrees in ((f"thigh_{side}", thigh_deg), (f"calf_{side}", calf_deg)):
            pose = armature.pose.bones[name]
            local = pose.bone.matrix_local.to_3x3().transposed() @ sagittal
            local.normalize()
            pose.rotation_mode = "QUATERNION"
            pose.rotation_quaternion = Quaternion(local, math.radians(degrees))


def skinning_matrix(pants, vertex_index):
    """Blended 3x3 deformation matrix for a base vertex (LBS linear block)."""
    mesh = pants.data
    result = np.zeros((3, 3))
    total_w = 0.0
    for vgroup in mesh.vertices[vertex_index].groups:
        w = vgroup.weight
        if w <= 1e-6:
            continue
        name = pants.vertex_groups[vgroup.group].name
        pb = bpy.data.objects["Human.rig"].pose.bones.get(name)
        bone = bpy.data.objects["Human.rig"].data.bones.get(name)
        if pb is None or bone is None:
            continue
        deform = pb.matrix @ bone.matrix_local.inverted()
        result += w * np.array(deform.to_3x3())
        total_w += w
    if total_w <= 1e-6:
        return np.eye(3)
    return result / total_w


def main():
    data = json.loads(MASK_JSON.read_text(encoding="utf-8"))
    scenario = data["poseScenarios"][0]
    assert abs(scenario["pose"]["calfDegrees"] - 70.0) < 0.01

    primary_ids = sorted({r["baseVertexId"] for r in scenario["maskVertexRecords"]
                          if r["role"] == "crossingTriangleVertex"})
    buffer_ids = sorted(set(scenario["oneRingBufferVertexIds"]) - set(primary_ids))
    print(f"GLM_EDIT3 primary={len(primary_ids)} buffer={len(buffer_ids)}")

    pants = bpy.data.objects[PANTS_NAME]
    body = bpy.data.objects[BODY_NAME]
    mesh = pants.data
    if len(mesh.vertices) != 211:
        raise RuntimeError(f"unexpected base vertex count {len(mesh.vertices)}")
    before_sha = hashlib.sha256(CAND_BLEND.read_bytes()).hexdigest()

    armature = bpy.data.objects["Human.rig"]
    names = [f"{kind}_{side}" for kind in ("thigh", "calf") for side in ("l", "r")]
    saved_q = {n: armature.pose.bones[n].rotation_quaternion.copy() for n in names}
    saved_loc = armature.location.copy()

    # neighbor map + hop weights
    all_mask = set(primary_ids) | set(buffer_ids)
    neighbors = {i: set() for i in all_mask}
    for edge in mesh.edges:
        a, b = edge.vertices
        if a in neighbors and b in neighbors:
            neighbors[a].add(b)
            neighbors[b].add(a)
    weights = {vid: 1.0 for vid in primary_ids}
    frontier, visited = set(primary_ids), set(primary_ids)
    hop = 0
    while frontier and hop < 2:
        hop += 1
        nxt = set()
        for vid in frontier:
            for nb in neighbors.get(vid, ()):
                if nb not in visited and nb in buffer_ids:
                    nxt.add(nb)
        for vid in nxt:
            visited.add(vid)
            weights[vid] = BUFFER_GAP_SCALE ** hop
        frontier = nxt

    # exclude protected vertices (crotch seam) from editing entirely
    protected_excluded = [vid for vid in weights if mesh.vertices[vid].co.z < PROTECT_Z_MIN]
    for vid in protected_excluded:
        del weights[vid]
    print(f"GLM_EDIT3 protected_excluded={len(protected_excluded)}")

    skin = {vid: skinning_matrix(pants, vid) for vid in weights}
    mw = pants.matrix_world
    mw_inv = mw.inverted()

    try:
        armature.location.z -= ROOT_DROP_M
        set_pose(armature, THIGH, CALF)
        bpy.context.view_layer.update()

        depsgraph = bpy.context.evaluated_depsgraph_get()
        body_eval = body.evaluated_get(depsgraph)
        body_mesh = body_eval.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        body_verts = [body.matrix_world @ v.co for v in body_mesh.vertices]
        body_mesh.calc_loop_triangles()
        body_tris = [tuple(t.vertices) for t in body_mesh.loop_triangles]
        body_tree = BVHTree.FromPolygons(body_verts, body_tris, epsilon=0.0, all_triangles=True)
        body_eval.to_mesh_clear()

        for it in range(ITERS):
            pants_eval = pants.evaluated_get(depsgraph)
            pm = pants_eval.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
            posed_world = [mw @ v.co for v in pm.vertices]  # index == base id
            pants_eval.to_mesh_clear()

            deltas = {}
            stats = {"inside": 0, "tooclose": 0, "ok": 0}
            for vid, w in weights.items():
                pos = posed_world[vid]
                hit = body_tree.find_nearest(pos, 1.0)
                if hit is None or hit[0] is None:
                    continue
                closest, normal, _idx, dist = hit
                side = (pos - closest).dot(normal)
                clearance = dist if side >= 0 else -dist
                gap_target = TARGET_GAP * w
                if clearance >= gap_target:
                    stats["ok"] += 1
                    continue
                stats["inside" if clearance < 0 else "tooclose"] += 1
                push_world = normal * (gap_target - clearance)
                delta_posted_obj = (mw_inv.to_3x3() @ push_world)
                m = skin[vid]
                delta_base = np.linalg.solve(m, np.array(delta_posted_obj))
                deltas[vid] = Vector(delta_base.tolist())
            print(f"GLM_EDIT3 iter={it} stats={json.dumps(stats)}")

            if not deltas:
                break
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bm.verts.ensure_lookup_table()
            for vid, delta in deltas.items():
                bm.verts[vid].co += delta
            # light relaxation within mask to avoid creases
            for _s in range(SMOOTH_ITERS):
                new_pos = {}
                for vid in weights:
                    nbrs = [bm.verts[n] for n in neighbors.get(vid, ()) if n in weights]
                    if not nbrs:
                        continue
                    avg = Vector((0.0, 0.0, 0.0))
                    for n in nbrs:
                        avg += n.co
                    avg /= len(nbrs)
                    new_pos[vid] = bm.verts[vid].co.lerp(avg, SMOOTH_FACTOR)
                for vid, p in new_pos.items():
                    bm.verts[vid].co = p
            bm.to_mesh(mesh)
            bm.free()
            bpy.context.view_layer.update()

        # diagnostic: any remaining verts below guard line
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        protected = 0
        for vid in weights:
            if bm.verts[vid].co.z < PROTECT_Z_MIN:
                protected += 1
        bm.to_mesh(mesh)
        bm.free()
        print(f"GLM_EDIT3 protected_below_guard={protected}")
    finally:
        armature.location = saved_loc
        for n, q in saved_q.items():
            armature.pose.bones[n].rotation_quaternion = q
        bpy.context.view_layer.update()

    bpy.ops.wm.save_as_mainfile(filepath=str(CAND_BLEND))
    after_sha = hashlib.sha256(CAND_BLEND.read_bytes()).hexdigest()
    print("GLM_EDIT3 sha_before=" + before_sha)
    print("GLM_EDIT3 sha_after=" + after_sha)

    manifest = {
        "status": "CANDIDATE_V3_LBS_INVERSE_SEPARATION",
        "method": "posed-space separation along body normal mapped to base space via per-vertex blended skinning matrix inverse; 3 iterations; mask-only relaxation",
        "primaryIds": primary_ids,
        "bufferIds": buffer_ids,
        "targetGapMeters": TARGET_GAP,
        "bufferGapScale": BUFFER_GAP_SCALE,
        "iterations": ITERS,
        "shaBeforeEdit": before_sha,
        "shaAfterEdit": after_sha,
    }
    (CAND_DIR / "edit-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


try:
    main()
except Exception:
    traceback.print_exc()
    sys.exit(1)
