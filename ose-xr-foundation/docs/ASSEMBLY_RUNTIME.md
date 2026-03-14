# ASSEMBLY_RUNTIME.md

## Purpose

This document defines the runtime architecture for the XR assembly application.

It explains how machine content is executed once loaded into the app, how steps progress, how parts are validated, how effects are triggered, how physical substitution is handled, how multiplayer can be added cleanly later, and how the runtime stays modular, data-driven, and cross-platform.

This runtime must support:

- guided assembly
- tutorial scaffolding
- virtual and physical placement
- instructional feedback
- visual and process effects
- cross-platform input
- performance-aware loading
- future remote assistance
- future optimal multiplayer
- optional challenge and speed-run modes
- reliable validation and recovery when something breaks

The runtime should be built using the latest stable Unity technologies that are proven and production-appropriate.

---

# 1. Runtime Intent

The runtime is not just a scene controller.

It is a **state-driven assembly execution engine**.

Its job is to:

- load a machine package
- prepare assemblies, parts, tools, and effects
- activate the current step
- expose the right interaction affordances
- validate user actions
- provide reinforcement and feedback
- advance progression when conditions are met
- preserve state safely
- support cross-platform behavior consistently
- remain open for multiplayer synchronization without architectural rewrites

The runtime should never depend on one-off scene-specific logic for a single machine.

Instead, scenes should act as host environments while the runtime interprets machine content data.

---

# 2. Core Runtime Principles

The runtime must follow these principles:

1. data-driven execution
2. strict modular separation
3. deterministic internal state transitions where practical
4. platform-agnostic interaction routing
5. graceful failure and recovery
6. performance-first orchestration
7. latest stable production-ready tech choices
8. future multiplayer readiness
9. educational clarity over cleverness
10. every system delegated to the correct module

No monolithic manager should own everything.

The runtime should be composed of focused systems that communicate through clear state boundaries and contracts.

---

# 3. Runtime Layers

The runtime should be split into the following layers.

## 3.1 Machine Session Layer

Owns the active session.

Responsibilities:

- open machine package
- initialize runtime state
- manage session lifecycle
- manage persistence and restore
- expose current machine, assembly, subassembly, and step context

Suggested module:
- `MachineSessionController`

---

## 3.2 Assembly Orchestration Layer

Owns progression through assemblies and steps.

Responsibilities:

- activate current step
- lock/unlock steps
- detect completion
- advance progression
- support tutorial and guided modes
- support review and challenge flows

Suggested modules:
- `AssemblyRuntimeController`
- `StepController`
- `ProgressionController`

---

## 3.3 Placement and Validation Layer

Owns correctness of actions.

Responsibilities:

- validate part placement
- validate orientation
- validate alignment
- validate multi-part completion
- validate physical substitution confirmation
- expose recovery hints when invalid

Suggested modules:
- `PlacementValidator`
- `StepCompletionEvaluator`
- `ConstraintValidationService`

---

## 3.4 Interaction Layer

Owns canonical actions, not device-specific behavior.

Responsibilities:

- receive input actions from platform adapters
- route actions based on current interaction context
- maintain selection and interaction focus
- manage inspect, grab, rotate, place, confirm, and cancel

Suggested modules:
- `InputActionRouter`
- `InteractionContextController`
- `SelectionService`
- `ManipulationController`

This layer should be built on the **Unity Input System** as the canonical input foundation.

---

## 3.5 Presentation and Reinforcement Layer

Owns how the runtime teaches and motivates.

Responsibilities:

- instruction panels
- hints
- part metadata overlays
- tool explanations
- progress display
- success feedback
- aha-moment transitions
- challenge feedback
- onboarding prompts

Suggested modules:
- `InstructionPresenter`
- `HintSystem`
- `ProgressHUDController`
- `PartInfoPresenter`
- `ToolInfoPresenter`

---

## 3.6 Effects Layer

Owns process and feedback effects.

Responsibilities:

- trigger welding, sparks, heat glow, fire/torch, dust, and assembly effects
- select performance-safe fallback effects
- synchronize effect lifetimes with step events
- keep effects modular and reusable

