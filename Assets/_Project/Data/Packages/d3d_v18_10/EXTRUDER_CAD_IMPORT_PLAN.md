# D3D Extruder CAD Import Plan

## Purpose

This note turns the D3D extruder CAD situation into an executable import plan for
`d3d_v18_10`.

It answers:

- which current placeholder parts should be replaced first
- which OSE file pages map to those parts
- which assets are safe to harden now
- which assets must wait until the exact `v18.10` carriage-mount variant is locked
- what local folder structure and acceptance checks should be used

## Current Package Parts To Replace

The current first-pass extruder slice in `machine.json` uses these placeholder parts:

- `d3d_titan_aero_core`
- `d3d_extruder_nozzle_assembly`
- `d3d_extruder_5015_blower`
- `d3d_extruder_sensor_holder`
- `d3d_extruder_8mm_sensor`

These are the first targets for geometry hardening.

## Safe Replacement Order

### Wave 1: safe to replace now

These can be hardened without locking the final carriage-mount variant.

1. `d3d_extruder_sensor_holder`
2. `d3d_extruder_8mm_sensor`
3. `d3d_extruder_5015_blower`
4. fan holder / duct support geometry

### Wave 2: replace after first inspection pass

These are usable soon, but should be checked against the nozzle-module layout after
Wave 1 imports land.

5. `d3d_extruder_nozzle_assembly`
6. `d3d_titan_aero_core`

### Wave 3: defer until mount variant is locked

Do not hard-commit these until the exact `v18.10` carriage-side mount path is locked.

7. `Titan Aero mount bracket`
8. `Titan Aero mount top plate`
9. `simplified carriage`
10. `extruder spacer`

## Exact OSE Acquisition Targets

### Wave 1 targets

#### Sensor holder

- Current package part:
  - `d3d_extruder_sensor_holder`
- Primary OSE source:
  - `File:Sensholder.fcstd`
- Supporting source:
  - `Titanaeromarcin.fcstd`
- Source index page:
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder

#### 8 mm sensor

- Current package part:
  - `d3d_extruder_8mm_sensor`
- Primary OSE source:
  - https://wiki.opensourceecology.org/wiki/File%3A8mmsensor.fcstd
- Notes:
  - the file page explicitly states a GitHub copy also exists in the
    `OpenSourceEcology/3D-Printer-Part-Library` path

#### 5015 blower

- Current package part:
  - `d3d_extruder_5015_blower`
- Primary OSE source:
  - `File:5015blower.fcstd`
- Source index page:
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder

#### Fan holder / duct references

- These do not map to a currently separate package part yet, but should be acquired as
  support references for the blower side:
  - `File:Fanholder.fcstd`
  - `File:Fanduct.fcstd`
- Source index page:
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder

### Wave 2 targets

#### Titan Aero body / motor core

- Current package part:
  - `d3d_titan_aero_core`
- Supporting OSE source pages:
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
  - https://wiki.opensourceecology.org/wiki/D3D_v19.02
- Notes:
  - this is acceptable as a visual/body hardening target
  - do not treat the raw mesh as final mount authority by itself

#### Nozzle assembly

- Current package part:
  - `d3d_extruder_nozzle_assembly`
- Supporting OSE source pages:
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
  - https://wiki.opensourceecology.org/wiki/D3D_v19.04
- Notes:
  - use this to improve nozzle-module appearance and nozzle-side proportions
  - still check final offsets against the module layout already authored in the package

### Wave 3 deferred targets

#### Carriage-side bracket and mount pieces

- `File:Bracket.fcstd`
- `File:Brackettop.fcstd`
- `File:Universal axis carriage side.fcstd`
- possible `Extruderspacer.fcstd` depending on the revision path

Relevant source pages:

- https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
- https://wiki.opensourceecology.org/wiki/D3D_v19.02
- https://wiki.opensourceecology.org/wiki/File%3AUniversal_axis_carriage_side.fcstd

