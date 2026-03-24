# D3D v18.10 Source Notes

## Scope

This package now covers all six D3D frame sides as source-backed layup, square-check,
hold-down, sequential corner tack-weld panels, first-pass cube stacking of the finished panels,
the initial cube alignment / corner-tack sequence, the first cube seam-weld / cleanup / recheck pass,
a short frame-acceptance phase, and a first authored axes stage.

The frame sides are authored in separate work zones so learners can focus on one 14 inch
square panel at a time. That raised, tightened layout is an instructional staging choice.
The panel geometry, overlap, measurements, and square-check targets remain tied to the
real machine.

This package now includes the placement of the finished panels into the open cube layout,
the first cube square-check, opposite-corner hold-down, lower/upper cube corner tacks,
lower/upper seam-weld passes, one grinder cleanup pass on the upper mounting joints, one
post-cleanup square check, a short acceptance phase, and one first-pass axes stage, but it
still stops before full weld-process detail, exact axis geometry, and later machine subsystems.

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

Every authored panel uses the same source-backed square geometry, but now with an
explicit overlap stack instead of impossible coplanar intersection:

- right bar center: `(0.1651, 0.0015875, 0.0)`, rotated 90 degrees about Y
- left bar center: `(-0.1651, 0.0015875, 0.0)`, rotated 90 degrees about Y
- top bar center: `(0.0, 0.0047625, 0.1651)`
- bottom bar center: `(0.0, 0.0047625, -0.1651)`

This keeps the 1 inch corner overlap described by the OSE frame notes while representing
the overlap as doubled flat-bar thickness at the corners instead of impossible coplanar geometry.

The six authored work zones are centered at:

- bottom side: `(-0.38, 0.5515875, 0.42)`
- top side: `(0.38, 0.5515875, 0.42)`
- left side: `(-0.38, 0.5515875, 0.0)`
- right side: `(0.38, 0.5515875, 0.0)`
- front side: `(-0.38, 0.5515875, -0.42)`
- rear side: `(0.38, 0.5515875, -0.42)`

These offsets are instructional staging positions only. They do not change the real panel geometry.

## Procedure Extension

The OSE Frame Construction Set notes explicitly state that the frame elements are welded and
that it is easier to produce six sides and then weld those sides into a cube.

This package now represents the realistic intermediate procedure that follows from that source:

- lay out one 14 inch square side from four 13 inch flat bars
- verify the side is square before heat is introduced
- hold the panel flat
- place short tack welds at the four overlap corners

Those four corner tacks are authored as one sequential weld step per panel rather than four
separate step cards. That keeps the procedure realistic without fragmenting one continuous tack-up
operation into four artificial step boundaries.

The authored tack order alternates corners rather than moving around the perimeter in a loop:

- upper-left
- lower-right
- upper-right
- lower-left

That order is an authoring inference based on common distortion-control practice. The OSE source
used here does not prescribe a corner-by-corner tack order.

The authored hold-down interaction is represented with a locking clamp tool. That is also an
authoring inference: the source confirms the need to weld the panel after squaring it, but does
not prescribe one exact clamp, magnet, or jig hardware choice for this stage.

## Cube Stacking Geometry

The package now adds a first-pass cube stacking phase after the six panels are fabricated.

This uses one explicit authored subassembly frame per finished panel. Each panel keeps its
member bars as first-class parts, but the learner places the completed panel as a rigid unit.

The cube target layout is an authoring inference built from the same 14 inch square side size:

- cube side length: `0.3556 m`
- half-side offset used for vertical panel centers: `0.1778 m`
- bottom panel center: `(0.0, 0.5515875, 0.0)`
- left panel center: `(-0.1778, 0.7293875, 0.0)`
- right panel center: `(0.1778, 0.7293875, 0.0)`
- front panel center: `(0.0, 0.7293875, -0.1778)`
- rear panel center: `(0.0, 0.7293875, 0.1778)`
- top panel center: `(0.0, 0.9071875, 0.0)`

Panel rotations are chosen so each finished square stands in the correct cube face orientation:

- left/right panels rotate into the `YZ` plane
- front/rear panels rotate into the `XY` plane
- bottom/top remain parallel to the work surface

Important boundary:

- the OSE source used here supports the sequence \"make six sides, then weld those sides into a cube\"
- it does not prescribe these exact training-scene cube target coordinates or the exact first panel ordering
- those cube target transforms are therefore documented authoring inferences, not claimed workshop measurements

## Cube Joining Extension

After all six panels are stacked into the cube, the package now continues with a first joining pass:

- verify the stacked cube at the top-front-left corner
- pin opposite top corners
- place four lower corner tacks
- place four upper corner tacks
- place four lower seam passes
- place four upper seam passes
- recheck the welded cube
- make four light grinder cleanup passes on the upper mounting joints
- recheck the cleaned frame at two top corners
- accept the frame for later motion hardware

The authored cube tack order alternates corners rather than walking the perimeter:

