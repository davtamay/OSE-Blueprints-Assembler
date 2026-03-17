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

- March 16, 2026 (Phase 12 Slice 3: onboarding tutorial upgraded to a 6-step tool flow with equip/use tool-action gating, movement lock hardening, and package cleanup/sync)

---

## Phase Numbering Authority

This file and `IMPLEMENTATION_CHECKLIST.md` now use one canonical phase sequence.

- Completed through **Phase 11** = Onboarding Tutorial Vertical Slice.
- Next **Phase 12** = Tool Use Framework and Modular Tool Actions.
- Following **Phase 13** = XR Validation and Challenge UX.

If a mismatch appears, update both docs in the same changeset.
During active implementation, this file remains the execution source of truth.

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

## Product Vision (Runner Model)

**This application is designed as a package runner.** The runtime is a general-purpose assembly training shell that streams in machine packages — JSON + 3D assets — and drives the full experience from that data. Swapping the package folder is all it takes to get a completely different machine, build sequence, and part set.

This means:
- The application itself ships as a build with no baked-in machine content.
- Machine packages live outside the build and are streamed in at runtime via `StreamingAssets/` (or later, a remote CDN/URL).
- Each package is self-contained: JSON schema, part models (GLB/FBX/USD), effects, UI references.
- Adding a new machine never requires rebuilding the application.
- The long-term goal is for OSE blueprint documentation to map directly to streamable packages that anyone can author and load.

This runner model is a core architectural constraint. No machine-specific logic, IDs, or assumptions should ever be baked into the runtime C# code.

---


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
  - `onboarding_tutorial`
  - `power_cube_frame_corner`
- The mechanics preview scene now reads content-driven step and part data from a machine package instead of relying only on hardcoded UI strings.
- Preview configuration was simplified after Phase 6 by moving scene-facing preview data into ScriptableObject assets under `Assets/_Project/Data/Preview/`. These were later consolidated into `SessionDriver` (see Phase 7).

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
- `SessionDriver` MonoBehaviour consolidates edit-mode content preview and play-mode runtime session into a single component. In edit mode it loads the package and previews a selected step. In play mode it starts a live session, subscribes to runtime events, and drives the UI panels from live step transitions.
- `SceneContentPreviewDriver`, `RuntimeSessionDriver`, `SceneContentPreviewProfile`, and their profile assets were removed — their responsibilities are unified in `SessionDriver`.
- `Test_Assembly_Mechanics.unity` scene updated with `SessionDriver` component.
- The scene harness subscribes to `StepStateChanged` via `RuntimeEventBus` and moves parts to their play positions when steps complete. The old timed `PlayModeSequence` coroutine is removed.
- `MechanicsSceneVisualProfile` timer fields (`_animateSamplePartOnPlay`, `_playModeAdvanceDelay`) removed — part movement is now fully event-driven.

---

## Current Visual Validation Path

Use `Assets/Scenes/Test_Assembly_Mechanics.unity`.

What the scene currently provides:

- a floor plane
- per-package GLB part meshes spawned from `assetRef` in each part definition
- a placement target marker
- ghost parts at target positions showing where parts should go
- a runtime `UIDocument` host
- the step shell panel
- the part info shell panel
- a Session HUD panel (hint guidance inline + compact challenge metrics row when challenge mode is active)
- a world hint bubble anchored to the hint target
- a `SessionDriver` that handles both edit-mode content preview and play-mode runtime sessions

How it behaves:

