"""Candidate edit v4: rigid crotch-panel let-out, zero smoothing.

All editable mask vertices (primary + one-ring minus protected) receive the
SAME displacement: the LBS-inverse-mapped mean separation vector computed at
the seated pose. A rigid shift preserves the panel's internal shape exactly,
so no new crease or silhouette step can appear inside or at the border of the
mask; the Subsurf modifier smooths the single border step across one ring.
Single pass, no relaxation.
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
    print(f"GLM_EDIT4 primary={len(primary_ids)} buffer={len(buffer_ids)}")

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

    all_mask = set(primary_ids) | set(buffer_ids)
    weights = {vid: 1.0 for vid in primary_ids}
    # one-ring gets slightly reduced influence at the border via subsurf, keep 1.0 here
    for vid in buffer_ids:
        weights[vid] = 1.0
    protected_excluded = [vid for vid in weights if mesh.vertices[vid].co.z < 0.62]
    for vid in protected_excluded:
        del weights[vid]
    print(f"GLM_EDIT4 protected_excluded={len(protected_excluded)} editable={len(weights)}")

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

        pants_eval = pants.evaluated_get(depsgraph)
        pm = pants_eval.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
        posed_world = [mw @ v.co for v in pm.vertices]
        pants_eval.to_mesh_clear()

        # collect separation vectors at pose, then average them
        world_vectors = []
        detail = {"inside": 0, "tooclose": 0, "ok": 0}
        for vid, w in weights.items():
            pos = posed_world[vid]
            hit = body_tree.find_nearest(pos, 1.0)
            if hit is None or hit[0] is None:
                continue
            closest, normal, _idx, dist = hit
            side = (pos - closest).dot(normal)
            clearance = dist if side >= 0 else -dist
            if clearance >= TARGET_GAP * w:
                detail["ok"] += 1
                continue
            detail["inside" if clearance < 0 else "tooclose"] += 1
            world_vectors.append(normal * (TARGET_GAP - clearance))
        if not world_vectors:
            raise RuntimeError("no separation vectors collected")
        mean_world = Vector(np.mean(np.array([tuple(v) for v in world_vectors]), axis=0).tolist())
        print(f"GLM_EDIT4 stats={json.dumps(detail)} mean_world={[round(c, 4) for c in mean_world]}")

        # rigid shift: same base-space delta for every editable mask vertex,
        # derived from the average world vector mapped through each vertex's
        # skin matrix averaged back (use the mean skin mapping to stay rigid)
        mean_skin = np.mean([skin[vid] for vid in weights], axis=0)
        delta_base = np.linalg.solve(mean_skin, np.array(mw_inv.to_3x3() @ mean_world))
        print(f"GLM_EDIT4 delta_base={[round(c, 5) for c in delta_base]}")

        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()
        for vid in weights:
            bm.verts[vid].co += Vector(delta_base.tolist())
        bm.to_mesh(mesh)
        bm.free()
    finally:
        armature.location = saved_loc
        for n, q in saved_q.items():
            armature.pose.bones[n].rotation_quaternion = q
        bpy.context.view_layer.update()

    bpy.ops.wm.save_as_mainfile(filepath=str(CAND_BLEND))
    after_sha = hashlib.sha256(CAND_BLEND.read_bytes()).hexdigest()
    print("GLM_EDIT4 sha_before=" + before_sha)
    print("GLM_EDIT4 sha_after=" + after_sha)

    manifest = {
        "status": "CANDIDATE_V4_RIGID_PANEL_LETOUT",
        "method": "uniform LBS-inverse mean separation vector applied rigidly to all editable mask verts; no smoothing; single pass",
        "primaryIds": primary_ids,
        "bufferIds": buffer_ids,
        "targetGapMeters": TARGET_GAP,
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
