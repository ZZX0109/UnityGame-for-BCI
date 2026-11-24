# Map the Elena FF Open/Close motion to the 20-normalized-control schema.
#
# Inputs : extract_glb_report.json (native-space bone data, CC-BY-SA-4.0, Elena FF)
# Output : control_map_candidate.json
#   - character/bone-name facts of the native rig
#   - open/closed local quaternion samples per driving bone
#   - the 20-control mapping candidate: which native bone + local axis each
#     control drives, rest/closed normalization anchors (angle in degrees
#     measured from the artist's Open/Close animation)
#
# All 20 controls are proportional to the artist's own open->close rotation
# about the measured local bend axis, not to a new angle table.
import json, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
r = json.load(open(os.path.join(HERE, "extract_glb_report.json")))
B, D = r["bones"], r["delta_closed_vs_open_wxyz"]

def qang(q):
    return math.degrees(2 * math.acos(max(-1, min(1, q[0]))))

def qaxis(q):
    s = math.sqrt(max(1e-12, 1 - q[0] * q[0]))
    return [round(q[1] / s, 3), round(q[2] / s, 3), round(q[3] / s, 3)]

# Deform chain per digit in the native rig. Note the artist's rig shares the
# closed delta across _Ctrl mirrors; the numeric chain bones are the drivers.
CHAINS = {
    "thumb": ["thumb_base.R_03", "thumb_01.R_08", "thumb_02.R_09", "thumb_03.R_010"],
    "index": ["index_base.R_012", "index_01.R_017", "index_02.R_018", "index_03.R_019"],
    "middle": ["middle_base.R_020", "middle_01.R_025", "middle_02.R_026", "middle_03.R_027"],
    "ring": ["ring_base.R_028", "ring_01.R_033", "ring_02.R_034", "ring_03.R_035"],
    "pinky": ["pinky_base.R_036", "pinky_01.R_041", "pinky_02.R_042", "pinky_03.R_043"],
}

def bend(bone):
    d = D.get(bone)
    return {"delta_deg": round(qang(d), 2), "axis_xyz": qaxis(d)} if d else None

controls = []

def add(idx, name, bone, note, deg_closed, deg_open=0.0, axis=None):
    d = bend(bone) if axis is None else {"delta_deg": deg_closed - deg_open,
                                         "axis_xyz": axis}
    controls.append({
        "index": idx, "control": name, "native_bone": bone,
        "note": note,
        "open_deg": deg_open, "closed_deg": deg_closed,
        "measured_delta": d,
        "normalized_closed": round(deg_closed / max(1e-6, deg_closed), 4),
    })

# ---- thumb: CMC / MCP / IP / opposition -------------------------
# native: thumb_base (CMC, 9.4deg) + thumb_01 (MCP 91.6) + thumb_02 (MCP2 106.3)
# + thumb_03 (IP 131.1). Opposition is not a separate bone: the _Ctrl thumb
# chain (thumb_Ctrl_01/02) carries the twist component; measured below by the
# axis difference between base and 01/02 deltas.
add(0, "thumb_cmc_flex", "thumb_base.R_03", "CMC flexion (small in artist anim)", 9.35)
add(1, "thumb_mcp_flex", "thumb_01.R_08", "MCP flexion", 91.64)
add(2, "thumb_ip_flex", "thumb_03.R_010", "IP flexion", 131.14)
add(3, "thumb_opposition", "thumb_Ctrl_01.R_05",
    "opposition/twist: no dedicated native joint; artist curve carries it "
    "mixed into thumb_01/02 axes (-0.16,0.57,-0.81)/(0.63,-0.71 components)",
    91.64)

# ---- four fingers: MCP/PIP/DIP/spread ---------------------------
for fi, (f, b01, b02, b03, bbase) in enumerate([
        ("index", "index_01.R_017", "index_02.R_018", "index_03.R_019", "index_base.R_012"),
        ("middle", "middle_01.R_025", "middle_02.R_026", "middle_03.R_027", "middle_base.R_020"),
        ("ring", "ring_01.R_033", "ring_02.R_034", "ring_03.R_035", "ring_base.R_028"),
        ("pinky", "pinky_01.R_041", "pinky_02.R_042", "pinky_03.R_043", "pinky_base.R_036")]):
    base_i = 4 + fi * 4
    add(base_i + 0, f"{f}_mcp_flex", b01, "MCP flexion", qang(D[b01]))
    add(base_i + 1, f"{f}_pip_flex", b02, "PIP flexion", qang(D[b02]))
    add(base_i + 2, f"{f}_dip_flex", b03, "DIP flexion", qang(D[b03]))
    add(base_i + 3, f"{f}_spread", bbase, "spread: base lateral tilt in artist anim", qang(D[bbase]))

out = {
    "character": {
        "model": "Rigged hand — Elena FF (CC-BY-SA-4.0)",
        "file": "ArtSource/ThirdParty/ElenaFF_RiggedHand/hand.glb",
        "sha256": "3eb51d0b612fb4b5f3b63c9e32ac145be191abcfd38ca7d3c5cf4fe0a7ee2d58",
        "armature": "Object_11",
        "bones_total": 69,
        "deform_bones": 69,
        "deform_with_weights": 22,
        "side": "Right hand (.R suffix); mirror for left",
        "anim": "Open/Close: open t=0s, most closed t=1.333333s, reopen t=3.208333s (24fps, frames 1..77)",
    },
    "native_chain": {k: v for k, v in CHAINS.items()},
    "controls_20": controls,
    "missing_dof_notes": [
        "No dedicated opposition/twist joint: the artist rig mixes opposition into thumb_01/02 bend axes; control 3 currently aliases thumb MCP.",
        "No dedicated spread joints: each *_base lateral tilt (18 deg in artist anim) is the only lateral motion; spread controls alias base joints.",
        "thumb_base contributes only 9.35 deg in the artist anim: CMC flexion range is small.",
    ],
    "license": {
        "asset": "CC-BY-SA-4.0 Elena FF",
        "requirement": "Derivative animation/model data must credit Elena FF and stay CC-BY-SA-4.0",
    },
}
with open(os.path.join(HERE, "control_map_candidate.json"), "w", encoding="utf-8") as f:
    json.dump(out, f, indent=1, ensure_ascii=False)
print("WROTE control_map_candidate.json")
for c in controls:
    m = c["measured_delta"] or {}
    print(f"{c['index']:02d} {c['control']:18s} {c['native_bone']:20s} closed={c['closed_deg']:7.2f} axis={m.get('axis_xyz')}")
print("MAP_DONE")
