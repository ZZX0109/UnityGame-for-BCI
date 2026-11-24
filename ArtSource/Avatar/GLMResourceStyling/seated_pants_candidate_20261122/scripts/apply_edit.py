"""Create the seated-pants repair candidate.

Edits ONLY the approved +70° primary mask (52 crossingTriangleVertex points)
plus its 28-vertex one-ring buffer on the base mesh of the pants object in the
candidate copy. Topology, vertex groups, modifiers, materials, armature and all
other objects stay untouched. Output is written only inside the isolated
candidate directory.
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
from mathutils import Vector

CAND_DIR = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\GLMResourceStyling\seated_pants_candidate_20261122")
MASK_JSON = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\BuildOutput\Game04-seated-pants-repair-target-20261122\run-04\repair-influence-mask.json")
CAND_BLEND = CAND_DIR / "neutral_adult_seated_pants_candidate.blend"
PANTS_NAME = "Human.cortu_cargo_pants"
BODY_NAME = "Human"

PUSH_METERS = 0.008          # exploration step for primary vertices
BUFFER_SHARE = 0.35          # max relative displacement for one-ring buffer
SMOOTH_ITERS = 6             # Laplacian smoothing iterations inside mask
PROTECT_Y_MIN = -0.14        # protect waistband region (front waist, object space)
PROTECT_Y_MAX = 0.10
PROTECT_Z_MIN = 0.62         # protect crotch seam low region


def load_mask():
    data = json.loads(MASK_JSON.read_text(encoding="utf-8"))
    scenario = data["poseScenarios"][0]
    assert abs(scenario["pose"]["calfDegrees"] - 70.0) < 0.01
    primary, buffer_ids = [], []
    for record in scenario["maskVertexRecords"]:
        if record["role"] == "crossingTriangleVertex":
            primary.append(record)
    for vid in scenario["oneRingBufferVertexIds"]:
        buffer_ids.append(vid)
    return scenario, primary, buffer_ids


def build_adjusted_directions(mesh_obj, mask_records):
    """For each mask vertex compute an outward push direction away from the
    nearest body surface (approximated by the direction from the body's local
    centroid axis through the vertex, in pants object space)."""
    directions = {}
    body_center = Vector((0.0, 0.0, 0.75))  # world approx torso center
    # Convert to pants object space
    mw = mesh_obj.matrix_world
    center_local = mw.inverted() @ body_center
    for record in mask_records:
        vid = record["baseVertexId"]
        co = Vector(record["baseObjectSpaceMeters"])
        # radial direction from body axis (x,y) plus slight up component
        radial = Vector((co.x - center_local.x, co.y - center_local.y, 0.0))
        if radial.length < 1e-6:
            radial = Vector((0.0, 0.0, 1.0))
        radial.normalize()
        directions[vid] = radial
    return directions


def neighbor_map(mesh_data, keep_ids):
    neighbors = {vid: set() for vid in keep_ids}
    for edge in mesh_data.edges:
        a, b = edge.vertices
        if a in neighbors and b in neighbors:
            neighbors[a].add(b)
            neighbors[b].add(a)
    return neighbors


def main():
    scenario, primary_records, buffer_ids = load_mask()
    primary_ids = [r["baseVertexId"] for r in primary_records]
    primary_set = set(primary_ids)
    buffer_set = set(buffer_ids) - primary_set
    print(f"GLM_EDIT mask primary={len(primary_set)} buffer={len(buffer_set)}")

    pants = bpy.data.objects[PANTS_NAME]
    mesh = pants.data
    if len(mesh.vertices) != 211:
        raise RuntimeError(f"unexpected base vertex count {len(mesh.vertices)}")

    before_sha = hashlib.sha256(CAND_BLEND.read_bytes()).hexdigest()

    directions = build_adjusted_directions(pants, primary_records)
    neighbors = neighbor_map(mesh, primary_set | buffer_set)

    # Weight for buffer vertices: 1 - (hop distance / max hop), bounded by BUFFER_SHARE
    weights = {}
    for vid in primary_set:
        weights[vid] = 1.0
    # BFS from primary through buffer vertices (max 2 hops)
    frontier = set(primary_set)
    visited = set(primary_set)
    hop = 0
    while frontier and hop < 2:
        hop += 1
        nxt = set()
        for vid in frontier:
            for nb in neighbors.get(vid, ()):
                if nb not in visited and nb in buffer_set:
                    nxt.add(nb)
        for vid in nxt:
            visited.add(vid)
            weights[vid] = BUFFER_SHARE / hop
        frontier = nxt

    # --- record pre-edit positions ---
    pre = {v.index: tuple(v.co) for v in mesh.vertices}

    # --- apply push ---
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    displacement = {}
    for vid, weight in weights.items():
        vertex = bm.verts[vid]
        direction = directions.get(vid)
        if direction is None:
            continue
        displacement[vid] = direction * (PUSH_METERS * weight)
    for vid, delta in displacement.items():
        bm.verts[vid].co += delta

    # --- Laplacian smoothing restricted to mask (relax to avoid creases) ---
    for _ in range(SMOOTH_ITERS):
        new_pos = {}
        for vid in weights:
            vertex = bm.verts[vid]
            nbrs = [bm.verts[n] for n in neighbors.get(vid, ()) if n in weights]
            if not nbrs:
                continue
            avg = Vector((0.0, 0.0, 0.0))
            for n in nbrs:
                avg += n.co
            avg /= len(nbrs)
            # strong relaxation inside mask keeps silhouette continuous
            new_pos[vid] = vertex.co.lerp(avg, 0.35)
        for vid, pos in new_pos.items():
            bm.verts[vid].co = pos

    # --- hard protection: waistband & crotch seam must not move ---
    protected = 0
    for vid in weights:
        co = bm.verts[vid].co
        if co.z < PROTECT_Z_MIN or not (PROTECT_Y_MIN <= co.y <= PROTECT_Y_MAX):
            original = Vector(pre[vid])
            if (co - original).length > 1e-9:
                protected += 1
            bm.verts[vid].co = original
    bm.to_mesh(mesh)
    bm.free()
    print(f"GLM_EDIT protected_reverted={protected}")

    moved = sum((Vector(tuple(v.co)) - Vector(pre[v.index])).length for v in mesh.vertices)
    print(f"GLM_EDIT total_movement={moved:.6f} m")

    # --- save candidate only into isolated directory ---
    bpy.ops.wm.save_as_mainfile(filepath=str(CAND_BLEND))
    after_sha = hashlib.sha256(CAND_BLEND.read_bytes()).hexdigest()
    print("GLM_EDIT sha_before=" + before_sha)
    print("GLM_EDIT sha_after=" + after_sha)
    print("GLM_EDIT saved=" + str(CAND_BLEND))

    manifest = {
        "status": "CANDIDATE_EDITED_TOPOLOGY_PRESERVED",
        "primaryMaskIds": sorted(primary_set),
        "bufferMaskIds": sorted(buffer_set),
        "pushMeters": PUSH_METERS,
        "bufferShare": BUFFER_SHARE,
        "smoothIters": SMOOTH_ITERS,
        "protectedZmin": PROTECT_Z_MIN,
        "shaBeforeEdit": before_sha,
        "shaAfterEdit": after_sha,
        "baseVertexCount": len(mesh.vertices),
        "basePolygonCount": len(mesh.polygons),
    }
    (CAND_DIR / "edit-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


try:
    main()
except Exception:
    traceback.print_exc()
    sys.exit(1)
