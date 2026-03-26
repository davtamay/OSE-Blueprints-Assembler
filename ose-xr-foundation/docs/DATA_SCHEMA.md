# DATA_SCHEMA.md

## Purpose

This document defines the canonical data schema for the XR assembly training application.

Its goal is to ensure that machine packages are authored consistently and can be interpreted reliably by the runtime without machine-specific code.

This schema covers:

- machine definitions
- assemblies
- subassemblies
- parts
- tools
- steps
- validation rules
- hints
- effects
- challenge metadata
- runtime package metadata

The schema is designed to support:

- data-driven content
- cross-platform runtime execution
- physical substitution workflows
- future multiplayer readiness
- future challenge modes
- clean authoring and validation
- long-term scalability across many Open Source Ecology machines

This file should be used together with:

- `TECH_STACK.md`
- `docs/CONTENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/PART_AUTHORING_PIPELINE.md`
- `docs/RUNTIME_EVENT_MODEL.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`

---

# 1. Core Schema Principle

The runtime must consume **structured machine packages**.

The runtime must not depend on:

- ad hoc scene configuration
- hidden prefab assumptions
- machine-specific code branches
- undocumented authoring conventions

The schema is the contract between:

- content authoring
- runtime systems
- UI systems
- validation
- effects
- future multiplayer/challenge systems

---

# 2. Schema Design Rules

The schema must be:

- explicit
- versioned
- modular
- serializable
- easy to validate
- easy to extend without breaking old packages
- readable to humans
- stable enough for agent-driven authoring

## 2.1 Field Rules

Fields should be:

- clearly named
- responsibility-specific
- optional only when truly optional
- documented with meaning and intended usage

## 2.2 Identity Rules

Every important content entity must have a stable unique id.

Examples:

- machine id
- assembly id
- subassembly id
- part id
- tool id
- step id
- effect id
- hint id

These ids are part of the runtime contract.

---

# 3. Package Structure Overview

A machine package should contain structured content such as:

```text
MachinePackage/
  machine.json
  assemblies/
  parts/
  tools/
  steps/
  effects/
  hints/
  assets/
```

Depending on implementation, these may be individual JSON files, one packaged JSON document, ScriptableObject-generated exports, or another structured export format.

The schema below defines the logical structure, not the exact file count.

---

# 4. Root Machine Package Schema

## 4.1 MachinePackageDefinition

This is the top-level package container.

### Fields

- `schemaVersion` : string  
  Version of the content schema.

- `packageVersion` : string  
  Version of this authored machine package.

- `machine` : MachineDefinition  
  The machine represented by the package.

- `assemblies` : array<AssemblyDefinition>  
  Major assemblies within the machine.

- `subassemblies` : array<SubassemblyDefinition>  
  Meaningful grouped construction units.

- `parts` : array<PartDefinition>  
  All parts referenced by the package.

- `tools` : array<ToolDefinition>  
  Tools referenced by the package.

- `steps` : array<StepDefinition>  
  All step definitions.

- `effects` : array<EffectDefinition> (optional)  
  Reusable effect definitions.

- `hints` : array<HintDefinition> (optional)  
  Reusable hint definitions.

- `challengeConfig` : ChallengeConfigDefinition (optional)  
  Challenge-related metadata.

- `assetManifest` : AssetManifestDefinition (optional)  
  Asset reference metadata.

- `previewConfig` : PackagePreviewConfigDefinition (optional)  
  Authored preview and presentation metadata, including finished-subassembly stacking frames.

### Example

```json
{
  "schemaVersion": "1.0.0",
  "packageVersion": "0.1.0",
  "machine": { "id": "power_cube_frame_corner", "name": "Power Cube Frame Corner" },
  "assemblies": [],
  "subassemblies": [],
  "parts": [],
  "tools": [],
  "steps": []
}
```

---

# 5. Machine Schema

## 5.1 MachineDefinition

Represents the top-level learning object.

### Fields

- `id` : string  
  Stable machine identifier.

- `name` : string  
  Display name.

- `displayName` : string (optional)  
  Alternate user-facing title if needed.

- `description` : string  
  High-level explanation of the machine or slice.

- `difficulty` : enum  
  Suggested values:
  - `beginner`
  - `intermediate`
  - `advanced`

- `estimatedBuildTimeMinutes` : integer (optional)

- `learningObjectives` : array<string>

