#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Game04 VirtualHand — Ninapro DB8 kinematic replay exporter (GLM task B).

Reads the official DB8 .mat (S1_E1_A3.mat, MATLAB5, fields all lowercase),
maps the 18 calibrated CyberGlove signals onto the 20 normalized rig controls,
and exports compact rest/thumb/index/grip replay clips as JSONL.

Facts verified against the local file + official sources:
  * glove (529099, 18) float32, no fs/time field. Official DB8 instructions:
    the glove is sampled at 100 Hz and UPSAMPLED to 2000 Hz synchronized with
    all other signals -> the row clock is 2000 Hz.
  * stimulus/restimulus labels (0..9): 0 rest, 1 thumb flex/ext,
    2 thumb abd/add, 3 index flex/ext, 4 middle flex/ext, 5 ring+little
    flex/ext, 6 index pointer, 7 cylindrical grip, 8 lateral grip,
    9 tripod grip. Labels are used ONLY to locate clips, never as targets.
  * Official CyberGlove diagram (ninapro DB8 page, DB7_glove.png): fingertip
    DIP sensors are "n/a" on this 18-sensor model; PIP sensors sit on index,
    middle, ring and little fingers. Channel-to-finger assignment below was
    additionally verified empirically: each single-finger stimulus moves its
    own channel pair (index: ch4+5, middle: ch6+7, ring: ch9+10,
    little: ch12+13; thumb tasks/grips: ch0-3).
  * Sensor values are calibrated signals but NOT degrees; per-channel robust
    min-max over the whole recording yields the normalized rig controls.
  * The glove is worn on the LEFT hand (contralateral to the EMG forearm).

Usage:
  python db8_export.py --mat D:/SoftwareManagement/datasets/NinaproDB8/S1_E1_A3.mat
