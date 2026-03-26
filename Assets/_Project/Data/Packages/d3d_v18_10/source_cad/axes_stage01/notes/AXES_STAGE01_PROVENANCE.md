# Axes Stage 01 Provenance

## Scope

This note records the first CAD acquisition batch used to harden the D3D `Axes Stage 01`
placeholder geometry.

## Acquired Source Files

- `raw/Universal_axis_motor_side.fcstd`
  - downloaded from OSE wiki redirect:
    - `https://wiki.opensourceecology.org/wiki/Special:Redirect/file/Universal_axis_motor_side.fcstd`
- `raw/Universal_Axis_Idler_Side_short_version.fcstd`
  - downloaded from OSE wiki redirect:
    - `https://wiki.opensourceecology.org/wiki/Special:Redirect/file/Universal_Axis_Idler_Side_short_version.fcstd`
- `raw/Peg_8mm_rods.fcstd`
  - downloaded from OSE wiki redirect:
    - `https://wiki.opensourceecology.org/wiki/Special:Redirect/file/Peg_8mm_rods.fcstd`
- `raw/Universal8mmaxis.zip`
  - downloaded from OSE wiki redirect:
    - `https://wiki.opensourceecology.org/wiki/Special:Redirect/file/Universal8mmaxis.zip`

## Selected Runtime Sources

### X-axis motor-holder unit

- selected source document:
  - `raw/Universal_axis_motor_side.fcstd`
- selected export object:
  - `Pocket009`
- export output:
  - `exported/stl/Universal_axis_motor_side_selected.stl`

### X-axis idler unit

- selected source document:
  - `raw/Universal_Axis_Idler_Side_short_version.fcstd`
- selected export object:
  - `Pocket020`
  - label: `Magnet Holes`
- export output:
  - `exported/stl/Universal_Axis_Idler_Side_short_version_selected.stl`

### X-axis belt peg

- selected source archive:
  - `raw/Universal8mmaxis.zip`
- selected file from archive:
  - `axis_8mm STL + FCSTD/peg_8mm_rods.stl`
- copied export:
  - `exported/stl/Peg_8mm_rods.stl`

### X-axis rod pair

- selected source document:
  - `source_cad/extruder_stage03/raw/Oseextruder1902_with_2nd_bracket.fcstd`
- selected export object:
  - `Pad`
  - label: `Rods`
- export output:
  - `exported/stl/XAxis_rod_pair_selected.stl`
- pair-spacing source:
  - `source_cad/extruder_stage03/exported/reports/Oseextruder1902_with_2nd_bracket.fcstd_report.json`
  - full-assembly rod-pair span used to recover the center spacing for the two-rod runtime mesh

### External M6x30 axis-mount bolts

- source-backed nominal callout:
  - `Axes` deck references `[2] M6x30 bolts` through `Y-right` into the idler
  - `Axes` deck references `[2] M6x30 bolts` through `Y-left` into the motor holder
- runtime mesh method:
  - `scripts/build_m6x30_bolt_glb.py`
  - dimension-authored in Blender CLI as an `ISO 4762 / DIN 912` style socket-head cap screw
- exact locked dimensions:
  - shaft diameter `6 mm`
  - shaft length `30 mm`
- standard-profile dimensions:
  - head diameter `10 mm`
  - head length `6 mm`
  - internal hex socket size `5 mm`
  - socket depth `3 mm`
- important inference:
  - the D3D source locks the `M6 x 30` callout, but not a specific fastener standard line
  - `ISO 4762 / DIN 912` socket-head geometry was chosen because the active tool path is
    a metric Allen key and the resulting head/socket proportions are standard, precise,
    and mechanically compatible with that tool

### Y-left axis unit

- selected source archive:
  - `raw/Universal8mmaxis.zip`
- selected files from archive:
  - `axis_8mm STL + FCSTD/motorSide_8mm_rods.stl`
  - `axis_8mm STL + FCSTD/carriage_8mm_rods.stl`
  - `axis_8mm STL + FCSTD/endstop_holder.stl`
- runtime mesh method:
  - `scripts/build_y_axis_unit_glb.py`
  - mounts the motor-side block as top/bottom end blocks
  - centers the carriage at the runtime origin
  - authors vertical `8 mm` rods with the recovered universal-axis spacing
  - adds the endstop holder as a left-only mounted-context detail
- important inference:
  - the full mounted Y-left runtime unit is a composite derived from source parts plus
    source-backed rod dimensions, not a one-file full-assembly export

### Y-right axis unit

- selected source archive:
  - `raw/Universal8mmaxis.zip`
- selected files from archive:
  - `axis_8mm STL + FCSTD/idlerSide_8mm_rods.stl`
  - `axis_8mm STL + FCSTD/carriage_8mm_rods.stl`
- runtime mesh method:
  - `scripts/build_y_axis_unit_glb.py`
  - mounts the idler-side block as top/bottom end blocks
  - centers the carriage at the runtime origin
  - authors vertical `8 mm` rods with the recovered universal-axis spacing
- important inference:
  - the full mounted Y-right runtime unit is a composite derived from source parts plus
    source-backed rod dimensions, not a one-file full-assembly export

## Why the Y-axis side units were composite-built in this batch

The current local source set is strong enough to harden the mounted Y-axis units only as
composite runtime meshes assembled from the raw universal-axis parts:

- the Y-left mounted unit
- the Y-right mounted unit
- the X-axis motor-holder block
- the X-axis idler block
- the X-axis rod pair
- the belt peg

It is still not strong enough to claim that these Y-unit meshes are perfect one-to-one
exports of a single exact `v18.10` mounted assembly file. The hardened assets now use the
best local mounted-context composite available from the source set.
