# D3D First Extruder Slice Extraction

## Purpose

This note defines the first defensible post-axes authoring target for
`d3d_v18_10` after `Axes Stage 01`.

It answers:

- which extruder path is supported strongly enough to continue the procedure
- what the next exact slice should be before touching `machine.json`
- which parts, tools, and step families that slice should use
- which remaining variant decisions still block a later carriage-to-printer mount

## Implementation Status

`Extruder Stage 01` is now authored into `machine.json` as steps `53` through `60`.

What was implemented directly from this brief:

- `assembly_d3d_extruder_stage_01`
- `subassembly_titan_aero_core`
- `subassembly_extruder_sensor_and_fan_attachment`
- `subassembly_extruder_nozzle_module`
- staged Titan Aero core placement
- nozzle assembly placement
- blower placement plus secure step
- sensor-holder placement plus secure step
- 8 mm sensor placement
- final nozzle / fan / sensor clearance check

What remains intentionally deferred:

- exact `v18.10` carriage-side holder / spacer geometry
- final extruder-to-X-axis mounting
- exact `v18.10` extruder meshes beyond first-pass placeholders

## Sources Consulted

- 3D Printer Manual
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- D3D BOM / v18.10 Bill of Materials
  - https://wiki.opensourceecology.org/wiki/Folgertech_Prusa_i3_Upgrade_Cost_to_D3D
- 3D Printer Genealogy
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Genealogy
- 3D Printer Extruder
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
- D3D v19.04
  - https://wiki.opensourceecology.org/wiki/D3D_v19.04

## Hard Facts Extracted

### Manual structure

The `3D Printer Manual` explicitly breaks the printer into modules including:

- `Frame`
- `Axes`
- `Heated Bed`
- `Control Panel`
- `FinalAssembly`

That means the next step after `Axes Stage 01` does not need to jump directly to a
full printer-wide final assembly. A bounded module slice remains acceptable.

### Extruder variant signal for v18.10

The strongest variant facts available are:

- `D3D v18.10` uses `Titan Aero`
- the `D3D BOM` exposes two extruder options:
  - `E3D Titan Aero`
  - `Prusa i3 MK2`

For `d3d_v18_10`, the package should stay on the `Titan Aero` path. That is the
clearer match to the named revision and the currently documented software / design
direction.

### v18.10 problem statements

The `3D Printer Genealogy` page records the v18.10 extruder-related issues:

- the print cooling fan still needed nozzle optimization
- the 2016 extruder holder consumed bed space
- extruder mounting should recover additional vertical space with horizontal X-axis
  mounting
- the sensor holder still needed fit cleanup

These notes are important because they explain why extruder geometry and mounting
variant cannot be guessed casually from later printers.

### Strongest exact procedure signal

The strongest explicit post-axes procedure signal now available is on the extruder
path, not on a generic rail / electronics path.

The `D3D v19.04` page publishes an extruder build sequence:

1. build nozzle
2. build fan attachment
3. add sensor holder

This is the cleanest exact continuation we currently have after the first axes slice.

### Strongest exact part signal

The `3D Printer Extruder` page and the `D3D v19.04` page both expose a concrete
extruder module part stack that is relevant to the Titan Aero path:

- `OSE Extruder v19.02`
- `nozzle assembly`
- `stock Titan Aero mount bracket`
- `Titan Aero with motor`
- `Titan Aero mount top plate`
- `simplified carriage`
- `8 mm sensor`
- `5015 blower`
- `40x10 fan`
- `extruder spacer` on the later v19.04 path

This is enough to define a modular extruder slice. It is not enough to claim the
exact final `v18.10` carriage-to-X-axis interface without another variant lock.

## Chosen Next Slice

The next concrete source-backed slice should be:

- `Extruder Stage 01: build the Titan Aero nozzle / fan / sensor module`

This is narrower and more defensible than:

- full extruder-to-X-axis mounting
- full final assembly
- heated bed + controls + extruder in one jump
- a guessed carriage redesign based on later overslung variants

## Why This Slice Is The Honest Next Boundary

This slice is the best next move because:

- it has an explicit build sequence in source material
- it stays on the `Titan Aero` path already associated with `v18.10`
- it avoids pretending we already know the exact holder / spacer / overslung variant
- it lets the package continue procedurally without locking the wrong carriage
  interface

