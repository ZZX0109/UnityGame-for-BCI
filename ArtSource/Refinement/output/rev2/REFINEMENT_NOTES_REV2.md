# Refinement rev2 — implementation notes (GLM task B, 2026-11-18)

## What changed vs rev1 (output/rev2/, rev1 kept at output/ root)

1. **True palm-normal cameras**: palm/back/side/fist views are now placed on
   the real palm surface normal — cross(wristTail→indexMCP,
   wristTail→littleMCP), per side. rev1 used the metacarpal HEADS, which all
   sit at the wrist joint in this rig (zero vectors → degenerate cross
   product → the camera fell back to a side-on view and the wrist cap faced
   the camera). Frame up-vector is wrist→middle-tip, so five fingers, nails
   and wrist are fully visible with margins. Back view is the exact opposite
   normal; side view looks from the thumb side.
2. **Region-aware weights**: each vertex only joins its own finger chain
   (metacarpal + 3 phalanx bones) plus Wrist/Forearm, chosen by nearest chain
   distance; one topology-neighbour smoothing pass relaxes borders, then rows
   renormalize. This removes the cross-finger thin-sheet/fold artifact from
   nearest-3-over-all-bones (the rev1 rule, which was the same global rule as
   the baseline and therefore not a fix).
3. **Crease-band densify + rounding**: after the linear pass, edges inside
   the knuckle rings, thumb web and palm-root bands are subdivided once more
   (vertices land where weights transition), and a 30% neighbour-average
   surface round pass rounds finger contours. 13646 tris/hand — in-band.
4. **Material**: highlight tamed (specular 0.25, roughness fallback 0.55 for
   unlinked sockets), baked magenta neutralized with a multiply node. The
   magenta hue itself comes from the baked diffuse in the BASELINE .blend
   (MakeHuman base color); a real fix means re-baking from a corrected
   source texture — out of scope for this pass, documented instead.

## Files (output/rev2/)

- `refined2_{r,l}_{palm,back,side,fist}.png` — 8 readable views
- `refined_hands.blend` — refined source at rest
- `refinement-summary.json` — tri counts, palm normal per hand

## Still unverified / not claimed

- Deformation under the real HandRigDriver in Unity (Codex run), final art
  sign-off, and fist/pinch naturalness once A's axis mapping lands — none of
  these are claimed PASS. The magenta base hue remains (bake-source fix
  pending; A owns the source textures).
- DHS 402 still blocks re-review.
