# -*- coding: utf-8 -*-
"""Conservative salvage of Elena FF GLB: keep the ORIGINAL armature/animation,
delete only source vertices and faces that geometrically cannot be hand skin,
and export a metric FBX. No rebind, no new pose table.
"""
from pathlib import Path
import json
import bpy
import bmesh
from mathutils import Vector, Matrix

HERE = Path(__file__).resolve().parent
OUT = HERE / "output_salvage"
GLB = HERE.parent / "ThirdParty" / "ElenaFF_RiggedHand" / "hand.glb"
OUT.mkdir(parents=True, exist_ok=True)
FPS, FRAME_OPEN, FRAME_CLOSED, FRAME_LAST = 24, 0, 32, 77
HAND_LENGTH_M = 0.19
WRIST = "hand.R_02"
MIDDLE = "middle_03.R_027"


def log(*a):
    print("VH-SALVAGE:", *a, flush=True)


def evaluated_positions(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = ev.to_mesh()
    points = [ev.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    return points


def point_segment_distance(p, a, b):
    d = b - a
    if d.length_squared < 1e-12:
        return (p - a).length
    t = max(0.0, min(1.0, (p - a).dot(d) / d.length_squared))
    return (p - (a + d * t)).length


def cleanup(source, arm, bone_segments, wrist, axis, metric):
    points = evaluated_positions(source)
    if len(points) != len(source.data.vertices):
        raise RuntimeError("Evaluated vertex count differs from source; cannot map deletions")
    forearm_cutoff = -0.065 / metric
    far_limit = 0.42
    max_edge = 0.20
    remove_vertices = set()
    for i, p in enumerate(points):
        nearest = min(point_segment_distance(p, a, b) for a, b in bone_segments)
        if nearest > far_limit or (p - wrist).dot(axis) < forearm_cutoff:
            remove_vertices.add(i)
    remove_faces = set()
    for face in source.data.polygons:
        vs = list(face.vertices)
        if any(i in remove_vertices for i in vs):
            continue
        edge = max((points[vs[j]] - points[vs[(j + 1) % len(vs)]]).length
                   for j in range(len(vs)))
        if edge > max_edge:
            remove_faces.add(face.index)
    bm = bmesh.new()
    bm.from_mesh(source.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    face_objects = [bm.faces[i] for i in remove_faces]
    vert_objects = [bm.verts[i] for i in remove_vertices]
    bmesh.ops.delete(bm, geom=face_objects, context="FACES")
    bmesh.ops.delete(bm, geom=vert_objects, context="VERTS")
    loose = [v for v in bm.verts if not v.link_edges]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(source.data)
    bm.free()
    source.data.update()
    log("removed", len(remove_faces), "long faces and", len(remove_vertices),
        "far/proximal vertices; remaining", len(source.data.vertices))


def studio():
    sc = bpy.context.scene
    w = bpy.data.worlds.new("SalvageWorld")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.58, 0.62, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    sc.world = w
    for name, loc, energy, size in (("Key", (0.3, -0.5, 0.7), 30, 0.4),
                                    ("Fill", (-0.4, 0.3, 0.4), 22, 0.35)):
        d = bpy.data.lights.new(name, "AREA")
        d.energy, d.size = energy, size
        o = bpy.data.objects.new(name, d)
        sc.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (-o.location).to_track_quat("-Z", "Y").to_euler()
    d = bpy.data.cameras.new("SalvageCamera")
    d.type = "ORTHO"
    o = bpy.data.objects.new("SalvageCamera", d)
    sc.collection.objects.link(o)
    sc.camera = o
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960


def render(mesh, tag, direction):
    sc = bpy.context.scene
    pts = evaluated_positions(mesh)
    mn = Vector(tuple(min(p[i] for p in pts) for i in range(3)))
    mx = Vector(tuple(max(p[i] for p in pts) for i in range(3)))
    center, span = (mn + mx) * 0.5, mx - mn
    d = Vector(direction).normalized()
    sc.camera.location = center + d * 2
    sc.camera.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera.data.ortho_scale = max(span) * 1.15
    sc.render.filepath = str(OUT / tag)
    bpy.ops.render.render(write_still=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(GLB))
    sc = bpy.context.scene
    for o in list(sc.objects):
        if o.name == "Icosphere" or o.type in {"LIGHT", "CAMERA"}:
            bpy.data.objects.remove(o, do_unlink=True)
    mesh = next(o for o in sc.objects if o.type == "MESH")
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()

    segments = []
    for name, pb in arm.pose.bones.items():
        if not any(name.startswith(p) for p in ("thumb_", "index_", "middle_", "ring_", "pinky_")):
            continue
        if "_Ctrl" in name or "_end_" in name:
            continue
        a = arm.matrix_world @ pb.head
        b = arm.matrix_world @ pb.tail
        if "_03." in name:
            b = a + (b - a) * 0.30
        segments.append((a, b))
    wrist = arm.matrix_world @ arm.pose.bones[WRIST].head
    middle = arm.matrix_world @ arm.pose.bones[MIDDLE].head
    axis = (middle - wrist).normalized()
    pts = evaluated_positions(mesh)
    source_len = max((p - wrist).dot(axis) for p in pts)
    metric = HAND_LENGTH_M / source_len
    log("source hand length", source_len, "metric", metric)

    cleanup(mesh, arm, segments, wrist, axis, metric)

    # Metric scale and wrist pivot on the existing top-level root; keep native rig intact.
    root = next(o for o in sc.objects if o.name == "Sketchfab_model")
    root.matrix_world = Matrix.Translation(-wrist * metric) @ Matrix.Scale(metric, 4) @ root.matrix_world
    bpy.context.view_layer.update()
    studio()
    for frame, tag in ((FRAME_OPEN, "open_palm"), (FRAME_CLOSED, "closed_palm"),
                       (FRAME_LAST, "reopen_palm")):
        sc.frame_set(frame)
        bpy.context.view_layer.update()
        render(mesh, "Elena_" + tag + ".png", (0, -1, 0.25))
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()

    bpy.ops.object.select_all(action="DESELECT")
    for o in [root] + list(root.children_recursive):
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=str(OUT / "ElenaHand_R.fbx"), use_selection=True,
        apply_unit_scale=True, object_types={"ARMATURE", "MESH", "EMPTY"},
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=False, mesh_smooth_type="OFF", path_mode="ABSOLUTE")
    (OUT / "rig-manifest.json").write_text(json.dumps({
        "method": "delete far/long source faces only; original armature and animation kept",
        "metricScale": metric,
        "license": "CC BY-SA 4.0 — Elena FF; derivative model/animation retain the same license",
    }, indent=2) + "\n", encoding="utf-8")
    log("DONE", OUT)


if __name__ == "__main__":
    main()
