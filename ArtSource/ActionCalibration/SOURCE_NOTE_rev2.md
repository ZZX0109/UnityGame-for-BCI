# Rev2 author presets — handoff note (A2, 2026-11-18)

## Deliverables (all under ArtSource/ActionCalibration/output/rev2/)
- `hand_rig_profile.json` — candidate profile: channels identical to
  `input/hand_rig_profile.json`; samples `open` / `fist` / `pinch` authored v2.
- `rev2_fist_palm.png`, `rev2_fist_side_thumb.png`, `rev2_fist_side_pinky.png`
- `rev2_pinch_palm.png`, `rev2_pinch_contact.png`

## Source (honest labeling)
- Samples are AUTHOR PRESETS with limited visual refinement, NOT solver output.
  Gesture targets were authored in degrees per channel and converted to 0..1
  controls through the input profile's rest/scale/sign; the written controls are
  decoded back through the same channels and re-verified (verify_rev2.py, PASS).
- The old author_presets.py hardcoded a 50-deg DIP normalization (wrong for the
  input scales 109/90/87/119). v2 has no second angle table and no post-hoc
  clamp: unreachable targets are reported and explicitly capped at the channel
  limit (see ADJUST_LOG below).
- Palm side was determined geometrically with a small positive-flexion motion
  (probe_v2.py): the meta-plane normal points PALM-side (PALMSIGN=+1); the
  delivered palm views look at that normal. No sign flips, no bone-axis changes,
  no model rebuilds.
- Thumb opposition direction (channel 3, Thumb1.Y): negative delta moves the
  thumb toward the fingers. Verified by A/B renders (flipped version rejected:
  thumb splays away, tip gap 0.14 vs 0.078).

## Channel-3 sign in these controls
- fist ch03 = 0.2750 (0.5 - 36/160), pinch ch03 = 0.1125 (0.5 - 62/160).

## Known adjustments (target -> delivered, per ADJUST_LOG)
- Index/Middle/Ring MCP (ch04/08/12): authored 78-82 deg, channel limit 75 deg -> control 1.0.
- Index/Ring PIP (ch05/13): authored 86-88 deg in an earlier pass — final PIP
  targets (85/85) are within limits; final fist controls ch05=ch13=1.0 (85 deg).
- All 20 controls decode to exactly the authored (or capped) degrees; audit
  printed OK for every channel in both Blender and the independent verify script.

## Unverified / not claimed
- Unity-side playback and visual acceptance NOT tested here — Codex must load
  output/rev2/hand_rig_profile.json with HandRigProfile and check in-editor.
- No DHS review, no final art PASS claimed.
- Minor dorsal skin-fold at index MCP in fist views is a skinning artifact of
  the shared rig, present at this pose; not fixable via the 20 controls.
- The side views use orthographic cameras; the "pinky side" view is included
  for completeness — palm and thumb-side views are the reference for acceptance.
- Old outputs in output/ (v1 solver previews, *_author.png) remain for
  provenance only; they are superseded by output/rev2/.

## Scripts (this directory)
- probe_v2.py   — palm-side determination probe (kept for record)
- author_presets_v2.py — full pipeline: degree tables -> controls -> writes
  output/rev2/hand_rig_profile.json -> re-reads that file -> renders previews
  driven ONLY by the re-read samples (JSON and images are same-source by construction)
- verify_rev2.py — independent (non-Blender) channel/decode check, PASS
