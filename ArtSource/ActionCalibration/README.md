# GLM A: staged action calibration

Only write scripts and candidate outputs inside ArtSource/ActionCalibration. Do not write canonical Assets, main build_hands.py, Hands, Driver, Refinement, Core, data or root status.

input/ is a read-only snapshot of actual canonical GLM B rev4 FBXs and the positive-flexion shared profile. output/hand_rig_profile.json must use the same schema/20-control meanings. Produce readable palm/side fist/pinch previews and a brief source note. Keep authored presets separate from optimization results. Do not regenerate/export canonical models or launch Unity. Codex integrates candidates after handoff.

Run20 negative-flexion samples remain preserved in Results/a-handoff-20261118_0654 and Results/unity-rig-preview/run20. Production was restored to the prior positive channel mapping and actual run19 controls; these old presets are still not natural-gesture acceptance. Do not infer palm-side sign from thumb CMC alone or treat a small min gap as pose quality.
