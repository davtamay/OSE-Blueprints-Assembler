# APP_CURRENT_PROGRESS_FOR_AGENT.md

## Purpose

This document is the live handoff baseline for any agent or developer picking up the project midstream.

Update it after every completed phase or major validated integration pass.

It should answer three questions quickly:

1. What is already built?
2. What is visually verifiable right now?
3. What is the next correct phase to implement?

---

## Last Updated

- March 14, 2026 (Phase 7 implementation)

---

## Project Snapshot

- Project: **OSE Blueprints Assembler**
- Engine baseline: **Unity 6.3**
- Render pipeline: **URP 17.3.0**
- Input: **Unity Input System 1.19.0**
- XR interaction baseline: **XRI 3.3.1**
- UI framework: **Unity UI Toolkit (runtime)**
- WebXR path: **De-Panther WebXR 0.22.1**
- Namespace root: `OSE.*`

The application goal is to turn OSE blueprint-driven machine documentation into guided interactive assembly training across XR, desktop, and mobile.

---

## Architecture Rules To Preserve

- Never bypass the canonical action model.
- `OSE.Core` stays free of feature dependencies.
- XRI events must route through adapters.
- UI controllers/panels own no gameplay truth.
- Runtime and content systems stay data-driven.
- Use `OseLog` instead of direct `Debug.Log`.
- Use `ServiceRegistry` for shared service access.
- Work one validated phase at a time.

These rules are enforced by the docs in this folder and by the current code layout under `Assets/_Project/Scripts/`.

---

## Completed Phases

### Phase 1: Repository and Project Structure

- `Assets/_Project/` folder structure created.
- Module folders and `.asmdef` layout established.
- Initial scenes created, including the mechanics test scene.

### Phase 2: Bootstrap and Core Contracts

- `AppBootstrap` exists.
- `ServiceRegistry` exists.
- Core enums, interfaces, state models, and validation result types exist.
- Shared logging conventions exist through `OseLog`.

### Phase 3: Input Foundation

- Canonical input actions asset exists.
- `InputActionRouter` implemented.
- Desktop, mobile, and XR input adapters implemented.
- Runtime input is routed through canonical actions instead of raw polling.

### Phase 4: XRI Foundation

- `XRIInteractionAdapter` maps XRI interaction into canonical actions.
- `SelectionService` tracks current selection/inspection state.
- The XR baseline remains XRI-first and vendor-agnostic.

### Phase 5: UI Toolkit Foundation

- `UIDocumentBootstrap` provides the runtime/root UI Toolkit setup path.
- `UIRootCoordinator` builds the root shell and binds presenter-driven panels.
- Step and part info shell panels exist.
- Presenter/controller split is established.
- UI remains presentation-only and does not own runtime state.

### Phase 6: Content Model Implementation

- Machine package definitions now exist for machines, assemblies, subassemblies, parts, tools, steps, validation rules, hints, effects, targets, challenge config, asset manifests, and source references.
- `MachinePackageValidator` validates ids, required fields, enum-like schema values, and cross-definition references.
- `MachinePackageLoader` loads `machine.json` packages from `Assets/StreamingAssets/MachinePackages/`.
- Two sample packages now exist:
  - `tutorial_build`
  - `power_cube_frame_corner`
- The mechanics preview scene now reads content-driven step and part data from a machine package instead of relying only on hardcoded UI strings.
- Preview configuration was simplified after Phase 6 by moving scene-facing preview data into ScriptableObject assets under `Assets/_Project/Data/Preview/`.

### Phase 7: Machine Session and Step Runtime Skeleton

- `RuntimeEventBus` added to `OSE.Core` for decoupled cross-system event communication.
- Six event structs defined: `SessionLifecycleChanged`, `SessionCompleted`, `StepActivated`, `StepStateChanged`, `AssemblyStarted`, `AssemblyCompleted`.
- `MachineSessionController` implemented as the top-level session orchestrator. Loads machine packages, manages session lifecycle, creates child controllers.
- `AssemblyRuntimeController` implemented for assembly-level orchestration. Resolves assembly steps from package data, coordinates step activation and advancement.
- `StepController` implemented with a guarded step state machine. Manages transitions (Locked → Available → Active → Completed), publishes events, logs diagnostics.
- `ProgressionController` implemented for step ordering, cursor advancement, and completion history tracking.
- All controllers are plain C# classes registered through `ServiceRegistry`, not MonoBehaviours.
- `AppBootstrap` updated to register `MachineSessionController` on startup.
- Session state (`MachineSessionState`) is updated atomically at each transition for persistence safety.
- `RuntimeSessionDriver` MonoBehaviour added as a scene bridge that starts a session on Play, ticks elapsed time, and exposes runtime state in the inspector.
- `Test_Assembly_Mechanics.unity` scene updated with `RuntimeSessionDriver` component and verbose logging enabled.

