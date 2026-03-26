# D3D Extruder Stage 03 Authoring Brief

## Purpose

This note turns the Stage 03 CAD acquisition and inspection into a concrete next
authoring brief for `d3d_v18_10`.

## Implementation Status

`Extruder Stage 03` is now authored in `machine.json` as steps `68` through `71`.

What was implemented directly from this brief:

- completed `subassembly_extruder_carriage_unit` mount onto the X-axis
- `d3d_x_axis_half_carriage` placement
- conservative half-carriage closure fastener step
- left-travel / right-travel / nozzle-clearance QC

What remains intentionally deferred:

- `Extruderspacer.fcstd`
- finer fastener closure story
- later heated-bed, control-panel, and wiring modules

This is the first printer-side extruder integration slice after:

- `Axes Stage 01`
- `Extruder Stage 01`
- `Extruder Stage 02`

## Locked Goal

Author the next slice as:

- `Extruder Stage 03: mount the completed extruder carriage unit onto the X-axis carriage interface, close the half-carriage clamp, and verify travel clearance`

## Strong Enough To Lock

### Part set

The next slice should be built around:

- `Universal axis carriage side`
- `Axis half carriage`
- completed `subassembly_extruder_carriage_unit`

### Procedure shape

The next slice should end with:

- a mounted carriage-side extruder unit
- a closed carriage clamp path
- a travel / interference QC step

### Runtime expectations

This slice can stay within existing families:

- `Place`
- `Use`
- `Confirm`

No new family is required.

## Still Deferred

### Extruder spacer

Do not lock the spacer into the first authored Stage 03 pass yet.

Reason:

- `Extruderspacer.fcstd` is present in later source
- but the acquired file reads like a duplicated print-layout body, not a clear installed
  one-to-one assembly file
- that is not strong enough for a precise runtime step without another evidence pass

### Fine fastener sequence

Keep the fastener story conservative in the first pass.

Reason:

- the CAD confirms the clamp and second bracket relationship
- it does not yet give a clean, fully narrated fastener order for `v18.10`

## Recommended Step Shape

The next authored slice should likely be:

1. stage the X-axis carriage-side interface
2. place the completed extruder carriage unit onto the carriage-side interface
3. stage the half-carriage clamp side
4. close the half-carriage over the carriage unit
5. secure the carriage closure
6. verify smooth X travel and no interference

## Recommended Data Policy

### Use exact new parts for:

- `d3d_x_axis_carriage_side`
- `d3d_x_axis_half_carriage`

These should come from the newly acquired Stage 03 candidate meshes after review.

### Reuse existing completed subassembly for:

- `subassembly_extruder_carriage_unit`

This keeps Stage 03 continuous with the already authored procedure.

### Defer a separate spacer part until:

- the spacer count and installed role are locked more tightly

## Package Scope Impact

This did extend the procedure beyond step `67` into the first true printer-side
extruder mount.

It would still stop before:

- wiring
- cable-chain routing
- endstop installation details
- final printer commissioning

## Immediate Recommendation

The implemented `machine.json` pass used:

- carriage side
- half carriage
- completed carriage-unit subassembly
- travel-clearance check

and explicitly left the spacer out of the first pass because stronger source
evidence still has not appeared.