Suggested modules:
- `EffectRuntimeController`
- `EffectDefinitionRegistry`
- `EffectPlaybackService`

Effects may use:

- HLSL shaders
- Unity particle systems
- VFX Graph where supported and stable enough for target platforms
- lightweight fallback materials or particles on constrained devices

---

## 3.7 Asset and Streaming Layer

Owns loading and lifecycle of assets.

Responsibilities:

- load machine manifests
- load part metadata
- stream models and textures
- prepare subassemblies just-in-time
- release unused assets
- preserve responsiveness during content transitions

Suggested modules:
- `MachineAssetLoader`
- `StreamingAssetCoordinator`
- `RuntimeAssetCache`
- `PartVisualSpawner`

---

## 3.8 Future Networking Layer

Owns multiplayer-ready boundaries.

Responsibilities:

- define what state is authoritative
- expose sync-safe state snapshots
- isolate network transport from assembly logic
- support future WebRTC or equivalent session sync
- support competitive challenge synchronization and scoreboard submission later

Suggested modules:
- `AssemblySyncStateAdapter`
- `RemoteAssistBridge`
- `ChallengeRunReporter`

The networking layer can remain disabled initially, but the boundaries must exist.

---

# 4. Runtime Lifecycle

The runtime should execute through an explicit lifecycle.

## 4.1 App Boot

At boot:

- initialize service container / bootstrap systems
- load platform capability profile
- initialize Unity Input System mappings
- initialize session persistence
- initialize asset cache
- initialize presentation systems

The app should reach a stable idle state before loading machine-specific content.

---

## 4.2 Machine Selection

When a machine is selected:

- load manifest
- validate content version compatibility
- preload minimal machine metadata
- show intro, prerequisites, difficulty, build time, and learning objectives
- select mode:
  - tutorial
  - guided
  - standard
  - challenge
  - review

---

## 4.3 Session Initialization

Create a machine session object that owns:

- machine id
- mode
- current assembly id
- current subassembly id
- current step id
- placed parts state
- physical substitution state
- validation history
- hint usage
- timer/challenge state
- effect state if relevant
- analytics hooks if later added

This session object should be serializable and restore-safe.

---

## 4.4 Step Activation

When a step activates:

- resolve required parts
- resolve required tools
- resolve placement targets
- resolve instructional text
- resolve optional hints
- resolve optional effects
- resolve challenge rules
- prepare interaction context
- update UI
- spawn or reveal needed visuals
- preload next likely assets in the background

---

## 4.5 Step Interaction

During an active step the runtime should:

- listen for canonical actions
- update selection/focus
- allow inspect / grab / rotate / place / confirm
- track candidate placement states
- provide live feedback where appropriate
- prevent invalid state corruption
- remain responsive on all supported devices

---

## 4.6 Step Validation

Validation should happen through explicit evaluators, not ad hoc checks in random scripts.

Validation may include:

- position tolerance
- rotation tolerance
- correct part identity
- tool requirement acknowledgement
- order dependencies
- multi-part completion requirements
- physical substitution confirmation
- challenge constraints
- effect completion, when a process action must finish before advancing

---

## 4.7 Step Completion

When a step completes:

- mark step state
- record completion metrics
- trigger success feedback
- optionally play a reinforcement effect or reveal
- update machine session state
- unlock next step
- persist progress safely
- transition to next step or completion summary

---

## 4.8 Assembly Completion

When an assembly completes:

- celebrate milestone
- expose structural understanding recap
- optionally show exploded-to-complete transition
- record subassembly mastery milestone
- unlock next assembly

This is an important place for aha moments.

---

## 4.9 Session Completion

When the machine session completes:

- show summary
- show completion time
- show mistakes or retries
- show hints used
- show challenge/scoreboard results when enabled
- show recommended next machine or review flow

---

# 5. Runtime State Model

The runtime should center around explicit state objects.

## 5.1 Machine Session State

Suggested state groups:

- machine identity
- progression state
- interaction state
- placement state
- physical substitution state
- UI/reinforcement state
- challenge state
- capability/performance state
- optional sync-ready state

Avoid hiding critical state across many MonoBehaviours with unclear ownership.