These should not replace package geometry yet until the exact `v18.10` carriage-side
mounting variant is locked.

## Local Folder Structure

Do not drop raw downloads directly into `assets/parts/`.

Use this structure:

- `Assets/_Project/Data/Packages/d3d_v18_10/source_cad/extruder/raw/`
  - original `FCStd`, STL, STEP, and downloaded reference files
- `Assets/_Project/Data/Packages/d3d_v18_10/source_cad/extruder/notes/`
  - per-file provenance and dimension notes
- `Assets/_Project/Data/Packages/d3d_v18_10/assets/parts/`
  - only approved runtime-ready meshes used by the package

Recommended filename policy:

- keep the original OSE filename in `raw/`
- use package-stable runtime names in `assets/parts/`
- one approved runtime mesh per current package part id where possible

## Import Workflow

For each Wave 1 file:

1. download the original OSE CAD source into `source_cad/extruder/raw/`
2. record:
   - source URL
   - file name
   - revision note if shown on the file page
3. inspect in FreeCAD or Blender
4. export a runtime-friendly mesh only after checking:
   - orientation
   - scale
   - origin
   - triangle count
5. place the runtime mesh in `assets/parts/`
6. update the package part template or part asset reference only after the mesh passes
   the acceptance checklist below

## Acceptance Checklist

Every imported replacement should pass all of these:

1. provenance locked
- source URL recorded
- file name recorded
- revision notes recorded

2. dimensions checked
- mesh bounds checked against known nominal dimensions where available
- any inferred dimensions explicitly noted

3. origin and orientation checked
- upright orientation consistent with current package conventions
- placement origin suitable for authored `playPosition` / `targetPlacement`

4. fit checked
- imported mesh does not force changes to authored relationships without a reason
- if a change is needed, document whether the package transform or the mesh origin was
  wrong

5. runtime readiness checked
- triangle count is reasonable
- no hidden non-uniform scale is required at runtime
- no obviously broken normals or missing faces

## Part Mapping Table

| Package Part | Replace First? | OSE Source | Status |
|---|---|---|---|
| `d3d_extruder_sensor_holder` | Yes | `Sensholder.fcstd` / `Titanaeromarcin.fcstd` | Candidate GLB exported, cleanup required |
| `d3d_extruder_8mm_sensor` | Yes | `File:8mmsensor.fcstd` | Approved mesh promoted |
| `d3d_extruder_5015_blower` | Yes | `File:5015blower.fcstd` | Approved mesh promoted |
| `d3d_extruder_nozzle_assembly` | After Wave 1 | extruder/nozzle references on OSE extruder pages | Acquire second |
| `d3d_titan_aero_core` | After Wave 1 | Titan Aero with motor references | Acquire second |
| carriage mount parts | No | `Bracket.fcstd`, `Brackettop.fcstd`, `Universal axis carriage side.fcstd` | Defer |

## Recommended Immediate Next Action

Wave 1 candidate conversion is complete. The next action is review and selective promotion of the remaining holder-side refs:

1. review `Sensholder.glb` for cleanup or scope mismatch
2. keep `Fanholder.glb` and `Fanduct.glb` as blower-side support references
3. promote only approved meshes into `assets/parts/`
4. then move on to Titan Aero core hardening

Current raw acquisition status:

- `Sensholder.fcstd` - acquired
- `8mmsensor.fcstd` - acquired
- `5015blower.fcstd` - acquired
- `Fanholder.fcstd` - acquired
- `Fanduct.fcstd` - acquired

See `source_cad/extruder/notes/WAVE1_PROVENANCE.md` for exact download URLs and file
sizes.
See `source_cad/extruder/notes/WAVE1_CONVERSION_STATUS.md` for the first-pass exported
candidate mesh results.

Already promoted into active package use:

- `assets/parts/d3d_8mm_sensor_approved.glb`
- `assets/parts/d3d_extruder_blower_approved.glb`

Only after that should the package move on to Titan Aero core hardening and then the
separate carriage-mount extraction.
