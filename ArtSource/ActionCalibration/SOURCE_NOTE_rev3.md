# Rev3 handoff — geometry-constrained contact solve (A2, 2026-11-18)

## Deliverables (ArtSource/ActionCalibration/output/rev3/)
- `hand_rig_profile.json` — candidate: channels identical to
  `input_rev7/hand_rig_profile.json`; `open` / `fist` / `pinch` samples.
- `rev3_fist_palm.png`, `rev3_fist_side_thumb.png`, `rev3_fist_side_pinky.png`
- `rev3_pinch_palm.png`, `rev3_pinch_contact.png`
- Script: `../../solve_rev3.py` (single file, deterministic, no Unity).

## Method
- Start = rev2 authored poses (degrees). Refinement = staged pattern search
  (coordinate descent with run-along-axis moves) minimizing SKIN-SURFACE
  objectives measured on the evaluated rev7 mesh:
  - fist fingers: each fingertip-pad skin within the 0..10 mm touch band of the
    palm skin cloud, with a palm-normal penetration guard; pad = mesh verts
    within 22 mm of the DIP bone tail (fallback PIP), palm cloud selected at
    rest pose (1704 verts).
  - fist thumb (Stage B, fingers frozen): thumb-tip skin cloud onto the index
    middle-phalanx skin region.
  - pinch (Stage C, multi-start rev2/tight/mcptop/reach + Stage C2 fine and
    hand variants): thumb-tip skin cloud onto index-tip skin cloud, plus pad
    normal opposition (target ndot <= -0.6); middle/ring/little frozen relaxed.
  - opposition sign guard: Thumb1.Y delta locked negative (toward fingers);
    the earlier +56 deg splay escape is impossible now.
- JSON is written first; renders are driven ONLY by the re-read file, so
  numbers and images are same-source by construction.

## Final measured skin gaps (from the re-read JSON, rev7 mesh)
- fist pads->palm skin: Index 5.3 mm, Middle 8.5 mm, Ring 7.8 mm, Little 2.8 mm
  (Little ~0 penetration, i.e. touching; all pads on the palm side, no
  through-penetration; no C-shape gap from the views shown)
- fist thumb tip skin -> index PIP region skin: 5.0 mm (was 41.7 mm)
- pinch thumb tip skin <-> index tip skin: 8.1 mm (was 43 mm), pad normals
  opposed (ndot = -0.725). Contact is pad-to-pad; this residual is mesh
  resolution + slight squash of the pose, not bone-center distance.
- The pinch solved table (degrees): ch0 CMC 11.0, ch1 MCP 79.4, ch2 IP 32.2
  (IP kept moderate on purpose: >55 deg dives the thumb under the index),
  ch3 opposition -75.3, ch4-6 index 52.3/64.9/73.5, ch7 spread -14.3.

## Channels / range honesty
- bone/axis/sign/rest/scale all unchanged (CHANNELS_IDENTICAL True, independent
  verify PASS; all 20 controls finite in 0..1).
- No scale adjustments were needed: contact was achieved within existing
  channel ranges (SCALE_OVERRIDES none). The rev2-inherited Index1/Ring1 MCP
  authored starts (78/82 deg) exceed the 75-deg channel limit and were clamped
  to 75.0 deg in the candidate (RANGE_CLAMP log; earlier rev2 JSON had silently
  written controls >1.0 for those; that was wrong and is fixed here).

## Unverified / not claimed
- Unity playback, in-editor look, and final art acceptance: Codex's call.
- Local skinning artifacts (dorsal index-MCP fold etc.) are rig-level and
  cannot be fixed via the 20 controls (B2's staging scope).
- The Little finger sits ~0.1 mm into the palm skin cloud at the final pose
  (within mesh resolution; reads as firm contact, no visible interpenetration
  in renders).
- input/ (old snapshot) untouched; only input_rev7/ was referenced and it is
  read-only for this task.
