# INTERACTION_PATTERN_MATRIX.md

## Purpose

This document defines **Interaction Pattern** as a canonical project concept -- the reusable learner-facing interaction contract that determines how a step is physically performed.

Interaction Pattern is distinct from:

- **Step Family** (what the step means semantically)
- **Profile** (specialized variation within a family)
- **Entity Role** (what the object is)

A single interaction pattern may be shared across multiple families and profiles. For example, **SelectPair** is used by both `Use.Measure` (tape measure between two anchors) and `Connect.Cable` (cable between two ports).

This file should be used together with:

- `docs/STEP_CAPABILITY_MATRIX.md`
- `docs/INTERACTION_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/DATA_SCHEMA.md`
- `docs/CONTENT_MODEL.md`
- `docs/SOURCE_OF_TRUTH_MAP.md`

---

# 1. Core Principle

The runtime must not hardcode a unique interaction mechanic for every family. Instead, families declare their semantic meaning, and the runtime resolves a reusable **interaction pattern** that implements the learner-facing physical interaction.

This separation enables:

- reuse of interaction code across families (e.g., `AnchorToAnchorInteraction` serves both measurement and cable connection)
- cleaner cross-platform design (each pattern maps to platform-specific input once, not per-family)
- scalable authoring (new profiles reuse existing patterns instead of requiring new mechanics)
- clearer documentation (what the step means vs how it is performed are separate concerns)

---

# 2. The Five Concepts

These five concepts form the complete step execution taxonomy.

| Concept | Question | Definition | Authoritative Doc |
|---|---|---|---|
| **Entity Role** | What is the object? | The functional classification of a scene object: Part, Tool, Connector, Consumable, Fixture. Implicit in schema definitions (`PartDefinition`, `ToolDefinition`). | This document S5 |
| **Step Family** | What does the step mean? | The semantic outcome of the step: Place, Use, Connect, Confirm. Determines top-level runtime dispatch. | `STEP_CAPABILITY_MATRIX.md` |
| **Profile** | What specialized variation? | A family-scoped refinement that selects specific behavior, effects, or validation: Torque, Measure, Weld, Cut, Cable, Clamp. | `STEP_CAPABILITY_MATRIX.md` |
| **Interaction Pattern** | How does the learner perform it? | The reusable physical interaction contract: PlaceOnZone, SelectPair, TargetHit, OrderedTargets, HoldOnTarget, PathProgress, SingleConfirm. | This document |
| **Payloads** | What data drives the capability layers? | Five authored data shapes: guidance, validation, feedback, reinforcement, difficulty. | `STEP_CAPABILITY_MATRIX.md` S4 |

**Key rule:** Entity Role != Step Family != Interaction Pattern.

- A Tool can be placed (Place family) or used (Use family)
- A Connector can reuse the same interaction pattern as measurement (SelectPair) without becoming the same family
- Two different families can share the same pattern implementation

---

# 3. Pattern Catalog

Each pattern is a reusable interaction contract that can be implemented once per platform and shared across families.

## 3.1 PlaceOnZone

The user moves an object to a target zone and releases it.

- **Learner action:** Drag or click-to-place
- **Runtime contract:** Ghost targets spawned; user grabs/selects a part, moves it toward a target, releases; validation checks position/rotation tolerance; on success the part snaps to the target
- **Current runtime class:** `PlaceStepHandler`
- **Used by:** Place.*(default)*, Place.Clamp

## 3.2 SelectPair

The user selects point A, then selects point B.

- **Learner action:** Two taps/clicks
- **Runtime contract:** Two anchor markers spawned; user taps the first anchor; a live visual tracks the cursor; user taps the second anchor; interaction completes
- **Current runtime class:** `AnchorToAnchorInteraction`
- **Used by:** Use.Measure, Connect.*(default)*, Connect.Cable

## 3.3 TargetHit

The user activates a tool on a single target.

- **Learner action:** One tap/click with tool equipped
- **Runtime contract:** Tool auto-equipped; target marker spawned; user activates tool on target; validation checks tool identity and target coverage
- **Current runtime class:** `UseStepHandler` (single target mode)
- **Used by:** Use.*(default)*, Use.Torque (single bolt)

## 3.4 OrderedTargets

The user activates a tool on multiple targets in sequence.

- **Learner action:** Sequential taps/clicks with tool equipped
- **Runtime contract:** Tool auto-equipped; targets revealed one at a time in `targetIds` order; each activation advances to the next target; step completes when all targets are hit
- **Current runtime class:** `UseStepHandler` (sequential mode, `targetOrder: "sequential"`)
- **Used by:** Use.Torque (multi-bolt)

## 3.5 HoldOnTarget

The user presses and holds a tool on a target for a duration.

- **Learner action:** Hold/press gesture
- **Runtime contract:** Tool auto-equipped; target marker spawned; user initiates hold; progress indicator fills; validation checks hold duration meets threshold
- **Current runtime class:** *(future -- not yet implemented)*
- **Used by:** Use.Weld *(planned)*

