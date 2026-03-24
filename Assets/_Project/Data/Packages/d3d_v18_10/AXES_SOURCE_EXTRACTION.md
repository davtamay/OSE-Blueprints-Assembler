# D3D First Axes Slice Extraction

## Purpose

This note defines the first defensible post-frame authoring target for `d3d_v18_10`
and records the concrete authored-slice brief that was then implemented in
`machine.json`.

It answers:

- which `Axes` / `FinalAssembly` content is strong enough to author next
- what the first post-frame axes slice should be
- which parts, tools, and step families that slice will require
- which runtime gap had to be handled before the slice could be implemented cleanly

## Implementation Status

`Axes Stage 01` is now authored into `machine.json` as steps `41` through `52`.

What was implemented directly from this brief:

- `assembly_d3d_axes_stage_01`
- `subassembly_y_left_axis`
- `subassembly_y_right_axis`
- `subassembly_x_axis`
- `subassembly_x_axis_fitting`
- constrained `AxisFit` placement for the X-axis motor-holder side
- explicit `tool_allen_key_metric` and `tool_pliers` reuse for the first pass

What remains intentionally simplified in the authored first pass:

- axis-module geometry is still schematic placeholder geometry, not source-backed
  v18.10 meshes
- the external `M6x30` mounting hardware is still represented implicitly through
  placement and tool-target interactions instead of fully explicit loose fasteners
- a dedicated low-torque drill interaction was deferred in favor of the metric
  Allen key so the first pass stays inside already-proven tool behavior

## Sources Consulted

- 3D Printer Manual
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- D3D Printer Design
  - https://wiki.opensourceecology.org/wiki/D3D_Printer_Design
- 3D Printer Genealogy
  - https://wiki.opensourceecology.org/wiki/3D_Printer_Genealogy
- OSE Lesson - Universal Axis
  - https://wiki.opensourceecology.org/wiki/OSE_Lesson_-_Universal_Axis
- Axes source deck
  - https://docs.google.com/presentation/d/1TTD37XprJd4XY3mH5VT_HldC_gVEJgwmnfkqqsi3-NM/edit
- Final assembly source deck
  - https://docs.google.com/presentation/d/1LRL6PQtWm0LT6j6YNjLNjDAdbKkd3TmO8_aOBdfskhI/edit

## Hard Facts Extracted

### Manual structure

The `3D Printer Manual` explicitly breaks the printer into modules including:

- `Frame`
- `Axes`
- `FinalAssembly`

That means the next content slice should come from `Axes`, with `FinalAssembly` used
to validate how the first axis assembly seats onto the completed frame.

### Motion architecture direction

The `D3D Printer Design` page gives the strongest high-level motion facts available in
public text:

- the printer uses an `XY-fixed build platform`
- the printer is based on `modular linear actuators`
- the `Z motor` is placed near the top so the frame can scale in height
- `leadscrew`-type threaded shafts were the first documented motion choice
- `linear bearings with flanges` are explicitly called out under linear guides

These facts are strong enough to shape the slice boundary, but not enough to author
exact mounting geometry by themselves.

### Universal-axis modularity

The `OSE Lesson - Universal Axis` page confirms that the axis system is intended as a
modular reusable unit:

- the Universal Axis is modular and scalable
- it is applicable to X, Y, and Z axes of cartesian robot systems
- it uses printed parts, metal plates, and driven motion elements

This supports authoring the next slice as one bounded axis-module fitting operation,
not a vague "install motion hardware" phase.

### v18.10-specific procedural hotspot

The `3D Printer Genealogy` page includes a concrete v18.10 procedural clue:

- `X axis mounting: needs procedure optimization for axis tightness`

That is the strongest high-level cue for the first axes slice.

## Additional Slide Facts Extracted

The embedded slide text from the `Axes` and `FinalAssembly` decks gives enough
specificity to move from a general direction to a concrete authoring brief.

### Assembly relationship

The slides state that:

- the `x-axis is connected to the two y-axes carriages`
- the `x-axis mounts between the y-axes`
- the `y-axes and z-axes mount directly to the frame`