---

## 5.2 Step State

A step should support:

- locked
- available
- active
- completed
- skipped
- reviewed
- failed attempt
- suspended (if resumed later)
- waiting for multiplayer sync (future)
- waiting for physical confirmation
- waiting for process effect completion

---

## 5.3 Part Placement State

Each part should be tracked independently.

Suggested part states:

- not introduced
- available
- selected
- inspected
- grabbed
- candidate placement
- invalid placement
- valid placement
- placed virtually
- marked physically present
- hidden because physical substitute exists
- removed/reset
- completed

---

## 5.4 Tool Awareness State

Tools do not always need full simulation, but the runtime should still track:

- required
- introduced
- acknowledged
- inspected
- linked to current step
- optional
- completed usage acknowledgement

---

# 6. Step State Machine

The runtime should use a clean step state machine.

## 6.1 High-Level Flow

Typical step flow:

available  
→ active  
→ interacting  
→ validating  
→ completed  
→ archived/reviewable

With failure loops:

interacting  
→ invalid attempt  
→ guided correction  
→ interacting

With physical substitution path:

active  
→ confirm physical substitute  
→ validate confirmation requirements  
→ completed

With process effect path:

active  
→ trigger process action  
→ effect playing  
→ process confirmed  
→ completed

---

## 6.2 Why a State Machine

A proper state machine helps:

- avoid brittle conditional logic
- support tutorial vs challenge differences
- support multiplayer waiting states later
- support re-entry after pause or restore
- support clean debugging

---

# 7. Placement Validation Architecture

Placement validation should be a dedicated subsystem.

## 7.1 Validation Inputs

Validation should consider:

- expected part id
- expected target anchor
- expected orientation
- tolerance values
- contextual dependencies
- whether physical substitution is allowed
- whether challenge mode demands stricter validation
- whether exact sequence matters

---

## 7.2 Validation Outputs

Validation should return structured results, not only booleans.

Suggested result data:

- valid / invalid
- reason code
- position error
- rotation error
- missing prerequisite
- suggested correction hint
- severity
- whether auto-snap is allowed
- whether reattempt penalty applies in challenge mode

---

## 7.3 Validation Modes

Support multiple validation modes:

- lenient tutorial
- guided standard
- stricter challenge
- exact review
- instructor override (future multiplayer/remote assist)

---

# 8. Physical Substitution Runtime

A core requirement is allowing real-world assembly in parallel with virtual guidance.

## 8.1 Runtime Behavior

When a part allows physical substitution:

- expose confirmation UI
- optionally request the user to inspect the virtual representation first
- record physical placement state
- hide or reduce the virtual part
- maintain logical completion state
- preserve later dependency correctness

---

## 8.2 Integrity Rules

Physical substitution should not bypass all learning automatically.

Depending on mode, the runtime may still require:

- part identification
- tool acknowledgement
- orientation understanding
- confirmation of position context
- mini knowledge check

---

# 9. Effects Runtime

Effects are part of the teaching and process communication system.

## 9.1 Supported Effect Roles

Effects may be used for:

- assembly placement feedback
- milestone feedback
- process demonstration
- welding
- sparks
- heat glow
- torch/fire
- dust
- cutting/grinding suggestions
- structural reveal transitions
- ghost guidance
- error correction highlights

---

## 9.2 Effect Trigger Points

Effects may trigger on:

- step enter
- step interact
- candidate valid placement
- placement snap
- process action begin
- process action sustain
- process action end
- step completion
- assembly completion
- challenge success

---

## 9.3 Effect Safety and Performance

Effects should be modular and quality-tier aware.

The runtime should choose between:

- full VFX Graph or particle effect
- lightweight particle fallback
- shader-only cue
- static animated icon fallback

This choice should depend on device capabilities.

---

# 10. Input Runtime Architecture

The app must use the **Unity Input System** as the cross-platform input foundation.

## 10.1 Conceptual Model

The Unity Input System handles native device input collection.

The runtime consumes normalized actions.

The runtime must not ask, “is this mouse or controller?” in random logic branches.

Instead:

native device input  
→ Unity Input Actions  
→ platform adapter  
→ canonical runtime action  
→ interaction context router  
→ runtime behavior

---

## 10.2 Canonical Actions

Suggested canonical actions:

- select
- inspect
- grab
- move
- rotate
- place
- confirm
- cancel
- navigate
- zoom
- orbit
- request hint
- next
- previous
- pause
- toggle physical mode
- challenge restart

---

## 10.3 Platform Adapters

Suggested adapters:

- `XRInputAdapter`
- `MobileTouchInputAdapter`
- `DesktopMouseKeyboardInputAdapter`

All adapters should output the same action language to the runtime.

---

## 10.4 Interaction Contexts

Actions should be context-sensitive:

- onboarding context
- navigation context
- inspection context
- assembly placement context
- process effect context
- UI menu context
- challenge context
- future multiplayer collaboration context

This avoids fragile input branching.

---

# 11. Tutorial and Reinforcement Runtime

The runtime should teach, not only evaluate.

## 11.1 Assistance Levels

Support levels such as:

- beginner tutorial
- guided mode
- standard mode
- challenge mode
- review mode

Each level can tune:

- hint frequency
- ghost placement visibility
- auto-snap
- validation strictness
- recap prompts
- challenge timers

---

## 11.2 Reinforcement Hooks

A step may include:

- why this part matters
- what tool is used and why
- what can go wrong
- why order matters
- structural role of the subassembly
- optional micro-checkpoint question

---

## 11.3 Aha-Moment Support

The runtime should support moments where deeper structure becomes obvious.

Examples:

- exploded-to-assembled transition
- dependency highlight
- reveal of hydraulic flow or force path
- before/after structural comparison
- subassembly completion payoff

---

# 12. Challenge and Fun Runtime

The app should be enjoyable and motivating.

## 12.1 Core Fun Principles

Fun should support learning.

It should not distract from instruction.

Possible runtime hooks:

- satisfying placement response
- milestone celebrations
- progress streaks
- confidence-building early wins
- challenge mode
- repeatable best-time runs
- leaderboard-ready reporting boundaries
- co-op build goals later

---

## 12.2 Speed Run Support

Challenge mode can include speed runs.

Track:

- total completion time
- retries
- invalid placements
- hint usage
- skipped explanations
- accuracy score

The runtime should keep challenge data isolated from core instructional progression logic.

---

## 12.3 Global Competitive Hooks

The runtime should remain open for later:

- global leaderboard submission
- friends-only comparisons
- machine-specific best times
- co-op speed runs
- seasonal challenge events

These should remain optional and configurable.

---

# 13. Multiplayer-Ready Runtime Boundaries

The architecture should leave the door open for optimal multiplayer.

## 13.1 What Must Be Multiplayer-Safe Now

Even before networking exists, define clean ownership for:

- current step state
- part placement state
- machine session state
- challenge timer state
- physical substitution confirmations
- instruction progression events
- effect trigger events when sync matters

---

## 13.2 What Should Not Be Hardwired

Do not hardwire:

- direct UI state as the source of truth
- hidden state only inside scene objects
- one-off local-only timers scattered everywhere
- effect logic that cannot be replayed or synchronized
- part placement completion that only exists visually

Instead, keep authoritative runtime state in explicit runtime models.

---

## 13.3 Future Sync Model Direction

Later multiplayer can choose authoritative models such as:

- host authoritative step progression
- server authoritative challenge timing
- hybrid co-op with local manipulation and shared validation
- remote instructor override channel

The runtime should be compatible with these options.

---

# 14. Error Handling and Recovery

The runtime should be built to detect and recover from breakage.

## 14.1 Validation Failures

On invalid placement:

- keep current state safe
- explain what is wrong
- highlight expected correction
- avoid silent failure

---

## 14.2 Missing Assets

If a part or effect asset is missing:

- log diagnostic information
- use fallback representation where possible
- keep the step playable if safe
- mark content issue clearly for debugging

---

## 14.3 Interrupted Sessions

On pause, tab background, or disconnect:

- preserve machine session state
- preserve current step and placed part status
- restore cleanly without corruption

---

## 14.4 Debuggability

