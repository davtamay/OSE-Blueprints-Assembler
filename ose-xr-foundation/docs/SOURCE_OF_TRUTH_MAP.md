# SOURCE_OF_TRUTH_MAP.md

## Purpose

This document defines the source-of-truth ownership model for the XR assembly training application.

Its goal is to prevent architectural drift by making three things explicit:

1. **Which runtime system owns which state**
2. **Which document is authoritative for each domain**
3. **Which files and modules agents must inspect before changing a subsystem**

This is especially important because the project is:

- large
- modular
- data-driven
- cross-platform
- agent-assisted
- intended to remain open for future multiplayer, challenge, and remote-assistance expansion

Without a clear source-of-truth map, systems tend to become ambiguous, duplicate responsibility, and silently diverge.

This file should be used together with:

- `AGENTS.md`
- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/INTERACTION_MODEL.md`
- `docs/UI_ARCHITECTURE.md`
- `docs/PART_AUTHORING_PIPELINE.md`
- `docs/DATA_SCHEMA.md`
- `docs/RUNTIME_EVENT_MODEL.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`
- `TASK_EXECUTION_PROTOCOL.md`
- `TEST_STRATEGY.md`
- `PERFORMANCE_ARCHITECTURE.md`

---

# 1. Core Principle

Every important responsibility must have a clearly defined owner.

That means:

- one runtime system should own the authoritative version of a piece of state
- one document should define the architectural truth for a domain
- one module area should be the first place to inspect before making changes

UI must not become truth.

Scene objects must not become hidden truth.

Agent convenience must not replace clear ownership.

---

# 2. Runtime State Ownership Map

This section defines which subsystem owns which categories of runtime truth.

## 2.1 Machine Session State

### Authoritative Owner

- `MachineSessionController`
- supporting runtime state store / session models

### Owns

- active machine id
- active mode
- active assembly id
- active subassembly id
- active step id
- persisted machine session context
- high-level session lifecycle state

### Must Not Be Owned By

- UI Toolkit panels
- scene-only references
- individual part views

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/ARCHITECTURE.md`

---

## 2.2 Step Progression State

### Authoritative Owner

- `StepController`
- `ProgressionController`

### Owns

- step lifecycle state
- current step progression
- completed/available/locked state
- transition readiness
- step completion and advancement logic

### Must Not Be Owned By

- UI button state
- arbitrary event listeners without runtime backing

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/RUNTIME_EVENT_MODEL.md`

---

## 2.3 Part Placement State

### Authoritative Owner

- `AssemblyRuntimeController`
- `PlacementValidator`
- explicit runtime part/placement state models

### Owns

- whether a part is introduced
- whether a part is selected
- whether a part is grabbed
- whether a part is a valid placement candidate
- whether a part is placed
- whether a part is physically substituted

### Must Not Be Owned By

- the rendered object alone
- highlight state alone
- UI panels

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/INTERACTION_MODEL.md`
- `docs/DATA_SCHEMA.md`

---

## 2.4 Validation Truth

### Authoritative Owner

- `PlacementValidator`
- `ConstraintValidationService`
- `StepCompletionEvaluator`

### Owns

- validation outcomes
- tolerance checks
- dependency checks
- acceptance/rejection reasons
- completion-mode-specific rules

### Must Not Be Owned By

- panel controllers
- effects systems
- one-off scene scripts

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/DATA_SCHEMA.md`
- `TEST_STRATEGY.md`

---

## 2.5 Hint Truth

### Authoritative Owner

- runtime hint service / hint system
- step-authoring data and step context

### Owns

- available hints
- active hint state
- hint usage tracking
- hint display eligibility

### Must Not Be Owned By

- UI-only visibility flags
- random local script booleans

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/DATA_SCHEMA.md`
- `docs/UI_ARCHITECTURE.md`

---

## 2.6 Tool Relevance State

### Authoritative Owner

- step runtime
- package data
- presenters that derive display data from runtime

### Owns

- which tools are relevant to the current step
- whether a tool has been introduced
- whether a tool is displayed for context

### Must Not Be Owned By

- static UI assumptions
- part prefabs alone

### Primary Docs

- `docs/DATA_SCHEMA.md`
- `docs/PART_AUTHORING_PIPELINE.md`
- `docs/UI_ARCHITECTURE.md`