- `recommendedMode` : enum (optional)  
  Suggested values:
  - `tutorial`
  - `guided`
  - `standard`
  - `challenge`

- `entryAssemblyIds` : array<string>  
  Which assemblies begin the experience.

- `prerequisiteNotes` : array<string> (optional)

- `sourceReferences` : array<SourceReferenceDefinition> (optional)

### Example

```json
{
  "id": "power_cube_frame_corner",
  "name": "Power Cube Frame Corner",
  "description": "A small structural frame corner assembly derived from the Open Source Ecology Power Cube path.",
  "difficulty": "beginner",
  "estimatedBuildTimeMinutes": 12,
  "learningObjectives": [
    "Understand bracket and plate relationships",
    "Practice bolt placement and order",
    "Recognize how reinforcement improves the frame"
  ],
  "entryAssemblyIds": ["assembly_frame_corner"]
}
```

---

# 6. Assembly Schema

## 6.1 AssemblyDefinition

Represents a major machine section.

### Fields

- `id` : string
- `name` : string
- `description` : string (optional)
- `machineId` : string
- `subassemblyIds` : array<string>
- `stepIds` : array<string>
- `dependencyAssemblyIds` : array<string> (optional)
- `learningFocus` : string (optional)

### Example

```json
{
  "id": "assembly_frame_corner",
  "name": "Frame Corner Assembly",
  "machineId": "power_cube_frame_corner",
  "subassemblyIds": ["subassembly_corner_bracket_unit"],
  "stepIds": ["step_place_plate", "step_attach_bracket", "step_insert_bolts"],
  "learningFocus": "Structural alignment and reinforcement"
}
```

---

# 7. Subassembly Schema

## 7.1 SubassemblyDefinition

Represents a smaller grouped construction unit.

### Fields

- `id` : string
- `name` : string
- `assemblyId` : string
- `description` : string (optional)
- `partIds` : array<string>
- `stepIds` : array<string>
- `milestoneMessage` : string (optional)

### Example

```json
{
  "id": "subassembly_corner_bracket_unit",
  "name": "Corner Bracket Unit",
  "assemblyId": "assembly_frame_corner",
  "partIds": ["frame_plate_a", "corner_bracket_a", "m8_hex_bolt"],
  "stepIds": ["step_place_plate", "step_attach_bracket", "step_insert_bolts"],
  "milestoneMessage": "The corner reinforcement is now structurally secured."
}
```

---

# 8. Part Schema

## 8.1 PartDefinition

Represents an individual build item.

### Fields

- `id` : string
- `name` : string
- `displayName` : string (optional)
- `category` : enum  
  Suggested values:
  - `plate`
  - `bracket`
  - `fastener`
  - `shaft`
  - `panel`
  - `housing`
  - `pipe`
  - `custom`

- `material` : string
- `function` : string
- `structuralRole` : string (optional)
- `quantity` : integer
- `toolIds` : array<string> (optional)
- `assetRef` : string
- `searchTerms` : array<string> (optional)
- `allowPhysicalSubstitution` : boolean
- `defaultOrientationHint` : string (optional)
- `tags` : array<string> (optional)

### Example

```json
{
  "id": "corner_bracket_a",
  "name": "Corner Bracket",
  "category": "bracket",
  "material": "steel",
  "function": "Connects the plate and reinforcement edge at the frame corner.",
  "structuralRole": "Reinforcement bracket",
  "quantity": 1,
  "toolIds": ["tool_wrench_13mm"],
  "assetRef": "assets/parts/corner_bracket_a.glb",
  "searchTerms": ["steel corner bracket", "power cube frame bracket"],
  "allowPhysicalSubstitution": true
}
```

---

# 9. Tool Schema

## 9.1 ToolDefinition

Represents a tool referenced during assembly.

### Fields

- `id` : string
- `name` : string
- `category` : enum  
  Suggested values:
  - `hand_tool`
  - `power_tool`
  - `measurement`
  - `safety`
  - `specialty`

- `purpose` : string
- `usageNotes` : string (optional)
- `safetyNotes` : string (optional)
- `searchTerms` : array<string> (optional)
- `assetRef` : string (optional)

### Example

```json
{
  "id": "tool_wrench_13mm",
  "name": "13mm Wrench",
  "category": "hand_tool",
  "purpose": "Used to tighten M8 fasteners in this assembly.",
  "usageNotes": "Use after the bracket and plate are aligned.",
  "searchTerms": ["13mm wrench", "metric wrench 13mm"]
}
```

