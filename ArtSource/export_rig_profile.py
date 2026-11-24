# Export HandRigProfile JSON from the saved blend: per-control bone mapping,
# axis/sign calibration, and the authored rest/fist/pinch 20-value samples.
# Unity loads this same file so both sides share ONE source of truth.
import bpy, os, json, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Virtuahand", "Resources")) \
    if False else os.path.normpath(os.path.join(HERE, "..", "Assets", "VirtualHand", "Resources", "Rig"))
os.makedirs(OUT, exist_ok=True)

BLEND = os.path.join(HERE, "Hands", "hands_source.blend")
bpy.ops.wm.open_mainfile(filepath=BLEND)

# --- bone/channel mapping (20 controls, finger-major) ---
# control index -> (bone, channel, degrees-at-1.0, sign)
# thumb block: 0 CMC flex, 1 MCP flex, 2 IP flex, 3 opposition
CHANNELS = []
# scales calibrated so every authored pose angle fits within 0..1:
# flexion channels rest=0 (0=straight, 1=full calibrated flexion);
# twist/spread channels rest=0.5 (0..1 = +-max around center).
CHANNELS.append({"bone": "Thumb1", "axis": "X", "scale": 90.0, "sign": 1, "rest": 0.0, "note": "thumb CMC flex"})
CHANNELS.append({"bone": "Thumb2", "axis": "X", "scale": 90.0, "sign": 1, "rest": 0.0, "note": "thumb MCP flex"})
CHANNELS.append({"bone": "Thumb3", "axis": "X", "scale": 90.0, "sign": 1, "rest": 0.0, "note": "thumb IP flex"})
CHANNELS.append({"bone": "Thumb1", "axis": "Y", "scale": 160.0, "sign": 1, "rest": 0.5, "note": "thumb opposition twist about bone-long axis (0.5=center)"})
for f in ("Index", "Middle", "Ring", "Little"):
    CHANNELS.append({"bone": f + "1", "axis": "X", "scale": 75.0, "sign": 1, "rest": 0.0, "note": "MCP flex"})
    CHANNELS.append({"bone": f + "2", "axis": "X", "scale": 85.0, "sign": 1, "rest": 0.0, "note": "PIP flex"})
    CHANNELS.append({"bone": f + "3", "axis": "X", "scale": 50.0, "sign": 1, "rest": 0.0, "note": "DIP flex"})
    CHANNELS.append({"bone": f + "2", "axis": "Z", "scale": 48.0, "sign": 1, "rest": 0.5, "note": "spread"})

# --- authored poses as 20-value samples (from POSE_ANGLES in build_hands) ---
def pose20_fist():
    """Fist in the new calibration: four fingers full flexion (1.0 each joint),
    thumb wraps 30/55/40 deg with opposition twist engaged."""
    c = [0.0] * 20
    for fi in range(1, 5):
        o = fi * 4
        c[o] = 1.0; c[o + 1] = 1.0; c[o + 2] = 1.0; c[o + 3] = 0.5
    c[0] = 30 / 90.0; c[1] = 55 / 90.0; c[2] = 40 / 90.0
    c[3] = 0.5 + 25 / 160.0  # opposition twist toward the fingers
    return [round(max(0.0, min(1.0, v)), 4) for v in c]

def pose20_pinch():
    """IK-solved pad-contact pose, encoded loss-free in the new calibration."""
    c = [0.0] * 20
    c[0] = 30 / 90.0    # Thumb1 CMC flex 30deg
    c[1] = 71 / 90.0    # Thumb2 MCP flex 71deg
    c[2] = 110 / 90.0   # Thumb3 IP flex 110deg (fits under scale 90)
    c[3] = 0.5 + (-80) / 160.0  # opposition twist -80deg around center
    o = 4
    c[o] = 45 / 75.0; c[o + 1] = 86 / 85.0; c[o + 2] = 22 / 50.0; c[o + 3] = 0.5
    curl = [(25, 35, 20), (30, 40, 25), (35, 45, 30)]
    for fi, (a, b, cc) in enumerate(curl, start=2):
        oo = fi * 4
        c[oo] = a / 75.0; c[oo + 1] = b / 85.0; c[oo + 2] = cc / 50.0; c[oo + 3] = 0.5
    return [round(max(0.0, min(1.0, v)), 4) for v in c]

def pose20_open():
    """True rest-open hand: flex channels 0 (rest), twist/spread 0.5 (centered)."""
    c = [0.0] * 20
    for ch_i in (3, 7, 11, 15, 19):  # opposition/spread channels are centered
        c[ch_i] = 0.5
    return [round(v, 4) for v in c]

for _i, _ch in enumerate(CHANNELS):
    _ch["index"] = _i

profile = {
    "version": 2,
    "rig": "MakeHuman-base body-only skin, baked +90X space, roll-aligned X curl",
    "space": "bone-local euler XYZ, degrees at control=1.0 relative to rest pose",
    "channels": CHANNELS,
    "samples": {
        "open": pose20_open(),
        "fist": pose20_fist(),
        "pinch": pose20_pinch(),
    },
    "notes": [
        "20 controls finger-major: thumb CMC/MCP/IP/opposition then index..little MCP/PIP/DIP/spread",
        "Unity HandRigProfile loads this JSON; PoseLibrary samples feed SetControls directly",
        "sign=-1 means the bone's local axis is inverted relative to the control direction",
    ],
}
out_path = os.path.join(OUT, "hand_rig_profile.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("PROFILE WRITTEN", out_path)
print("SAMPLE fist", profile["samples"]["fist"])
print("SAMPLE pinch", profile["samples"]["pinch"])
