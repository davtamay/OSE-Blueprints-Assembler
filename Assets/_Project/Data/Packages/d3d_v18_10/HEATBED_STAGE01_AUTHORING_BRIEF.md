# D3D Heated Bed Stage 01 Authoring Brief

## Purpose

This note turns the heated-bed extraction into a concrete next authoring brief for
`d3d_v18_10`.

It answers:

- what the first honest heated-bed slice is
- which heated-bed parts are strong enough to include first
- what must remain deferred
- which ids and step sequence should anchor the first authored pass

## Implementation Status

`Heated Bed Stage 01` is not yet authored in `machine.json`.

This brief locks the next conservative mechanical-first heated-bed slice after:

- `Axes Stage 01`
- `Extruder Stages 01-03`

## Sources Consulted

- 3D Printer Manual
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- 3D Printer Genealogy
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Genealogy
- D3D v19.04
  - https://wiki.opensourceecology.org/wiki/D3D_v19.04
- D3D v19.06
  - https://wiki.opensourceecology.org/wiki/D3D_v19.06
- D3D Universal with Dual Z-Axis 3D CAD
  - https://wiki.opensourceecology.org/wiki/D3D_Universal_with_Dual_Z-Axis_3D_CAD

## Locked Conclusion

The next honest authored slice is:

- `Heated Bed Stage 01: stage the bed body and first mechanical retainers before any electrical heater details`

This slice should include only the mechanical bed path that is strong enough from
the current sources:

- heatbed body
- snap-buckle mounting parts
- bed-envelope / nozzle-clearance quality check

This slice must stop before:

- heater wiring
- thermistor routing
- control-panel integration
- PSU / SSR / MOSFET wiring
- claims that the later insulated bed stack is a one-to-one exact `v18.10` body

## Why This Is The Honest Boundary

This is the correct next step because:

- the `3D Printer Manual` explicitly places `Heated Bed` before both control-panel
  modules
- the strongest later-source heated-bed files are mechanical:
  - `Heatbed body1904.fcstd`
  - `Heatbed snapbuckle1904.fcstd`
  - `Heatbed wirelock.fcstd`
  - `D3Dfinalassemblyv1902.fcstd`
- the current package already reaches the first printer-side extruder mount, so the
  next major machine structure is the bed system

This boundary stays conservative because:

- bed size and frame relationships changed across nearby revisions
- the strongest bed-body files are later than `v18.10`
- the electrical heater implementation is more revision-sensitive than the
  mechanical bed family

## Locked Variant Policy

The first authored heated-bed pass should lock these assumptions:

- `d3d_v18_10` remains the authoritative printer revision
- later-source bed files are supporting geometry/mechanical references only
- the first heated-bed slice is mechanical-first
- the actual electrical heater stack remains deferred

The first authored heated-bed pass must not claim:

- final exact `v18.10` insulated bed-body equivalence
- full electrical heater implementation
- final cable routing

## Recommended Top-Level Ids

- `assembly_d3d_heatbed_stage_01`
- `subassembly_heatbed_mounting_path`
- `subassembly_heatbed_mechanical_stack`

## Recommended First-Pass Parts

New first-pass parts:

- `d3d_heatbed_body`
- `d3d_heatbed_snapbuckle_left`
- `d3d_heatbed_snapbuckle_right`

Possible deferred parts, pending CAD inspection:

- `d3d_heatbed_wirelock`
- explicit heater plate layering
- insulated under-stack
- fastener families if the CAD does not make them clean enough to author honestly

## Recommended Tool Set

Keep the tool set conservative and provisional until CAD inspection resolves the
fastener family:

- `tool_allen_key_metric`

If CAD inspection shows a different mounting/fastener system, revise the tool set
before writing `machine.json`.

## Concrete Authored-Slice Brief

### Slice id

- `heatbed_phase_01_mechanical_mount_path`

### Slice name

- `Assemble the first heated-bed mechanical path`

### Preconditions

This slice assumes:

- `Extruder Stage 03` complete
- printer frame and first X/Y axis relationships already present
- printer-side extruder carriage is already mounted
- the heated-bed CAD set has been inspected enough to justify the chosen part family