- In edit mode, the `SessionDriver` loads the configured machine package and pushes step/part data into the UI panels for visual preview. The `_previewStepSequenceIndex` field controls which step is shown. GLB parts spawn under `Preview Scaffold`, named by `part.id`.
- `MechanicsSceneVisualProfile_Default.asset` drives the scene camera/geometry preview settings.
- In play mode, the `SessionDriver` automatically starts a `MachineSessionController` session:
  - the console shows session lifecycle transitions, step state changes, and part state changes
  - the inspector on `Test Scene Setup` shows live runtime state (lifecycle, current assembly, current step, elapsed time, etc.)
  - **click a part** to select it (yellow highlight), its info appears in the part info panel
  - selection and grab now route through `InputActionRouter` + `SelectionService`; XR grab works when an `XRInteractionManager` is present
  - **drag a part** toward the ghost/target position and release — if within tolerance, the part snaps to target (green) and the step auto-completes; if too far, the part flashes red and stays where dropped
  - **ghost parts** appear at target positions when a step activates, showing placement targets with transparent blue material
  - right-click the `SessionDriver` component and use **Complete Current Step** to manually advance through steps
  - right-click the `PartInteractionBridge` and use **Place Selected Part at Target** as a debug shortcut
  - when a step completes, its required parts move to their assembled (play) positions and turn green; ghost parts are cleared
  - a **Session HUD** panel shows challenge metrics (step/total time, failed attempts, hints) only when challenge mode is active
  - requesting a hint shows guidance inline in the Session HUD plus a world bubble; the target ghost flashes to draw attention
- UI panels are mode-aware via a per-session UI profile on `UIRootCoordinator` (tutorial/guided show hints; standard/review hide Session HUD by default)
  - **touch input** works the same as mouse — touch to select, drag to place
  - the UI step and part info panels update live as steps activate and parts are selected
  - the session auto-advances through assemblies and completes when all steps are done
- Verbose logging is enabled so all `[Step]`, `[Session]`, and `[PartRuntime]` events appear in the console.
- If the package id is changed in the `SessionDriver` inspector, the session will use that package on next Play.
- XR rig switching is available: the scene now contains both a controller rig and a hands rig, and `XRRigModeSwitcher` activates the appropriate one based on tracked devices at runtime.

Primary scripts:

- `Assets/_Project/Scripts/UI/Root/PreviewSceneSetup.cs` — scaffold, floor, target marker, camera, UI host
- `Assets/_Project/Scripts/UI/Root/PackagePartSpawner.cs` — subscribes to PackageChanged, spawns/positions GLB parts
- `Assets/_Project/Scripts/UI/Root/PartInteractionBridge.cs` — play-mode click-select, ghosts, step response, placement
- `Assets/_Project/Scripts/UI/Root/MaterialHelper.cs` — shared material utility
- `Assets/_Project/Scripts/Runtime/Preview/SessionDriver.cs`
- `Assets/_Project/Scripts/Runtime/Preview/MechanicsSceneVisualProfile.cs`

