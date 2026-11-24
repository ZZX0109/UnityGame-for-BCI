# Reopen-verification: open the built avatar .blend in a fresh Blender process
# and dump structural statistics to reopen_report.json.
# Run: blender.exe -b <blend> -P verify_reopen.py
import bpy
import json

out = {
    "blender_version": bpy.app.version_string,
    "objects": [],
    "armatures": [],
    "error": None,
}
try:
    for ob in bpy.context.scene.objects:
        entry = {"name": ob.name, "type": ob.type,
                 "mpfb_object_type": str(ob.get("object_type", "")),
                 "asset_source": str(ob.get("asset_source", "")),
                 "parents": ob.parent.name if ob.parent else None}
        if ob.type == 'MESH':
            entry["verts"] = len(ob.data.vertices)
            entry["faces"] = len(ob.data.polygons)
            entry["materials"] = [s.material.name if s.material else None
                                  for s in ob.material_slots]
        if ob.type == 'ARMATURE':
            entry["bones"] = len(ob.data.bones)
            out["armatures"].append(entry["name"])
        out["objects"].append(entry)
    out["totals"] = {
        "objects": len(out["objects"]),
        "meshes": sum(1 for o in out["objects"] if o["type"] == 'MESH'),
        "materials": len(bpy.data.materials),
        "images": len(bpy.data.images),
        "armatures": len(out["armatures"]),
    }
    out["reopen"] = "OK"
except Exception as e:  # noqa: BLE001
    out["error"] = repr(e)
    out["reopen"] = "FAILED"

with open("reopen_report.json", "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=2)
print("REOPEN_RESULT", out.get("reopen"), "totals=", out.get("totals"), "error=", out.get("error"))