---

## 2.7 Effects Truth

### Authoritative Owner

- runtime state and effect trigger events
- `EffectRuntimeController`
- `EffectDefinitionRegistry`

### Owns

- when effects should play
- which effect id is referenced
- fallback selection by quality tier
- whether an effect is presentation only or part of process confirmation

### Must Not Be Owned By

- arbitrary animation timelines with no runtime backing
- scene-only assumptions

### Primary Docs

- `docs/RUNTIME_EVENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `PERFORMANCE_ARCHITECTURE.md`

---

## 2.8 Challenge State

### Authoritative Owner

- `ChallengeRunTracker`
- challenge runtime state model

### Owns

- timer state
- retry counts
- hint penalty tracking
- score inputs
- completion metrics

### Must Not Be Owned By

- UI timer labels alone
- debug state only
- isolated step scripts

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/DATA_SCHEMA.md`
- `PERFORMANCE_ARCHITECTURE.md`

---

## 2.9 Persistence Truth

### Authoritative Owner

- `SessionPersistenceService`
- persistence models derived from runtime state

### Owns

- saved session data
- restore data
- migration/version handling for saved state
- resume-safe session reconstruction

### Must Not Be Owned By

- scattered `PlayerPrefs` assumptions
- random UI state snapshots

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `TEST_STRATEGY.md`

---

## 2.10 Input Truth

### Authoritative Owner

- Unity Input System action maps
- platform adapters
- `InputActionRouter`

### Owns

- native input collection
- canonical runtime action dispatch
- control scheme mapping
- adapter translation from device signals to runtime actions

### Must Not Be Owned By

- individual feature scripts doing raw device polling
- random UI scripts
- world object scripts reading hardware directly

### Primary Docs

- `TECH_STACK.md`
- `docs/INTERACTION_MODEL.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`

---

## 2.11 UI State Ownership

### Authoritative Owner

UI is **not** authoritative for gameplay/runtime truth.

UI owns only:

- presentation state
- local panel visibility
- visual transitions
- user input callbacks routed into runtime systems
- document/view lifecycle

### Primary Runtime Owners UI Depends On

- session runtime
- step runtime
- hint runtime
- challenge runtime
- persistence/runtime restore flow

### Primary Docs

- `docs/UI_ARCHITECTURE.md`
- `docs/INTERACTION_MODEL.md`

---

## 2.12 Multiplayer-Ready Shared State

### Authoritative Owner

Current ownership remains in runtime models, but the state must remain sync-safe.

Likely future sync-relevant state includes:

- active step id
- placed part states
- physical substitution confirmations
- current challenge timing state
- completion events
- interaction ownership hints

### Must Not Be Owned By

- visual-only state
- ephemeral-only animations
- panel-only state

