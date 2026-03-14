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

- March 14, 2026

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

How it behaves:

- In edit mode, the scene shows the preview scaffold directly in the editor so progress is visible before pressing Play.
- The `MachinePackagePreviewDriver` on `Test Scene Setup` loads `tutorial_build` and pushes real package data into the step and part panels.
- In play mode, the same scaffold remains present, the sample beam moves closer to the target, and the package-driven preview advances to the next authored step.
- If the package id is changed in the inspector, the UI should update to the chosen package after the content reload completes.

Primary script:

- `Assets/_Project/Scripts/UI/Root/TestAssemblyMechanicsSceneHarness.cs`
- `Assets/_Project/Scripts/Runtime/Preview/MachinePackagePreviewDriver.cs`

This harness is a visualization bridge. It is not the future runtime authority for real content or progression logic.

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
- `Assets/_Project/Scripts/Runtime/Preview/MachinePackagePreviewDriver.cs`
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

- **Phase 6: Content Model Implementation**

Why this phase is considered complete:

- the machine package definitions exist
- package validation rules exist
- machine package loading exists
- tutorial and sample machine packages exist
- the mechanics test scene now has a content-driven preview path tied to `machine.json` data
- content remains outside scene-specific gameplay logic

### Next Formal Phase

- **Phase 7: Machine Session and Step Runtime Skeleton**

This should introduce:

- `MachineSessionController`
- `AssemblyRuntimeController`
- `StepController`
- `ProgressionController`
- explicit step activation and advancement flow
- state-driven progression that consumes the content package data now available

---

## Recommended Next Tasks

1. Implement `MachineSessionController`, `StepController`, and `ProgressionController` in the `OSE.Runtime` module.
2. Feed the active step and selected part from runtime state rather than directly from the preview driver.
3. Keep `Test_Assembly_Mechanics.unity` as the integration sandbox while Phase 7 comes online.
4. Preserve the package-driven preview path as a fallback diagnostic scene while runtime controllers are still early.

---

## Known Constraints

- Direct headless Unity validation may fail while the editor is already open.
- When that happens, use a compile-only verification path against the Unity assemblies and then verify visually in the open editor.
- The mechanics scene harness is intentionally temporary and content-agnostic.
- The `MachinePackagePreviewDriver` is a bridge for visualization, not the long-term authoritative runtime.
- The UI shell is still a foundation layer, not a complete runtime UI system.

---

## Update Protocol

After each phase or major validated integration pass, update this file with:

- current completed phase
- what was added
- how to visualize or validate it
- known limitations
- next recommended phase

If this file and the code disagree, update the file immediately as part of the same changeset.
