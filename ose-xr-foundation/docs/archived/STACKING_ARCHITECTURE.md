# STACKING_ARCHITECTURE.md

## Purpose

This document defines the canonical content and runtime model for steps that move a
previously completed subassembly as one finished unit.

Examples:

- stand a welded frame side upright
- place a finished panel onto a cube face
- move a completed module into a later integration target

This architecture exists because the runtime is fundamentally part-centric, while some
real assembly procedures require learners to manipulate a finished group of parts as one
rigid unit.

---

## Core Rule

Do not replace finished subassemblies with fake composite parts.

The canonical model is:

- parts keep their original identities for metadata, history, validation, and replay
- a finished subassembly gets a runtime placement proxy
- that proxy temporarily owns the transform for the whole completed unit
- member part poses are derived from the proxy plus authored local offsets

This preserves part truth while enabling real stacking operations.

---

## Why The Runtime Uses A Proxy

The current runtime assumes each part remains a first-class runtime object and that most
existing systems reason about parts directly:

- part spawning
- selection and highlighting
- replay and navigation restore
- placement validation
- hinting and previewing

Reparenting actual part GameObjects under ad hoc scene hierarchies would create broad
breakage and hidden coupling.

So the canonical approach is:

- keep real part objects under the normal preview/runtime hierarchy
- create a separate subassembly placement proxy
- move the proxy
- recompute member part poses from that proxy

---

## Spatial Representation Model

Completed-unit stacking uses three different spatial states on purpose.

### 1. Fabrication / Stacking Representation

This is the learner-facing representation during the stack step.

It is used for:

- selecting the completed unit
- showing the source panel or module
- guided docking toward the target
- composite source/target previewing

The learner should perceive this as "move the finished panel," not "move one part."

### 2. Completed Parking Representation

This is the persisted display for a finished unit after fabrication but before later
integration.

It is used for:

- keeping one shared active fabrication bay near the learner
- preserving visible progress in the scene
- staging the source position for later stacking

Use this when a machine has many repeated fabricated panels or modules and a distributed
"one bay per unit" layout would push the active work too far from the learner.

### 3. Canonical Integrated Representation

This is the representation that remains visible after the stacking step is committed.

It is used for:

- final display of the assembled machine
- replay after completion
- step navigation restore
- avoiding overlapping teaching geometry once the unit is integrated

If leaving the fabrication panel geometry in place would create overlap, duplicated edge
ownership, or z-fighting, the runtime should bake member parts to explicit integrated
member poses and hide the temporary stacking proxy.

---

## Adjustable Fitting Extension

Not every completed-unit operation is a rigid stack.

Some later machine steps fit a completed subassembly while one side is already anchored
and the other side must move along a constrained internal degree of freedom.

Reference case:

- D3D X-axis fitting between the completed Y axes

For this class of step:

- keep the step in the normal `Place` family
- keep the movable unit as a real subassembly, not a fake replacement part
- do not invent a separate `Adjust` family
- resolve the special behavior from `family + profile + step data shape`

Runtime expectation:

- one side of the finished unit may remain effectively anchored
- the movable end is constrained to a single linear or rotational fit axis
- the interaction should snap when the authored fit condition is satisfied

This is an extension of the same transform-owner model used for rigid stacking, not a
separate architecture.

---

## JSON Contract

The stacking contract is additive. Existing part-only packages remain valid.

### StepDefinition

Use `requiredSubassemblyId` when the learner must move a finished subassembly as one
unit.

```json
{
  "requiredSubassemblyId": "subassembly_left_frame_side"
}
```

Rules:

- optional
- v1: mutually exclusive with `requiredPartIds`
- use on normal `Place` steps; do not introduce a separate stack family

### TargetDefinition

Use `associatedSubassemblyId` when the target belongs to a finished subassembly rather
than a single loose part.

```json
{
  "id": "target_cube_left_side",
  "associatedSubassemblyId": "subassembly_left_frame_side"
}
```

Rules:

- optional
- v1: mutually exclusive with `associatedPartId`
- must match the step's `requiredSubassemblyId`

### previewConfig.subassemblyPlacements

This defines the authored fabrication reference frame for a finished subassembly.

```json
{
  "subassemblyPlacements": [
    {
      "subassemblyId": "subassembly_left_frame_side",
      "position": { "x": -0.38, "y": 0.55, "z": 0.0 },
      "rotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
      "scale": { "x": 1, "y": 1, "z": 1 }
    }
  ]
}
```

Interpretation:

- this is the authored frame of the completed unit in its fabrication pose
- it is not a second final pose for each part
- member local offsets are derived relative to this frame