These components are visualization bridges.
They are not the future runtime authority for real content or progression logic.

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
- `Assets/_Project/Scripts/Interaction/XRRigModeSwitcher.cs`
- `Assets/_Project/Scripts/Interaction/SelectionService.cs`
- `Assets/_Project/Scripts/Content/Definitions/MachinePackageDefinition.cs`
- `Assets/_Project/Scripts/Content/Validation/MachinePackageValidator.cs`
- `Assets/_Project/Scripts/Content/Loading/MachinePackageLoader.cs`
- `Assets/_Project/Scripts/Core/RuntimeEvents.cs`
- `Assets/_Project/Scripts/Runtime/Session/MachineSessionController.cs`
- `Assets/_Project/Scripts/Runtime/Session/AssemblyRuntimeController.cs`
- `Assets/_Project/Scripts/Runtime/Session/StepController.cs`
- `Assets/_Project/Scripts/Runtime/Session/ProgressionController.cs`
- `Assets/_Project/Scripts/Runtime/Preview/SessionDriver.cs`
- `Assets/_Project/Scripts/Runtime/Preview/MechanicsSceneVisualProfile.cs`
- `Assets/_Project/Scripts/UI/Bindings/UIDocumentBootstrap.cs`
- `Assets/_Project/Scripts/UI/Root/UIRootCoordinator.cs`
- `Assets/_Project/Scripts/UI/Root/HintWorldCanvas.cs`
- `Assets/_Project/Scripts/UI/Root/PreviewSceneSetup.cs`
- `Assets/_Project/Scripts/UI/Root/PackagePartSpawner.cs`
- `Assets/_Project/Scripts/UI/Root/PartInteractionBridge.cs`
- `Assets/_Project/Scripts/UI/Root/MaterialHelper.cs`
- `Assets/_Project/Scripts/UI/Controllers/StepPanelController.cs`
- `Assets/_Project/Scripts/UI/Controllers/PartInfoPanelController.cs`
- `Assets/_Project/Scripts/UI/Controllers/SessionHudPanelController.cs`
- `Assets/_Project/Scripts/UI/Presenters/SessionHudPanelPresenter.cs`
- `Assets/_Project/Scripts/UI/Presenters/StepPanelPresenter.cs`
- `Assets/_Project/Scripts/UI/Presenters/PartInfoPanelPresenter.cs`
- `Assets/_Project/Scripts/Content/Definitions/PackagePreviewConfig.cs`
- `Assets/_Project/Scripts/Runtime/Session/PartRuntimeController.cs`
- `Assets/_Project/Scripts/Runtime/Session/PlacementValidator.cs`
- `Assets/_Project/Scripts/Editor/PackageSyncTool.cs`
- `Assets/_Project/Scripts/Editor/PackageBrowserWindow.cs`
- `Assets/_Project/Data/Packages/onboarding_tutorial/machine.json`

---

## Phase Status

### Current Completed Phase

- **Phase 11: Onboarding Tutorial Vertical Slice**

Why this phase is considered complete:

- `onboarding_tutorial` package now runs a full 6-step interaction-first flow with tool equip/use inserted between beam and bracket placement.
- `completionMode: "confirmation_only"` steps now show a touch-friendly Continue button wired to `StepController.CompleteStep()`.
- Session HUD now shows a short step-completion toast and a persistent session-completion milestone banner.
- Step panel now includes a progress bar reflecting current step / total steps.
- Session defaults switched to tutorial mode and the onboarding package for immediate end-to-end validation.

### Active Phase In Progress

- **Phase 12: Tool Use Framework and Modular Tool Actions (Slice 1-3 implemented)**

What is now implemented:

- `ToolRuntimeController` added and registered via `AppBootstrap`; tracks package tools, active step required tools (`relevantToolIds`), and active equipped tool.
- `MachineSessionController` now initializes/disposes `ToolRuntimeController` with the active package lifecycle.
- New bottom-center UI Toolkit tool dock menu added with:
  - collapsible Tools button
  - package-driven tool list
  - required-tools-first ordering per active step
  - active tool status display
