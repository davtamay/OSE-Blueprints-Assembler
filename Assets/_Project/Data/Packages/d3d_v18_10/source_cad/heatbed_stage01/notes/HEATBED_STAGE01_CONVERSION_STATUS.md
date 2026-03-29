# D3D Heated Bed Stage 01 Conversion Status

## Toolchain Used

Heated-bed conversion was executed with:

- `C:\Program Files\FreeCAD 1.0\bin\python.exe`
- `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`

Scripts reused:

- `source_cad/extruder/scripts/export_fcstd_to_stl.py`
- `source_cad/extruder/scripts/stl_to_glb.py`

## Outputs Produced

Intermediate STL exports:

- `source_cad/heatbed_stage01/exported/stl/D3Dfinalassemblyv1902.stl`
- `source_cad/heatbed_stage01/exported/stl/Heatbed_body1904.stl`
- `source_cad/heatbed_stage01/exported/stl/Heatbed_snapbuckle1904.stl`
- `source_cad/heatbed_stage01/exported/stl/Heatbed_wirelock.stl`

Candidate runtime meshes:

- `source_cad/heatbed_stage01/exported/glb_candidates/D3Dfinalassemblyv1902.glb`
- `source_cad/heatbed_stage01/exported/glb_candidates/Heatbed_body1904.glb`
- `source_cad/heatbed_stage01/exported/glb_candidates/Heatbed_snapbuckle1904.glb`
- `source_cad/heatbed_stage01/exported/glb_candidates/Heatbed_wirelock.glb`

Per-file reports:

- `source_cad/heatbed_stage01/exported/reports/*.fcstd_report.json`
- `source_cad/heatbed_stage01/exported/reports/*.glb_report.json`

## Initial Dimensional Read

Blender-side candidate bounds after mm-to-m scaling:

- `Heatbed_body1904.glb`
  - `203.2 x 41.3 x 203.2 mm`
- `Heatbed_snapbuckle1904.glb`
  - `12.0 x 33.9 x 10.0 mm`
- `Heatbed_wirelock.glb`
  - `142.7 x 85.5 x 17.4 mm`
- `D3Dfinalassemblyv1902.glb`
  - `454.3 x 480.9 x 453.3 mm`

## Key Inspection Findings

### Heatbed body

This is a strong candidate for eventual direct package promotion.

Why:

- compact `203.2 mm` square bed footprint
- file converts cleanly as a small mechanical family rather than a full printer
- later-source documentation explicitly ties this file to the heatbed path

Practical conclusion:

- the first authored heated-bed slice can reasonably center on this body as a
  later-source mechanical reference

### Heatbed snapbuckle

This is also a strong candidate for eventual direct package promotion.

Why:

- small, single-purpose retainer geometry
- the FCStd contains two identical shapes, which fits a left/right authored-pair
  teaching model
- dimensions are in the right range for a printed fastening/retention part

Practical conclusion:

- Stage 01 can treat the snapbuckle as a real first-pass mechanical retainer

### Heatbed wirelock

This should not be promoted blindly.

Why:

- the file contains two separated bodies with a large combined span
- the combined envelope reads more like layout/context geometry than one clean
  installed part
- the source pages confirm file existence, but not enough installed-role detail to
  make it safe for the first authored pass

Practical conclusion:

- keep wirelock in the evidence set
- defer it from the first `machine.json` bed slice unless its installed role is
  isolated more cleanly

### D3D final assembly reference

This is useful reference geometry, but not a safe direct runtime part candidate.

Why:

- it is a full contextual assembly with `61` shapes
- it includes frame, axes, carriage, bed, and other machine context together
- its value is as a reference for:
  - bed location
  - nozzle/bed clearance framing
  - overall relation to the printer frame

Practical conclusion:

- use this file as a geometry reference for target placement and QC framing
- do not promote it as a package part

## Current Gate

Heated Bed Stage 01 is no longer blocked by missing CAD.

The remaining gate is narrower:

1. lock the first mounted bed-body relationship against the final-assembly context
2. decide whether Stage 01 should use:
   - just `Heatbed_body1904 + snapbuckle pair + clearance QC`
   - or that set plus a wirelock placement step
3. keep fastener detail conservative unless the snapbuckle files make it explicit

## Recommended Immediate Next Action

The next honest authoring move is:

1. keep the Stage 01 brief mechanical-first
2. treat `Heatbed_body1904` and the snapbuckle pair as the core authored parts
3. use `D3Dfinalassemblyv1902` only as a reference frame for clearance and mounting
4. keep `Heatbed_wirelock` deferred unless a cleaner installed role is extracted