### previewConfig.completedSubassemblyParkingPlacements

Use this when completed subassemblies should leave the active fabrication bay but remain
visible in the scene before stacking.

```json
{
  "completedSubassemblyParkingPlacements": [
    {
      "subassemblyId": "subassembly_left_frame_side",
      "position": { "x": -0.72, "y": 0.55, "z": 0.0 },
      "rotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
      "scale": { "x": 1, "y": 1, "z": 1 }
    }
  ]
}
```

Interpretation:

- this is a presentation-space parking pose for the completed unit
- it does not change part identity or the authored fabrication frame
- the runtime should apply it after fabrication completion and before later stacking

### previewConfig.integratedSubassemblyPlacements

Use this when the final committed machine should not remain a literal stack proxy or a
literal shell of duplicated teaching panels.

```json
{
  "integratedSubassemblyPlacements": [
    {
      "subassemblyId": "subassembly_left_frame_side",
      "targetId": "target_cube_left_side",
      "memberPlacements": [
        {
          "partId": "d3d_frame_left_top_bar",
          "position": { "x": 0.0, "y": 0.0, "z": 0.0 },
          "rotation": { "x": 0, "y": 0, "z": 0, "w": 1 },
          "scale": { "x": 1, "y": 1, "z": 1 }
        }
      ]
    }
  ]
}
```

Interpretation:

- this is the canonical final integrated display for the completed stacking step
- it is keyed by `(subassemblyId, targetId)`
- the runtime uses these member poses after successful placement and on replay

---

## Runtime Behavior

### Selection

On a stacking step:

- any member hit should normalize to the completed subassembly proxy
- hover and selection should read as the whole finished unit
- hints should target the whole finished unit and the composite target, not one member

### Guided Docking

Stacking steps are still `Place` steps, but the UX is guided:

- learner selects the finished unit
- learner drags it toward the composite target
- runtime eases orientation toward the authored target pose
- docking/snap completes once the proxy reaches the acceptance zone

The runtime should not require manual free-rotation unless a future interaction model
explicitly supports that cleanly.

### Commit

On successful placement:

- if no integrated placement exists, the proxy may remain the visible committed result
- if an integrated placement exists, bake member parts to the authored integrated poses
- hide the temporary proxy after commit

### Replay And Navigation

Completed stacking steps must replay deterministically:

1. restore completed part steps
2. park completed-but-not-yet-stacked subassemblies when parking poses are authored
3. restore completed stacking steps
4. if integrated placements exist, apply those member poses
5. only show the proxy while the active stacking step is in progress

---

## Authoring Rules

Use this model when:

- the learner must move a previously completed unit as one object
- the finished unit has a meaningful rigid transform of its own
- later steps depend on the integrated position of that finished unit

Do not use this model when:

- the step is still just placing loose parts one by one
- the finished grouping is only conceptual and never moved as one rigid unit
- a simpler part-only placement step is sufficient

Authoring requirements:

- keep original part ids; do not invent fake composite parts
- author exactly one `requiredSubassemblyId` per v1 stack step
- do not mix `requiredPartIds` and `requiredSubassemblyId` in the same v1 step
- make the step target resolve to the same subassembly via `associatedSubassemblyId`
- author `subassemblyPlacements` for every stackable finished unit
- author `completedSubassemblyParkingPlacements` when one shared fabrication bay is
  used and completed units must persist nearby before stacking
- author `integratedSubassemblyPlacements` whenever final display would otherwise
  overlap, z-fight, or misrepresent edge ownership
- document the integrated convention in the package source notes

---

## Validation Rules

Validators should reject or warn on these cases:

- step uses both `requiredPartIds` and `requiredSubassemblyId`
- target uses both `associatedPartId` and `associatedSubassemblyId`
- `requiredSubassemblyId` does not resolve
- stacking step has no valid target
- target subassembly id does not match the step's required subassembly id
- stackable subassembly has no `subassemblyPlacements` entry
- parking placement references a missing subassembly
- integrated placement references a missing target, missing part, or part outside the
  referenced subassembly

---

## D3D Reference Pattern

The first concrete use of this architecture is the `d3d_v18_10` frame package.

That package uses:

- completed square frame sides as stackable finished subassemblies
- one shared near-camera fabrication bay plus parked finished-panel side slots
- guided docking to move a finished side into cube position
- canonical integrated member poses after commit so the final visible cube avoids
  overlapping coplanar panel-shell geometry

This is the reference implementation agents should follow unless a later canonical
document replaces it.