---

## Current Visual Validation Path

Use `Assets/Scenes/Test_Assembly_Mechanics.unity`.

What the scene currently provides:

- a floor plane
- a sample beam part
- a placement target marker
- a runtime `UIDocument` host
- the step shell panel
- the part info shell panel
- a package-driven preview component that loads machine content from `StreamingAssets`
- a `RuntimeSessionDriver` that starts a live session on Play and exposes runtime state in the inspector

How it behaves:

- In edit mode, the scene shows the preview scaffold directly in the editor so progress is visible before pressing Play.
- `MechanicsSceneVisualProfile_Default.asset` drives the scene camera/geometry preview settings.
- `SceneContentPreviewProfile_Tutorial.asset` drives which package and step the preview UI shows.
- The `SceneContentPreviewDriver` on `Test Scene Setup` loads `tutorial_build` and pushes real package data into the step and part panels.
- In play mode, the `RuntimeSessionDriver` automatically starts a `MachineSessionController` session:
  - the console shows session lifecycle transitions and step state changes
  - the inspector on `Test Scene Setup` shows live runtime state (lifecycle, current assembly, current step, elapsed time, etc.)
  - right-click the `RuntimeSessionDriver` component and use **Complete Current Step** to manually advance through steps
  - the session auto-advances through assemblies and completes when all steps are done
- Verbose logging is enabled so all `[Step]` and `[Session]` events appear in the console.
- If the package id is changed in the `RuntimeSessionDriver` inspector, the session will use that package on next Play.

Primary scripts:

- `Assets/_Project/Scripts/UI/Root/TestAssemblyMechanicsSceneHarness.cs`
- `Assets/_Project/Scripts/Runtime/Preview/SceneContentPreviewDriver.cs`
- `Assets/_Project/Scripts/Runtime/Preview/RuntimeSessionDriver.cs`
- `Assets/_Project/Scripts/Runtime/Preview/MechanicsSceneVisualProfile.cs`
- `Assets/_Project/Scripts/Runtime/Preview/SceneContentPreviewProfile.cs`

The harness and preview drivers are visualization bridges.
They are not the future runtime authority for real content or progression logic.
The `RuntimeSessionDriver` is a scene bridge for testing the Phase 7 runtime controllers.

---

## Important Implemented Files

- `Assets/_Project/Scripts/App/ServiceRegistry.cs`
- `Assets/_Project/Scripts/Bootstrap/AppBootstrap.cs`
- `Assets/_Project/Scripts/Core/CoreEnums.cs`
- `Assets/_Project/Scripts/Core/CoreInterfaces.cs`
- `Assets/_Project/Scripts/Core/MachineSessionState.cs`
- `Assets/_Project/Scripts/Core/RuntimeStepState.cs`
- `Assets/_Project/Scripts/Core/PlacementValidationResult.cs`
- `Assets/_Project/Scripts/Input/InputActionRouter.cs`
- `Assets/_Project/Scripts/Interaction/XRIInteractionAdapter.cs`
- `Assets/_Project/Scripts/Interaction/SelectionService.cs`
- `Assets/_Project/Scripts/Content/Definitions/MachinePackageDefinition.cs`
- `Assets/_Project/Scripts/Content/Validation/MachinePackageValidator.cs`
- `Assets/_Project/Scripts/Content/Loading/MachinePackageLoader.cs`
- `Assets/_Project/Scripts/Core/RuntimeEvents.cs`
- `Assets/_Project/Scripts/Runtime/Session/MachineSessionController.cs`
- `Assets/_Project/Scripts/Runtime/Session/AssemblyRuntimeController.cs`
- `Assets/_Project/Scripts/Runtime/Session/StepController.cs`
- `Assets/_Project/Scripts/Runtime/Session/ProgressionController.cs`
- `Assets/_Project/Scripts/Runtime/Preview/SceneContentPreviewDriver.cs`
- `Assets/_Project/Scripts/Runtime/Preview/RuntimeSessionDriver.cs`
- `Assets/_Project/Scripts/Runtime/Preview/MechanicsSceneVisualProfile.cs`
- `Assets/_Project/Scripts/Runtime/Preview/SceneContentPreviewProfile.cs`
- `Assets/_Project/Scripts/UI/Bindings/UIDocumentBootstrap.cs`
- `Assets/_Project/Scripts/UI/Root/UIRootCoordinator.cs`
- `Assets/_Project/Scripts/UI/Root/TestAssemblyMechanicsSceneHarness.cs`
- `Assets/_Project/Scripts/UI/Controllers/StepPanelController.cs`
- `Assets/_Project/Scripts/UI/Controllers/PartInfoPanelController.cs`
- `Assets/_Project/Scripts/UI/Presenters/StepPanelPresenter.cs`
- `Assets/_Project/Scripts/UI/Presenters/PartInfoPanelPresenter.cs`

