# D3D Extruder Final Integration Slice Extraction

## Purpose

This note records the extraction boundary that produced the first defensible
printer-side extruder integration slice for `d3d_v18_10`.

It answers:

- what the next honest extruder continuation is
- which parts and relationships are supported strongly enough to plan next
- which remaining details are still too weak to author directly into `machine.json`
- what must be extracted next before the procedure can continue without guessing

## Status

This slice is now authored in `machine.json` as `Extruder Stage 03`.

It was the recommended next extraction boundary after:

- `Axes Stage 01`
- `Extruder Stage 01`
- `Extruder Stage 02`

Stage 03 carriage-side CAD has now been acquired locally and inspected:

- `source_cad/extruder_stage03/raw/Axis_half_carriage.fcstd`
- `source_cad/extruder_stage03/raw/Extruderspacer.fcstd`
- `source_cad/extruder_stage03/raw/Universal_axis_carriage_side.fcstd`
- `source_cad/extruder_stage03/raw/Oseextruder1902_with_2nd_bracket.fcstd`

See also:

- `source_cad/extruder_stage03/notes/STAGE03_PROVENANCE.md`
- `source_cad/extruder_stage03/notes/STAGE03_CONVERSION_STATUS.md`
- `EXTRUDER_STAGE03_AUTHORING_BRIEF.md`

## Sources Consulted

- D3D v18.10
  - https://wiki.opensourceecology.org/wiki/D3D_v18.10
- 3D Printer Manual
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- 3D Printer Genealogy
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Genealogy
- 3D Printer Extruder
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Extruder
- D3D v19.02
  - https://wiki.opensourceecology.org/wiki/D3D_v19.02
- D3D v19.04
  - https://wiki.opensourceecology.org/wiki/D3D_v19.04
- D3D v19.06
  - https://wiki.opensourceecology.org/wiki/D3D_v19.06
- File:Universal axis carriage side.fcstd
  - https://wiki.opensourceecology.org/index.php?title=File%3AUniversal_axis_carriage_side.fcstd

## Hard Facts Extracted

### Revision authority

`D3D v18.10` remains authoritative for:

- the printer revision being simulated
- Titan Aero as the chosen extruder path
- the mount-space problem statement tied to bed-space and vertical-space recovery

### What v18.10 actually says about the next integration

The `3D Printer Genealogy` page records:

- the 2016 extruder holder eats bed space
- extruder mounting should recover additional vertical space
- that recovery should come with horizontal X-axis mounting

That is strong evidence that the next slice must involve real printer-side
extruder mounting, not another detached bench subassembly.

### Strongest stable part-set signal

Across `D3D v19.02`, `D3D v19.04`, and `D3D v19.06`, the printer CAD lists stay
consistent about the carriage-side integration parts that follow the mount stack:

- `Universal axis carriage side.fcstd`
- `Axis half carriage.fcstd`
- `Oseextruder1902.fcstd`
- `Extruderspacer.fcstd`

This is strong enough to say the next slice involves closing the completed
extruder carriage unit onto the X-axis carriage interface.

The new local CAD inspection strengthens this further:

- `Universal axis carriage side` converts as a clean single carriage-side body
- `Axis half carriage` converts as a clean clamp-side carriage half
- the archived `Oseextruder1902` with the second bracket confirms the carriage-unit
  mounting context exists as a real OSE assembly reference

### What the carriage file itself confirms

The `Universal axis carriage side.fcstd` file page adds direct evidence that the
carriage is part of the extruder-side X-axis integration path:

- a simplified carriage was positioned for the X-axis of the extruder
- rod holes were added
- a wide belt hole was added

This is important because it ties the carriage geometry directly to the X-axis
extruder interface rather than only to a generic printed-part library.

The current file page also explicitly says:

- use the widened belt-hole version for printing

That is enough to keep the carriage-side part choice stable for authoring.

### What the spacer file does and does not confirm

`Extruderspacer.fcstd` is now locally inspected.

What it confirms:

- a spacer is part of the later Titan Aero carriage-side evidence set

What it does **not** confirm cleanly:

- count
- installed orientation
- whether the file is a print-layout duplicate rather than a single installed part

The acquired FCStd currently reads more like duplicated print geometry than a clean
one-to-one assembly part.