---

# 10. Step Schema

## 10.1 StepDefinition

Represents one coherent learner action.

### Fields

- `id` : string
- `name` : string
- `assemblyId` : string
- `subassemblyId` : string (optional)
- `sequenceIndex` : integer
- `instructionText` : string
- `whyItMattersText` : string (optional)
- `requiredPartIds` : array<string>
- `requiredSubassemblyId` : string (optional)
- `optionalPartIds` : array<string> (optional)
- `relevantToolIds` : array<string> (optional)
- `targetIds` : array<string> (optional)
- `completionType` : enum (required — legacy, see deprecation note below)
  - `placement` — user drags parts onto preview targets
  - `tool_action` — user performs tool actions (e.g. tighten bolts)
  - `pipe_connection` — user selects two endpoints to form a connection
  - `confirmation` — user presses a Continue/Confirm button

- `targetOrder` : enum (optional, default `"parallel"`)
  - `parallel` — all previews and tool targets visible simultaneously
  - `sequential` — one target at a time, in `targetIds` array order

- `validationRuleIds` : array<string> (optional)
- `hintIds` : array<string> (optional)
- `effectTriggerIds` : array<string> (optional)
- `allowSkip` : boolean (optional)
- `challengeFlags` : StepChallengeFlagsDefinition (optional)
- `eventTags` : array<string> (optional)

#### Schema-Bridge Fields (Phase 14a + Phase 3)

The following fields are implemented in `StepDefinition.cs` with full validator support. Legacy flat fields remain supported as fallback. See `STEP_CAPABILITY_MATRIX.md` for the full taxonomy.

##### Family and Profile (Phase 14a — wired)

- `family` : enum (optional)
  - `Place` — spatial placement onto preview targets
  - `Use` — tool activation on targets
  - `Connect` — two-endpoint connection
  - `Confirm` — non-spatial acknowledgement/verification

  When absent, derived from `completionType` using the legacy mapping in `STEP_CAPABILITY_MATRIX.md` §6.

- `profile` : string (optional)
  Family-scoped refinement. Examples: `Place.Clamp`, `Use.Torque`, `Use.Weld`, `Use.Cut`, `Connect.Cable`.
  When absent, the family default behavior applies.

- `viewMode` : string (optional)
  Semantic classification of how the camera should frame the step's spatial context. Valid values: `SourceAndTarget`, `PairEndpoints`, `WorkZone`, `PathView`, `Overview`, `Inspect`.
  When absent, resolved from family + profile using the default mapping in `STEP_VIEW_FRAMING.md` section 4.
  This is NOT an authored camera angle (yaw/pitch/distance) -- it is a semantic intent that the runtime resolves into camera parameters using the step's spatial data.

Note: **Interaction pattern** (e.g. PlaceOnZone, SelectPair, TargetHit) is NOT an authored schema field. It is resolved at runtime from the combination of `family`, `profile`, and step data shape. See `INTERACTION_PATTERN_MATRIX.md` for the pattern catalog and resolution rules.

Important:

- constrained fitting of a finished unit should remain inside the existing `Place`
  family
- do not add a new `Adjust` family for operations such as fitting a completed X-axis
  between already-mounted Y axes
- that behavior should resolve from `Place` + an appropriate profile plus step data
  shape

##### Finished Subassembly Placement

- `requiredSubassemblyId` : string (optional)

  Use this when the learner must move a previously completed subassembly as one rigid
  unit.

  Rules:

  - mutually exclusive with `requiredPartIds` in the current contract
  - used on normal `Place` steps; do not create a separate stack family
  - must resolve to an authored subassembly
  - should pair with a target that references the same subassembly through `associatedSubassemblyId`

  See `STACKING_ARCHITECTURE.md` for the runtime model and authoring rules.

##### Capability Payloads (Phase 3 — wired)

Each payload groups related step capabilities into a single optional object. When a payload is present, its fields take precedence over the corresponding flat fields on StepDefinition. When absent, the flat fields are used as fallback via `Resolved*` accessor properties.

- `guidance` : StepGuidancePayload (optional)
  - `instructionText` : string — what to do
  - `whyItMattersText` : string — why this step exists
  - `hintIds` : array\<string\> — progressive hints
  - `contextualDiagramRef` : string — optional diagram asset reference