---

## Phase Status

### Current Completed Phase

- **Phase 7: Machine Session and Step Runtime Skeleton**

Why this phase is considered complete:

- `RuntimeEventBus` provides decoupled event communication across all modules
- `MachineSessionController` manages full session lifecycle from package loading through completion
- `AssemblyRuntimeController` orchestrates assembly-level step flow
- `StepController` implements a guarded step state machine with diagnostic logging
- `ProgressionController` manages step ordering, cursor advancement, and completion history
- controllers are plain C# classes registered via `ServiceRegistry` (testable, multiplayer-safe)
- `MachineSessionState` is updated atomically at each transition for persistence safety
- all state transitions publish events and log via `OseLog`

### Next Formal Phase

- **Phase 8: Part Presentation and Basic Interaction**

This should introduce:

- basic part spawning from content references
- inspection flow through the selection service
- manipulation flow for the first supported platform
- placement targets and ghost/preview representation
- part info and tool info display wired to runtime state
- step instruction panel driven by runtime controllers instead of preview driver

---

## Recommended Next Tasks

1. Wire the UI presenters (StepPanelPresenter, PartInfoPanelPresenter) to subscribe to `StepStateChanged` events from the runtime controllers instead of relying on the preview driver.
2. Implement basic part spawning from content package asset references.
3. Implement placement targets that the runtime step data can reference.
4. Keep `Test_Assembly_Mechanics.unity` as the integration sandbox.
5. Preserve the package-driven preview path as a fallback diagnostic while Phase 8 interaction comes online.

---

## Known Constraints

- Direct headless Unity validation may fail while the editor is already open.
- When that happens, use a compile-only verification path against the Unity assemblies and then verify visually in the open editor.
- The mechanics scene harness is intentionally temporary and content-agnostic.
- The scene preview now uses a hybrid pattern:
  - JSON machine packages remain the runtime content contract.
  - ScriptableObject preview profiles are editor-facing setup data for fast swapping and readability.
- The `SceneContentPreviewDriver` is a bridge for visualization, not the long-term authoritative runtime.
- The UI shell is still a foundation layer, not a complete runtime UI system.

---

## Update Protocol

After each phase or major validated integration pass, update this file with:

- current completed phase
- what was added
- how to visualize or validate it
- what was added to the test scene to make the phase observable
- known limitations
- next recommended phase

### Scene Visualization Rule

Every completed phase must update `Assets/Scenes/Test_Assembly_Mechanics.unity` so the phase's work is visible when the scene is opened and played.

This typically means adding or updating a lightweight driver or harness component that:

- exercises the phase's systems on Play
- exposes key runtime state in the inspector
- provides context menu actions for manual testing
- logs diagnostic output to the console

These components are diagnostic bridges. They must not own runtime truth or alter the architecture. When a later phase provides real systems that replace a driver's role, the driver should be simplified or removed.

The test scene is the project's living proof-of-work. It should always reflect the cumulative state of all completed phases.

If this file and the code disagree, update the file immediately as part of the same changeset.
