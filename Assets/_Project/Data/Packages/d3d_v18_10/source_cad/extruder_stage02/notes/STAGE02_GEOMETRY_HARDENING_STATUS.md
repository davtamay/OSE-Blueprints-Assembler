# D3D Extruder Geometry Hardening Status

## Scope

This note tracks which fit-critical extruder parts are now backed by OSE CAD-derived
runtime meshes and which geometry risks remain after the current hardening pass.

## Source Files Acquired

- `raw_extra/Bracket.fcstd`
- `raw_extra/Brackettop.fcstd`
- `raw_extra/Nozzleassembly.fcstd`
- `../extruder/raw/Sensholder.fcstd`
- `../extruder_stage03/raw/Oseextruder1902_with_2nd_bracket.fcstd`

## Approved Runtime Promotions

These have been promoted into active package use:

- `assets/parts/d3d_titan_aero_core_approved.glb`
  - source selection: `Stepper Motor` + `Titan Aero Volcano`
  - source document: `source_cad/extruder_stage03/raw/Oseextruder1902_with_2nd_bracket.fcstd`
  - centered Blender bounds: approximately `59 x 87 x 77 mm`
- `assets/parts/d3d_extruder_nozzle_assembly_approved.glb`
  - source selection: `Compound`
  - source document: `source_cad/extruder_stage02/raw_extra/Nozzleassembly.fcstd`
  - centered Blender bounds: approximately `73 x 42 x 80 mm`
- `assets/parts/d3d_extruder_sensor_holder_approved.glb`
  - source selection: `Sensor Holder Final` + `Sensor Holder Gusset`
  - source document: `source_cad/extruder/raw/Sensholder.fcstd`
  - centered Blender bounds: approximately `86 x 67 x 66 mm`
- `assets/parts/d3d_extruder_mount_bracket_approved.glb`
  - source candidate: `source_cad/extruder_stage02/exported/glb_candidates/Bracket.glb`
  - Blender bounds: approximately `62 x 44 x 40 mm`
- `assets/parts/d3d_extruder_mount_top_plate_approved.glb`
  - source candidate: `source_cad/extruder_stage02/exported/glb_candidates/Brackettop.glb`
  - Blender bounds: approximately `62 x 79 x 14 mm`

Both approved meshes were packed with project-standard `gltfpack` settings:

- `-noq -cc`

## Current Active State

Active CAD-derived extruder meshes now include:

- `d3d_titan_aero_core_approved.glb`
- `d3d_extruder_nozzle_assembly_approved.glb`
- `d3d_extruder_sensor_holder_approved.glb`
- `d3d_8mm_sensor_approved.glb`
- `d3d_extruder_blower_approved.glb`
- `d3d_extruder_mount_bracket_approved.glb`
- `d3d_extruder_mount_top_plate_approved.glb`

The package now uses real mesh scale (`1,1,1`) for all of the fit-critical extruder parts
above. The earlier tiny placeholder scale factors have been removed from staged placements,
target ghosts, and integrated extruder member poses.

## Next Gate

Before claiming the full extruder slice is CAD-accurate in-editor, the next gate is no longer
missing geometry. The next gate is fit verification:

1. confirm the centered approved meshes sit correctly in Stage 01 / 02 / 03
2. verify carriage-side and X-axis mounted offsets with the real meshes active
3. decide whether the `Extruderspacer` should be introduced in a later refinement pass