This matters because the first X-axis slice is not a loose-part build on the floor.
It is a fitted module installation between two already-mounted side axes.

### X-axis fitting order

The slides define a specific five-step fitting order for the X-axis:

1. tighten the idler screws
2. fasten the idler to the y-right carriage
3. fasten the motor holder to the y-left carriage so the axis reaches exact length
4. tighten the motor holder screws
5. tighten the belt

The slides explicitly justify this order:

- the idler side is anchored first so the motor-holder side can be lengthened across
  the rods
- the belt is tightened last because the correct axis length must be set first

### Fastener and adjustment details

The slide text also provides concrete hardware and handling details:

- the fitting procedure references `[2] M6x30 bolts` through the `y-right carriage`
  into the X-axis idler
- the fitting procedure references `[2] M6x30 bolts` through the `y-left carriage`
  into the X-axis motor holder
- the procedure references `[3] M6x18 bolts` and `[4] M3 motor screws` on the axis
  itself
- the fitting procedure removes the `belt peg` that does not currently retain the
  belt, lengthens the X-axis, then reinserts the peg after belt tensioning
- the belt is tensioned with `pliers`
- some fastening is done first loosely by hand, then with a `power drill` on a low
  torque setting
- the slides explicitly mention an `allen key` on the loose motor-holder attach step

### Tightness and quality-control criteria

The slides provide explicit acceptance criteria for the fitted X-axis:

- the X-axis should travel along the full Y-axis range without binding
- the X-axis should butt tightly to the Y axes with `no gap`
- the X-axis should trigger the end stop on the `Y-Left Axis`
- if tightness occurs, the procedure calls for loosening the `M6x30` bolts to locate
  the tight point
- if rod imperfections cause tightness, clean them
- lubricate the rods to smooth motion

These checks are strong enough to justify a dedicated post-fit QC step instead of a
generic confirm-only step.

## Chosen Next Slice

The first concrete axes slice should be:

- `X-axis fitting and mounting between completed Y axes`

This is narrower and more defensible than:

- generic rail installation
- full gantry authoring
- full bed-holder authoring
- full XYZ motion authoring in one jump

## Locked Prerequisite Policy

The next package expansion should not pretend that the Y axes already exist.

Locked policy:

- the next authored expansion should be one `Axes Stage 01` assembly increment
- that increment should first mount `Y-left` and `Y-right`
- the `X-axis fitting and mounting between completed Y axes` slice then follows inside
  that same expansion
- the X-axis fit remains the focal source-backed hotspot, but it should not rely on
  hidden runtime state or skipped prerequisite content

Recommended top-level ids for that expansion:

- `assembly_d3d_axes_stage_01`
- `subassembly_y_left_axis`
- `subassembly_y_right_axis`
- `subassembly_x_axis`
- `subassembly_x_axis_fitting`

Meaning:

- `subassembly_y_left_axis` and `subassembly_y_right_axis` are the two prerequisite
  mounted side-axis units
- `subassembly_x_axis` is the completed movable X-axis unit
- `subassembly_x_axis_fitting` is the procedural integration group that owns the fit /
  tension / QC steps

## Concrete Authored-Slice Brief

### Recommended slice id

- `axes_phase_01_x_axis_fit_between_y_axes`

### Recommended slice name

- `Fit and mount the X-axis between the completed Y axes`

### Boundary

This slice should teach the first honest transition from:

- welded frame plus mounted Y axes

to:

- a fitted, tensioned, and checked X-axis installed between them

### Preconditions

This slice should not start from bare frame alone.

It assumes these are already available:

- welded D3D frame complete
- `subassembly_y_left_axis` mounted to the frame
- `subassembly_y_right_axis` mounted to the frame
- `subassembly_x_axis` preassembled enough that:
  - idler side exists
  - motor-holder side exists
  - rods exist
  - internal fasteners are present but not fully finalized
  - belt is routed but not finally tensioned

Important extruder note:

- the `FinalAssembly` deck contains an extruder-to-X-axis operation
- that operation varies by extruder path
- keep extruder attachment out of this first slice unless the exact v18.10 extruder
  variant is locked

