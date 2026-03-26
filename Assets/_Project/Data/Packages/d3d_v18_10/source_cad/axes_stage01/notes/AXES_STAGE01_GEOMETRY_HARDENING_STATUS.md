# Axes Stage 01 Geometry Hardening Status

## Scope

This note records the first geometry-hardening pass applied to the authored
`Axes Stage 01` slice in `d3d_v18_10`.

## Promoted In This Batch

The following runtime meshes are now sourced from OSE CAD-derived exports and are
active in the package:

- `d3d_y_left_axis_unit`
  - approved mesh: `assets/parts/d3d_y_left_axis_unit_approved.glb`
  - source method:
    - `raw/Universal8mmaxis/axis_8mm STL + FCSTD/motorSide_8mm_rods.stl`
    - `raw/Universal8mmaxis/axis_8mm STL + FCSTD/carriage_8mm_rods.stl`
    - `raw/Universal8mmaxis/axis_8mm STL + FCSTD/endstop_holder.stl`
    - dimension-authored `8 mm` rods using the recovered universal-axis rod spacing
  - important inference:
    - this is a mounted-context composite runtime mesh with carriage-centered pivot,
      not a one-click export from a single full Y-left assembly source file
- `d3d_y_right_axis_unit`
  - approved mesh: `assets/parts/d3d_y_right_axis_unit_approved.glb`
  - source method:
    - `raw/Universal8mmaxis/axis_8mm STL + FCSTD/idlerSide_8mm_rods.stl`
    - `raw/Universal8mmaxis/axis_8mm STL + FCSTD/carriage_8mm_rods.stl`
    - dimension-authored `8 mm` rods using the recovered universal-axis rod spacing
  - important inference:
    - this is a mounted-context composite runtime mesh with carriage-centered pivot,
      not a one-click export from a single full Y-right assembly source file

- `d3d_x_axis_motor_holder_unit`
  - approved mesh: `assets/parts/d3d_x_axis_motor_holder_unit_approved.glb`
  - source selection: `raw/Universal_axis_motor_side.fcstd` -> `Pocket009`
- `d3d_x_axis_idler_unit`
  - approved mesh: `assets/parts/d3d_x_axis_idler_unit_approved.glb`
  - source selection:
    `raw/Universal_Axis_Idler_Side_short_version.fcstd` -> `Pocket020`
- `d3d_x_axis_rod_pair`
  - approved mesh: `assets/parts/d3d_x_axis_rod_pair_approved.glb`
  - source selection:
    `source_cad/extruder_stage03/raw/Oseextruder1902_with_2nd_bracket.fcstd` -> `Rods`
  - pair spacing recovered from:
    `source_cad/extruder_stage03/exported/reports/Oseextruder1902_with_2nd_bracket.fcstd_report.json`
- `fastener_x_axis_belt_peg`
  - approved mesh: `assets/parts/d3d_x_axis_belt_peg_approved.glb`
  - source selection:
    `raw/Universal8mmaxis.zip` -> `axis_8mm STL + FCSTD/peg_8mm_rods.stl`
- external `M6x30` axis-mount bolts
  - approved mesh: `assets/parts/d3d_axis_mount_m6x30_bolt_approved.glb`
  - source method: dimension-authored Blender CLI mesh using the source-backed
    nominal `M6 x 30` callout from the Axes deck
  - current profile: `ISO 4762 / DIN 912` style socket-head cap screw
  - exact locked dimensions:
    - shaft diameter `6 mm`
    - shaft length `30 mm`
    - head diameter `10 mm`
    - head length `6 mm`
    - internal hex socket `5 mm`
    - socket depth `3 mm`
  - remaining realism gap:
    - runtime still does not use explicit socket-center / engagement-depth metadata for a
      true insert -> rotate -> retract fastener action

These runtime meshes were then:

1. exported from FreeCAD as STLs
2. normalized in Blender CLI with centered pivots and authored base materials
3. packed with `gltfpack -noq -cc`
4. switched into the active D3D package templates in `machine.json`

## Resulting Package State

`Axes Stage 01` is now hardened through the first mounted universal-axis envelope:

- the Y-left unit is now a CAD-derived composite runtime mesh
- the Y-right unit is now a CAD-derived composite runtime mesh
- the X-axis motor-holder block is CAD-derived
- the X-axis idler block is CAD-derived
- the X-axis rod pair is CAD-derived
- the belt peg is CAD-derived
- the external `M6x30` axis-mount bolts are explicit socket-head hardware

### External mount hardware

The authored procedure no longer treats the external mount bolts as implicit fastening logic.
They are now explicit loose parts with a real `5 mm` hex socket, but the runtime still needs
socket-center engagement metadata before the Allen key can feel fully inserted instead of
only visually aligned.

## Next Hardening Targets

The next axis hardening pass should focus on:

1. validating the hardened Y-unit mounted offsets in-editor against the explicit bolt targets
2. refining the bolt head/contact relationship if the carriage-face clearance still reads wrong
3. expanding beyond `Axes Stage 01` only after the current mounted-context fit is stable

## Related Notes

- `AXES_STAGE01_PROVENANCE.md`
- `AXES_SOURCE_EXTRACTION.md`
- `SOURCE_NOTES.md`