## 3.6 PathProgress

The user follows a path with a tool.

- **Learner action:** Drag along a path
- **Runtime contract:** Tool auto-equipped; path visualization shown; user drags tool along the path; progress tracks coverage; validation checks path completion percentage
- **Current runtime class:** *(future -- not yet implemented)*
- **Used by:** Use.Cut *(planned)*

## 3.7 SingleConfirm

The user presses a Continue or Confirm button.

- **Learner action:** Button press
- **Runtime contract:** No ghost targets, no tool equip, no spatial interaction; the user reads instructional content and presses Continue/Confirm; the confirmation action itself completes the step
- **Current runtime class:** `ConfirmStepHandler`
- **Used by:** Confirm.*(default)*

---

# 4. Family-to-Pattern Mapping

This table defines which interaction pattern each Family.Profile combination uses by default.

| Family | Profile | Default Pattern | Notes |
|---|---|---|---|
| Place | *(default)* | PlaceOnZone | |
| Place | Clamp | PlaceOnZone | May add secondary confirmation |
| Use | *(default)* | TargetHit | |
| Use | Torque | TargetHit / OrderedTargets | Depends on target count and `targetOrder` |
| Use | Measure | SelectPair | Reuses `AnchorToAnchorInteraction` |
| Use | Weld | HoldOnTarget | *(future)* |
| Use | Cut | PathProgress | *(future)* |
| Connect | *(default)* | SelectPair | |
| Connect | Cable | SelectPair | Reuses `AnchorToAnchorInteraction` with `CableLineVisual` |
| Confirm | *(default)* | SingleConfirm | |

When a new profile is added, its default pattern should be selected from this catalog. If no existing pattern fits, a new pattern may be needed -- see S7.

---

# 5. Entity Role Reference

Entity Role classifies what a scene object **is**, independent of what step it participates in or how the interaction works.

## 5.1 Roles

| Role | Definition | Schema type |
|---|---|---|
| **Part** | A physical component assembled into the machine | `PartDefinition` |
| **Tool** | An instrument used during assembly but not permanently attached | `ToolDefinition` |
| **Connector** | A flexible link between two points (cable, hose, pipe) | `PartDefinition` with `category: "pipe"` |
| **Fixture** | A device that holds or secures parts during assembly (clamp, jig) | `PartDefinition` or `ToolDefinition` depending on context |
| **Consumable** | A material consumed during a process (welding rod, sealant) | *(future -- not yet in schema)* |

Entity Role is currently implicit in the existing schema types. No dedicated `role` field exists. A future schema field is possible if explicit role classification becomes needed for runtime dispatch or content authoring.

## 5.2 Canonical Examples

| Object | Entity Role | Step Family | Interaction Pattern | Profile |
|---|---|---|---|---|
| Tape Measure | Tool | Use | SelectPair | Measure |
| Cable / Hose | Connector | Connect | SelectPair | Cable |
| Clamp | Fixture | Place | PlaceOnZone | Clamp |
| Torque Wrench (single bolt) | Tool | Use | TargetHit | Torque |
| Torque Wrench (multi-bolt) | Tool | Use | OrderedTargets | Torque |
| Grinder / Saw | Tool | Use | PathProgress | Cut |
| Welder | Tool | Use | HoldOnTarget | Weld |
| Bracket / Plate | Part | Place | PlaceOnZone | *(default)* |
| Bolt / Fastener | Part | Place | PlaceOnZone | *(default)* |
| Safety Check | *(n/a)* | Confirm | SingleConfirm | *(default)* |

---

# 6. Schema Note

**Interaction Pattern is NOT a schema field in `machine.json`.**

Pattern is resolved at runtime from the combination of:

- `family` (or derived from `completionType`)
- `profile`
- Step data shape (target count, `targetOrder`, payload fields)

This is an intentional design choice:

- Authoring stays simple -- authors only set `family` and `profile`
- The runtime picks the correct interaction pattern automatically
- Pattern resolution logic lives in handlers, not in authored data

A future explicit `pattern` override field is possible if content authors need to force a specific pattern that differs from the family+profile default. This would be additive -- the current resolution remains the default.

---

# 7. Rules for Adding Patterns

## Adding a new pattern

1. The pattern must represent a genuinely distinct learner-facing interaction shape.
2. The pattern should be needed by at least two Family.Profile combinations, OR represent a shape that cannot be expressed as a variation of an existing pattern.
3. The pattern name must describe the **learner's physical action**, not the runtime implementation.
4. The pattern must be implementable across desktop, mobile, and XR platforms.
5. Add the pattern to S3 (Pattern Catalog), S4 (Family-to-Pattern Mapping), and update S5.2 examples if relevant.

## Reusing an existing pattern for a new Family.Profile

1. Confirm the existing pattern's interaction contract matches the new use case.
2. Add the new Family.Profile row to S4.
3. The runtime handler should use the shared pattern implementation (e.g., `AnchorToAnchorInteraction`) with pluggable visuals as needed.