Provide robust logging around:

- step enter
- step exit
- validation failures
- asset load failures
- effect failures
- persistence saves
- challenge state transitions

This is critical for agent-driven development so mistakes are visible and recoverable.

---

# 15. Performance Runtime Strategy

Performance is a first-class runtime concern.

## 15.1 Main Thread Responsibilities

The main thread should focus on:

- rendering
- immediate interaction
- UI
- lightweight orchestration

Do not place heavy parsing or preprocessing here when it can be avoided.

---

## 15.2 Background Work

Use WebWorkers and/or worker-compatible web integrations when needed for web-side preprocessing where practical.

Use WebAssembly only if it provides real value.

Good candidates for background work:

- content parsing
- machine manifest preprocessing
- search indexing
- challenge replay analysis
- heavy geometric validation helpers if ever required

---

## 15.3 Asset Streaming

Use staged loading:

- machine metadata first
- first required parts second
- nearby step assets next
- optional richer content later

This reduces startup cost and keeps the app responsive.

---

## 15.4 Quality Tier Adaptation

The runtime should dynamically honor performance tiers for:

- model detail
- texture resolution
- effects
- simultaneous visible content
- animation richness
- update frequency of optional systems

---

# 16. Persistence Model

The runtime should persist meaningful progress.

Persist at least:

- machine id
- mode
- current assembly
- current step
- placed part states
- physical substitution states
- challenge metrics if relevant
- hint usage
- settings

Persistence must survive safe restart and later support cross-session review.

---

# 17. Suggested Runtime Module List

Suggested modules:

- `AppBootstrap`
- `MachineSessionController`
- `MachineRuntimeState`
- `AssemblyRuntimeController`
- `StepController`
- `ProgressionController`
- `PlacementValidator`
- `StepCompletionEvaluator`
- `ConstraintValidationService`
- `InputActionRouter`
- `InteractionContextController`
- `SelectionService`
- `ManipulationController`
- `InstructionPresenter`
- `HintSystem`
- `PartInfoPresenter`
- `ToolInfoPresenter`
- `ProgressHUDController`
- `EffectRuntimeController`
- `EffectDefinitionRegistry`
- `EffectPlaybackService`
- `MachineAssetLoader`
- `StreamingAssetCoordinator`
- `RuntimeAssetCache`
- `PartVisualSpawner`
- `SessionPersistenceService`
- `CapabilityProfileService`
- `ChallengeRunTracker`
- `ChallengeRunReporter`
- `AssemblySyncStateAdapter`
- `RemoteAssistBridge`

These are conceptual module names and should be adapted carefully, not copied blindly.

---

# 18. Reliability Rules for Agent-Driven Development

Because agents will help implement this runtime, the codebase must be structured so changes are safe and traceable.

Rules:

- one responsibility per module
- no giant god managers
- strong naming clarity
- explicit ownership of state
- preserve clean boundaries between content, runtime, UI, and effects
- prefer incremental validated changes
- auto stage and commit after every meaningful validated changeset
- verify no regressions after each change
- never bypass the architecture for speed
- use latest stable and production-safe Unity technologies only

This is essential to avoid silent architectural drift.

---

# 19. Recommended First Vertical Slice Runtime

The first runtime milestone should include:

- one tutorial machine/build
- one real OSE-targeted machine path starter
- machine package loading
- session creation
- step activation
- part inspection
- basic virtual placement
- physical substitution toggle
- validation loop
- simple effect playback
- progress persistence
- Unity Input System integration across desktop and XR
- basic quality-tier adaptation

Only after this is stable should larger systems expand.

---

# 20. Long-Term Runtime Goal

The long-term goal is a runtime that can execute many assembly experiences consistently across web-delivered XR, mobile, and desktop devices while remaining educational, performant, modular, and multiplayer-ready.

It should scale from:

- simple onboarding builds
- to modular OSE machine assemblies
- to collaborative guided construction
- to challenge modes and competitive learning
- to remote expert assistance
- to large open-hardware learning systems
- possibly all the way to housing-scale instructional builds

The runtime foundation should be designed so that growth happens by adding content and modules, not by rewriting the core.