- New tool info panel added in the right column (under part info), showing hovered/selected tool metadata (category, purpose, usage, safety).
- Tool menu selection now equips a runtime-active tool and updates tool info presentation immediately.
- `Test_Assembly_Mechanics.unity` now defaults to `onboarding_tutorial` in tutorial mode for immediate end-to-end onboarding validation.
- Core tool-action contracts/events now exist (`ToolActionType`, `ToolActionFailureReason`, `ActiveToolChanged`, `ToolActionProgressed`, `ToolActionCompleted`, `ToolActionFailed`).
- `step_fasten_bolts` now includes `requiredToolActions` content and requires `tool_wrench_13mm` tighten actions (count = 3) before completion.
- Confirmation flow now routes through `ToolRuntimeController.TryExecutePrimaryAction()`; steps with required tool actions no longer complete until tool-action criteria are satisfied.
- Canonical action groundwork now includes `CanonicalAction.ToolPrimaryAction` and `CanonicalAction.ToggleToolMenu`.
- Tool schema usage now requires `tools[].assetRef`; `MachinePackageValidator` validates this so packages explicitly declare tool assets.
- Package authoring now expects `assets/tools/` as a sibling to `assets/parts/`; dummy tool GLBs were seeded in package folders for immediate preview/runtime use.
- `PackageDummyMeshGenerator` now generates missing dummy meshes from both part and tool `assetRef` entries (single pass across package JSON).
- Tool dock now supports explicit unequip UX (`Clear Active Tool`) and re-click on equipped tool chip to toggle unequip.
- Active equipped tool now renders as a pointer-follow cursor badge with mouse/touch-aware offsets (touch badge is offset above finger to avoid occlusion).
- `PartInteractionBridge` now spawns a tool-ghost indicator at the required tool-action target when the matching tool is equipped for the active step.
- Tool-action failures now increment session mistake count (`MachineSessionState.MistakeCount`) via `ToolActionFailed` event handling.
- Active tool persistence now coexists with part placement: tool mode only hard-locks parts while a step has an unresolved primary tool action.
- Drag start now re-validates part lock state so already placed/completed tutorial parts cannot be re-grabbed.
- `onboarding_tutorial` now uses explicit tool-action steps: `step_equip_tape_measure` (confirmation-gated equip) and `step_use_tape_measure` (targeted measure pass on `target_bracket_slot`).

### Phase 10 Hardening: Multi-Target Regression Test

- **`power_cube_frame_corner` restructured**: The two sequential single-part steps (`step_place_frame_plate`, `step_attach_corner_bracket`) were replaced with a single multi-target step (`step_stage_plate_and_bracket`) requiring both `frame_plate_a` and `corner_bracket_a` placed in the same step, followed by a bolt fastening step (`step_fasten_bolts`).
- **Missing previewConfig entries added**: `corner_bracket_a` and `m8_hex_bolt` now have full `partPlacements` entries (start/play positions, colors). `target_corner_bracket_slot_a` and `target_bolt_slot` now have `targetPlacements` entries. Ghost spawning and snap-to-target now have data for all parts and targets.
- **Snap animation: single-slot → list-based**: `_snappingPart` (single field) was replaced with `_activeSnaps` (list of `SnapEntry`). Multiple parts can now snap-animate concurrently in multi-target steps without overwriting each other's target pose.
- **Flash animation: single-slot → list-based**: `_flashPart` (single field) was replaced with `_activeFlashes` (list of `FlashEntry`). Multiple parts can now flash red concurrently.
- **Per-part ghost removal**: When a part successfully snaps to its target, only that part's ghost is destroyed via `RemoveGhostForPart(partId)`. Remaining ghosts stay visible so the user knows which targets still need parts. Full `ClearGhosts()` still runs on step completion.
- **Multi-target validation flow**: `FindNearestGhostForPart` enforces per-part matching (a part can only snap to its own ghost via `GhostPlacementInfo.MatchesPart`). Wrong-part/wrong-target drops fail. `AreActiveStepRequiredPartsPlaced()` gates step completion on all required parts being `PlacedVirtually`.

### Phase 8: Part Presentation and Basic Interaction

