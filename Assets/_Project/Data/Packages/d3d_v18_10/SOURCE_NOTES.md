# D3D v18.10 Source Notes

## Scope

This package now covers all six D3D frame sides as source-backed layup panels.

The frame sides are authored in separate work zones so learners can focus on one 14 inch
square panel at a time. That raised, tightened layout is an instructional staging choice.
The panel geometry, overlap, measurements, and square-check targets remain tied to the
real machine.

This package still stops before final cube joining, clamping, and welding sequence work.

## Primary Sources

- https://wiki.opensourceecology.org/wiki/D3D
- https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- https://wiki.opensourceecology.org/wiki/Frame_Construction_Set

## Source-Backed Dimensions

The OSE Frame Construction Set notes state:

- use 1/8 inch x 1 inch flat stock
- cut stock to 13 inch lengths
- overlap at the corners to produce a 14 inch square frame

Converted dimensions used in this package:

- flat bar length: 13.0 in = 330.2 mm = 0.3302 m
- flat bar width: 1.0 in = 25.4 mm = 0.0254 m
- flat bar thickness: 1/8 in = 3.175 mm = 0.003175 m
- finished outer square side: 14.0 in = 355.6 mm = 0.3556 m
- target center offset from frame center to each bar centerline:
  - 14.0 / 2 - 1.0 / 2 = 6.5 in = 165.1 mm = 0.1651 m

## Real-World Placement Logic

Every authored panel uses the same source-backed target geometry:

- top bar center: `(0.0, 0.0015875, 0.1651)`
- right bar center: `(0.1651, 0.0015875, 0.0)`, rotated 90 degrees about Y
- bottom bar center: `(0.0, 0.0015875, -0.1651)`
- left bar center: `(-0.1651, 0.0015875, 0.0)`, rotated 90 degrees about Y

Those placements preserve the 1 inch corner overlap described by the OSE frame notes.

The six authored work zones are centered at:

- bottom side: `(-0.38, 0.5515875, 0.42)`
- top side: `(0.38, 0.5515875, 0.42)`
- left side: `(-0.38, 0.5515875, 0.0)`
- right side: `(0.38, 0.5515875, 0.0)`
- front side: `(-0.38, 0.5515875, -0.42)`
- rear side: `(0.38, 0.5515875, -0.42)`

These offsets are instructional staging positions only. They do not change the real panel geometry.

## Appearance Notes

The package assumes plain mild steel flat bar with a dark hot-rolled or lightly ground mill finish:

- rectangular strip profile
- crisp straight-cut ends
- no holes
- no logos
- no paint or powder coat
- no decorative bevels or rounded toy-like edges

## Meshy Prompt Brief

Use one shared part model for all 24 bars.

Recommended prompt:

`Mild steel flat bar for OSE D3D printer frame, exact rectangular strip, 13 inches long, 1 inch wide, 1/8 inch thick, hot-rolled dark steel mill finish, sharp straight saw-cut ends, no holes, no logos, no text, no background, no people, simple industrial part, longest axis along X, thickness along Y.`

Recommended negative constraints:

- no rust holes
- no rounded toy proportions
- no decorative chamfers
- no extra brackets
- no bolts or welds attached

Current asset status:

- Shared preview GLB generated into `assets/parts/d3d_frame_flat_bar.glb`
- Generated from the prompt above through the local Meshy wrapper
- Raw Meshy bounds are not yet proportion-clean enough for runtime use
- The authored package currently stays on primitive fallback until an approved flat-bar asset is reviewed and saved as `assets/parts/d3d_frame_flat_bar_approved.glb`

## Known Gaps

- The source used here defines stock size and resulting square geometry, but not every workshop-specific jig, clamp, or weld order.
- Final cube joining, clamping, and tack sequence still need dedicated source-backed authoring.
- Exact photographic appearance for a specific v18.10 workshop build still needs reference-image confirmation before final Meshy generation.
- The current generated candidate GLB is a research artifact, not the approved runtime asset.
