# Street Crossing visual refinement — 2026-11-13

S01 now has a garden-walk identity with a pergola and facade planters; S02 has
commercial shopfronts, striped projecting canopies and a market-street sign;
S03 has a blue-grey corner-square identity and a wayfinding pylon. All three
retain bright morning lighting so visual theme does not introduce a new
low-visibility task condition.

## Implementation

| Source / asset | Result |
|---|---|
| `Scripts/Presentation/StreetVisualProfile.cs`, `Scenarios/S01Visual.asset`–`S03Visual.asset` | Explicit, validated identity and colour/config assets, referenced by each scenario. No traffic, geometry, timing or RNG fields live in these assets. |
| `StreetVisualDetails.cs` | Recessed market windows with frame/mullion/door pulls, original shelf/package geometry, shelter roof trim and timetable, slatted seating/bench, flush kerb drains, facade/landmark differences. The entire `Visual details - …` hierarchy has no colliders. |
| `StreetDetailMesh.cs` | Original rounded extruded mesh silhouettes with front/back/side normals and UVs, used for street furniture, lamp housings, circular lenses and pedestrian glyphs. |
| `StreetWorld.cs` | Cached rounded meshes, per-world cleanup, material caching by colour **and** smoothness; layered tree crowns, thinner visual-only crossing paint, profile-driven shop accents. Existing solid shop/frame/glazing collider envelopes are retained while replacing flat window rendering with display geometry. |
| `TrafficSignalView.cs` | Rounded housings, lens bezels/hoods, circular lenses, standing/walking pedestrian glyphs; modest 0.35 emission multiplier and high contrast shapes work without bloom. Icons, bilingual captions and vehicle lamps all read the existing `IntersectionController`; no independent phase clock. |

No downloaded models/textures were added. `Art/ORIGINAL_ART.txt` names the new
runtime mesh sources; `tools/build_original_art.py` remains responsible for the
existing OBJ, texture and prefab outputs. Runtime geometry is deterministic and
does not consume either traffic RNG or the scenario seed.

## Preserved task contracts

Compared with pinned pre-refinement commit
`2bf5aacb64380015248670811c81158185ae66a6`, each S01/S02/S03 scenario file differs
only by its new `visualProfile` reference. All previous bytes, including seed,
observation dwell, road/lane sizes, car counts/speeds/headways, destination and
spawn positions, are unchanged. All three phase tables are byte-identical.

Original left/right road observation targets are unchanged. S03's deliberately
opaque kerb shelter stays at `(-2.7, 1.1, -4.1)` with size `(0.9, 2.2, 1.4)` and
its existing collider. The safe forward repositioning path is unchanged.
The original eight signal collider envelopes (four poles, four housings) are
preserved; all newly added trim, lenses and glyphs have no colliders.

The existing visibility algorithm treats colliders as opaque. No transparent
decorative glass or additional decoration collider is introduced. The shop's
display recesses are opaque visual displays in front of the retained physical
boundary, not walk-through or see-through windows.

## Verification and remaining engine work

Executed successfully:

- `python docs/Game03_StreetCrossing/tools/validate_street.py`: project/GUID/importer
  references, existing safe windows, explicit profile wiring and bundled font coverage.
- `python docs/Game03_StreetCrossing/tools/verify_visual_contract.py`: repeatable,
  read-only comparison against the pinned commit above. It compares all original
  scenario text excluding the one `visualProfile` reference, all phase tables,
  observation-target expressions, exact S03 blocker arguments and the four signal
  position/size definitions repeated twice (eight blockers). It also rejects
  collider/primitive APIs in the new detail builder and its mesh factory. Equivalent
  decimal spellings such as `.5f` and `0.5f` are normalized for source comparisons.
  No Git fetch/checkout, generation or repository writes occur. `--baseline <commit>`
  explicitly selects a different existing local baseline when a reviewed task
  contract intentionally changes.
- `python -m unittest discover -s Tools/tests -p 'test_*.py'`: **55 Python tool
  tests passed** after main integration. These verify tooling, not Unity runtime.
- Source inspection: the detail builder contains no primitive/collider creation;
  only `VisualRounded`, which creates MeshFilter/MeshRenderer, creates its parts.

Authored engine regression cases:

- `StreetVisualProfileTests`: serialized identity + existing road/traffic baseline;
  generated housing bounds and outward-facing non-degenerate triangles.
- `StreetVisualContractTests`: all three landmark variants; no detail colliders;
  eight original signal blockers; glyph state follows the real phase authority;
  S03 blocker position/size/enabled collider remains unchanged.
- Existing `StreetAcceptanceTests` continue to cover blocked/clear S03 views,
  real input-driven S01/S02/S03 crossing, traffic clearance and reset.

**Unity import/compilation, NUnit execution, rendered screenshots/video,
Windows build/playthrough and performance are not executed in this environment.**
Static checks do not establish those outcomes. Run the shared delivery/capture
tools on the exact licensed Editor and graphics host before accepting the art.
Check near/far signal readability, window depth/no z-fighting, new silhouette
placement, road observation at both S03 waiting positions, four target resolutions,
and repeated scenario changes without mesh/material growth. Do not introduce
new street props with colliders to cure visual placement issues.