### Output state

At slice completion:

- X-axis is attached between Y-left and Y-right
- X-axis is at final fitted length
- X-axis belt is tensioned
- axis travel is checked for binding
- no visible gap remains between the X-axis and Y-axis interfaces
- the Y-left end stop trigger condition is verified

## Recommended Content Model

### Subassemblies to author

- `subassembly_y_left_axis`
- `subassembly_y_right_axis`
- `subassembly_x_axis`
- `subassembly_x_axis_fitting`

### Explicit loose parts to author

The first authored pass keeps only this one loose part explicit:

- `fastener_x_axis_belt_peg`

Reason:

- it materially changes state during the fitting sequence
- it fits the existing `Place` interaction cleanly
- the four external `M6x30` mount bolts are currently represented implicitly through
  the mount / tighten sequence rather than as separate loose fasteners

### Preassembled fasteners to keep as tool-only targets

To keep the contract clean, do not make every internal X-axis fastener a loose part in
the first pass. Keep these as authored tool targets on the prebuilt X-axis:

- `[3] M6x18` idler / holder screws
- `[4] M3` motor screws

This keeps the learner focused on the fitting order instead of exploding the slice into
small hardware handling steps.

## Recommended Tool Set

Add or reuse these tools for the slice:

- `tool_allen_key_metric`
- `tool_pliers`

Deferred tool:

- `tool_power_drill_low_torque`

Reason:

- the first authored pass reuses proven hand-tool interaction paths first
- the slide source supports a low-torque drill, but the current package does not need
  it to preserve the fitting order or acceptance logic

Do not add a lubricant or abrasive tool to the mainline path yet.

Why:

- the source mentions cleaning rod imperfections and lubrication as troubleshooting /
  correction paths
- those should stay in notes or a later troubleshooting slice unless the runtime gains
  branch handling

## Recommended Authored Step Order

This is the concrete step sequence that best matches the slide text while staying
compatible with the current content system.

Use only the current family set:

- `Place`
- `Use`
- `Confirm`

Do not introduce a new `Adjust` or `Inspect` family in `machine.json`.
Any special axis-fit behavior should resolve from `family + profile + step data shape`.

### Step 1

- `family`: `Use`
- `profile`: `Fasten`
- `name`: `Tighten the X-axis idler screws`
- `targets`: 3 sequential tool targets
- `tool`: `tool_allen_key_metric` or low-torque drill, depending on how the tool
  visuals are standardized

Intent:

- lock the rods flush against the idler before frame-side fitting begins

### Step 2

- `family`: `Place`
- `requiredSubassemblyId`: `subassembly_x_axis`
- `name`: `Anchor the X-axis idler to the Y-right carriage`
- `target strategy`: guided docking of the X-axis idler side to the Y-right carriage

Follow immediately with:

- `family`: `Use`
- `profile`: `Fasten`
- 2 sequential `M6x30` tool targets

Intent:

- anchor the X-axis on the idler side first because that side has less adjustment room

### Step 3

- `family`: `Use`
- `profile`: `Fasten`
- `name`: `Loosely start the X-axis motor-holder bolts on the Y-left carriage`
- `targets`: 2 sequential `M6x30` targets
- `tool`: `tool_allen_key_metric`

Intent:

- hold the motor side loosely enough that axis length can still be adjusted

### Step 4

- `family`: `Place`
- `profile`: `AxisFit`
- `name`: `Lengthen the X-axis to fit tightly between the Y axes`
- `interaction`: controlled linear extension of the motor-holder side until it butts
  to the Y-left axis with rods still flush in the idler

Intent:

- set final span before the internal fasteners and belt are finalized

Important:

- this was the one step that did not map cleanly to the original runtime behavior
- it is now handled by the constrained-fit subassembly placement contract

### Step 5

- `family`: `Use`
- `profile`: `Fasten`
- `name`: `Lock the motor-holder screws`
- `targets`: 7 sequential tool targets
  - 3 `M6x18`
  - 4 `M3 motor screws`
- `tool`: low-torque drill or hex-key, depending on final standardization

