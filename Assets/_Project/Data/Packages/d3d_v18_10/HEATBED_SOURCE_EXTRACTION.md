# D3D Heated Bed Source Extraction

## Purpose

This note defines the next defensible post-`Extruder Stage 03` boundary for
`d3d_v18_10`.

It answers:

- whether the next module should be heated bed, control panel, or wiring
- what is strong enough to lock from current OSE source material
- what still blocks honest heated-bed authoring into `machine.json`

## Status

This slice is not yet authored in `machine.json`.

It is the recommended next extraction boundary after:

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

## Hard Facts Extracted

### Manual module order

The `3D Printer Manual` explicitly breaks the printer into modules including:

- `Frame`
- `Axes`
- `Heated Bed`
- `Control Panel 1`
- `Control Panel 2`

That is strong enough to say the next module after the current frame / axes /
extruder work should be the heated-bed path, not control electronics or wiring.

### Revision signal around bed size

The `3D Printer Genealogy` page says:

- `D3D v18.11` is the `12\" bed version`
- `D3D v19.02` is similar to `D3D v18.10` but uses a `12\" frame` and an `8\" print bed`

That means bed geometry and mounting evolved across nearby revisions.
It is not safe to casually assume that a later heatbed body is a one-to-one exact
`v18.10` part without documenting the inference.

### Strong later-source mechanical part set

`D3D v19.04` gives the strongest exact heated-bed part list currently available:

- `D3Dfinalassemblyv1902.fcstd`
- `Heatbed snapbuckle1904.fcstd`
- `Heatbed body1904.fcstd`
- `Heatbed wirelock.fcstd`

It also says:

- `D3D v19.02` was used for gross parts including the `heatbed`
- `Marlin v19.04` should be the `8\" bed standard` from then on

This is strong enough to lock a later-source mechanical heated-bed part family.

### Later heatbed construction facts

`D3D v19.06` states that the current heatbed uses:

- `16 ga steel plate`
- a `1/8\" steel` print surface
- `PEI` on top

It also states the current heatbed reached:

- `178C` max continuous temperature

That is useful later-source construction detail, but it is not direct proof that
`d3d_v18_10` should use that exact final insulated bed stack without qualification.

### Wire lock context

Both `D3D v19.04` and `D3D Universal with Dual Z-Axis 3D CAD` explicitly list:

- `Heatbed wirelock.fcstd`

That is enough to say the heated-bed slice should eventually include a physical
wire-retention or strain-relief part.
It is not enough yet to author the full heater wiring sequence for `v18.10`.

## Chosen Next Slice

The next honest module boundary should be:

- `Heated Bed Stage 01: lock the mechanical bed-body and mounting path before electrical heater details`

That means, at minimum:

- heatbed body
- snap-buckle mounting parts
- physical wire-lock part if treated only as strain relief
- bed-envelope / nozzle-clearance QC

This slice should stop before:

- heater wiring
- thermistor routing
- control-panel integration
- PSU / SSR / MOSFET wiring

## Why This Is The Honest Boundary

This is the correct next move because:

- the manual module order puts `Heated Bed` before the control-panel modules
- later sources provide a concrete mechanical bed part family
- the current authored package already reaches the first printer-side extruder mount,
  so the next meaningful printer structure is the bed system

At the same time, this is where the source quality drops for exact `v18.10`
claims:

- the strongest exact bed-body files are later-source (`v19.04` / `v19.06`)
- bed-size and frame relationships changed across nearby revisions
- the electrical heater implementation is even more revision-sensitive

So the next source-backed move is a heated-bed extraction and acquisition pass,
not blind authoring into `machine.json`.

## Locked Working Conclusion

What is strong enough to lock now:

- the next module should be heated bed, not control panel or wiring
- a first heated-bed slice should be mechanical-first
- the next acquisition set should center on:
  - `Heatbed body1904`
  - `Heatbed snapbuckle1904`
  - `Heatbed wirelock`
  - `D3Dfinalassemblyv1902`

What is not strong enough to lock yet:

- that the later insulated heatbed body is an exact `v18.10` match
- the exact `v18.10` mounting relationship between bed body and frame / Y-axis context
- the full electrical heater stack for the first authored bed slice

## Recommended Next Tasks

Before touching `machine.json` again:

1. acquire or inspect the heated-bed CAD set:
   - `Heatbed body1904.fcstd`
   - `Heatbed snapbuckle1904.fcstd`
   - `Heatbed wirelock.fcstd`
   - `D3Dfinalassemblyv1902.fcstd`
2. verify whether the first authored bed slice can stay mechanical-only for `v18.10`
3. only then write a conservative `Heated Bed Stage 01` brief