## Locked Variant Policy

The next authored expansion should lock these assumptions:

- `d3d_v18_10` follows the `Titan Aero` path, not the `Prusa i3 MK2` option
- `v18.10` remains the authority for *which* extruder family is correct
- `v19.04` and `3D Printer Extruder` are supporting sources for:
  - module decomposition
  - part naming
  - build sequence
- `v19.04` is **not** yet authority for the exact final carriage-mount geometry of
  `d3d_v18_10`

## Recommended Top-Level Ids

- `assembly_d3d_extruder_stage_01`
- `subassembly_extruder_nozzle_module`
- `subassembly_titan_aero_core`
- `subassembly_extruder_sensor_and_fan_attachment`

Meaning:

- `subassembly_titan_aero_core` is the sourced Titan Aero + motor unit
- `subassembly_extruder_sensor_and_fan_attachment` covers the cooling / sensing side
- `subassembly_extruder_nozzle_module` is the completed first authored output of this
  slice

## Concrete Authored-Slice Brief

### Recommended slice id

- `extruder_phase_01_titan_aero_nozzle_module`

### Recommended slice name

- `Build the Titan Aero nozzle, fan, and sensor module`

### Boundary

This slice should teach the first honest post-axes extruder work:

- from a completed frame and first axes stage
- to a completed nozzle-side extruder module ready for later carriage integration

This slice should **not** claim final carriage mounting yet.

### Preconditions

This slice assumes:

- welded frame complete
- `Axes Stage 01` complete
- the `Titan Aero` variant is locked
- a simplified carriage reference exists, but exact mounted geometry is still deferred

### Output state

At slice completion:

- nozzle-side extruder module exists
- print cooling fan / blower attachment exists
- sensor holder exists
- 8 mm sensor is represented
- the module is ready for later carriage / X-axis integration

It does **not** yet promise:

- exact final carriage orientation
- exact spacer usage
- final X-axis carriage mounting of the extruder

## Exact Source-Backed Core Sequence

The exact sequence supported directly by source is:

1. build nozzle
2. build fan attachment
3. add sensor holder

That three-step sequence should stay visible in any first package authoring pass.

## Recommended First-Pass Package Representation

A clean first authored pass can represent that source sequence as:

1. place / attach the nozzle assembly
2. place the print cooling fan or blower attachment
3. secure the sensor holder
4. place the 8 mm sensor
5. confirm service clearance around the nozzle / fan / sensor stack

Notes:

- steps 1 to 3 map directly to the source-backed core sequence
- step 4 is a justified elaboration because the part library names the `8 mm sensor`
  explicitly
- step 5 is a justified QC addition because v18.10 notes already call out fan / sensor
  fit issues

## Recommended Parts

Use these as the first-pass authored parts or sourced modules:

- `part_titan_aero_core`
- `part_extruder_nozzle_assembly`
- `part_extruder_sensor_holder`
- `part_extruder_8mm_sensor`
- `part_extruder_5015_blower`
- `part_extruder_40x10_fan`

Deferred until the next slice unless exact v18.10 geometry is locked:

- `part_titan_aero_mount_bracket`
- `part_titan_aero_mount_top_plate`
- `part_extruder_simplified_carriage`
- `part_extruder_spacer`

## Recommended Tools

The first pass should stay conservative:

- `tool_hex_driver_metric_small`
- `tool_screwdriver_small`

If those tool models do not exist yet, placeholder reuse is acceptable, but the data
should stay explicit.

## Recommended Step Families

This slice does not need any new runtime behavior.

Use only:

- `Place`
- `Use`
- `Confirm`

## Runtime Gap

No new runtime gap is required **if** this slice stops at the nozzle / fan / sensor
module boundary.

The later carriage-mount slice may still need a variant-aware mounting policy, but
that is not a blocker for `Extruder Stage 01`.

## Next Boundary After This Slice

With `Extruder Stage 01` now in data, the next extraction should lock:

- exact carriage-side holder geometry for `d3d_v18_10`
- whether the later `overslung` / `underslung` evidence is compatible enough to reuse
- whether the `extruder spacer` belongs to the chosen revision

Only after that should the package author:

- the carriage-side extruder mount
- final extruder-to-X-axis integration
