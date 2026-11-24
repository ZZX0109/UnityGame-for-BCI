# -*- coding: utf-8 -*-
"""Build clean metric Elena FF hands using the artist's OPEN pose as bind pose.

The source GLB's literal armature REST does not match its visible mesh in Blender
5.2. This pipeline preserves the visible open-pose skin, rebinds it to an
open-pose skeleton, and transfers only native relative bone rotations. It crops
the long proximal arm to a short forearm, creates a true mirrored left hand, and
exports native Open/Close clips. Source and derivative assets remain CC BY-SA 4.0.
"""
from pathlib import Path
import json
import math
import bpy
import bmesh
from mathutils import Matrix, Vector, Quaternion

HERE = Path(__file__).resolve().parent
OUT = HERE / "output_openbind"
GLB = HERE.parent / "ThirdParty" / "ElenaFF_RiggedHand" / "hand.glb"
OUT.mkdir(parents=True, exist_ok=True)
FPS = 24
FRAME_OPEN, FRAME_CLOSED, FRAME_LAST = 0, 32, 77
HAND_LENGTH_M = 0.19
FOREARM_LENGTH_M = 0.065
WRIST = "hand.R_02"
MIDDLE_TIP = "middle_03.R_027"


def log(*args):
    print("VH-OPENBIND:", *args, flush=True)


