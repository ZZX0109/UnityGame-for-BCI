> Historical first-iteration report. Superseded by [AUDIT_IMPLEMENTATION_2026-11-13.md](AUDIT_IMPLEMENTATION_2026-11-13.md), which implements the expanded S01–S03 audit scope.

# S01 M0+M1 first implementation — 2026-11-12

## Entry and scope

Open this project in **Unity 2022.3.62f3c1 (China)**, open `Assets/StreetCrossing/Scenes/StreetCrossingScene.unity`, and press Play. The scene references its own bootstrap, editable S01 scenario, phase table, retained Standard surface material, sedan collision prefab and bundled Chinese font. `Street Crossing/Open S01 Scene` and `Street Crossing/Validate S01 Assets` are available in the Editor. The original PuzzleScene remains enabled and first in Build Settings; the street scene is appended. `Street Crossing/Build S01 Windows x64` builds only the street entry to `BuildOutput/StreetCrossing/Windows/StreetCrossing.exe` (override using `STREET_BUILD_OUTPUT`).

This is one complete **first-person, continuous-walking S01 crossing prototype**, scoped to the branch's M0+M1 task. It does not introduce S02/S03, turning traffic, NPC pedestrians, a city map, VR, EEG, UDP, hardware services or clinical assessments. Existing Sokoban assets, scripts, save keys and packages are untouched.

## Implemented behavior

- Warm daylight residential street, believable scale, two opposing lanes, stop lines, crosswalk, tactile waiting paving and flush curb opening, cream residential façades, shop destination, bus shelter, trees, signs, collision geometry and detailed simple sedans. Procedural art is original. World signs resize to their physical backing bounds after font layout.
- CharacterController movement aligned with camera yaw: WASD, mouse look with pitch limits, Shift bounded fast walk, E line-of-sight inspection within 3 m, Esc pause/settings, R restart confirmation. Eye height 1.65 m, default 1.35 m/s, comfort 1.05 m/s, fast walk 1.85 m/s, FOV 75–85 degrees. No jumping, bob or dynamic blur.
- One authoritative signal controller and 20 ms world clock: VehicleGreen → VehicleAmber → AllRedBeforeWalk → Walk → WalkFlashing → AllRedAfterWalk. Editable phase data explicitly admits only the matching traffic. Amber decisions latch per vehicle. Vehicles that committed before red retain clearance until their **rear bumper** leaves the conflict area, including while still upstream. Walk cannot begin before they clear.
- Red stop-line targets, conservative reaction margin, bounded acceleration/deceleration, front/rear queue spacing and pooled far-end recycling. S01 caps traffic at two cars, one per direction. Spawn sequence has a dedicated seeded RNG and reproducible reset; UI/audio do not consume it. Queued amber vehicles that stop fully before the line release their unused reservation.
- Pedestrian occupancy prevents new vehicle admission; all-red after Walk extends until the roadway clears. A watchdog reports prolonged occupancy and retains red, never forces cars into an occupied crossing. WalkFlashing permits an existing crossing to finish and rejects new legal entry.
- Explicit approach, kerb, entry, roadway conflict, exit and shop destination volumes are sampled centrally, avoiding trigger-order ambiguity. Crossing entry phase is latched; the opposite shop destination is required. Wrong-signal entry, leaving the marked path, dangerous moving/closing encounters, actual collider contact, timeout and arrival are distinct. A collision freezes immediately with a non-graphic result. Touching even a stopped car currently counts as physical contact and needs usability review.
- Camera heading must remain in each left/right sector at the near kerb for 0.35 s to record observation; events are not invented by movement or button taps. These are heading metrics, not gaze or medical diagnoses. Safety, observation and absence of near misses determine three stars; completion speed gives no reward.
- Main menu, compact HUD, pause, restart confirmation, result explanation, sensitivity/FOV/comfort/fast-walk/volume/text/fullscreen settings, isolated `StreetCrossing.*` preference/progress keys and per-session UTF-8 JSONL logs under `Application.persistentDataPath/StreetCrossing/Logs`. Logging failure is shown in results. Log records contain session/scenario/attempt, UTC and simulation time, position, phase and event detail. Optional original gentle walk-start cue supplements world signals.
- `IStreetInput` is a local adapter boundary for future accessibility/BCI inputs; only the legacy Input Manager adapter ships. Vehicle kinematics, phase timing, observation and evaluation rules are separated from presentation.

## Validation evidence and limits

The source baseline was hydrated blob-for-blob against remote branch head `96c55b2dd9dcbd5b36a21ee3b9cfb23fe9a2a0f1`, actual tree `6914a5790e4d137b9c3ddad6f123e995d6592226` (zero mismatches). Local hydration commit IDs differ from GitHub because the starting repository was a local snapshot; the baseline tree is identical.