### Primary Docs

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/RUNTIME_EVENT_MODEL.md`
- `PERFORMANCE_ARCHITECTURE.md`

---

## 2.13 Step Capability Shape

### Authoritative Owner

- step authoring data (machine package JSON)
- `STEP_CAPABILITY_MATRIX.md`

### Owns

- family classification (Place, Use, Connect, Confirm)
- profile selection (family-scoped refinement)
- capability payload shape contract (guidance, validation, feedback, reinforcement, difficulty)
- legacy mapping from `completionType` to family
- rules for extending the family and profile taxonomy

### Must Not Be Owned By

- runtime dispatch branches without documented family backing
- UI assumptions about step type
- one-off mechanic scripts

### Primary Docs

- `docs/STEP_CAPABILITY_MATRIX.md`
- `docs/DATA_SCHEMA.md`
- `docs/ASSEMBLY_RUNTIME.md`

---

# 3. Document Authority Map

This section defines which document is authoritative for each domain.

## 3.1 Product / Mission / Agent Behavior

### Authoritative Doc

- `AGENTS.md`

### Governs

- project mission
- high-level product intent
- agent behavior expectations
- broad development priorities

---

## 3.2 Technology Choices

### Authoritative Doc

- `TECH_STACK.md`

### Governs

- Unity version baseline
- stable-upgrade policy
- De-Panther WebXR choice
- Input System choice
- UI Toolkit choice
- render path policy
- optional future tooling like Unity/Meta/Porfeto MCP

---

## 3.3 System Architecture

### Authoritative Doc

- `docs/ARCHITECTURE.md`

### Governs

- overall system decomposition
- major module boundaries
- runtime layers
- content/runtime/presentation separation

---

## 3.4 Runtime Execution Behavior

### Authoritative Doc

- `docs/ASSEMBLY_RUNTIME.md`

### Governs

- machine session lifecycle
- step execution model
- runtime ownership
- progression and validation flow
- physical substitution runtime semantics

---

## 3.5 Unity Project Structure

### Authoritative Doc

- `docs/UNITY_PROJECT_STRUCTURE.md`

### Governs

- folder layout
- module placement
- `.asmdef` organization
- script/module boundaries
- UI Toolkit structure placement

---

## 3.6 Interaction Semantics

### Authoritative Doc

- `docs/INTERACTION_MODEL.md`

### Governs

- canonical actions
- desktop/mobile/XR mapping
- interaction contexts
- UI vs world routing
- physical substitution interaction semantics

---

## 3.7 UI Architecture

### Authoritative Doc

- `docs/UI_ARCHITECTURE.md`

### Governs

- UI Toolkit layering
- presenters/controllers/root coordination
- presentation-only rule
- panel structure
- XR/screen-space UI strategy

---

## 3.8 Content Authoring Workflow

### Authoritative Doc

- `docs/PART_AUTHORING_PIPELINE.md`

### Governs

- blueprint-to-package workflow
- part/tool extraction
- subassembly design
- metadata/step authoring flow
- content validation pass expectations

---

## 3.9 Canonical Data Shape

### Authoritative Doc

- `docs/DATA_SCHEMA.md`

### Governs

- machine package schema
- part/tool/step/validation/effect/hint fields
- id rules
- package versioning
- challenge metadata shape

---

## 3.10 Runtime Events and Signals

### Authoritative Doc

- `docs/RUNTIME_EVENT_MODEL.md`

### Governs

- event categories
- event bus semantics
- event payload guidelines
- decoupled runtime communication

---

## 3.11 Build Order and Delivery Sequence

### Authoritative Doc

- `docs/IMPLEMENTATION_CHECKLIST.md`

### Governs

- phased execution order
- validation gates
- high-level implementation sequence

---

## 3.12 Task-by-Task Working Style

### Authoritative Doc

- `TASK_EXECUTION_PROTOCOL.md`

### Governs

- how agents should execute tasks
- sizing rules
- validation rules
- commit rules
- planning-mode rules

---

## 3.13 Testing Discipline

### Authoritative Doc

- `TEST_STRATEGY.md`

### Governs

- unit/system/runtime/content/performance testing expectations
- regression testing expectations
- CI direction

---

## 3.14 Performance Strategy

### Authoritative Doc

- `PERFORMANCE_ARCHITECTURE.md`

### Governs

- CPU/GPU boundaries
- streaming
- tiering
- UI/effects performance rules
- WebWorker/WASM decision boundaries
- Porfeto MCP profiling consideration

---

## 3.15 First Slice Content Choice

### Authoritative Docs

- `docs/VERTICAL_SLICE_SPEC.md`
- `docs/MACHINE_SELECTION_RESEARCH.md`

### Governs

- tutorial slice
- first authentic OSE slice
- recommended Power Cube-aligned starting point

---

# 4. Module Inspection Map

Before changing a subsystem, agents should inspect these areas first.

## 4.1 Before Changing Input

Inspect:

- `TECH_STACK.md`
- `docs/INTERACTION_MODEL.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`

Likely code areas:

- `Scripts/Input/`
- `Scripts/Interaction/`

---

## 4.2 Before Changing UI

Inspect:

- `TECH_STACK.md`
- `docs/UI_ARCHITECTURE.md`
- `docs/INTERACTION_MODEL.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`

Likely code areas:

- `Scripts/UI/`
- `UI/UXML/`
- `UI/USS/`
- `UI/Documents/`

---

## 4.3 Before Changing Runtime Progression

Inspect:

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/ARCHITECTURE.md`
- `docs/RUNTIME_EVENT_MODEL.md`

Likely code areas:

- `Scripts/Runtime/`
- `Scripts/Validation/`
- `Scripts/Presentation/`

---

## 4.4 Before Changing Content Schema

Inspect:

- `docs/DATA_SCHEMA.md`
- `docs/PART_AUTHORING_PIPELINE.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `TEST_STRATEGY.md`

Likely code areas:

- `Scripts/Content/`
- content validators
- package export tooling

---

## 4.5 Before Changing Validation Rules

Inspect:

- `docs/DATA_SCHEMA.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `TEST_STRATEGY.md`

Likely code areas:

- `Scripts/Validation/`
- `Scripts/Runtime/`

---

## 4.6 Before Changing Effects

Inspect:

- `docs/RUNTIME_EVENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `PERFORMANCE_ARCHITECTURE.md`

Likely code areas:

- `Scripts/Effects/`
- `Shaders/`
- `VFX/`

---

## 4.7 Before Changing Persistence

Inspect:

- `docs/ASSEMBLY_RUNTIME.md`
- `TEST_STRATEGY.md`
- `TASK_EXECUTION_PROTOCOL.md`

Likely code areas:

- `Scripts/Persistence/`
- runtime state models

---

## 4.8 Before Changing Performance-Sensitive Systems

Inspect:

- `PERFORMANCE_ARCHITECTURE.md`
- `TEST_STRATEGY.md`
- `TECH_STACK.md`

Likely code areas:

- runtime hot paths
- input hot paths
- UI update paths
- asset loading paths
- effect systems

---

## 4.9 Before Changing Multiplayer-Open Boundaries

Inspect:

- `docs/ASSEMBLY_RUNTIME.md`
- `docs/RUNTIME_EVENT_MODEL.md`
- `docs/INTERACTION_MODEL.md`
- `PERFORMANCE_ARCHITECTURE.md`

Likely code areas:

- runtime state models
- networking seams
- sync-safe event boundaries

---

# 5. What UI Is Allowed to Own

UI may own:

- local panel visibility
- panel animation state
- local focus state
- temporary visual emphasis
- input callback wiring
- document lifecycle
- visual layout and style
- non-authoritative display models derived from runtime state

UI may **not** own:

- true step completion
- true placement validity
- actual machine state
- challenge truth
- persistence truth
- multiplayer truth

If a UI element appears to own these, the architecture is drifting.

---

# 6. What Scene Objects Are Allowed to Own

Scene objects may own:

- transform presence
- view-only visual state
- renderer/material setup
- panel anchors
- local helper components
- world-space UI host transforms
- content anchor references

Scene objects may **not** be the only source of truth for:

- completion state
- challenge metrics
- hint usage
- step progression
- package-defined validation logic

If a runtime truth exists only in the scene, the architecture is drifting.

---

# 7. What Data Packages Are Allowed to Own

Machine packages and content data may own:

- structured machine/assembly/subassembly/part/tool/step definitions
- validation configuration
- hints
- effects references
- challenge flags
- source references
- asset references

Packages may **not** own:

- hidden runtime-only mutable state
- scene-resident transient state
- UI lifecycle state

Content data is declarative, not the live runtime itself.

---

# 8. Change Escalation Rules

If a change appears to affect multiple ownership boundaries, stop and review before implementing.

Examples:

- changing step semantics likely affects:
  - `docs/ASSEMBLY_RUNTIME.md`
  - `docs/DATA_SCHEMA.md`
  - `docs/RUNTIME_EVENT_MODEL.md`
  - tests

- changing UI interaction rules likely affects:
  - `docs/INTERACTION_MODEL.md`
  - `docs/UI_ARCHITECTURE.md`
  - input code
  - UI code

- changing package format likely affects:
  - content schema
  - runtime loading
  - authoring pipeline
  - validation tooling
  - tests

Use Plan Mode when the change crosses multiple truth boundaries.

---

# 9. Documentation Drift Rules

Whenever a change alters an authoritative behavior or ownership rule, update the authoritative document.

Do not let the code and docs disagree silently.

If multiple docs overlap, update the **authoritative** one first, then revise dependent docs if needed.

---

# 10. Final Guidance

The correct way to keep this project coherent is:

- explicit runtime ownership
- explicit document authority
- explicit module boundaries
- explicit checks before changes
- no hidden truth in UI or scenes
- no casual schema drift
- no silent architecture rewrites

This map exists so human developers and agents can answer three critical questions before every major change:

1. **Who owns this truth?**
2. **Which doc defines it?**
3. **Which module should I inspect first?**

If those are clear, the project can grow safely.

