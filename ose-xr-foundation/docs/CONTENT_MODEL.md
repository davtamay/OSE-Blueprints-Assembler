# CONTENT_MODEL.md
## OSE XR Foundation – Conceptual Content Model

This document describes the conceptual structure of machine training content.

For canonical schema definitions see:

DATA_SCHEMA.md

---

# Purpose

The content model defines how Open Source Ecology blueprints are transformed into structured XR training experiences.

The model supports:

- modular machine assemblies
- reusable components
- scalable machine libraries
- deterministic assembly validation
- explicit finished-subassembly stacking when a completed unit must later move as one object

---

# Content Hierarchy

Machine content is structured as:

Machine
 └ Assembly
     └ Subassembly
         └ Step
             ├ Part
             ├ Tool
             └ Instruction

---

# Machine

Represents a complete buildable machine.

Examples:

- Brick Press
- Tractor
- CNC Torch Table
- Power Cube

Contains:

- metadata
- assemblies
- references
- asset manifests

---

# Assembly

Major subsystem of a machine.

Examples:

- frame
- hydraulic system
- electrical system
- engine mount

---

# Subassembly

Mechanical grouping inside an assembly.

Examples:

- wheel hub
- pump mount
- frame cross‑brace

---

# Step

A single instructional action.

Every step belongs to one of four **families** that define its fundamental interaction shape:

- **Place** — move a part to a target position (e.g. place component, insert bolt)
- **Use** — wield a tool against targets (e.g. tighten fastener, weld joint)
- **Connect** — link two endpoints (e.g. attach hose, route cable)
- **Confirm** — acknowledge or verify without spatial interaction (e.g. safety check, review)

A step may optionally declare a **profile** — a family-scoped refinement that selects specific behavior, effects, or validation within its family. Examples: `Place.Clamp`, `Use.Torque`, `Use.Weld`, `Connect.Cable`.

For the full step capability taxonomy see `STEP_CAPABILITY_MATRIX.md`. The runtime automatically resolves the learner-facing **interaction pattern** (e.g. PlaceOnZone, SelectPair, TargetHit) from the family + profile — see `INTERACTION_PATTERN_MATRIX.md`.

Each step resolves a **view mode** that defines how the camera should frame the step's spatial context. View modes are resolved automatically from family + profile, or declared explicitly. See `STEP_VIEW_FRAMING.md`.

Each step defines:

- instructions
- required parts
- required tools
- validation rules
- five capability payloads: guidance, validation, feedback, reinforcement, difficulty

---

# Parts

Physical components used during assembly.

Examples:

- bolts
- plates
- bearings
- hoses

Parts may include:

- 3D assets
- metadata
- purchasing references

---

# Physical Fidelity Standard

Instructional content is allowed to simplify complexity, but it is not allowed to
silently fake physical reality.

Every authored machine package must preserve the real-world relationships that matter
for learning:

- what each object is
- how large it is
- where it sits relative to neighboring parts
- how it is oriented
- what it looks like
- what is known exactly vs inferred

If the runtime uses simplified collision, exaggerated ghosting, or proxy geometry,
the authored source data must still reflect the real machine as closely as the source
material allows.

## Measurement Requirements

Every authored part and tool must have source-backed physical dimensions.

At minimum, authoring records must capture:

- width, height, and depth in meters
- the source of those dimensions
- whether the value is exact, converted, estimated, or inferred
- any important secondary dimensions that affect recognition or assembly

Examples of secondary dimensions:

- hole spacing
- tube outer diameter and wall thickness
- bracket leg lengths
- hose inner and outer diameter
- wrench drive size

If exact dimensions are unknown:

- infer carefully from the best available source
- record the assumption explicitly
- prefer a documented approximation over an undocumented guess

The dimension catalog and package metadata should represent real-world scale, not just
"what looks right in Unity."

## Placement and Orientation Requirements

