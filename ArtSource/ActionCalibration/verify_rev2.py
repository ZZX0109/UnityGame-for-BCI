# Independent verification of output/rev2/hand_rig_profile.json:
#  1) channels byte-identical to input profile
#  2) every sample value finite in 0..1
#  3) decode each sample control back through the SAME channels and print
#     the resulting degrees (reviewable against the gesture targets)
# No Blender required.
import json, os, math

HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "input", "hand_rig_profile.json"), encoding="utf-8") as f:
    pin = json.load(f)
with open(os.path.join(HERE, "output", "rev2", "hand_rig_profile.json"), encoding="utf-8") as f:
    pout = json.load(f)

ok = True
same = json.dumps(pout["channels"], sort_keys=True) == json.dumps(pin["channels"], sort_keys=True)
print("CHANNELS_IDENTICAL", same)
ok &= same
print("SOURCE_NOTE", pout.get("source_note", "<missing>")[:100], "...")

for name, vals in pout["samples"].items():
    inrange = all(0.0 <= v <= 1.0 and math.isfinite(v) for v in vals)
    print(f"SAMPLE {name}: len={len(vals)} in[0,1]={inrange}")
    ok &= inrange and len(vals) == 20
    for i, (ch, v) in enumerate(zip(pout["channels"], vals)):
        deg = (v - ch["rest"]) * ch["scale"] * ch["sign"]
        print(f"  ch{i:02d} {ch['bone']:8s}.{ch['axis']} ctrl={v:.4f} -> {deg:7.2f} deg")

print("VERIFY", "PASS" if ok else "FAIL")