- `validation` : StepValidationPayload (optional)
  - `validationRuleIds` : array\<string\> — references to validation rule definitions

- `feedback` : StepFeedbackPayload (optional)
  - `effectTriggerIds` : array\<string\> — references to effect definitions

- `reinforcement` : StepReinforcementPayload (optional)
  - `milestoneMessage` : string — completion achievement summary
  - `consequenceText` : string — structural consequence explanation
  - `safetyNote` : string — safety implication
  - `counterfactualText` : string — "what would happen if..." explanation

- `difficulty` : StepDifficultyPayload (optional)
  - `allowSkip` : boolean
  - `challengeFlags` : StepChallengeFlagsDefinition
  - `timeLimitSeconds` : number
  - `hintAvailability` : enum — `"always"` (default), `"limited"`, `"none"`

#### Deprecation Note: `completionType`

`completionType` remains the **active runtime field** and is required for all current authored content. It will not be removed.

The target direction replaces `completionType` with `family` + `profile`. When `family` is present on a step, it takes precedence. When absent, the runtime derives `family` from `completionType`. See `STEP_CAPABILITY_MATRIX.md` §6 for the mapping and §8 for the migration path.

### Example

```json
{
  "id": "step_attach_bracket",
  "name": "Attach Corner Bracket",
  "assemblyId": "assembly_frame_corner",
  "subassemblyId": "subassembly_corner_bracket_unit",
  "sequenceIndex": 2,
  "instructionText": "Align the corner bracket against the frame plate and move it into position.",
  "whyItMattersText": "The bracket reinforces the corner and helps the frame resist deformation.",
  "requiredPartIds": ["corner_bracket_a"],
  "relevantToolIds": ["tool_wrench_13mm"],
  "targetIds": ["target_corner_bracket_slot_a"],
  "completionType": "placement",
  "validationRuleIds": ["validation_corner_bracket_alignment"],
  "hintIds": ["hint_align_bracket_edge"],
  "effectTriggerIds": ["effect_valid_placement_pulse"]
}
```

---

# 11. Validation Schema

## 11.1 ValidationRuleDefinition

Represents structured validation logic inputs.

### Fields

- `id` : string
- `type` : enum  
  Suggested values:
  - `placement`
  - `orientation`
  - `part_identity`
  - `dependency`
  - `multi_part`
  - `confirmation`

- `targetId` : string (optional)
- `expectedPartId` : string (optional)
- `positionToleranceMm` : number (optional)
- `rotationToleranceDeg` : number (optional)
- `requiredStepIds` : array<string> (optional)
- `requiredPartIds` : array<string> (optional)
- `modeOverrides` : ValidationModeOverrideDefinition (optional)
- `failureMessage` : string (optional)
- `correctionHintId` : string (optional)

### Example

```json
{
  "id": "validation_corner_bracket_alignment",
  "type": "placement",
  "targetId": "target_corner_bracket_slot_a",
  "expectedPartId": "corner_bracket_a",
  "positionToleranceMm": 6.0,
  "rotationToleranceDeg": 12.0,
  "failureMessage": "The bracket is not aligned closely enough to the target corner.",
  "correctionHintId": "hint_align_bracket_edge"
}
```

---

# 12. Hint Schema

## 12.1 HintDefinition

Represents an optional guidance element.

### Fields

- `id` : string
- `type` : enum  
  Suggested values:
  - `text`
  - `highlight`
  - `preview`
  - `directional`
  - `explanatory`
  - `tool_reminder`

- `title` : string (optional)
- `message` : string
- `targetId` : string (optional)
- `partId` : string (optional)
- `toolId` : string (optional)
- `priority` : enum (optional)  
  Suggested values:
  - `low`
  - `medium`
  - `high`

### Example

```json
{
  "id": "hint_align_bracket_edge",
  "type": "highlight",
  "title": "Align the bracket edge",
  "message": "Match the inside edge of the bracket to the outside corner of the plate before confirming placement.",
  "targetId": "target_corner_bracket_slot_a",
  "priority": "medium"
}
```

---

# 13. Effects Schema

## 13.1 EffectDefinition

Represents a reusable effect reference.

### Fields

- `id` : string
- `type` : enum  
  Suggested values:
  - `placement_feedback`
  - `success_feedback`
  - `error_feedback`
  - `welding`
  - `sparks`
  - `heat_glow`
  - `fire`
  - `dust`
  - `milestone`

