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

How it behaves:

- In edit mode, the scene shows the preview scaffold directly in the editor so progress is visible before pressing Play.
- In play mode, the same scaffold remains present and the harness runs a small transition so the sample part moves and the UI updates to a later step.

Primary script:

- `Assets/_Project/Scripts/UI/Root/TestAssemblyMechanicsSceneHarness.cs`

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

- **Phase 5: UI Toolkit Foundation**

Why this phase is considered complete:

- the UI Toolkit root path exists
- the controller/presenter conventions are established
- the shell panels render
- the mechanics test scene provides an editor-visible and play-mode-visible validation path
- UI state is still routed through presentation objects instead of becoming gameplay truth

### Next Formal Phase

- **Phase 6: Content Model Implementation**

This should introduce:

- `MachineDefinition`
- `AssemblyDefinition`
- `SubassemblyDefinition`
- `StepDefinition`
- `PartDefinition`
- `ToolDefinition`
- `EffectDefinition`
- machine package metadata and validation/loading

---

## Recommended Next Tasks

1. Implement the Phase 6 content definition classes in the `OSE.Content` module.
2. Create at least one small sample machine package and one tutorial package.
3. Replace hardcoded preview text in the mechanics scene harness with data loaded from content models.
4. Keep `Test_Assembly_Mechanics.unity` as the visual integration sandbox while Phase 6 and Phase 7 come online.

Do not skip to full runtime progression before the content model exists.

---

## Known Constraints

- Direct headless Unity validation may fail while the editor is already open.
- When that happens, use a compile-only verification path against the Unity assemblies and then verify visually in the open editor.
- The mechanics scene harness is intentionally temporary and content-agnostic.
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
