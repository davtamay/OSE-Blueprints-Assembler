# IMPLEMENTATION_CHECKLIST.md

## Purpose

This document converts the project vision, architecture, runtime design, and Unity structure into a practical implementation sequence.

It is intended to help a human developer and coding agent work through the app **one validated step at a time** while reducing regressions, architecture drift, and accidental breakage.

This checklist should be used as the execution companion to:

- `AGENTS.md`
- `docs/TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/CONTENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `TASK_EXECUTION_PROTOCOL.md`

The goal is to move from zero to a stable first vertical slice, then expand safely.

---

# Core Execution Rules

These rules apply to every phase.

1. Use **Unity 6.3** as the current engine baseline.
2. Stay open to upgrading as newer **stable** versions become available, but document and validate upgrades deliberately.
3. Use the **Unity Input System** as the canonical input foundation.
4. Use **Unity XR Interaction Toolkit (XRI)** as the primary XR interaction framework.
5. Use **Unity UI Toolkit (runtime)** as the primary UI framework.
6. Keep the codebase modular and delegated to the correct scripts and folders.
7. Prefer data-driven systems over machine-specific hardcoding.
8. Validate every meaningful change before moving on.
9. Auto stage and commit after every meaningful validated changeset.
10. Double-check that nothing broke before continuing.
11. Keep multiplayer-open boundaries in mind even when building single-player first.
12. Keep performance in mind from the beginning, especially for web delivery.
13. Preserve fun, clarity, and instructional quality as equal priorities alongside technical correctness.

---

# Definition of Done for a Meaningful Changeset

Before a changeset is staged and committed, verify:

- project compiles
- no obvious console spam or new errors
- affected scene/prefab references still work
- architecture boundaries were respected
- naming and folder placement are correct
- no unrelated files were changed accidentally
- the feature behaves at least at its intended slice level
- a visual validation path exists if the feature is user-visible

Suggested commit style:

- `feat(bootstrap): create project folder structure and asmdefs`
- `feat(input): add canonical Unity Input System action map`
- `feat(xr): add XRI-aligned XR interaction adapter baseline`
- `feat(ui): add UI Toolkit base panel controller`
- `feat(runtime): add machine session controller skeleton`
- `feat(validation): add placement validator result model`

---

# Phase 0 - Foundation and Research Lock

## Goal

Freeze the initial technical direction before implementation spreads.

## Checklist

- [ ] Confirm **Unity 6.3** as the current project baseline.
- [ ] Document the policy that future Unity upgrades should occur when newer stable versions become available and are validated.
- [ ] Confirm De-Panther WebXR exporter version using the latest stable compatible release.
- [ ] Confirm Unity Input System package version.
- [ ] Confirm **XR Interaction Toolkit (XRI)** as the primary XR interaction baseline.
- [ ] Confirm UI Toolkit runtime approach and project conventions.
- [ ] Confirm the initial web target path:
  - WebXR deployment
  - WebGPU preferred
  - WebGL fallback
- [ ] Confirm current Quest browser support assumptions and document them clearly.
- [ ] Confirm whether the initial vertical slice will target:
  - desktop first
  - desktop + XR first
  - desktop + mobile + XR first
- [ ] Confirm the first real OSE machine candidate.
- [ ] Confirm the smaller tutorial build that will come before the first real OSE machine.
- [ ] Document version constraints in `docs/TECH_STACK.md` or update architecture docs.
- [ ] Create a branch for the implementation foundation, such as:
  - `feature/foundation-vertical-slice`

## Validation

- [ ] All chosen versions are documented.
- [ ] No unstable experimental dependency is accidentally treated as production baseline.
- [ ] The stack direction is clear enough that agents do not guess.

## Commit

- [ ] Stage and commit the stack/foundation decision.

---

# Phase 1 - Repository and Unity Project Structure

## Goal

Create the repo and Unity project structure so the project remains organized from day one.

## Checklist

- [ ] Create or verify root docs:
  - `AGENTS.md`
  - `TASK_EXECUTION_PROTOCOL.md`
  - `docs/ARCHITECTURE.md`
  - `docs/CONTENT_MODEL.md`
  - `docs/ASSEMBLY_RUNTIME.md`
  - `docs/UNITY_PROJECT_STRUCTURE.md`
  - `docs/IMPLEMENTATION_CHECKLIST.md`
- [ ] Create Unity folder structure under `Assets/_Project/`.
- [ ] Create UI Toolkit folders under `Assets/_Project/UI/`.
- [ ] Create `StreamingAssets/MachinePackages/`.
- [ ] Create `Scripts/` submodules:
  - App
  - Bootstrap
  - Core
  - Content
  - Runtime
  - Input
  - Interaction
  - Validation
  - Presentation
  - UI
  - Effects
  - Assets
  - Persistence
  - Networking
  - Challenge
  - Platform
  - Authoring
  - Utilities
  - Editor
  - Tests
- [ ] Create initial Assembly Definition Files (`.asmdef`) with clean dependency directions.
- [ ] Create minimal boot scene.
- [ ] Create minimal frontend scene.
- [ ] Create minimal machine runtime scene.
- [ ] Add a project README if missing.

## Validation

- [ ] Folder structure matches the architecture docs.
- [ ] Assembly definitions compile.
- [ ] Scenes open without missing script references.
- [ ] No module is acting as a god-folder.

## Commit

- [ ] Stage and commit the project structure baseline.

---

# Phase 2 - Bootstrap and Core Runtime Contracts

## Goal

Create the non-feature-specific foundation that everything else depends on.

## Checklist

- [ ] Implement `AppBootstrap`.
- [ ] Implement a service registration or dependency bootstrap approach.
- [ ] Define core runtime enums and result types.
- [ ] Define `MachineSessionState`.
- [ ] Define `RuntimeStepState`.
- [ ] Define `PlacementValidationResult`.
- [ ] Define capability/performance tier models.
- [ ] Define shared interfaces for:
  - content loading
  - validation
  - effect playback
  - persistence
  - input routing
  - UI presentation adapters where needed
- [ ] Create diagnostics/logging conventions for runtime events.

## Validation

- [ ] Core models compile cleanly.
- [ ] Ownership boundaries are clear.
- [ ] Nothing feature-specific leaked into Core.

## Commit

- [ ] Stage and commit runtime contracts and bootstrap.

---

# Phase 3 - Unity Input System Foundation

## Goal

Create the canonical cross-platform input base before interaction logic spreads.

## Checklist

- [ ] Enable and configure the Unity Input System properly.
- [ ] Create the shared input actions asset.
- [ ] Define control schemes for:
  - desktop
  - mobile
  - XR hands
  - XR controllers
- [ ] Define canonical runtime actions:
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
- [ ] Implement `InputActionRouter`.
- [ ] Implement platform adapters:
  - `DesktopMouseKeyboardInputAdapter`
  - `MobileTouchInputAdapter`
  - `XRInputAdapter`
- [ ] Implement interaction context routing.
- [ ] Verify that runtime code consumes canonical actions, not raw device checks.

## Validation

- [ ] Input actions fire correctly on intended targets.
- [ ] No scattered direct input polling exists outside the input layer.
- [ ] Input remains modular and device-agnostic.
- [ ] Desktop interaction path works first, even if XR/mobile are partial.

## Commit

- [ ] Stage and commit Unity Input System foundation.

---

# Phase 4 - XR Interaction Toolkit Foundation

## Goal

Establish the vendor-agnostic XR interaction baseline before XR logic spreads.

## Checklist

- [ ] Confirm XRI packages and versions are compatible with the chosen Unity baseline.
- [ ] Define how XRI interactor behavior maps into canonical runtime actions.
- [ ] Keep XRI event usage routed through adapters rather than bypassing runtime ownership.
- [ ] Create an initial XRI-compatible XR interaction harness or test scene.
- [ ] Verify that XR interaction assumptions remain vendor-agnostic.
- [ ] Do **not** introduce Meta Interaction SDK as a baseline dependency.

## Validation

- [ ] XR interaction baseline is explicit and modular.
- [ ] XRI does not bypass the canonical action model.
- [ ] No vendor-specific dependency has been treated as required.

## Commit

- [ ] Stage and commit XRI baseline setup.

---

# Phase 5 - UI Toolkit Foundation

## Goal

Establish the primary UI framework before feature panels multiply.

## Checklist

- [ ] Set up the base UI Toolkit runtime approach.
- [ ] Create initial UI Toolkit folder structure:
  - UXML
  - USS
  - Documents
  - Panels
- [ ] Define panel/controller conventions.
- [ ] Create a root `UIDocument` setup path for frontend or runtime scenes.
- [ ] Create a basic step panel shell.
- [ ] Create a basic part info panel shell.
- [ ] Confirm UI state comes from runtime/presenter systems rather than owning gameplay truth.
- [ ] Confirm the UI structure works for both screen-space and future XR-presented panels.

## Validation

- [ ] UI Toolkit panels render correctly.
- [ ] Controllers are separated from runtime state ownership.
- [ ] UI framework direction is now explicit and reusable.

## Commit

- [ ] Stage and commit UI Toolkit foundation.

---

# Phase 6 - Content Model Implementation

## Goal

Turn the content schema into actual runtime-readable models.

## Checklist

- [ ] Implement content definition classes:
  - `MachineDefinition`
  - `AssemblyDefinition`
  - `SubassemblyDefinition`
  - `StepDefinition`
  - `PartDefinition`
  - `ToolDefinition`
  - `EffectDefinition`
  - challenge-related definitions
- [ ] Implement machine package version metadata.
- [ ] Implement content validation rules.
- [ ] Implement parsing/loading path for machine packages.
- [ ] Add error reporting for malformed content.
- [ ] Add at least one sample machine package.
- [ ] Add at least one tutorial machine package.

## Validation

- [ ] Definitions deserialize correctly.
- [ ] Invalid content fails clearly, not silently.
- [ ] Content is independent from scene logic.
- [ ] Sample package loads without runtime corruption.

## Commit

- [ ] Stage and commit content model implementation.

---

# Phase 7 - Machine Session and Step Runtime Skeleton

## Goal

Create the basic state-driven assembly runtime loop.

## Checklist

- [ ] Implement `MachineSessionController`.
- [ ] Implement `AssemblyRuntimeController`.
- [ ] Implement `StepController`.
- [ ] Implement `ProgressionController`.
- [ ] Implement state transitions for:
  - machine loaded
  - step available
  - step active
  - step completed
  - assembly completed
- [ ] Implement step activation flow.
- [ ] Implement session persistence-safe state updates.
- [ ] Add diagnostic logs for step enter/exit.

## Validation

- [ ] Machine session starts cleanly.
- [ ] A step can activate from content data.
- [ ] A step can complete and advance.
- [ ] State is explicit and inspectable.

## Commit

- [ ] Stage and commit runtime step skeleton.

---

# Phase 8 - Part Presentation and Basic Interaction

## Goal

Let the user see, inspect, and manipulate parts safely.

## Checklist

- [ ] Implement basic part spawning from content references.
- [ ] Implement selection service.
- [ ] Implement inspection flow.
- [ ] Implement manipulation flow for the first supported platform.
- [ ] Implement placement targets.
- [ ] Implement ghost or preview representation.
- [ ] Implement part info display:
  - name
  - function
  - material
  - tool
  - search terms
- [ ] Implement tool info display.
- [ ] Implement basic step instruction panel using UI Toolkit.

## Validation

- [ ] User can inspect a part.
- [ ] User can move a part into candidate placement.
- [ ] Metadata displays correctly.
- [ ] Nothing is hardcoded to one specific machine.

## Commit

- [ ] Stage and commit basic interaction and presentation.

---

# Phase 9 - Placement Validation and Completion Logic

## Goal

Make assembly behavior correct, explainable, and recoverable.

## Checklist

- [ ] Implement `PlacementValidator`.
- [ ] Implement position tolerance checks.
- [ ] Implement rotation tolerance checks.
- [ ] Implement correct-part identity checks.
- [ ] Implement completion evaluator.
- [ ] Support at least:
  - virtual-only completion
  - virtual-or-physical completion
  - confirmation-only completion
- [ ] Add structured validation result reasons.
- [ ] Add user-facing correction hints.
- [ ] Add tutorial leniency vs challenge strictness hooks.

## Validation

- [ ] Invalid placements fail safely.
- [ ] Valid placements complete correctly.
- [ ] Failure reasons are understandable.
- [ ] Completion logic is not buried inside UI scripts.

## Commit

- [ ] Stage and commit validation layer.

---

# Phase 10 - Physical Substitution Workflow

## Goal

Support real-world assembly alongside virtual training.

## Checklist

- [ ] Implement physical substitution toggle/action.
- [ ] Add UI to mark a part as physically present.
- [ ] Hide or reduce virtual representation when appropriate.
- [ ] Record physical substitution state in session state.
- [ ] Ensure later steps respect physical presence.
- [ ] Optionally require identification/acknowledgement before allowing substitution.

## Validation

- [ ] Physical substitution works for eligible parts.
- [ ] Progression remains correct after substitution.
- [ ] Session restore preserves physical state.
- [ ] No virtual-only assumptions break the flow.

## Commit

- [ ] Stage and commit physical substitution support.

---

# Phase 11 - Tutorial Vertical Slice

## Goal

Create the first onboarding experience that teaches interaction and confidence.

## Checklist

- [ ] Choose a very simple starter build.
- [ ] Create tutorial content package.
- [ ] Teach:
  - navigation
  - selection
  - inspection
  - rotating/manipulating a part
  - moving a part toward a target
  - placing a part
  - reading part information
  - understanding required tool info
  - using a hint
  - confirming a placement
  - optionally marking a part as physically present
  - understanding progression from one step to the next
- [ ] Add success feedback and milestone moments.
- [ ] Keep the tutorial short, friendly, and confidence-building.

## Validation

- [ ] A new user can complete the tutorial without prior knowledge.
- [ ] The tutorial teaches the interaction language of the app.
- [ ] Early success feels satisfying.

## Commit

- [ ] Stage and commit tutorial vertical slice.

---

# Phase 12 - First Real OSE Machine Vertical Slice

## Goal

Create the first authentic machine learning experience from Open Source Ecology blueprint material.

## Checklist

- [ ] Identify the simplest realistic OSE machine path to start with.
- [ ] Prefer something modular and well documented.
- [ ] Gather blueprint references and source notes.
- [ ] Simplify the first scope to one assembly or subassembly, not the entire machine.
- [ ] Build the first machine package from that scope.
- [ ] Add accurate part metadata.
- [ ] Add tool metadata.
- [ ] Add instructional steps.
- [ ] Add placement targets and validation.
- [ ] Add structural “why this matters” reinforcement.

## Validation

- [ ] The machine experience feels like a real assembly lesson, not only a toy demo.
- [ ] Scope is small enough to finish cleanly.
- [ ] Content remains data-driven.

## Commit

- [ ] Stage and commit first OSE machine vertical slice.

---

# Phase 13 - Effects, Process Visuals, and Instructional Feedback

## Goal

Add process-specific effects that help communicate construction steps and make the experience more engaging.

## Checklist

- [ ] Define effect roles in content:
  - welding
  - sparks
  - heat glow
  - torch/fire
  - dust
  - cutting/grinding cues
  - placement feedback
  - milestone feedback
- [ ] Implement `EffectRuntimeController`.
- [ ] Implement effect registry and playback service.
- [ ] Add at least one HLSL shader-backed effect where appropriate.
- [ ] Add particle/VFX equivalents where appropriate.
- [ ] Add lower-end fallback effect path for constrained devices.
- [ ] Allow effects to trigger on:
  - step enter
  - valid placement
  - process action begin/end
  - step completion
  - assembly completion
- [ ] Ensure effects enhance instruction instead of distracting from it.

## Validation

- [ ] Effects trigger when expected.
- [ ] Effects do not break low-end runtime behavior.
- [ ] Process visuals meaningfully improve understanding.
- [ ] Effect code is modular and not mixed into unrelated systems.

## Commit

- [ ] Stage and commit effects runtime and first process visuals.

---

# Phase 14 - Persistence and Recovery

## Goal

Make progress durable and resilient.

## Checklist

- [ ] Implement session persistence.
- [ ] Save:
  - machine id
  - mode
  - current assembly
  - current step
  - placed parts
  - physical substitutions
  - challenge metrics if active
  - settings
- [ ] Restore cleanly into the active runtime scene.
- [ ] Handle interrupted sessions.
- [ ] Handle missing content or version mismatch safely.

## Validation

- [ ] Progress survives app restart.
- [ ] Restore does not corrupt runtime state.
- [ ] Partial sessions remain playable.

## Commit

- [ ] Stage and commit persistence and restore support.

---

# Phase 15 - Platform Capability and Performance Tiering

## Goal

Keep the app responsive and portable across desktop, mobile, and XR.

## Checklist

- [ ] Implement capability profile service.
- [ ] Implement quality/performance tier selection.
- [ ] Define what changes across tiers:
  - texture quality
  - model detail
  - number of visible parts
  - effect quality
  - optional animation richness
  - UI complexity where needed
- [ ] Add staged asset loading.
- [ ] Add lightweight asset caching.
- [ ] Identify workloads that may need WebWorker/WASM support later.
- [ ] Keep the main thread focused on rendering, interaction, and UI.

## Validation

- [ ] Low-end and constrained paths degrade gracefully.
- [ ] Startup and step transitions remain reasonable.
- [ ] Performance logic is centralized, not scattered.

## Commit

- [ ] Stage and commit platform/performance tier support.

---

# Phase 16 - Cross-Platform Expansion Pass

## Goal

Broaden support after the first stable slice exists.

## Checklist

- [ ] Harden desktop flow.
- [ ] Add mobile touch behavior refinement.
- [ ] Add XR interaction refinement through the XRI-first stack.
- [ ] Verify canonical action parity across platforms.
- [ ] Verify instruction readability across devices.
- [ ] Verify UI Toolkit readability and routing across devices.
- [ ] Verify effects degrade correctly across devices.
- [ ] Verify physical substitution flow remains usable across devices.

## Validation

- [ ] Core flow works on each target platform tier being claimed.
- [ ] No platform-specific hack has polluted core runtime systems.

## Commit

- [ ] Stage and commit cross-platform hardening.

---

# Phase 17 - Challenge Mode and Fun Layer

## Goal

Make the experience more enjoyable, replayable, and motivating without harming instructional quality.

## Checklist

- [ ] Add challenge mode hooks.
- [ ] Track:
  - completion time
  - retries
  - invalid placements
  - hint usage
  - skipped explanations
- [ ] Add challenge summary screen.
- [ ] Add optional best-time run tracking.
- [ ] Investigate whether speed-run competition fits the target audience and learning goals.
- [ ] Keep competition opt-in and secondary to learning.
- [ ] Add satisfying milestone feedback.
- [ ] Ensure fun reinforces mastery instead of causing confusion.

## Validation

- [ ] Challenge data is isolated from core instructional logic.
- [ ] The experience remains welcoming, not only competitive.
- [ ] Speed-run framing does not undermine learning clarity.

## Commit

- [ ] Stage and commit challenge mode foundation.

---

# Phase 18 - Multiplayer-Ready Boundaries

## Goal

Prepare the codebase for future optimal multiplayer without prematurely overbuilding networking.

## Checklist

- [ ] Audit runtime state ownership.
- [ ] Identify what must be sync-safe:
  - current step
  - part placement state
  - physical substitution state
  - challenge timing state
  - effect trigger events if relevant
- [ ] Create explicit sync snapshot models.
- [ ] Create networking boundary/adapters without committing to the full transport yet.
- [ ] Prevent UI from becoming the source of truth.
- [ ] Prevent hidden scene state from being required for correctness.

## Validation

- [ ] The codebase is cleaner after the multiplayer audit, not messier.
- [ ] State can theoretically be serialized/synchronized cleanly.
- [ ] Core runtime does not assume single-player-only hidden state.

## Commit

- [ ] Stage and commit multiplayer-ready boundaries.

---

# Phase 19 - Optional Multiplayer Prototype

## Goal

Only after the single-player vertical slice is stable, explore a minimal multiplayer test.

## Checklist

- [ ] Choose the smallest meaningful collaborative scenario.
- [ ] Sync:
  - current step
  - placed parts
  - confirmation actions
- [ ] Keep authoritative state minimal and explicit.
- [ ] Test whether collaboration improves motivation and understanding.
- [ ] Investigate whether co-op challenge or co-op speed runs are a good fit.
- [ ] Do not let prototype networking rewrite the architecture.

## Validation

- [ ] The multiplayer prototype proves or disproves value clearly.
- [ ] The single-player path still works cleanly.
- [ ] Sync concerns remain modular.

## Commit

- [ ] Stage and commit only if the prototype is stable and worth keeping.

---

# Phase 20 - Authoring Tooling

## Goal

Make it easier to add more machines and steps without manual chaos.

## Checklist

- [ ] Add machine package validator tools.
- [ ] Add editor tools for part metadata authoring.
- [ ] Add editor tools for step creation/inspection.
- [ ] Add package export tooling.
- [ ] Add content linting rules.

## Validation

- [ ] Adding a new machine is easier than before.
- [ ] Authoring errors are caught earlier.
- [ ] Content creation becomes more repeatable.

## Commit

- [ ] Stage and commit authoring tool baseline.

---

# Phase 21 - QA and Stability Pass

## Goal

Double-check that nothing broke and improve trustworthiness before expansion.

## Checklist

- [ ] Audit console errors and warnings.
- [ ] Audit missing references.
- [ ] Audit scene dependencies.
- [ ] Audit serialization safety.
- [ ] Audit package loading error handling.
- [ ] Audit challenge metrics.
- [ ] Audit performance on representative devices.
- [ ] Audit docs to ensure they still match the codebase.
- [ ] Audit git history to ensure changesets stayed meaningful and traceable.
- [ ] Audit version assumptions if any package or engine upgrades were introduced.

## Validation

- [ ] The project feels stable enough for broader iteration.
- [ ] Documentation and code are aligned.
- [ ] No major hidden architecture drift exists.

## Commit

- [ ] Stage and commit QA/stability pass.

---

# Recommended First Real Build Order

Use this as the shortest reliable path:

1. Foundation lock
2. Unity project structure
3. Bootstrap and core contracts
4. Unity Input System foundation
5. XR Interaction Toolkit baseline
6. UI Toolkit foundation
7. Content model implementation
8. Machine session and step runtime skeleton
9. Basic interaction and presentation
10. Placement validation
11. Physical substitution
12. Tutorial slice
13. First OSE machine slice
14. Effects/process visuals
15. Persistence
16. Performance tiering
17. Cross-platform hardening
18. Challenge hooks
19. Multiplayer-ready boundaries
20. Optional multiplayer prototype
21. Authoring tools
22. Stability pass

---

# Double-Check Routine Before Every New Task

Before starting a new task, ask:

- Does this belong to the current phase?
- Does the architecture already define where this should live?
- Am I adding a new module when an existing one should own it?
- Is this using the Unity Input System correctly?
- Is this using XR Interaction Toolkit as the primary XR interaction baseline where XR is involved?
- Is this using UI Toolkit in a presentation-only role?
- Is this stable-tech aligned?
- Does this accidentally hardcode machine-specific logic?
- Does this stay open for future multiplayer?
- Does this preserve performance awareness?
- Did I verify nothing broke after the previous step?

If the answer to any of these is unclear, stop and resolve that first.

---

# Recommended First OSE Starting Point

Start with:

1. a **tiny tutorial build** that teaches the interaction language of the app
2. then a **small, modular, well-documented OSE subassembly**, likely from the **Power Cube** path rather than attempting a full complex machine immediately

Reason:

- it is modular
- it maps well to subassemblies
- it is easier to teach progressively
- it keeps the first authentic OSE scope realistic

Avoid starting with a huge machine or house-scale build first.

---

# Final Execution Guidance

This project is large.

The correct strategy is not “build everything fast.”

The correct strategy is:

- build one reliable layer at a time
- validate constantly
- commit every meaningful validated changeset
- protect architecture boundaries
- keep the codebase clean and delegated
- keep the experience fun, educational, and scalable
- leave the door open for multiplayer and competitive modes without forcing them too early
- keep the XR baseline XRI-first and vendor-agnostic

A stable vertical slice is more valuable than a broad but fragile prototype.