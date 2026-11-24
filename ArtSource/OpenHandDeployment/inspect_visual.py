# -*- coding: utf-8 -*-
"""Render the untouched Elena FF GLB to separate source issues from export issues."""
from pathlib import Path
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
GLB = HERE.parent / "ThirdParty" / "ElenaFF_RiggedHand" / "hand.glb"
OUT = HERE / "source-review"
OUT.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(GLB))
sc = bpy.context.scene
for ob in list(sc.objects):
    if ob.name == "Icosphere" or ob.type in {"LIGHT", "CAMERA"}:
        bpy.data.objects.remove(ob, do_unlink=True)
mesh = next(o for o in sc.objects if o.type == "MESH")
arm = next(o for o in sc.objects if o.type == "ARMATURE")

# Vertex-group bounds distinguish actual hand skin from sleeve/structural mesh.
for group in mesh.vertex_groups:
    selected = []
    for vertex in mesh.data.vertices:
        weight = next((g.weight for g in vertex.groups if g.group == group.index), 0.0)
        if weight > 0.01:
            selected.append(mesh.matrix_world @ vertex.co)
    if selected:
        mn = Vector(tuple(min(p[i] for p in selected) for i in range(3)))
        mx = Vector(tuple(max(p[i] for p in selected) for i in range(3)))
        print("SOURCE_GROUP", group.name, "count", len(selected),
              "min", tuple(round(v, 4) for v in mn),
              "max", tuple(round(v, 4) for v in mx), flush=True)

# Connected components reveal whether the source contains separate sleeve/prop geometry.
import bmesh
sc.frame_set(0)
bpy.context.view_layer.update()
deps = bpy.context.evaluated_depsgraph_get()
ev = mesh.evaluated_get(deps)
evaluated = ev.to_mesh()
bm = bmesh.new()
bm.from_mesh(evaluated)
for v in bm.verts:
    v.co = ev.matrix_world @ v.co
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
bm.verts.ensure_lookup_table()
seen = set()
components = []
for v in bm.verts:
    if v in seen:
        continue
    stack, verts = [v], []
    seen.add(v)
    while stack:
        cur = stack.pop()
        verts.append(cur)
        for edge in cur.link_edges:
            other = edge.other_vert(cur)
            if other not in seen:
                seen.add(other)
                stack.append(other)
    points = [v.co for v in verts]
    mn = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    mx = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    components.append((len(verts), tuple(round(x, 4) for x in mn), tuple(round(x, 4) for x in mx)))
for index, stats in enumerate(sorted(components, reverse=True)):
    print("SOURCE_COMPONENT", index, stats, flush=True)
bm.free()
ev.to_mesh_clear()

world = bpy.data.worlds.new("SourceReviewWorld")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.65, 0.65, 0.65, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.7
sc.world = world
for name, loc, energy, size in (
    ("Key", (3, -4, 6), 450, 4),
    ("Fill", (-3, 2, 4), 350, 4),
    ("Rim", (0, 4, -3), 300, 3),
):
    data = bpy.data.lights.new(name, "AREA")
    data.energy, data.shape, data.size = energy, "DISK", size
    ob = bpy.data.objects.new(name, data)
    sc.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (-ob.location).to_track_quat("-Z", "Y").to_euler()

cam_data = bpy.data.cameras.new("SourceReviewCamera")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("SourceReviewCamera", cam_data)
sc.collection.objects.link(cam)
sc.camera = cam
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 960
sc.render.resolution_percentage = 100


def bounds():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    me = ev.to_mesh()
    pts = [ev.matrix_world @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    mn = Vector(tuple(min(p[i] for p in pts) for i in range(3)))
    mx = Vector(tuple(max(p[i] for p in pts) for i in range(3)))
    return mn, mx


def capture(tag, direction):
    mn, mx = bounds()
    center = (mn + mx) * 0.5
    span = mx - mn
    d = Vector(direction).normalized()
    up = Vector((0, 0, 1))
    if abs(d.dot(up)) > 0.95:
        up = Vector((0, 1, 0))
    cam.location = center + d * 20
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam_data.ortho_scale = max(span) * 1.15
    sc.render.filepath = str(OUT / (tag + ".png"))
    print("SOURCE_REVIEW", tag, "min", tuple(mn), "max", tuple(mx), flush=True)
    bpy.ops.render.render(write_still=True)


for frame, tag in ((0, "source_open"), (32, "source_closed"), (77, "source_reopen")):
    sc.frame_set(frame)
    bpy.context.view_layer.update()
    capture(tag + "_a", (1, -1, 0.6))
    capture(tag + "_b", (-1, 1, 0.6))

arm.animation_data.action = None
sc.frame_set(0)
bpy.context.view_layer.update()
capture("source_no_action_a", (1, -1, 0.6))
arm.data.pose_position = "REST"
bpy.context.view_layer.update()
capture("source_true_rest_a", (1, -1, 0.6))
capture("source_true_rest_b", (-1, 1, 0.6))
print("SOURCE_REVIEW_DONE", flush=True)