Step targets, preview layouts, and final assembled positions must be authored from the
real machine layout, not arbitrary spacing.

For every placed or connected item, the content author should know:

- where the part attaches in the real assembly
- which face, edge, hole, or port is the intended interface
- which direction the part faces when installed
- what neighboring parts constrain its orientation

If the instructional experience needs visual separation for clarity, that offset must
be a deliberate presentation choice, not a replacement for the real assembled pose.
The real pose should remain recoverable in the source package.

## Appearance Requirements

Every authored part or tool should be described in terms a modeler or generator can
execute reliably.

Appearance descriptions should specify:

- base shape and silhouette
- material family
- surface finish
- dominant colors
- notable hardware details
- functional features that affect recognition
- what should be omitted

Examples:

- "black powder-coated square steel tube"
- "zinc-plated hex-head fastener with washer"
- "cast aluminum motor housing with cooling fins"
- "rubber hydraulic hose with crimped silver fittings"

Avoid vague descriptions like:

- "metal bracket"
- "engine part"
- "tool handle"

## AI Asset Prompt Requirements

When using Meshy or any other text/image-to-3D workflow, the prompt is part of the
content definition and must be treated as engineering input, not marketing copy.

Every prompt should include:

- exact or explicitly approximate dimensions with units
- the dominant aspect ratio
- the intended upright orientation
- the real-world material and finish
- visible connection or mounting features
- the intended level of completeness
- negative constraints for unwanted details

A good prompt answers:

- what is this object
- what are its exact measurements
- what is it made of
- how is it oriented
- which details are essential for recognition
- which decorative or misleading details must be excluded

Example structure:

"Corner bracket for D3D printer frame, right-angle steel bracket, 65 mm wide, 65 mm tall,
8 mm thick, black powder-coated steel, two bolt holes on each leg, crisp machined edges,
upright with bracket corner at world origin, no logos, no text, no background, no people."

## Source Provenance Rule

Physical fidelity claims must be traceable.

For each authored slice, keep a source record that identifies:

- which document, drawing, photo, or BOM informed the dimensions
- which source established placement and orientation
- which source established appearance
- which details were inferred by the author
- which details still need confirmation

This provenance belongs in the package research notes and should be easy to audit later.

## Canonical Rule

If there is a conflict between "looks good enough" and "matches the real machine,"
the real machine wins unless the deviation is explicitly documented as an instructional
simplification.

---

# Tools

Instruments used during assembly.

Examples:

- wrench
- drill
- welder
- socket

---

# Instructions

Guidance provided to the learner.

May include:

- text
- diagrams
- animations
- hints
- validation feedback

---

# Runtime Relationship

During runtime:

- content packages are loaded
- assembly steps drive the training session
- validation confirms correct actions
- UI presents contextual information

---

# Stacking And Finished Subassemblies

Some real procedures require a learner to move a previously completed subassembly as one
finished unit.

Examples:

- stand a welded frame side upright
- place a completed panel into a later machine frame
- move a finished module into its integration position

The canonical model for this is documented in `STACKING_ARCHITECTURE.md`.

The short version is:

- parts keep their original identities
- the runtime uses a placement proxy for the finished subassembly during the stack step
- completed units may move to parked presentation slots after fabrication so one active work bay stays readable
- the source fabrication pose and the final integrated pose are authored separately when needed
- the final visible machine may bake to canonical integrated member poses after commit

Use stacking architecture when the procedure is genuinely about moving a completed unit,
not when the learner is still placing loose parts one by one.

---

# Design Principles

Modularity – machines decomposed into reusable pieces.

Scalability – supports simple tools up to complex builds.

Educational clarity – steps scaffold learning.

Data‑driven design – machines defined by structured data, not hardcoded logic.

---

# Relationship to DATA_SCHEMA.md

CONTENT_MODEL.md describes the conceptual structure.

DATA_SCHEMA.md defines:

- canonical JSON schema
- field definitions
- validation constraints
- package structure
