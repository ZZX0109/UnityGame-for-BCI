# Render the two additional required views (side + first-person wrist closeup)
# from the built avatar .blend, reusing the scene's existing lighting rig.
# Run: blender.exe -b neutral_adult_avatar.blend -P render_extra_views.py
import bpy
import os
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
if hasattr(scene, 'eevee') and hasattr(scene.eevee, 'taa_render_samples'):
    scene.eevee.taa_render_samples = 64

# Bounds from the character (scale already normalized to 1.70 m at origin).
zmin, zmax = 0.0, 1.70
cam_data = bpy.data.cameras.new('ExtraViews')
cam = bpy.data.objects.new('ExtraViews', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

def shoot(name, loc, target, lens):
    cam_data.lens = lens
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
    print("WROTE", scene.render.filepath)

cx, cy = 0.0, 0.0
# Side view (left profile), full body framing.
shoot("side.png", (cx - 4.15, cy, 0.85), (cx, cy, 0.85), 50)
# First-person wrist closeup: camera at the head looking down-forward at the
# right cuff area (~0.72 m height), 35mm equivalent.
shoot("wrist_closeup.png", (cx + 0.06, cy - 0.09, 1.58), (cx + 0.25, cy - 0.02, 0.72), 35)