- **Harness fixes**: `TryLoadPackageAsset` now uses `PrefabUtility.InstantiatePrefab` (correct editor API for GLTFast-imported GLBs). `HandlePackageChanged` now only destroys spawned part GOs — keeps `_previewRoot`, floor, target marker, and UI Root alive across package changes, eliminating the UIDocument teardown bug.
- **PartRuntimeController** added (plain C# in `OSE.Runtime`, registered via `ServiceRegistry`). Tracks per-part `PartPlacementState` transitions (NotIntroduced → Available → Selected → Grabbed → ValidPlacement → PlacedVirtually → Completed). Subscribes to `StepStateChanged` — when a step becomes Active, its required/optional parts are made Available; when a step completes, its parts are marked Completed.
- **PlacementValidator** added — implements `IPlacementValidator`, validates position and rotation against tolerance values from `ValidationRuleDefinition`. Converts mm tolerance to Unity units. Also provides `ValidateExact()` for context-menu placement.
- **PartStateChanged** and **PlacementAttempted** runtime events added to `RuntimeEventBus`. All part state transitions and placement attempts are published for decoupled consumption.
- **MachineSessionController** now initializes and disposes `PartRuntimeController` alongside session lifecycle.
- **Ghost parts**: When a step activates, `PartInteractionBridge` spawns transparent copies of the part mesh (same `assetRef`, ghost material applied via `MaterialHelper.ApplyGhost`) at target positions. Ghost position comes from `previewConfig.partPlacements[].playPosition` or `targetPlacements[]`. Ghosts are cleared when the step completes.
- **Click-to-select**: In play mode, mouse click raycasts through the camera. If a spawned part is hit, `PartRuntimeController.SelectPart` is called. Selected parts turn yellow; completed parts turn green.
- **Part info driven by runtime state**: Both `SessionDriver` and the harness subscribe to `PartStateChanged`. When a part is Selected or Inspected, part info (name, function, material, tools, search terms) is pushed to the UI panels via `IPresentationAdapter.ShowPartInfoShell`.
- **"Place Selected Part at Target"** context menu on the harness: validates the placement (exact for context menu), moves the part to its play position, and completes the active step.
- **Colliders preserved in play mode**: `TryLoadPackageAsset` keeps existing colliders (or adds a `BoxCollider`) for raycasting; only strips colliders in edit mode.
- **AppBootstrap** registers `PartRuntimeController` and `PlacementValidator` (`IPlacementValidator`) as services.

### Post-Phase 8 Architectural Refactor

The monolithic `TestAssemblyMechanicsSceneHarness` was split into three focused components:

- **PreviewSceneSetup** — owns the scene scaffold (Preview Scaffold parent, floor plane, target marker, camera, UI Root host). Reads `MechanicsSceneVisualProfile`. Knows nothing about packages, parts, or runtime events.
- **PackagePartSpawner** — subscribes to `SessionDriver.PackageChanged`. Spawns/destroys GLB part GOs via `TryLoadPackageAsset`. Positions parts and target marker from `previewConfig`. Exposes `SpawnedParts` list and lookup helpers for sibling components.
- **PartInteractionBridge** — play-mode only. Subscribes to `StepStateChanged`/`PartStateChanged`. Handles mouse click-to-select via `Mouse.current`. Spawns ghost parts at target positions. Applies visual feedback colours (yellow=selected, green=completed). Provides "Place Selected Part at Target" context menu.

A shared **MaterialHelper** static utility provides `Apply()` (opaque URP/Lit material) and `ApplyGhost()` (transparent material) used by all three components.

All three components live on the same GameObject and use `[RequireComponent]` to declare their dependencies.

### Post-Refactor Bug Fix: Ghost Spawn Race Condition

**Problem**: `SessionDriver.StartSessionAsync` fired `PackageChanged` **after** `_session.StartSessionAsync()` returned. But `StartSessionAsync` synchronously calls `BeginCurrentAssembly()` → step activates → `StepStateChanged(Active)` fires during the await. This meant `PartInteractionBridge` tried to spawn ghosts before `PackagePartSpawner` had received `PackageChanged`, so `_spawner.CurrentPackage` was null and ghosts silently failed to spawn.

**Fix**: Added `MachineSessionController.PackageReady` event — fires after package load and controller initialization but **before** `BeginCurrentAssembly()`. `SessionDriver` subscribes to `PackageReady` and fires `PackageChanged` from there, ensuring parts are spawned before any step events reach `PartInteractionBridge`.

### Removed `ghostAssetRef` from Schema

Ghost parts now reuse the same `assetRef` mesh with a transparent material applied via `MaterialHelper.ApplyGhost()`. The separate `ghostAssetRef` field was removed from `PartDefinition`, all `machine.json` files, `DATA_SCHEMA.md`, and `PackageDummyMeshGenerator`. Ghost GLB files were deleted.

### Phase 9: Drag-and-Drop Placement and Validation Feedback

- **Unified pointer input**: `TryGetPointerState()` abstracts `Mouse.current` and `Touchscreen.current` into a single pressed/released/position interface. Both input sources follow the same code path.
- **Drag state machine**: pointer-down on a part selects it and prepares for drag. If pointer moves more than 5 pixels, `PartRuntimeController.GrabPart()` is called and the part follows the pointer on its camera-distance plane. On pointer-up, placement is validated.
- **Tolerance-based validation**: `ResolveTolerances()` looks up the active step's `validationRuleIds`, finds the rule matching the target, and reads `positionToleranceMm` / `rotationToleranceDeg`. Falls back to 50mm / 30° if no rule is found.
- **PlacementValidationRequest**: built with the part's current local position/rotation vs the expected target position from `previewConfig.partPlacements[].playPosition`.
- **Snap animation**: valid placements lerp to exact target position/rotation/scale over multiple frames using `SnapLerpSpeed = 12`.
- **Invalid flash**: invalid placements flash red for 0.3 seconds, then the part stays where dropped and is deselected.
- **Visual feedback colors**: Selected = yellow (1.0, 0.85, 0.2), Grabbed = orange (1.0, 0.65, 0.1), Completed = green (0.3, 0.9, 0.4), Invalid flash = red (1.0, 0.2, 0.2).
- **Context menu preserved**: "Place Selected Part at Target" still works as a debug shortcut for exact placement.
- **Post-completion updates**:
  - Drag plane is camera-facing (`new Plane(-cam.transform.forward, partPosition)`) so parts follow the pointer naturally, not constrained to Y-up.
  - Depth control while dragging: Shift+drag vertical, Q/E keys, mouse scroll wheel, touch pinch (mobile), right/middle mouse drag.
  - Parts now stay where dropped outside the snap zone (red flash only, no return to start).
  - Laptop touchpad two-finger input does not drive depth — OS abstracts it as mouse, no raw finger data.
  - All updates live in `Assets/_Project/Scripts/UI/Root/PartInteractionBridge.cs`.
  - User confirmed basic drag and snap-to-ghost works; Shift+drag depth not yet re-confirmed after latest fix.

### Phase 10: XR Grab Interaction and Canonical Input Wiring

- **XR grab setup**: `PackagePartSpawner` adds `XRGrabInteractable` (plus kinematic `Rigidbody`) to spawned parts when XR is active.
- **SelectionService wiring**: `PartInteractionBridge` now routes Select/Inspect through `SelectionService` and canonical actions.
- **XRI adapter routing**: `XRIInteractionAdapter` notifies `SelectionService` and injects canonical Grab/Place on XR select enter/exit.
- **Canonical grab/place**: `PartInteractionBridge` handles Grab/Place actions for mouse/touch/XR and validates placement on release.
- **Multi-target steps**: ghost matching is per-part; steps only complete after all required parts are placed. `PartRuntimeController.AreActiveStepRequiredPartsPlaced()` added.
- **Challenge metrics**: `MachineSessionState` tracks hints used, failed attempts, and per-step timing; `HintRequested` events added.
- **SessionDriver** shows per-step timing metrics in the inspector.
- **XR rig switching**: `Test_Assembly_Mechanics.unity` now includes both `XR Origin Hands (XR Rig)` and a controller rig (`XR Origin (Controllers)`), with `XRRigModeSwitcher` enabling the correct rig based on tracked devices (hands vs controllers). The switcher can auto-add `XRIInteractionAdapter` to active interactors.
- **XRI sample content imported**: Starter Assets, Hands Interaction Demo, and XR Interaction Simulator samples are copied into `Assets/XRI/` so prefab references resolve in-scene.
- **XR ghost proximity fix**: `PartInteractionBridge` now tracks XR grabs to update ghost highlight and perform placement on release without relying on pointer drag state.
- **XR canonical wiring hardening**: `PartInteractionBridge` now guarantees `InputActionRouter` + `SelectionService` availability in play mode for the mechanics harness and forces `InputContext.StepInteraction` so injected XR actions are not context-gated.
- **XRI dependency late-binding**: `XRIInteractionAdapter` now re-resolves `InputActionRouter`/`SelectionService` on enable and on select enter/exit, preventing initialization-order null refs from dropping XR Grab/Place dispatch.
- **Pointer drag bounds**: pointer-driven drag now clamps to floor bounds and viewport margin; dragged parts can no longer be moved below the floor or outside the usable camera area.

### Package Authoring Workflow

- **Authoring home**: `Assets/_Project/Data/Packages/` — Unity imports and visualizes everything here including GLB/FBX/USD models.
- **Runtime home**: `Assets/StreamingAssets/MachinePackages/` — generated by the sync tool, never hand-edited.
- **In-editor loader**: `MachinePackageLoader` reads from the authoring folder during play-in-editor (`#if UNITY_EDITOR`). No sync step needed to press Play.
- **PackageSyncTool**: `OSE → Sync Packages to StreamingAssets` copies JSON and binary assets, skipping `.meta` files. Also runs automatically before every build via `IPreprocessBuildWithReport` so builds always ship the latest authored packages.
- **PackageBrowserWindow**: `OSE → Package Browser` — read-only tree view of all packages in the Data folder. Shows machine info, assemblies, steps, parts, tools. Clicking a part's `assetRef` pings the file in the Project window. Includes a Sync button.
- **Package cleanup**: `tutorial_build` package has been retired/removed; onboarding now serves as the canonical starter tutorial package.
- To **add a new package**: create a folder under `Assets/_Project/Data/Packages/<package-id>/`, add a `machine.json`, place any model files in an `assets/` subfolder. The browser and loader pick it up immediately.


### Phase 11: Onboarding Tutorial Vertical Slice

- **New tutorial package**: `onboarding_tutorial` - 6-step package teaching every core interaction in order: Orient Yourself, Select & Inspect, Drag & Place (beam), Equip Tape Measure, Use Tape Measure on bracket target, Place the Bracket. 2 parts, 2 targets, 3 hints. Platform-neutral instruction text throughout ("tap or click", "drag or grab"). `challengeConfig.enabled: false`, `recommendedMode: "tutorial"`.
- **Confirmation step button**: Steps with `completionMode: "confirmation_only"` now show a green "Continue" button (44px min height for touch accessibility) in the step panel. Button wires to `StepController.CompleteStep()`. Auto-hides for non-confirmation steps.
- **Step completion toast**: When a step completes, a green "Step Complete!" banner appears in the Session HUD for 2 seconds and auto-hides. Uses the same timer pattern as hint toasts.
- **Session completion banner**: When `SessionCompleted` fires, a persistent green "Tutorial Complete! (Xm Xs)" card appears in the Session HUD showing total time.
- **Progress bar**: A thin green progress bar below the step panel header shows completion ratio (current step / total steps). Updates on each step transition.
- **SessionDriver defaults**: `_packageId` set to `"onboarding_tutorial"` and `_sessionMode` set to `Tutorial`.
- **IPresentationAdapter extended**: `ShowStepShell` now accepts `bool showConfirmButton`, new `ShowStepCompletionToast(string)` method added.
- **SessionHudViewModel extended**: Added `ShowStepToast`, `StepToastMessage`, `ShowMilestone`, `MilestoneMessage` fields.
- **StreamingAssets synced**: `onboarding_tutorial/machine.json` copied to `Assets/StreamingAssets/MachinePackages/onboarding_tutorial/`.

### Next Formal Phase

- **Phase 12: Tool Use Framework and Modular Tool Actions**

Scope (proposed, pending validation):

- Define canonical tool action intents and runtime states (equip/select/use/release) that work across mouse, touch, and XR.
- Add a modular tool runtime layer (`ToolRuntimeController` + events) so tools are not hardcoded into part interaction scripts.
- Extend package schema usage for step-level tool requirements (tool id + action type + optional target/tolerance metadata).
- Implement at least two concrete tool actions in the mechanics scene (for example: wrench tighten and hammer strike) with visual/UI feedback.
- Route tool actions through canonical input and interaction orchestration so existing architecture boundaries remain intact.

### Following Phase

- **Phase 13: XR Validation and Challenge UX**

Scope (proposed, pending validation):

- Validate XR grab and tool actions in-headset and refine interaction layers / attach behavior.
- Re-confirm depth controls (Shift+drag, scroll, pinch) and tune sensitivity.
- Decide whether to reintroduce tolerance-based validation vs snap-zone only.
- Tune hint bubble placement/scale for XR readability and confirm ghost highlight timing.

---

## Recommended Next Tasks

1. Enter Play mode in `Test_Assembly_Mechanics.unity` and validate full tighten loop: equip 13mm wrench, trigger tighten action 3x, confirm step completion gates correctly, and verify wrong-tool feedback.
2. Add explicit `IToolActionHandler` abstraction so action logic is pluggable per tool/action type instead of centralized in `ToolRuntimeController`.
3. Implement second action type (`strike`) with content + runtime handling and verify coexistence with tighten.
4. Add active-tool cursor/reticle/hand visual representation per platform (mouse/touch/XR), keeping runtime truth in services.
5. Wire canonical `ToolPrimaryAction` bindings for desktop/mobile/XR input maps and verify parity.
6. After two action types are stable, execute Phase 13 XR validation in-headset for grab + tool interactions.

---

## Known Constraints

- Direct headless Unity validation may fail while the editor is already open.
- When that happens, use a compile-only verification path against the Unity assemblies and then verify visually in the open editor.
- The scene harness components (PreviewSceneSetup, PackagePartSpawner, PartInteractionBridge) are intentionally temporary and content-agnostic.
- `MechanicsSceneVisualProfile` now only controls camera position, floor appearance, and visibility flags. All part and target scene placement comes from `previewConfig` in the package JSON.
- The UI shell is still a foundation layer, not a complete runtime UI system.
- **Depth control validation**: Shift+drag depth adjustment has not yet been re-confirmed after the camera-facing drag plane fix.
- **Touchpad limitation**: Laptop touchpad two-finger gestures do not provide raw touch data; they are exposed as mouse input only.
- **XR validation pending**: XR grab interaction is implemented but not yet validated in-headset.
- **Tool mechanics partial**: First tool action flow (`tighten`) is implemented; pluggable multi-action handler architecture (`IToolActionHandler`) is still pending.
- **Tool menu status**: Tool dock/menu + tool info panel are implemented and now drive active-tool-gated tighten progression.
- **XR rig switching pending validation**: controller vs hand activation is implemented but still needs in-headset confirmation across devices.
- **Multi-target validation**: Multi-target steps are supported and `power_cube_frame_corner` now exercises them (`step_stage_plate_and_bracket` requires two parts). In-editor visual regression test pending.
- **Hint world bubble is display-only**: the world hint `UIDocument` is non-interactive and uses UI Toolkit visuals only (no XR UI interaction yet).
- Ghost parts use a basic transparent URP/Lit material via `MaterialHelper.ApplyGhost()`. URP shader graph ghost effects could be added later.
- `MaterialHelper.Apply()` replaces renderer materials with a fresh URP/Lit material. This works for dummy meshes but may strip textures/properties from production GLB assets with authored materials. Consider preserving existing Lit-shader materials and only overriding color properties when real assets are used.
- `StreamingAssets/MachinePackages/` is generated by `PackageSyncTool`. Do not hand-edit files there ? edit the authoring copies in `Assets/_Project/Data/Packages/` instead.

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