def tri_count(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def world_points(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    me = ev.to_mesh()
    pts = [ev.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    return pts


def local_pose(pb):
    if pb.parent:
        return (pb.parent.matrix.inverted() @ pb.matrix).to_quaternion()
    return pb.matrix.to_quaternion()


def create_open_mesh(source, source_arm):
    """Evaluate the visible open skin into world space and retain UV/weights."""
    deps = bpy.context.evaluated_depsgraph_get()
    ev = source.evaluated_get(deps)
    data = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True,
                                           depsgraph=deps)
    data.name = "ElenaFF.OpenSkin"
    obj = bpy.data.objects.new("ElenaSkin.R", data)
    bpy.context.scene.collection.objects.link(obj)
    obj.matrix_world = Matrix.Identity(4)
    for v in data.vertices:
        v.co = ev.matrix_world @ v.co
    for group in source.vertex_groups:
        obj.vertex_groups.new(name=group.name)
    if len(data.vertices) != len(source.data.vertices):
        raise RuntimeError("Evaluated vertex order/count changed; cannot transfer weights safely")
    # new_from_object normally retains weights; explicitly copy by stable index.
    for i, sv in enumerate(source.data.vertices):
        for g in sv.groups:
            name = source.vertex_groups[g.group].name
            obj.vertex_groups[name].add([i], g.weight, "REPLACE")
    return obj


def point_segment_distance(point, head, tail):
    direction = tail - head
    length_sq = direction.length_squared
    if length_sq < 1e-12:
        return (point - head).length
    t = max(0.0, min(1.0, (point - head).dot(direction) / length_sq))
    return (point - (head + direction * t)).length


def remove_geometry_far_from_finger_bones(obj, source_arm, radius):
    """Discard source geometry far outside the actual finger-bone envelope.

    The asset is one connected component, so this must be a geometric cleanup,
    not a component delete. End bones extend beyond the visible fingertips in
    the source; terminal segments are therefore shortened to 30%.
    """
    prefixes = ("thumb_", "index_", "middle_", "ring_", "pinky_")
    segments = []
    for name, pb in source_arm.pose.bones.items():
        if not name.startswith(prefixes) or "_Ctrl" in name:
            continue
        head = source_arm.matrix_world @ pb.head
        tail = source_arm.matrix_world @ pb.tail
        if any(token in name for token in ("_03.", "_end_")):
            tail = head + (tail - head) * 0.30
        segments.append((head, tail))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    remove = []
    for vertex in bm.verts:
        nearest = min(point_segment_distance(vertex.co, a, b)
                      for a, b in segments)
        if nearest > radius:
            remove.append(vertex)
    bmesh.ops.delete(bm, geom=remove, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    log("bone-envelope cleanup removed", len(remove), "of",
        len(remove) + len(obj.data.vertices), "vertices; radius", radius)


def remove_long_surface_faces(obj, max_edge_length):
    """Remove source artifacts made of triangles spanning unrelated skin regions."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    faces = [f for f in bm.faces
             if max(edge.calc_length() for edge in f.edges) > max_edge_length]
    bmesh.ops.delete(bm, geom=faces, context="FACES")
    loose = [v for v in bm.verts if not v.link_edges]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    log("long-face cleanup removed", len(faces), "faces and", len(loose),
        "loose vertices; max edge", max_edge_length)


def crop_forearm(obj, wrist, axis, metric):
    """Keep the whole hand plus a short proximal forearm; discard the long arm."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    cutoff = -FOREARM_LENGTH_M / metric
    remove = [v for v in bm.verts if (v.co - wrist).dot(axis) < cutoff]
    bmesh.ops.delete(bm, geom=remove, context="VERTS")
    # Do not holes_fill every boundary: UV/split boundaries can be connected into
    # giant polygons. The source skin remains unchanged apart from the crop.
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def create_open_armature(source_arm, transform):
    """Create a skeleton whose REST matrices equal the source OPEN world pose."""
    data = bpy.data.armatures.new("ElenaFF.OpenRig")
    arm = bpy.data.objects.new("ElenaRig.R", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for source_bone in source_arm.data.bones:
        pb = source_arm.pose.bones[source_bone.name]
        world = source_arm.matrix_world @ pb.matrix
        head = transform @ (source_arm.matrix_world @ pb.head)
        tail = transform @ (source_arm.matrix_world @ pb.tail)
        direction = tail - head
        if direction.length < 1e-6:
            direction = world.to_3x3() @ Vector((0, 0.001, 0))
            tail = head + direction
        eb = data.edit_bones.new(source_bone.name)
        eb.head, eb.tail = head, tail
        eb.align_roll((world.to_3x3() @ Vector((0, 0, 1))).normalized())
        eb.use_deform = source_bone.use_deform
    for source_bone in source_arm.data.bones:
        if source_bone.parent:
            data.edit_bones[source_bone.name].parent = data.edit_bones[source_bone.parent.name]
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)
    arm.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    return arm


def bind(mesh, arm):
    mod = mesh.modifiers.new("Native skin — open bind", "ARMATURE")
    mod.object = arm
    mod.use_deform_preserve_volume = True


def transfer_native_action(source_arm, target_arm):
    """Transfer open->current local rotation deltas, excluding location tracks."""
    sc = bpy.context.scene
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()
    open_local = {pb.name: local_pose(pb) for pb in source_arm.pose.bones}
    action = bpy.data.actions.new("ElenaFF Native Open-Close")
    target_arm.animation_data_create()
    target_arm.animation_data.action = action
    for pb in target_arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    for frame in range(FRAME_OPEN, FRAME_LAST + 1):
        sc.frame_set(frame)
        bpy.context.view_layer.update()
        for source_pb in source_arm.pose.bones:
            delta = open_local[source_pb.name].rotation_difference(local_pose(source_pb))
            if delta.magnitude < 1e-8:
                delta = Quaternion()
            target_pb = target_arm.pose.bones[source_pb.name]
            target_pb.rotation_quaternion = delta.normalized()
            target_pb.keyframe_insert("rotation_quaternion", frame=frame, group=source_pb.name)
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()
    log("native rotation-only action transferred", tuple(action.frame_range))
    return action


def rest_rel(pb):
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()


def mirror_hand(mesh_r, arm_r):
    """Create a positive-scale left mesh/rig and numerically bake mirrored motion."""
    sc = bpy.context.scene
    M = Matrix.Diagonal((-1, 1, 1, 1))
    mesh_l = bpy.data.objects.new("ElenaSkin.L", mesh_r.data.copy())
    sc.collection.objects.link(mesh_l)
    for group in mesh_r.vertex_groups:
        mesh_l.vertex_groups.new(name=group.name)
    bm = bmesh.new()
    bm.from_mesh(mesh_l.data)
    for v in bm.verts:
        v.co = M @ v.co
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh_l.data)
    bm.free()

    data = bpy.data.armatures.new("ElenaFF.OpenRig.L")
    arm_l = bpy.data.objects.new("ElenaRig.L", data)
    sc.collection.objects.link(arm_l)
    bpy.context.view_layer.objects.active = arm_l
    arm_l.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for b in arm_r.data.bones:
        eb = data.edit_bones.new(b.name)
        eb.head, eb.tail = M @ b.head_local, M @ b.tail_local
        target = M @ b.matrix_local @ M
        eb.align_roll((target.to_3x3() @ Vector((0, 0, 1))).normalized())
        eb.use_deform = b.use_deform
    for b in arm_r.data.bones:
        if b.parent:
            data.edit_bones[b.name].parent = data.edit_bones[b.parent.name]
    bpy.ops.object.mode_set(mode="OBJECT")
    arm_l.select_set(False)
    bind(mesh_l, arm_l)

    action = bpy.data.actions.new("ElenaFF Native Open-Close.L")
    arm_l.animation_data_create()
    arm_l.animation_data.action = action
    order = []
    def walk(pb):
        order.append(pb)
        for child in pb.children:
            walk(child)
    for pb in (p for p in arm_l.pose.bones if p.parent is None):
        walk(pb)
    for pb in order:
        pb.rotation_mode = "QUATERNION"
    for frame in range(FRAME_OPEN, FRAME_LAST + 1):
        sc.frame_set(frame)
        bpy.context.view_layer.update()
        posed_l = {}
        for pb in order:
            posed_r = arm_r.pose.bones[pb.name].matrix.copy()
            target = M @ posed_r @ M
            rr = rest_rel(pb)
            if pb.parent:
                basis = rr.inverted() @ posed_l[pb.parent.name].inverted() @ target
            else:
                basis = rr.inverted() @ target
            loc, rot, _ = basis.decompose()
            pb.location = loc
            pb.rotation_quaternion = rot.normalized()
            pb.keyframe_insert("location", frame=frame, group=pb.name)
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            posed_l[pb.name] = target
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()
    return mesh_l, arm_l


def make_root(name, objects):
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    for obj in objects:
        obj.parent = root
        obj.matrix_parent_inverse = Matrix.Identity(4)
    return root


def export_fbx(root, side):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in [root] + list(root.children):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = OUT / f"ElenaHand_{side}.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, apply_unit_scale=True,
        object_types={"ARMATURE", "MESH", "EMPTY"}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
        mesh_smooth_type="OFF", path_mode="ABSOLUTE")
    log("exported", path.name)


def export_textures(mesh):
    saved = {}
    for mat in mesh.data.materials:
        if not mat or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type != "TEX_IMAGE" or not node.image:
                continue
            role = "extra"
            for link in mat.node_tree.links:
                if link.from_node != node:
                    continue
                if link.to_node.type == "BSDF_PRINCIPLED" and link.to_socket.name == "Base Color":
                    role = "basecolor"
                elif link.to_node.type == "SEPARATE_COLOR":
                    role = "metallicroughness"
                elif link.to_node.type == "MATH":
                    role = "occlusion"
            path = OUT / f"ElenaFF_{role}.png"
            node.image.filepath_raw = str(path)
            node.image.file_format = "PNG"
            node.image.save()
            saved[role] = path.name
    return saved


def add_studio():
    sc = bpy.context.scene
    world = bpy.data.worlds.new("ElenaStudioWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.58, 0.62, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.65
    sc.world = world
    for name, loc, energy, size in (("Key", (0.3, -0.4, 0.6), 35, 0.35),
                                    ("Fill", (-0.35, 0.25, 0.35), 25, 0.3),
                                    ("Rim", (0.1, 0.4, -0.25), 30, 0.25)):
        data = bpy.data.lights.new(name, "AREA")
        data.energy, data.size = energy, size
        obj = bpy.data.objects.new(name, data)
        sc.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = (-obj.location).to_track_quat("-Z", "Y").to_euler()
    data = bpy.data.cameras.new("ElenaStudioCamera")
    data.type = "ORTHO"
    cam = bpy.data.objects.new("ElenaStudioCamera", data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.resolution_percentage = 100
    return cam


def render_hand(cam, mesh, arm, side, frame, tag, direction):
    sc = bpy.context.scene
    visibility = {}
    for candidate in sc.objects:
        if candidate.type == "MESH":
            visibility[candidate] = candidate.hide_render
            candidate.hide_render = candidate != mesh
    sc.frame_set(frame)
    bpy.context.view_layer.update()
    pts = world_points(mesh)
    mn = Vector(tuple(min(p[i] for p in pts) for i in range(3)))
    mx = Vector(tuple(max(p[i] for p in pts) for i in range(3)))
    center = (mn + mx) * 0.5
    span = mx - mn
    d = Vector(direction).normalized()
    cam.location = center + d * 2
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = max(span) * 1.2
    path = OUT / f"Elena_{side}_{tag}.png"
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for candidate, hidden in visibility.items():
        candidate.hide_render = hidden
    return path.name


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(GLB))
    sc = bpy.context.scene
    for obj in list(sc.objects):
        if obj.name == "Icosphere" or obj.type in {"LIGHT", "CAMERA"}:
            bpy.data.objects.remove(obj, do_unlink=True)
    source_mesh = next(o for o in sc.objects if o.type == "MESH")
    source_arm = next(o for o in sc.objects if o.type == "ARMATURE")
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()

    wrist = source_arm.matrix_world @ source_arm.pose.bones[WRIST].head
    middle = source_arm.matrix_world @ source_arm.pose.bones[MIDDLE_TIP].head
    axis = (middle - wrist).normalized()
    open_points = world_points(source_mesh)
    # Furthest skin point along the anatomical long axis approximates the middle tip.
    hand_length_source = max((p - wrist).dot(axis) for p in open_points)
    metric = HAND_LENGTH_M / hand_length_source
    transform = Matrix.Scale(metric, 4) @ Matrix.Translation(-wrist)
    log("wrist", tuple(wrist), "middle", tuple(middle),
        "source hand length", hand_length_source, "metric", metric)

    mesh_r = create_open_mesh(source_mesh, source_arm)
    remove_geometry_far_from_finger_bones(mesh_r, source_arm, radius=0.35)
    remove_long_surface_faces(mesh_r, max_edge_length=0.22)
    crop_forearm(mesh_r, wrist, axis, metric)
    for v in mesh_r.data.vertices:
        v.co = transform @ v.co
    arm_r = create_open_armature(source_arm, transform)
    bind(mesh_r, arm_r)
    transfer_native_action(source_arm, arm_r)
    mesh_l, arm_l = mirror_hand(mesh_r, arm_r)
    root_r = make_root("ElenaHand.R", [arm_r, mesh_r])
    root_l = make_root("ElenaHand.L", [arm_l, mesh_l])

    # Keep source import out of the editable deliverable and all renders.
    bpy.data.objects.remove(source_mesh, do_unlink=True)
    bpy.data.objects.remove(source_arm, do_unlink=True)
    cam = add_studio()
    renders = []
    for side, mesh, arm in (("R", mesh_r, arm_r), ("L", mesh_l, arm_l)):
        for frame, tag in ((FRAME_OPEN, "open"), (FRAME_CLOSED, "closed"),
                           (FRAME_LAST, "reopen")):
            renders.append(render_hand(cam, mesh, arm, side, frame, tag + "_palm", (0, -1, 0.25)))
            renders.append(render_hand(cam, mesh, arm, side, frame, tag + "_back", (0, 1, 0.25)))
    sc.frame_set(FRAME_OPEN)
    bpy.context.view_layer.update()
    textures = export_textures(mesh_r)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "ElenaHands.blend"))
    export_fbx(root_r, "R")
    export_fbx(root_l, "L")

    manifest = {
        "method": "visible native open pose rebound; relative native rotations only; short forearm crop; positive-scale numeric left mirror",
        "source": "Rigged hand by Elena FF, CC BY-SA 4.0",
        "sourceSha256": "3eb51d0b612fb4b5f3b63c9e32ac145be191abcfd38ca7d3c5cf4fe0a7ee2d58",
        "changes": "Rebased bind pose to source open pose, removed long proximal arm, capped forearm, mirrored left hand, transferred native Open/Close rotations.",
        "handLengthMetres": HAND_LENGTH_M,
        "forearmLengthMetres": FOREARM_LENGTH_M,
        "frames": {"open": FRAME_OPEN, "closed": FRAME_CLOSED, "reopen": FRAME_LAST},
        "seconds": {"open": 0.0, "closed": FRAME_CLOSED / FPS, "reopen": FRAME_LAST / FPS},
        "trianglesPerHand": tri_count(mesh_r.data),
        "textures": textures,
        "renders": renders,
        "license": "CC BY-SA 4.0 — Elena FF; derivative model and animation retain the same license",
    }
    (OUT / "rig-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    log("DONE", OUT)


if __name__ == "__main__":
    main()