Executed here:

```text
python3 Tools/validate_project.py
python3 docs/Game03_StreetCrossing/tools/build_street_font.py --check
python3 docs/Game03_StreetCrossing/tools/validate_street.py
```

Read-only checks cover asset/meta presence, unique GUIDs and resolved references, importer types, assembly JSON, both build-scene paths, S01 data and its comfortable-speed window, bundled font bytes/license/coverage, isolated namespace/dependency scope and C# delimiter balance. These are **static checks**, not compilation or gameplay evidence.

Included Unity tests: 22 EditMode methods / 24 cases for phase order/boundaries, paused and invalid clock deltas, ten authorization cycles, both clearance interlocks/watchdog, stop distance/gradual braking/queue/restart, amber reservation, full-body and slow upstream clearance, seeded generation, heading dwell, latched entry/vehicle conflict, route and event/outcome deduplication. One PlayMode smoke test loads the entry, checks first-person eye height, pauses the clock/cars/signals and resets twenty times without duplicate active cars.

**No Unity Editor or C# compiler is installed in this environment. Unity import, compilation, these NUnit tests, actual scene rendering, manual S01 completion, Windows build, performance, screenshots and GUI video are unverified. No XML, video, player ZIP or engine phase trace is fabricated.** Run and retain those outputs against the published implementation SHA before calling this accepted or release-ready.

Example Editor CLI commands (replace `Unity` with the installed exact Editor executable):

```text
Unity -batchmode -nographics -projectPath PROJECT -runTests -testPlatform EditMode -assemblyNames StreetCrossing.EditModeTests -testResults TestResults/StreetCrossing-EditMode.xml -logFile TestResults/StreetCrossing-EditMode.log
Unity -batchmode -nographics -projectPath PROJECT -runTests -testPlatform PlayMode -assemblyNames StreetCrossing.PlayModeTests -testResults TestResults/StreetCrossing-PlayMode.xml -logFile TestResults/StreetCrossing-PlayMode.log
Unity -batchmode -nographics -quit -projectPath PROJECT -executeMethod StreetCrossing.Editor.StreetSceneBuilder.BuildWindows -logFile TestResults/StreetCrossing-WindowsBuild.log
```

## Required manual acceptance

1. Start S01 and verify a ground-level first-person view, legible shop/pedestrian signs, visible left and right traffic, route/ramp accessibility and car/building collision.
2. Wait at the near tactile paving; turn left and right for at least 0.35 s each, then face the crosswalk. On Walk, verify both directions have stopped before crossing. Reach the shop mat and confirm safe arrival and observations.
3. Enter near the end of Walk and finish through WalkFlashing/all-red. Cars must wait until the roadway is empty. Stop on the roadway beyond a full cycle: all-red must hold and report a watchdog rather than force release.
4. Enter on red/flashing, leave the marked crossing, bypass the route and contact a moving/stopped car in separate attempts. Confirm distinct explanatory outcomes, one event per encounter and no duplicate results. Repeat twenty restarts; confirm cars, seed, clock and observations reset together.
5. Watch ten cycles for conflicting permissions, stop-line creep, instant braking, queue penetration, unexpected pool teleportation or stuck amber reservations. Inspect the real JSONL phase/spawn log.
6. Test Esc, R/cancel/confirm, settings and focus-loss pause; frozen traffic and signals must resume together. Check 1280×720, 1366×768, 1920×1080 and 1280×1024 for clipping and text legibility. Record a complete S01 playthrough and Console output.
7. Build Windows x64, manually complete S01 in that build, record frame times/GC/memory and verify shader/font inclusion. The 60 FPS setting is a target, not measured performance.

## Known limits

Prototype traffic timing is not a claim of compliance with any region's road law. Art uses built-in primitives and runtime assembly, so visual proportions/readability still require Editor tuning. The lone optional tone is not a full traffic ambience/footstep package. Inspection text currently includes object names. S01 uses straight two-way traffic only; NPC animation, turning yield, additional scenarios and clinical/hardware validation are deferred as designed. The deterministic world clock caps long render stalls at 0.1 s to prevent large unsafe motion jumps; it measures simulation time, not elapsed wall time.

Dedicated font: Street UI Sans, renamed SC subset of Noto Sans CJK 2.004, full SIL OFL 1.1 and attribution bundled in `Assets/StreetCrossing/Resources/StreetCrossing/Fonts/LICENSE.txt`. Rebuild with the documented script if runtime text changes. No downloaded models, textures or audio are introduced.
