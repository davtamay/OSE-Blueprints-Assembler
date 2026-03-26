# D3D Extruder Stage 03 Conversion Status

## Toolchain Used

Stage 03 conversion was executed with:

- `C:\Program Files\FreeCAD 1.0\bin\python.exe`
- `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`

Scripts reused:

- `source_cad/extruder/scripts/export_fcstd_to_stl.py`
- `source_cad/extruder/scripts/stl_to_glb.py`

## Outputs Produced

Intermediate STL exports:

- `source_cad/extruder_stage03/exported/stl/Axis_half_carriage.stl`
- `source_cad/extruder_stage03/exported/stl/Extruderspacer.stl`
- `source_cad/extruder_stage03/exported/stl/Universal_axis_carriage_side.stl`
- `source_cad/extruder_stage03/exported/stl/Oseextruder1902_with_2nd_bracket.stl`

Candidate runtime meshes:

- `source_cad/extruder_stage03/exported/glb_candidates/Axis_half_carriage.glb`
- `source_cad/extruder_stage03/exported/glb_candidates/Extruderspacer.glb`
- `source_cad/extruder_stage03/exported/glb_candidates/Universal_axis_carriage_side.glb`
- `source_cad/extruder_stage03/exported/glb_candidates/Oseextruder1902_with_2nd_bracket.glb`

Per-file reports:

- `source_cad/extruder_stage03/exported/reports/*.fcstd_report.json`
- `source_cad/extruder_stage03/exported/reports/*.glb_report.json`

## Initial Dimensional Read

Blender-side candidate bounds after mm-to-m scaling:

- `Axis_half_carriage.glb`
  - `46.1 x 73.6 x 23.6 mm`
- `Extruderspacer.glb`
  - `61.8 x 39.9 x 18.0 mm`
- `Universal_axis_carriage_side.glb`
  - `52.0 x 74.0 x 12.0 mm`
- `Oseextruder1902_with_2nd_bracket.glb`
  - `203.2 x 107.0 x 204.9 mm`

## Key Inspection Findings

### Universal axis carriage side

This is a strong candidate for eventual direct package promotion.

Why:

- one clean compound body
- dimensions line up with the known carriage-side envelope
- file-page notes explicitly tie it to the X-axis extruder path

### Axis half carriage

This is also a strong candidate for eventual direct package promotion.

Why:

- compact single-purpose clamp-side geometry
- dimensions and file-page notes clearly align with bearing-side carriage closure

### Extruder spacer

This should not be promoted blindly.

Why:

- the FCStd contains two bodies separated vertically, not one obvious installed spacer
- that strongly suggests a print-layout or duplicate-body file, not a clean one-to-one assembly part
- the file page itself does not explain count, placement, or mandatory use

Practical conclusion:

- keep the spacer in the evidence set
- do not author its installed role into `machine.json` until the assembly relationship is tighter

### OSE extruder with second bracket

This is useful reference geometry, but not a safe direct runtime part candidate.

Why:

- it is a full contextual assembly, not one package-stable part
- it includes rods, carriage, motor, nozzle-side content, fan-side content, and mount geometry together
- its current best value is as a geometry reference that confirms:
  - the carriage-side unit exists as a mounted assembly
  - a second bracket is part of the mounting path

## Current Gate

Stage 03 is no longer blocked by missing carriage-side CAD.

The remaining gate is narrower:

1. lock whether `Extruderspacer` is mandatory or optional for the chosen path
2. decide whether Stage 03 should use:
   - just `Universal axis carriage side + half carriage + completed extruder carriage unit`
   - or that set plus a spacer placement step
3. keep fastener detail conservative unless stronger sequence evidence is extracted

## Recommended Immediate Next Action

The next honest authoring step is no longer generic extraction.

It is:

1. use the acquired CAD to write an exact Stage 03 authoring brief
2. keep the spacer role deferred unless stronger evidence appears
3. author the next procedure slice around:
   - carriage-side interface
   - half-carriage closure
   - travel-clearance QC
