"""Probe candidate copy scene structure. Read-only; prints JSON summary."""
import json
from pathlib import Path

import bpy

CAND = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\GLMResourceStyling\seated_pants_candidate_20261122\neutral_adult_seated_pants_candidate.blend")

info = {"loaded": bpy.data.filepath, "engine": None, "objects": [], "cams": [],
        "lights": [], "world": None, "bones": {}, "pantsMods": []}

info["engine"] = bpy.context.scene.render.engine
for obj in bpy.data.objects:
    entry = {"name": obj.name, "type": obj.type, "verts": None, "mods": []}
    if obj.type == "MESH":
        entry["verts"] = len(obj.data.vertices)
        entry["polys"] = len(obj.data.polygons)
        entry["mods"] = [(m.name, m.type) for m in obj.modifiers]
        entry["vgroups"] = len(obj.vertex_groups)
        entry["mats"] = [m.name if m else None for m in obj.data.materials]
    info["objects"].append(entry)
    if obj.type == "CAMERA":
        info["cams"].append(obj.name)
    if obj.type == "LIGHT":
        info["lights"].append({"name": obj.name, "type": obj.data.type
                               if obj.data else None})
w = bpy.context.scene.world
info["world"] = None if w is None else w.name

rig = bpy.data.objects.get("Human.rig")
if rig:
    info["bones"] = {name: {"head": list(bone.head_local), "tail": list(bone.tail_local)}
                     for name, bone in rig.data.bones.items()
                     if any(s in name.lower() for s in ("head", "eye", "neck", "spine", "thigh", "calf", "pelvis", "foot"))}
pants = bpy.data.objects.get("Human.cortu_cargo_pants")
if pants:
    info["pantsMods"] = [{"name": m.name, "type": m.type,
                          "levels": getattr(m, "levels", None),
                          "render_levels": getattr(m, "render_levels", None),
                          "strength": getattr(m, "strength", None),
                          "mid_level": getattr(m, "mid_level", None)}
                         for m in pants.modifiers]
    info["pantsHide"] = {"hide_render": pants.hide_render, "hide_viewport": pants.hide_viewport}
body = bpy.data.objects.get("Human")
if body:
    info["bodyHide"] = {"hide_render": body.hide_render, "hide_viewport": body.hide_viewport}

print("PROBE_JSON " + json.dumps(info, default=str))
