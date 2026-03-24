# D3D Extruder Carriage-Mount Slice Extraction

## Purpose

This note defines the defensible post-`Extruder Stage 01` carriage-mount slice for
`d3d_v18_10`.

It answers:

- what the next honest extruder continuation is
- which mount-stack parts are supported strongly enough to author next
- what still remains deferred before full printer integration
- which exact ids and step sequence anchor the authored Stage 02 slice

## Status

This slice is now authored in `machine.json` as `Extruder Stage 02`.

It is the implemented authored boundary after:

- `Axes Stage 01`
- `Extruder Stage 01`

## Sources Consulted

- 3D Printer Manual
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- 3D Printer Genealogy
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Genealogy
- 3D Printer Extruder
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
- D3D v19.06
  - https://wiki.opensourceecology.org/wiki/D3D_v19.06
- D3D v19.02
  - https://wiki.opensourceecology.org/wiki/D3D_v19.02

## Hard Facts Extracted

### v18.10 authority

`D3D v18.10` remains authoritative for:

- the printer revision we are simulating
- the fact that the machine uses the Titan Aero path
- the known mount-space problems recorded during that revision

### v18.10 mount problem statements

The `3D Printer Genealogy` page records the key v18.10 issues:

- the 2016 extruder holder consumed bed space
- extruder mounting should recover additional vertical space
- that recovery should come with horizontal X-axis mounting

Those statements are strong enough to justify the next slice being about the
carriage-side extruder mount, not about bed, electronics, or generic final assembly.

### Strongest exact mount-stack signal

The `3D Printer Extruder` page explicitly lists the carriage-side parts used across
the v18.12-v19.02 working-doc range:

- stock Titan Aero mount bracket
- Titan Aero mount top plate
- simplified carriage (`Universal axis carriage side.fcstd`)
- sensor holder
- 8 mm sensor
- blower / fan-side files

The same page also distinguishes:

- `Underslung Extruder v18.12`
- `Underslung Extruder v19.02`
- `Overslung Extruder v19.02.22`

That matters because it means the broader extruder-family parts are real and stable,
while the exact final overslung/underslung integration path changes across revisions.

### Strongest current continuation

`D3D v19.06` repeats the same relevant extruder mount-stack:

- `Oseextruder1902.fcstd`
- `Nozzleassembly.fcstd`
- `Bracket.fcstd`
- `Brackettop.fcstd`
- `Universal axis carriage side.fcstd`
- `8mmsensor.fcstd`
- `5015blower.fcstd`
- `Extruderspacer.fcstd`

This is enough to lock a carriage-side mount subassembly.

It is not enough to claim the final `v18.10` printer-wide extruder orientation without
explicitly marking the remaining inference boundary.

## Chosen Slice

The concrete source-backed slice is:

- `Extruder Stage 02: assemble the carriage-side Titan Aero mount stack`

This means:

- simplified carriage
- stock Titan Aero bracket
- Titan Aero top plate
- completed nozzle / fan / sensor module from Stage 01

This slice should stop before:

- final mounted position on the printer X-axis
- any claim about the exact final overslung/underslung production path
- wiring or endstop integration

## Why This Is The Honest Boundary

This slice is the right next move because:

- the carriage-side mount parts are explicitly listed in source
- v18.10 explicitly calls out mount-space optimization as a real issue
- the completed nozzle module from Stage 01 is already authored and available as an
  input subassembly
- the exact printer-wide mounted orientation is still variant-sensitive

So the mount-stack assembly itself is strong enough to author now, while the final
on-printer integration is still a later slice.

## Locked Variant Policy

The next authored expansion should lock these assumptions:

- `d3d_v18_10` stays on the Titan Aero path
- Stage 02 assembles the carriage-side mount stack only
- `3D Printer Extruder` and later v19 pages are supporting sources for:
  - mount-stack decomposition
  - part naming
  - carriage-side printed-part set
- these later pages are **not** authority to claim that `d3d_v18_10` should already be
  authored as the final overslung v19.02.22 arrangement

## Recommended Next Top-Level Ids

- `assembly_d3d_extruder_stage_02`
- `subassembly_extruder_carriage_mount_stack`
- `subassembly_extruder_carriage_unit`

## Recommended First-Pass Parts

New first-pass parts:

- `d3d_extruder_mount_bracket`
- `d3d_extruder_mount_top_plate`
- `d3d_extruder_simplified_carriage`

Already-authored Stage 01 inputs:

- `d3d_titan_aero_core`
- `d3d_extruder_nozzle_assembly`
- `d3d_extruder_5015_blower`
- `d3d_extruder_sensor_holder`
- `d3d_extruder_8mm_sensor`

Resulting Stage 02 input subassembly:

- `subassembly_extruder_nozzle_module`

## Recommended Tool Set

Keep this conservative:

- `tool_hex_driver_metric_small`
- `tool_allen_key_metric`

Do not add a broader wiring/electronics tool set yet.

## Concrete Authored-Slice Brief

### Slice id

- `extruder_phase_02_carriage_mount_stack`

### Slice name

- `Assemble the carriage-side Titan Aero mount`

### Preconditions

This slice assumes:

- `Extruder Stage 01` complete
- the nozzle / fan / sensor module exists
- first-pass carriage-side mount parts are available

### Output state

At slice completion:

- the Titan Aero module is attached to a carriage-side bracket/top-plate stack
- the simplified carriage is represented
- the resulting unit is ready for later X-axis/printer integration

It does **not** yet promise:

- final on-printer mounted orientation
- exact spacer usage
- final cable-chain or wiring routing

## Recommended Step Sequence

This is the recommended first-pass procedure order:

1. stage the simplified carriage
2. place the stock Titan Aero mount bracket
3. secure the bracket
4. place the Titan Aero mount top plate
5. secure the top plate
6. place the completed nozzle module onto the carriage-side mount stack
7. confirm mount clearance and service access

## Recommended Exact Ids

### Step ids

- `step_stage_extruder_simplified_carriage`
- `step_place_titan_aero_mount_bracket`
- `step_secure_titan_aero_mount_bracket`
- `step_place_titan_aero_mount_top_plate`
- `step_secure_titan_aero_mount_top_plate`
- `step_mount_extruder_module_to_carriage_stack`
- `step_check_extruder_carriage_mount_clearance`

### Target ids

- `target_extruder_simplified_carriage_stage`
- `target_extruder_mount_bracket_attach`
- `target_extruder_mount_bracket_fastener_a`
- `target_extruder_mount_bracket_fastener_b`
- `target_extruder_mount_top_plate_attach`
- `target_extruder_mount_top_plate_fastener_a`
- `target_extruder_mount_top_plate_fastener_b`
- `target_extruder_module_to_carriage_mount`
- `target_extruder_mount_clearance_body`
- `target_extruder_mount_clearance_nozzle`
- `target_extruder_mount_clearance_sensor`

## Runtime Impact

This slice should remain inside the current family set:

- `Place`
- `Use`
- `Confirm`

No new interaction contract is required yet.

That is important: the next slice is still a normal carriage-side subassembly build,
not a new runtime feature.

## Remaining Deferred Boundary

Still defer:

- exact final overslung vs underslung on-printer position
- exact use of `Extruderspacer.fcstd`
- final carriage-to-X-axis mounted relationship
- wiring / cable-chain routing

Those belong to the next slice after Stage 02, not this one.
