import bpy
import mathutils

HERE = r"C:/Users/Ait/Desktop/Neuralife/Unity/Game04-VirtualHand/ArtSource/Refinement"
bpy.ops.wm.open_mainfile(filepath=HERE + "/output/rev4/refined_hands.blend")
obj = bpy.data.objects["Hand.R"]
arm = bpy.data.objects["Armature.R"]
amw = arm.matrix_world
w_h = amw @ mathutils.Vector(arm.data.bones["Wrist.R"].head_local)
i_mcp = amw @ mathutils.Vector(arm.data.bones["IndexMeta.R"].tail_local)
l_mcp = amw @ mathutils.Vector(arm.data.bones["LittleMeta.R"].tail_local)
normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()
up = (arm.matrix_world @ mathutils.Vector(
    arm.data.bones["Middle3.R"].tail_local) - w_h).normalized()
center = (i_mcp + l_mcp + w_h) / 3
print("RES normal", [round(v, 3) for v in normal])

# lights (diag ladder showed nothing without them being linked)
sc = bpy.context.scene
lt = bpy.data.lights.new("k", "AREA")
lt.energy = 1000
lt.size = 2
lo = bpy.data.objects.new("k", lt)
lo.location = center + normal * 3
bpy.context.collection.objects.link(lo)
d = normal.normalized()
lo.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()

cam = bpy.data.cameras.new("d")
cam.type = "ORTHO"
cam.ortho_scale = 1.2
go = bpy.data.objects.new("d", cam)
bpy.context.collection.objects.link(go)
sc.camera = go
go.location = center + normal * 5
z = -d
x = up.cross(z).normalized()
y = z.cross(x).normalized()
m = mathutils.Matrix(((x.x, y.x, z.x, 0), (x.y, y.y, z.y, 0),
                      (x.z, y.z, z.z, 0), (0, 0, 0, 1)))
go.rotation_euler = m.to_euler()
sc.render.filepath = HERE + "/output/rev4/_diag_matrix.png"
bpy.ops.render.render(write_still=True)
print("RES rendered")
print("RES center", [round(v, 3) for v in center])