That means the spacer should stay deferred from the first authored Stage 03 pass
unless stronger evidence is added.

### What the archived OSE extruder file confirms

The archived `2019-02-24` `Oseextruder1902` file was acquired specifically because
the current simplified file says it still needs the lower bracket added.

The archive history adds a critical confirmation:

- `Added 2nd bracket for attaching extruder to carriage`

This is strong enough to lock that the printer-side mount path is not just a loose
single-face docking step. It is a carriage-unit closure path with real clamp/bracket
context.

### Later revision evidence is useful but not direct v18.10 authority

`D3D v19.02` says:

- the overslung extruder became the standard option
- the overslung arrangement was chosen for serviceability
- the overslung arrangement increases print volume by increasing Z

That is useful evidence for the direction of later design evolution.
It is not strong enough, by itself, to let us claim that `d3d_v18_10` should be
authored as the final overslung arrangement without an explicit note that this is
an inference.

### QC implication for the next slice

`D3D v19.04` includes a build-checklist requirement to verify that all axes move
smoothly through their full range without interference with:

- structure
- bolts
- wiring
- endstops

That is strong enough to require a motion-clearance or travel-clearance check at
the end of the next integration slice.

## Chosen Next Slice

The next concrete source-backed extraction target should be:

- `Extruder Stage 03: integrate the completed extruder carriage unit onto the X-axis carriage interface and verify travel clearance`

This means, at minimum:

- the completed carriage-side Titan Aero unit from `Extruder Stage 02`
- the mating carriage-side / half-carriage X-axis interface
- the first printer-side travel-clearance check

This slice should stop before:

- cable-chain routing
- endstop wiring
- final wire management
- claiming one exact final overslung or underslung v18.10 mounted orientation unless
  the source boundary is tightened further

## Why This Is The Honest Boundary

This is the right next move because:

- the current extruder procedure already stops at a completed carriage-side unit
- the next stable part-set in source is the carriage / half-carriage / spacer path
- later sources explicitly require smooth full-travel motion without interference

At the same time, this is where the evidence quality drops:

- the available source pages do not publish a clear assembly-order narrative for the
  final `v18.10` extruder-on-printer mount
- spacer use is listed in later sources, but not pinned cleanly to a final
  `v18.10` procedure sequence here
- later overslung evidence is real, but not cleanly authoritative for `v18.10`

So this slice is the correct next extraction target, and the authoring gate is now
much narrower than before.

It is still not safe to pretend the spacer role is fully solved.
But it is now safe to define the first authored Stage 03 pass around:

- carriage side
- half carriage
- completed carriage-unit subassembly
- travel-clearance QC

## Locked Working Conclusion

What is strong enough to lock now:

- the next build continuation is printer-side extruder integration, not bed,
  controls, or wiring
- the carriage-side interface must involve the simplified carriage family and the
  half-carriage family
- the next slice must end with a travel / interference QC step
- the first authored Stage 03 pass can omit the spacer until its installed role is
  proven more tightly

What is not strong enough to lock yet:

- exact final mounted orientation for `v18.10`
- exact role of `Extruderspacer.fcstd`
- exact fastener sequence for the final printer-side mount
- exact order for closing the carriage sandwich around rods / bearings / carriage body

## Candidate Authored-Slice Shape

With the new CAD inspection, the next slice should likely look like:

1. stage the X-axis carriage-side interface
2. place the completed extruder carriage unit onto that interface
3. stage the half-carriage clamp side
4. close the half-carriage onto the assembly
5. secure the closure conservatively
6. confirm full travel and no interference

See:

- `EXTRUDER_STAGE03_AUTHORING_BRIEF.md`

## Recommended Next Extraction Tasks

Before this slice was written into `machine.json`, the next pass still needed to
extract or verify:

- whether `Extruderspacer.fcstd` is mandatory for the chosen revision path
- whether the final mounted unit is authored as overslung, underslung, or still mixed
  in the strongest available `v18.10`-compatible evidence
- what the final travel-clearance targets should reference geometrically

## Immediate Recommendation

Do not author the spacer into `Extruder Stage 03` yet.

That recommendation was followed in the implemented package slice.

Current handoff:

1. treat this note as the evidence record for implemented `Extruder Stage 03`
2. continue with the heated-bed boundary in `HEATBED_SOURCE_EXTRACTION.md`