### Output state

At slice completion:

- a first mechanical bed body is staged in the printer
- first snap-buckle retainers are placed
- the bed-envelope / nozzle-clearance relationship is checkable

This slice does not yet promise:

- powered heater wiring
- thermistor routing
- control-panel links
- final electronics commissioning

## Recommended Step Sequence

This is the conservative first-pass step order:

1. stage the heatbed body
2. place the left snap-buckle
3. secure the left snap-buckle
4. place the right snap-buckle
5. secure the right snap-buckle
6. confirm bed-envelope / nozzle-side clearance

This sequence intentionally keeps wire-lock out of the first authored pass unless
its installed role is isolated more cleanly than the current CAD inspection
supports.

## Recommended Step Families

Stage 01 should stay inside the current runtime families:

- `Place`
- `Use`
- `Confirm`

No new runtime family should be introduced for the first bed slice.

## Recommended Part / Target Policy

### Heatbed body

Use one explicit authored part:

- `d3d_heatbed_body`

It should be a real mounted body, not a generic flat teaching panel.

### Snap-buckles

Use two explicit authored parts first:

- `d3d_heatbed_snapbuckle_left`
- `d3d_heatbed_snapbuckle_right`

This keeps the slice readable and mechanical.

### Wire-lock

Keep `d3d_heatbed_wirelock` deferred from the first authored pass unless its
installed role is isolated more cleanly than the current CAD inspection supports.

## Recommended Step / Target / Tool Ids

### Assembly ids

- `assembly_d3d_heatbed_stage_01`
- `subassembly_heatbed_mounting_path`
- `subassembly_heatbed_mechanical_stack`

### Parts

- `d3d_heatbed_body`
- `d3d_heatbed_snapbuckle_left`
- `d3d_heatbed_snapbuckle_right`

### Steps

- `step_stage_heatbed_body`
- `step_place_heatbed_snapbuckle_left`
- `step_secure_heatbed_snapbuckle_left`
- `step_place_heatbed_snapbuckle_right`
- `step_secure_heatbed_snapbuckle_right`
- `step_check_heatbed_envelope_clearance`

### Targets

- `target_heatbed_body_mount`
- `target_heatbed_snapbuckle_left`
- `target_heatbed_snapbuckle_right`
- `target_heatbed_clearance_left`
- `target_heatbed_clearance_right`
- `target_heatbed_nozzle_clearance`

### Tools

- `tool_allen_key_metric`

## Geometry / Runtime Standard

The first bed slice should follow the same acceptance rule used on the current
axes/extruder work:

- harden only the fit-critical bed parts for this slice
- do not harden the whole bed/electronics stack first
- keep all staged parts physically supported, not floating in empty space

This means:

- the bed body must rest on an authored support/mount context
- snap-buckles must seat on real contact faces
- wire-lock must be authored as a retained physical part, not a floating symbol

## Explicit Inferences

These are the only intended inferences in the first authored pass:

- the later-source bed-body family is used as a conservative mechanical reference
- two first-pass snap-buckles are enough to teach the initial mounting path

These inferences must remain documented and must not be silently upgraded to
historical claims about exact `v18.10` final bed implementation.

## Blocking Questions Before `machine.json`

The first authoring pass should not start until these are answered from acquired
CAD or direct inspection:

1. is `Heatbed body1904` usable as the first mounted body reference without
   conflicting with the `v18.10` frame/bed relationship
2. do the snap-buckle files imply explicit fasteners that must appear in Stage 01
3. what is the correct first-pass nozzle / bed-envelope clearance target frame

## Recommended Next Tasks

Before touching `machine.json`:

1. inspect the acquired CAD set:
   - `Heatbed body1904.fcstd`
   - `Heatbed snapbuckle1904.fcstd`
   - `Heatbed wirelock.fcstd`
   - `D3Dfinalassemblyv1902.fcstd`
2. confirm the first mounted bed-body relationship
3. confirm whether explicit fasteners belong in Stage 01
4. then author `Heated Bed Stage 01` conservatively from this brief