Dependencies: numpy, scipy.
"""

import argparse
import json
import os
import sys

import numpy as np
from scipy.io import loadmat

FS = 2000.0            # official synchronized DB8 sample clock (Hz)
FPS = 40.0             # export rate, inside the contracted 30-60 Hz band
MAX_CLIP_S = 8.0       # representative segment cap per clip

STIM_NAMES = {
    0: "rest", 1: "thumb", 3: "index", 7: "cylindrical_grip",
    8: "lateral_grip", 9: "tripod_grip",
}
# intent is the coarse four-class visual tag for the demo UI; the pose always
# comes from the glove channels themselves (source="glove").
STIM_INTENT = {0: "rest", 1: "left", 3: "left", 7: "both", 8: "both", 9: "both"}

# Documented mapping from the 18 glove channels (0-based) to rig controls.
THUMB = (0, 1, 2, 3)               # opposition, CMC flex, MCP flex, IP flex
FINGER_PAIRS = {                   # finger -> (MCP ch, PIP ch); DIP coupled to PIP
    "index": (4, 5),
    "middle": (6, 7),
    "ring": (9, 10),
    "little": (12, 13),
}
SPREAD_CH = (8, 11, 14)            # inter-finger abduction sensors
UNUSED_CH = (15, 16, 17)           # palm arch / wrist sensors, not finger pose


def robust01(x, lo_q=1.0, hi_q=99.0):
    """Per-channel robust min-max normalization to [0,1] (not degrees)."""
    lo, hi = np.percentile(np.asarray(x, dtype=np.float64), [lo_q, hi_q])
    if hi - lo < 1e-9:
        return np.full(np.shape(x), 0.5, dtype=np.float32)
    return np.clip((x - lo) / (hi - lo), 0.0, 1.0).astype(np.float32)


def build_controls(glove):
    """(T,18) calibrated glove signals -> (T,20) normalized left-hand controls.

    Joint order (finger-major): thumb [CMC flex, MCP flex, IP flex, opposition],
    then index/middle/ring/little each [MCP, PIP, DIP, spread].
    """
    n = glove.shape[0]
    ctrl = np.zeros((n, 20), dtype=np.float32)
    # Thumb block: 20-joint order is CMC, MCP, IP, opposition.
    ctrl[:, 0] = robust01(glove[:, THUMB[1]])   # CMC flexion
    ctrl[:, 1] = robust01(glove[:, THUMB[2]])   # MCP flexion
    ctrl[:, 2] = robust01(glove[:, THUMB[3]])   # IP flexion
    ctrl[:, 3] = robust01(glove[:, THUMB[0]])   # opposition (rotation/abd)
    for f, (mcp_ch, pip_ch) in enumerate(FINGER_PAIRS.values()):
        o = (f + 1) * 4
        ctrl[:, o] = robust01(glove[:, mcp_ch])   # MCP
        ctrl[:, o + 1] = robust01(glove[:, pip_ch])  # PIP
        ctrl[:, o + 2] = ctrl[:, o + 1]           # DIP coupled to PIP (no sensor)
    spread = robust01(glove[:, list(SPREAD_CH)].mean(axis=1))
    for f in range(4):
        ctrl[:, (f + 1) * 4 + 3] = spread
    return ctrl


def longest_runs(stim, label):
    """Indices of the longest contiguous run with stim == label."""
    idx = np.where(stim == label)[0]
    if len(idx) == 0:
        return np.array([], dtype=int)
    runs = np.split(idx, np.where(np.diff(idx) > 1)[0] + 1)
    return max(runs, key=len)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="DB8 glove replay exporter")
    ap.add_argument("--mat", default="D:/SoftwareManagement/datasets/NinaproDB8/S1_E1_A3.mat")
    ap.add_argument("--outdir", default=os.path.join(
        here, "..", "Assets", "StreamingAssets", "Replays", "db8"))
    ap.add_argument("--fps", type=float, default=FPS)
    ap.add_argument("--mirror-right", action="store_true",
                    help="also fill rightJoints with the mirrored left glove")
    args = ap.parse_args()

    m = loadmat(args.mat)
    glove = np.asarray(m["glove"], dtype=np.float64)
    stim = np.asarray(m["restimulus"]).ravel().astype(int)  # re-repaired labels
    subject = f"S{int(np.asarray(m['subject']).ravel()[0])}"
    exercise = f"E{int(np.asarray(m['exercise']).ravel()[0])}"
    os.makedirs(args.outdir, exist_ok=True)

    n = glove.shape[0]
    ctrl = build_controls(glove)
    step = max(1, int(round(FS / args.fps)))
    keep = np.arange(0, n, step)                       # uniform decimation
    ctrl_d = ctrl[keep]
    stim_d = stim[keep]
    dur = 1.0 / args.fps

    meta = {"subject": subject, "exercise": exercise,
            "source_file": os.path.basename(args.mat),
            "provenance": "Ninapro DB8 (CC-BY-SA 4.0), ninapro.hevs.ch",
            "fs_native": FS, "export_fps": args.fps,
            "channel_mapping": {
                "thumb_opposition": THUMB[0], "thumb_cmc_flex": THUMB[1],
                "thumb_mcp_flex": THUMB[2], "thumb_ip_flex": THUMB[3],
                "finger_mcp_pip": {k: list(v) for k, v in FINGER_PAIRS.items()},
                "dip": "coupled to PIP (no DIP sensors on this glove model)",
                "spread": list(SPREAD_CH), "unused_palm_wrist": list(UNUSED_CH)},
            "normalization": "per-channel 1-99 percentile min-max over whole file",
            "measured_hand": "left (right optional mirror, marked)",
            "clips": []}
    for label, name in STIM_NAMES.items():
        run = longest_runs(stim_d, label)
        if len(run) < 5:
            continue
        if len(run) > MAX_CLIP_S * args.fps:           # representative middle part
            half = int(len(run) * MAX_CLIP_S * args.fps / (2.0 * len(run)))
            mid = len(run) // 2
            run = run[mid - half:mid + half]
        file_name = f"db8_{subject}_{name}.jsonl"
        out = os.path.join(args.outdir, file_name)
        t0 = run[0] / args.fps
        n_written = 0
        with open(out, "w", encoding="utf-8", newline="\n") as fh:
            for i in run:
                frame = {"version": 1,
                         "time": round(i / args.fps - t0, 4),
                         "duration": round(dur, 4),
                         "intent": STIM_INTENT[label],
                         "confidence": 1.0,
                         "source": "glove",
                         "subject": subject,
                         "run": f"{exercise}/{name}",
                         "leftJoints": [round(float(v), 4) for v in ctrl_d[i]]}
                if args.mirror_right:
                    frame["rightJoints"] = list(frame["leftJoints"])  # marked mirror
                fh.write(json.dumps(frame, ensure_ascii=False,
                                    separators=(",", ":")) + "\n")
                n_written += 1
        meta["clips"].append({"name": name, "stimulus_label": int(label),
                              "file": file_name, "frames": n_written,
                              "seconds": round(n_written / args.fps, 1),
                              "right_mirror": bool(args.mirror_right)})
        print(f"wrote {out} frames={n_written}")

    with open(os.path.join(args.outdir, "db8_provenance.json"), "w",
              encoding="utf-8") as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    print("provenance:", os.path.join(args.outdir, "db8_provenance.json"))


if __name__ == "__main__":
    main()
