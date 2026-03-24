# D3D Extruder Wave 1 Conversion Status

## Toolchain Used

Wave 1 conversion was executed with:

- `C:\Program Files\FreeCAD 1.0\bin\python.exe`
- `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`
- `tools/gltfpack.exe`

Scripts used:

- `source_cad/extruder/scripts/export_fcstd_to_stl.py`
- `source_cad/extruder/scripts/stl_to_glb.py`

## Outputs Produced

Intermediate STL exports:

- `source_cad/extruder/exported/stl/8mmsensor.stl`
- `source_cad/extruder/exported/stl/Sensholder.stl`
- `source_cad/extruder/exported/stl/5015blower.stl`
- `source_cad/extruder/exported/stl/Fanholder.stl`
- `source_cad/extruder/exported/stl/Fanduct.stl`

Candidate runtime meshes:

- `source_cad/extruder/exported/glb_candidates/8mmsensor.glb`
- `source_cad/extruder/exported/glb_candidates/Sensholder.glb`
- `source_cad/extruder/exported/glb_candidates/5015blower.glb`
- `source_cad/extruder/exported/glb_candidates/Fanholder.glb`
- `source_cad/extruder/exported/glb_candidates/Fanduct.glb`

Per-file reports:

- `source_cad/extruder/exported/reports/*_freecad.json`
- `source_cad/extruder/exported/reports/*_blender.json`
- `assets/parts/d3d_8mm_sensor_approved.glb.gltfpack_report.json`
- `assets/parts/d3d_extruder_blower_approved.glb.gltfpack_report.json`

## Initial Dimensional Read

Blender-side candidate bounds after mm-to-m scaling:

- `8mmsensor.glb`
  - `23.1 x 78.1 x 28.4 mm`
- `5015blower.glb`
  - `32.1 x 84.6 x 80.3 mm`
- `Fanduct.glb`
  - `25.3 x 17.6 x 25.2 mm`
- `Sensholder.glb`
  - `214.2 x 250.2 x 208.2 mm`
- `Fanholder.glb`
  - `121.7 x 154.2 x 59.4 mm`

## Assessment

These look immediately plausible as direct package-part candidates:

- `8mmsensor.glb`
- `5015blower.glb`

These have now been promoted into active package use as:

- `assets/parts/d3d_8mm_sensor_approved.glb`
- `assets/parts/d3d_extruder_blower_approved.glb`

Active-package mesh compression applied with project-standard `gltfpack` settings:

- `-noq -cc`
- `d3d_8mm_sensor_approved.glb`
  - `1,940,576` bytes -> `927,568` bytes
- `d3d_extruder_blower_approved.glb`
  - `30,292` bytes -> `13,416` bytes

These are useful reference exports, but should not be promoted blindly:

- `Fanduct.glb`
- `Fanholder.glb`

These need manual inspection or cleanup before runtime use:

- `Sensholder.glb`

Reason:

- the exported `Sensholder` candidate appears far larger than a simple sensor holder
- the exported `Fanholder` candidate also reads more like a broader support assembly than a
  direct one-to-one replacement for a single currently authored part

## Current Gate

Wave 1 is no longer blocked by missing tools.

The current gate is review quality for the remaining holder-side exports:

1. inspect the candidate meshes in Blender
2. verify origin and upright orientation
3. decide which candidates are one-to-one replacements
4. only then copy approved meshes into `assets/parts/` and update `machine.json`

## Recommended Immediate Next Action

The two low-risk candidates have already been promoted:

1. `8mmsensor.glb`
2. `5015blower.glb`

Keep `Sensholder`, `Fanholder`, and `Fanduct` in the review lane until their fit and scope are
confirmed.
