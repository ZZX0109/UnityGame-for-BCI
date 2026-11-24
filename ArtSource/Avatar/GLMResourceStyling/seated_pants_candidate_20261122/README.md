# Seated pants repair candidate — GLM run 2026-11-22

**Status: `CANDIDATE_COMPLETE_PENDING_HOST_ACCEPTANCE`** — geometric gates pass, visual self-review pass with one borderline note (see below). Not verified in Unity/PICO/VR.

## Deliverables

- `neutral_adult_seated_pants_candidate.blend` — candidate (frozen source copy + mask-only edit)
- `renders/{source,candidate}/{source,candidate}_{front,side,threequarter,firstperson}.png` — fixed-camera pairs, seated pose thigh −80°/calf +70°, root drop 0.35 m, EEVEE 64 samples, body fully visible
- `evidence/baseline_render_equivalent.json` — frozen-source crossings at RENDER-equivalent topology (subsurf level 1: 815 verts / 1568 tris)
- `evidence/candidate_render_equivalent.json` — candidate crossings, same method, includes per-pose outside-region audit
- `events/<pose>/{body_pants,sweater_pants}.json` — every crossing event with mean world point and outside-region flag
- `edit-manifest.json`, `scripts/`, run logs

## Method (final = v4)

Uniform rigid let-out: the mean separation vector (closest-point-on-body along surface normal, 6 mm target gap) computed at the +70° pose over the approved 80-vertex mask (52 primary + 28 one-ring, 4 crotch-seam verts protected/excluded), mapped to base-object space through the averaged blended skinning matrix, applied rigidly to all 76 editable mask vertices. Single pass, zero smoothing, topology/weights/modifiers/materials untouched. Frozen source re-verified unchanged.

Rejected attempts retained as evidence: v1 radial push (widened crotch, more skin), v2 posed-space separation (LBS rotation broke it, +27%), v3 iterative LBS-inverse with relaxation (silhouette serration at mask border).

## Results (same BVH + triangle-SAT, render-equivalent topology)

| Pair | Pose | Source | Candidate | Δ |
|---|---|---:|---:|---|
| body↔pants | −80/+70 | 768 | **277** | −63.9% |
| body↔pants | −80/+80 | 824 | **335** | −59.3% |
| sweater↔pants | −80/+70 | 495 | **401** | −19% |
| sweater↔pants | −80/+80 | 495 | **401** | −19% |

Outside approved mask region: **0 events** in all four audits. Note: render-equivalent counts (768/824) differ from run-04's viewport counts (284/342) because subdivided topology yields more events; viewport IDs were never reused for render.

## Visual self-review (visual-judge subagent unavailable — provider error `provider-not-found: GLM-5.3-Flash`; fallback performed by the same GLM session that produced the candidate, so independence is limited)

- Front: crotch/inner-thigh exposure unchanged vs source; original outer-thigh skin patches remain (pre-existing defect, out of approved mask). Pass.
- Side: smooth silhouette, no step/crease/bulge; marginally more skin flecks at knee bend inside the one-ring feather. Borderline pass — flagged for host review.
- Three-quarter: near-identical to source; volume and looseness preserved. Pass.
- First person: knee outlines visible in lower field, center view unobstructed. Pass.

## Limits

- Blender-only candidate; Unity/PICO/VR explicitly **not** verified.
- Render-level topology verified via viewport-levels raised to render level 1 (equivalent graph to RENDER); a literal `RENDER` depsgraph walk was not separately executed.
- +80° stress pose improves but retains crossings (335 events); the remaining deep crotch/knee-bend contacts would need topology changes beyond the approved mask, which the handoff forbids.
- Visual review is self-performed (no independent judge available this round).