- `assetRef` : string (optional)
- `shaderRef` : string (optional)
- `fallbackRef` : string (optional)
- `triggerPolicy` : enum (optional)  
  Suggested values:
  - `on_step_enter`
  - `on_valid_candidate`
  - `on_success`
  - `on_failure`
  - `on_completion`

- `notes` : string (optional)

### Example

```json
{
  "id": "effect_valid_placement_pulse",
  "type": "placement_feedback",
  "assetRef": "assets/effects/placement_success.prefab",
  "fallbackRef": "assets/effects/placement_success_simple.prefab",
  "triggerPolicy": "on_success"
}
```

---

# 14. Target Schema

## 14.1 TargetDefinition

Represents a logical assembly target.

### Fields

- `id` : string
- `name` : string (optional)
- `anchorRef` : string  
  Runtime-recognizable anchor or transform reference.

- `description` : string (optional)
- `associatedPartId` : string (optional)
- `associatedSubassemblyId` : string (optional)
- `tags` : array<string> (optional)

### Example

```json
{
  "id": "target_corner_bracket_slot_a",
  "anchorRef": "anchors/frame/corner_bracket_slot_a",
  "associatedPartId": "corner_bracket_a"
}
```

---

## 14.2 PackagePreviewConfigDefinition

Represents authored preview and presentation metadata used by the runtime.

### Fields

- `defaultAssemblyScaleMultiplier` : number (optional)
- `subassemblyPlacements` : array<SubassemblyPreviewPlacement> (optional)  
  Authored fabrication reference frames for completed subassemblies that may later move as one rigid unit.
- `completedSubassemblyParkingPlacements` : array<SubassemblyPreviewPlacement> (optional)  
  Presentation-space parking poses for completed subassemblies that should persist after fabrication but before later stacking.
- `integratedSubassemblyPlacements` : array<IntegratedSubassemblyPreviewPlacement> (optional)  
  Canonical final member poses used after a stacking step is committed.

### SubassemblyPreviewPlacement

- `subassemblyId` : string
- `position` : Vector3Like
- `rotation` : QuaternionLike
- `scale` : Vector3Like (optional, default `1,1,1`)

### IntegratedSubassemblyPreviewPlacement

- `subassemblyId` : string
- `targetId` : string
- `memberPlacements` : array<IntegratedMemberPreviewPlacement>

### IntegratedMemberPreviewPlacement

- `partId` : string
- `position` : Vector3Like
- `rotation` : QuaternionLike
- `scale` : Vector3Like (optional, default `1,1,1`)

### Notes

- `subassemblyPlacements` defines the fabrication reference frame of the finished unit.
- `completedSubassemblyParkingPlacements` defines where a finished unit should persist after fabrication when one shared work bay is used.
- `integratedSubassemblyPlacements` defines the canonical committed display after the finished unit has been placed.
- Use `integratedSubassemblyPlacements` when leaving the fabrication panel shell in place would create overlap, z-fighting, or misleading final geometry.
- See `STACKING_ARCHITECTURE.md` for the full rationale and runtime behavior.

---

# 15. Challenge Schema

## 15.1 ChallengeConfigDefinition

Represents optional machine-level challenge behavior.

### Fields

- `enabled` : boolean
- `trackTime` : boolean
- `trackRetries` : boolean
- `trackHintUsage` : boolean
- `leaderboardReady` : boolean
- `strictValidationModeAvailable` : boolean

### Example

```json
{
  "enabled": true,
  "trackTime": true,
  "trackRetries": true,
  "trackHintUsage": true,
  "leaderboardReady": true,
  "strictValidationModeAvailable": true
}
```

## 15.2 StepChallengeFlagsDefinition

Represents step-specific challenge behavior.

### Fields

- `penalizeHintUsage` : boolean (optional)
- `penalizeInvalidPlacement` : boolean (optional)
- `stricterToleranceAvailable` : boolean (optional)

### Example

```json
{
  "penalizeHintUsage": true,
  "penalizeInvalidPlacement": true,
  "stricterToleranceAvailable": true
}
```

---

# 16. Asset Manifest Schema

## 16.1 AssetManifestDefinition

Tracks runtime-resolvable asset references.

### Fields

- `modelRefs` : array<string>
- `textureRefs` : array<string> (optional)
- `effectRefs` : array<string> (optional)
- `uiRefs` : array<string> (optional)

