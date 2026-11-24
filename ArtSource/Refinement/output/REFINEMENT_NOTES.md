# Refinement pass — implementation notes (GLM task B, 2026-11-18)

## What was produced (ArtSource/Refinement/output/)

- `refined_hands.blend` — refined editable source at REST (same rest rules as
  baseline; pose cleared before save).
- `Hand_R_refined.fbx` / `Hand_L_refined.fbx` — wrist-local exports through
  the baseline rule (parent/world matrices cached first, 0.19 m wrist→middle
  tip metric, identity root, wrist verified at component origin; metric
  0.1192 m per source unit, same value as the baseline exporter reports).
- `refined_{r,l}_{palm,back,side}.png` — rest review set (6).
- `refined_{r,l}_{fist,pinch}.png` — deformed closeups (4).
- `refinement-summary.json` — tri counts before/after, export provenance.
- `build/` — intermediate bake outputs from the in-memory session.

## What the refinement changes (and only this)

1. **Subdivision**: baseline 3414 tris/hand → **13622 tris/hand** (one whole-
   mesh edge-subdivide pass, quad grid fill, single cut). Inside the
   contracted 10–20k band. No new topology direction — flow preserved.
2. **Weights rebuilt** on the denser mesh with the baseline's own production
   rule (nearest 3 bones, inverse-cube falloff, tip vertices beyond 0.25
   blended 60/40 into the parent segment). This removes the hard metacarpal
   crease seen in run13, which came from distance weights over a sparse mesh.
3. **Smooth shading** on all faces (baseline was flat, hence the faceted
   look).
4. **Material**: non-metal skin; the baseline .blend's baked textures are
   repacked (stale //../../Assets paths repointed at baseline copies) and the
   baked magenta is neutralized with a Mix(RGB→skin tone) node; subsurface
   kept at a subtle 0.05 red-shift. Diffuse/roughness links from the bake
   are preserved.
5. **Armature untouched**: bone names, hierarchy, rest matrices, curl axes,
   bone lengths — identical to baseline. Pose actions reused as-authored.

## Preserved invariants

- Wrist local identity + metric length (0.19 m) export rule unchanged, same
  code path as `reexport_wrist_local.py` (cache-then-bake, no double offsets).
- Continuous single skinned mesh per hand — no capsules, no static swaps.
- Baseline directory, canonical build_hands.py, Hands/, Models/,
  Resources/Rig, Driver, Core, data exports — all read-only for this pass.

## Not verified / not claimed

- Not run in Unity: deformation quality under the real HandRigDriver, bone
  axis mapping (A is reworking multi-axis mapping), and final art approval
  remain Codex's to verify after merge.
- The captures in `Results/unity-rig-preview/run13/` were produced with the
  baseline mesh; re-running the preview against the refined FBX is the next
  integration step (Codex).
- No accuracy/art PASS is claimed anywhere.

## How to re-run

```
D:/SoftwareManagement/apps/Blender/blender.exe --background --factory-startup \
  --python ArtSource/Refinement/refine_hands.py
```
