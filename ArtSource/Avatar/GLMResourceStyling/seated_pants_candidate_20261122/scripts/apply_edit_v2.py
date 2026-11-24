"""Candidate edit v2: separation-vector method.

For each approved mask vertex (primary + one-ring), evaluated in the seated
pose (thigh -80°, calf +70°, root drop), find the closest point on the body
surface. If the vertex lies inside the body surface or closer than
TARGET_GAP, push it outward along the body-surface normal so it reaches
TARGET_GAP clearance. One-ring buffer vertices get a weight-scaled target so
the edit feathers out. Laplacian relaxation inside the mask prevents creases.
Waistband/crotch-seam protection reverts protected verts exactly.

Base mesh topology, vertex groups, modifiers and materials are untouched.
Pose is restored before the candidate blend is saved.
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
TARGET_GAP = 0.006          # desired clearance outside body surface (m)
BUFFER_GAP_SCALE = 0.5      # buffer verts target = TARGET_GAP * scale * weight
SMOOTH_ITERS = 4
SMOOTH_FACTOR = 0.30
PROTECT_Z_MIN = 0.62        # object-space guards (waistband / crotch seam)


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
    scenario = data["poseScenarios"][0]
    assert abs(scenario["pose"]["calfDegrees"] - 70.0) < 0.01

    primary_ids = sorted({r["baseVertexId"] for r in scenario["maskVertexRecords"]
                          if r["role"] == "crossingTriangleVertex"})
    buffer_ids = sorted(set(scenario["oneRingBufferVertexIds"]) - set(primary_ids))
    print(f"GLM_EDIT2 primary={len(primary_ids)} buffer={len(buffer_ids)}")

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

    try:
        armature.location.z -= ROOT_DROP_M
        set_pose(armature, THIGH, CALF)
        bpy.context.view_layer.update()

        depsgraph = bpy.context.evaluated_depsgraph_get()
        # Body evaluated surface for closest-point queries
        body_eval = body.evaluated_get(depsgraph)
        body_mesh = body_eval.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        body_verts = [body.matrix_world @ v.co for v in body_mesh.vertices]
        body_mesh.calc_loop_triangles()
        body_tris = [tuple(t.vertices) for t in body_mesh.loop_triangles]
        body_tree = BVHTree.FromPolygons(body_verts, body_tris, epsilon=0.0, all_triangles=True)
        body_eval.to_mesh_clear()

        # Pants evaluated (viewport) surface: base↔evaluated identity validated
        pants_eval = pants.evaluated_get(depsgraph)
        pm = pants_eval.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        posed_world = [pants.matrix_world @ v.co for v in pm.vertices]  # index == base id
        pants_eval.to_mesh_clear()

        # neighbor map from base mesh edges
        neighbors = {i: set() for i in set(primary_ids) | set(buffer_ids)}
        for edge in mesh.edges:
            a, b = edge.vertices
            if a in neighbors and b in neighbors:
                neighbors[a].add(b)
                neighbors[b].add(a)

        # BFS hop weights: primary=1.0, buffer decays by hop
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
                weights[vid] = (BUFFER_GAP_SCALE ** hop)
            frontier = nxt

        # compute per-vertex world-space push
        mw_inv = pants.matrix_world.inverted()
        deltas_obj = {}
        stats = {"inside": 0, "tooclose": 0, "ok": 0}
        for vid, w in weights.items():
            pos = posed_world[vid]
            hit = body_tree.find_nearest(pos, 1.0)
            if hit is None or hit[0] is None:
                continue
            closest, normal, _idx, dist = hit
            # signed side: positive if pos is on the normal side (outside)
            side = (pos - closest).dot(normal)
            clearance = dist if side >= 0 else -dist
            gap_target = TARGET_GAP * w
            if clearance >= gap_target:
                stats["ok"] += 1
                continue
            if clearance < 0:
                stats["inside"] += 1
            else:
                stats["tooclose"] += 1
            push_world = normal * (gap_target - clearance)
            deltas_obj[vid] = mw_inv.to_3x3() @ push_world

        print(f"GLM_EDIT2 stats={json.dumps(stats)} moved={len(deltas_obj)}")

        # apply via bmesh
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        pre = {vid: bm.verts[vid].co.copy() for vid in weights}
        for vid, delta in deltas_obj.items():
            bm.verts[vid].co += delta

        # Laplacian relaxation inside mask
        for _ in range(SMOOTH_ITERS):
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

        # hard protection: revert protected verts exactly
        protected = 0
        for vid in weights:
            co = bm.verts[vid].co
            if co.z < PROTECT_Z_MIN:
                if (co - pre[vid]).length > 1e-9:
                    protected += 1
                bm.verts[vid].co = pre[vid]
        bm.to_mesh(mesh)
        bm.free()
        print(f"GLM_EDIT2 protected_reverted={protected}")

        total_move = sum((Vector(tuple(v.co)) - pre.get(v.index, Vector(tuple(v.co)))).length
                         for v in mesh.vertices if v.index in weights)
        print(f"GLM_EDIT2 total_mask_movement={total_move:.6f} m")
    finally:
        armature.location = saved_loc
        for n, q in saved_q.items():
            armature.pose.bones[n].rotation_quaternion = q
        bpy.context.view_layer.update()

    bpy.ops.wm.save_as_mainfile(filepath=str(CAND_BLEND))
    after_sha = hashlib.sha256(CAND_BLEND.read_bytes()).hexdigest()
    print("GLM_EDIT2 sha_before=" + before_sha)
    print("GLM_EDIT2 sha_after=" + after_sha)

    manifest = {
        "status": "CANDIDATE_V2_SEPARATION_VECTORS",
        "method": "closest-point-on-body separation along body normal, per-vertex gap target; mask-only Laplacian relaxation; waistband/crotch guard",
        "primaryIds": primary_ids,
        "bufferIds": buffer_ids,
        "targetGapMeters": TARGET_GAP,
        "bufferGapScale": BUFFER_GAP_SCALE,
        "smoothIters": SMOOTH_ITERS,
        "stats": stats,
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