- front-left
- rear-right
- front-right
- rear-left

That order is an authoring inference based on the same distortion-control logic used for the flat
panels. The OSE source used here supports welding the six sides into a cube, but it does not
prescribe one exact clamp placement, cube-corner tack order, or seam-pass order for the training scene.

The post-weld cleanup / recheck / acceptance slice is also an authoring inference. It is included
because real machine build quality depends on checking that the welded frame remains usable as a
reference for later rails and motion hardware. The local source used here does not prescribe one
exact grinder-pass sequence, cleanup criterion, or handoff gate between welding and subsystem mounting.

## Axes Stage 01

The package now extends one step beyond frame acceptance into a first authored axes slice:

- mount the Y-left axis unit to the welded frame
- confirm the Y-left axis seats cleanly
- mount the Y-right axis unit to the welded frame
- confirm the Y-axis pair establishes the X-axis span
- tighten the staged X-axis idler screws
- place the X-axis idler side, rod pair, and motor-holder side into pre-fit positions
- loosely start the motor-holder side on Y-left
- fit the X-axis span with a constrained `AxisFit` interaction
- lock the motor-holder screws
- tension the X-axis belt
- reinsert the belt peg
- check X-axis tightness, travel, and the Y-left end-stop relationship

Source-backed facts used directly for this slice:

- the D3D manual breaks the build into `Frame`, `Axes`, and `FinalAssembly`
- the X-axis mounts between the completed Y axes
- the Y axes mount directly to the frame
- the X-axis fitting order is: tighten idler, anchor on Y-right, loosely start on Y-left,
  fit exact length, lock the motor-holder screws, then tension the belt
- the belt peg is removed and reinserted during the fit sequence
- the QC logic checks travel, binding, no visible gap, and the Y-left end-stop condition

Important authored simplifications:

- the Y/X axis shapes are still schematic placeholders, not exact v18.10 meshes
- the external `M6x30` mounting bolts are still implicit in the first pass rather than
  explicit loose hardware parts
- the first pass reuses `tool_allen_key_metric` and `tool_pliers`; a dedicated low-torque
  drill interaction was deferred

See also:

- `AXES_SOURCE_EXTRACTION.md` for the exact evidence and implementation boundary for this slice

## Extruder Stages 01-02

The package now extends beyond `Axes Stage 01` into the first two authored extruder slices:

- `Extruder Stage 01`
  - stage the Titan Aero core in the extruder work zone
  - place the nozzle assembly onto the staged core
  - place the first print-cooling blower attachment
  - secure the blower attachment
  - place the sensor holder
  - secure the sensor holder
  - place the 8 mm sensor
  - check nozzle / fan / sensor clearance before later carriage integration
- `Extruder Stage 02`
  - stage the simplified carriage
  - place the stock Titan Aero mount bracket
  - secure the bracket
  - place the mount top plate
  - secure the top plate
  - mount the completed nozzle module onto the carriage-side stack
  - check carriage-side body / nozzle / sensor clearance and service access

Source-backed facts used directly for this slice:

- `d3d_v18_10` stays on the `Titan Aero` path rather than the `Prusa i3 MK2` option
- the strongest published next extruder sequence is:
  - build nozzle
  - build fan attachment
  - add sensor holder
- the extruder part-library sources explicitly name:
  - Titan Aero with motor
  - nozzle-side assembly
  - 5015 blower
  - sensor holder
  - 8 mm sensor
- the later extruder part-library and v19 sources explicitly name the carriage-side mount stack:
  - stock Titan Aero mount bracket
  - Titan Aero mount top plate
  - simplified carriage
- the `3D Printer Genealogy` page records the mount-space problem statement that justifies
  carrying the procedure forward into the carriage-side mount stack before full printer-wide
  extruder integration

Important authored simplifications:

- the Titan Aero body, nozzle assembly, carriage bracket, top plate, and simplified carriage
  are still schematic placeholders rather than exact `v18.10` meshes
- Wave 1 approved CAD promotions are now active only for:
  - the 8 mm sensor
  - the 5015 blower
- the current authored scope now stops after the carriage-side mount stack, before final
  carriage-to-X-axis / printer-wide extruder integration
- the exact `v18.10` holder / spacer / final mounted-orientation variant is still deferred
- the small driver tools currently reuse the existing small-driver placeholder asset

See also:

- `EXTRUDER_SOURCE_EXTRACTION.md` for the exact evidence and implementation boundary for this slice
- `EXTRUDER_MOUNT_SOURCE_EXTRACTION.md` for the exact evidence and implementation boundary for the carriage-side mount-stack slice

## Next Source Extraction

The next source-backed slice after `Extruder Stage 02` should not jump straight into
bed, controls, or electronics. The current strongest honest continuation is the final
carriage-to-X-axis / printer-wide extruder integration, but only after the exact
`v18.10` holder, spacer, and mounted-orientation boundary is locked more precisely.

Current extraction result:

- `d3d_v18_10` should stay on the `Titan Aero` path
- the nozzle / fan / sensor module is now authored in data
- the carriage-side mount stack is now authored in data
- the exact final printer-wide extruder mount variant for `v18.10` is still not locked strongly
  enough to author honestly yet

Recommended next authoring boundary:

- the final carriage-to-X-axis / printer-wide extruder mounting slice only
- do **not** jump straight to full final assembly, heated bed, controls, or wiring

Useful source links for the next pass:

- D3D manual index: https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- D3D BOM / v18.10 BOM: https://wiki.opensourceecology.org/wiki/Folgertech_Prusa_i3_Upgrade_Cost_to_D3D
- Genealogy / v18.10 issue notes: https://wiki.opensourceecology.org/wiki/3D_Printer_Genealogy
- Extruder part library: https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
- v19.04 extruder build sequence: https://wiki.opensourceecology.org/wiki/D3D_v19.04

What is now strong enough to author next:

- the `Titan Aero` variant choice for `d3d_v18_10`
- the completed carriage-side nozzle-module stack as a subassembly target
- a later final extruder-mount procedure once the holder / spacer / mounted-orientation
  variant is locked

What still must be locked before the later final extruder-mount slice:

- exact `v18.10` carriage-side holder geometry
- exact use of the `extruder spacer`
- whether later `overslung` / `underslung` evidence is compatible enough to reuse
- exact carriage-to-X-axis mounted relationship

See also:

- `AXES_SOURCE_EXTRACTION.md` for the implemented first-axes boundary
- `EXTRUDER_SOURCE_EXTRACTION.md` for the implemented first-extruder authored-slice brief
- `EXTRUDER_MOUNT_SOURCE_EXTRACTION.md` for the implemented carriage-side extruder mount boundary
- `EXTRUDER_CAD_IMPORT_PLAN.md` for the exact OSE file acquisition order and placeholder-replacement plan
- `source_cad/extruder/notes/WAVE1_PROVENANCE.md` for the completed Wave 1 raw CAD acquisition record
- `source_cad/extruder/notes/WAVE1_CONVERSION_STATUS.md` for the completed Wave 1 candidate conversion record

Wave 1 approved promotions already in active package use:

- `assets/parts/d3d_8mm_sensor_approved.glb`
- `assets/parts/d3d_extruder_blower_approved.glb`

## Canonical Integrated Cube Representation

The runtime now uses a dual representation for the cube phase:

- the learner still manipulates a finished square panel as one rigid unit during stacking
- after placement succeeds, the visible final cube uses authored canonical member poses instead of leaving the panel shell proxy visible

Why:

- the earlier first-pass panel stacking representation could leave adjacent panel bars on coincident face-edge regions
- that caused overlap and z-fighting when all six finished panels were displayed together

The canonical integrated member poses in `previewConfig.integratedSubassemblyPlacements`
use a welded-shell display convention:

- keep the committed panel centered half a flat-bar thickness outward from the cube target plane
- keep the final visible frame visually attached with no deliberate face gap
- apply only a tiny presentation bias to the top/bottom members within each finished face
- presentation bias used: `0.0001 m`

That tiny member bias is only there to break coplanar corner ties inside a finished face.
It is small enough to avoid the visibly separated composition created by the earlier
full-thickness display offset.

Important note:

- the final integrated display is still a trainer-side approximation of six finished panels joined into a cube
- the `0.0001 m` bias is not a claimed physical weld gap or measured assembly spacing
- if the frame is later re-authored with a stricter unique-edge ownership model, this display bias should be removed

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
- Tool visuals currently reuse existing in-project assets rather than bespoke D3D tool models:
  - `tool_framing_square.glb`
  - `tool_locking_clamp.glb` (copied from the generic Power Cube clamp)
  - `tool_mig_torch.glb` (copied from the generic Power Cube welder)
  - `tool_angle_grinder.glb` (copied from the generic Power Cube grinder)
  - `tool_allen_key_metric.glb` (copied from the generic Power Cube line wrench as a first-pass placeholder)
  - `tool_pliers.glb` (copied from the generic Power Cube wire crimper as a first-pass placeholder)

## Known Gaps

- The source used here defines stock size, square geometry, and the fact that welding is required, but not every workshop-specific jig, clamp, or weld-fixture detail.
- Full weld-process detail for the assembled cube still needs dedicated source-backed expansion.
- Cleanup / grinding process detail and the first post-axes rail/gantry mounting steps still need stronger local source-backed expansion before authoring.
- `Axes Stage 01` still uses schematic placeholder geometry and implicit external mount hardware rather than exact v18.10 Y/X-axis meshes and fully explicit `M6x30` fastener handling.
- Cooling guidance and exact post-weld finishing criteria after weld-out are not authored yet.
- Exact photographic appearance for a specific v18.10 workshop build still needs reference-image confirmation before final Meshy generation.
- The current generated candidate GLB is a research artifact, not the approved runtime asset.