Intent:

- lock the motor-holder side only after the exact axis span is set

### Step 6

- `family`: `Use`
- `profile`: `BeltTension`
- `name`: `Tension the X-axis belt`
- `targets`: 1 belt-tension target
- `tool`: `tool_pliers`

Follow immediately with:

- `family`: `Place`
- `name`: `Reinsert the belt peg`
- `requiredPartIds`: `fastener_x_axis_belt_peg`
- `targets`: 1 peg-insertion target

Intent:

- the source clearly separates belt loosening, length adjustment, then final tensioning

### Step 7

- `family`: `Confirm`
- `viewMode`: `Inspect`
- `name`: `Check X-axis tightness and travel`
- `targets`: 3 authored checkpoints
  - full travel / no binding
  - no gap at the X-to-Y interface
  - Y-left end-stop trigger

Intent:

- keep the mainline contract inside the current family set while still framing the
  learner around the real source-backed acceptance checks

Upgrade path:

- if a later motion-check interaction is added, this step can move from
  `Confirm + Inspect view` to a richer axis-travel validation pattern without changing
  the slice boundary

## Draft Exact Ids

These ids are concrete enough to use in the next implementation pass.

### Step ids

- `step_mount_y_left_axis_to_frame`
- `step_verify_y_left_axis_motion`
- `step_mount_y_right_axis_to_frame`
- `step_verify_y_axis_pair_alignment`
- `step_tighten_x_axis_idler_screws`
- `step_anchor_x_axis_idler_to_y_right`
- `step_loosen_start_x_axis_motor_holder_bolts`
- `step_fit_x_axis_span_between_y_axes`
- `step_lock_x_axis_motor_holder_screws`
- `step_tension_x_axis_belt`
- `step_reinsert_x_axis_belt_peg`
- `step_check_x_axis_tightness_and_travel`

### Target ids for the X-axis fit sub-slice

- `target_x_axis_idler_screw_1`
- `target_x_axis_idler_screw_2`
- `target_x_axis_idler_screw_3`
- `target_x_axis_idler_mount_y_right_a`
- `target_x_axis_idler_mount_y_right_b`
- `target_x_axis_motor_mount_y_left_a`
- `target_x_axis_motor_mount_y_left_b`
- `target_x_axis_span_fit`
- `target_x_axis_motor_holder_m6x18_1`
- `target_x_axis_motor_holder_m6x18_2`
- `target_x_axis_motor_holder_m6x18_3`
- `target_x_axis_motor_holder_m3_1`
- `target_x_axis_motor_holder_m3_2`
- `target_x_axis_motor_holder_m3_3`
- `target_x_axis_motor_holder_m3_4`
- `target_x_axis_belt_tension`
- `target_x_axis_belt_peg_insert`
- `target_x_axis_qc_left_travel`
- `target_x_axis_qc_right_travel`
- `target_x_axis_qc_y_left_endstop`

### Tool ids

- `tool_power_drill_low_torque`
- `tool_allen_key_metric`
- `tool_pliers`

## Runtime Gap

This slice exposed one new capability gap clearly:

- the X-axis is not a purely rigid subassembly during fitting
- its span changes while one side is already anchored

The original finished-subassembly proxy model was not enough by itself for Step 4.

### Implemented runtime target

Add a constrained adjustable-subassembly interaction:

- one end of the X-axis remains anchored
- the motor-holder side slides along the rod axis only
- travel is clamped to one linear degree of freedom
- authored completion snaps when the holder butts to the Y-left target correctly

The constrained-fit contract is now in place, so the fallback is no longer the active
plan for `d3d_v18_10`.

## Next Authoring Boundary

With `Axes Stage 01` now in data, the next agent pass should not keep expanding this
slice sideways. The next concrete boundary is now documented in
`EXTRUDER_SOURCE_EXTRACTION.md`.

That means the honest next sequence is:

1. keep the implemented `Axes Stage 01` boundary intact
2. use `EXTRUDER_SOURCE_EXTRACTION.md` to lock the first Titan Aero module slice
3. only then extend `machine.json` beyond Step 52