### Example

```json
{
  "modelRefs": [
    "assets/parts/frame_plate_a.glb",
    "assets/parts/corner_bracket_a.glb",
    "assets/parts/m8_hex_bolt.glb"
  ],
  "effectRefs": [
    "assets/effects/placement_success.prefab"
  ]
}
```

---

# 17. Source Reference Schema

## 17.1 SourceReferenceDefinition

Captures research/source provenance.

### Fields

- `id` : string (optional)
- `type` : enum  
  Suggested values:
  - `blueprint`
  - `photo`
  - `diagram`
  - `author_note`
  - `reference_doc`

- `title` : string
- `referencePath` : string (optional)
- `notes` : string (optional)

### Example

```json
{
  "type": "blueprint",
  "title": "Power Cube Frame Corner Reference Drawing",
  "referencePath": "sources/power_cube/frame_corner_reference_01.png",
  "notes": "Used to infer bracket placement and plate proportions."
}
```

---

# 18. Physical Substitution Rules

Physical substitution is a first-class workflow.

## 18.1 Part-Level Control

`allowPhysicalSubstitution` on `PartDefinition` determines eligibility.

## 18.2 Step-Level Control

`completionType` on `StepDefinition` determines the step's completion mechanism (`placement`, `tool_action`, `pipe_connection`, or `confirmation`). The target direction replaces this with `family` + `profile` — see `STEP_CAPABILITY_MATRIX.md`.

## 18.3 Rule

A part being physically substitutable does not automatically mean every step using it can complete via physical substitution.

The step controls the actual workflow.

---

# 19. Event Model Compatibility

The schema should remain compatible with the runtime event model.

Useful fields for event-driven runtime include:

- stable ids for all entities
- step `eventTags`
- effect ids
- hint ids
- target ids
- challenge flags

This allows runtime systems to emit clear events without inventing content state on the fly.

---

# 20. Multiplayer Readiness Rules

The schema must stay open for future multiplayer.

That means content should allow runtime systems to represent:

- active step identity
- part identity
- target identity
- placement completion
- physical substitution confirmation
- hint usage
- challenge timing relevance

The schema does not need full multiplayer fields everywhere now, but it must not prevent them later.

---

# 21. UI Compatibility Rules

The schema should support UI Toolkit-driven presentation.

That means the data should expose clean user-facing text inputs such as:

- instruction text
- why-it-matters text
- part display information
- tool display information
- hint messages
- milestone messages

The UI should consume schema-derived display data without inventing core content meaning.

---

# 22. ScriptableObject vs JSON Notes

This schema is logical and can be implemented through:

- JSON exports
- ScriptableObject authoring with export
- a hybrid approach

## Recommended Direction

For long-term flexibility, prefer:

- clear logical schema
- authoring tools that may use ScriptableObjects internally
- export to runtime-loadable package data where appropriate

This gives the best balance between Unity authoring convenience and portable machine packages.

---

# 23. Validation Rules for Schema Quality

Before approving a package, validate:

- all ids are unique
- all references resolve
- no step references missing parts/tools/targets
- no unused critical assets or definitions exist accidentally
- required fields are present
- enumerations use valid values
- package and schema versions are declared
- no machine-specific logic is hidden outside the package

---

# 24. Anti-Patterns to Avoid

Avoid:

- inconsistent ids
- unnamed assets mapped by guesswork
- step logic hidden only in scenes
- effects with no effect definitions
- validation defined only in code
- vague part categories that mean nothing
- giant monolithic steps
- hint data embedded only in UI scripts
- runtime assumptions not represented in content

---

# 25. Recommended First Package Scope

The first authentic package should remain small.

Recommended first real package:

- one Power Cube-aligned frame corner or bracket subassembly
- modest part count
- simple tool story
- clear targets
- clear step sequence
- minimal but real validation
- at least one hint
- at least one optional effect hook
- optional challenge config readiness

This is the correct place to prove the schema.

---

# 26. Final Guidance

The correct schema strategy is:

- keep identities explicit
- keep responsibilities separate
- author content as a package, not a scene hack
- make steps, validation, hints, effects, and challenge fields first-class data
- keep the schema readable and versioned
- let runtime systems interpret content reliably
- preserve room for future multiplayer and content growth

That is how the data layer becomes strong enough to support the long-term vision of the project.
