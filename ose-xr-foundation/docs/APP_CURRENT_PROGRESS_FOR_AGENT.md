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

- March 14, 2026 (Phase 9 complete — Drag-and-Drop Placement and Validation Feedback; Phase 8 harness refactored into 3 focused components)

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
  - `tutorial_build`
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
- a `SessionDriver` that handles both edit-mode content preview and play-mode runtime sessions

How it behaves:

- In edit mode, the `SessionDriver` loads the configured machine package and pushes step/part data into the UI panels for visual preview. The `_previewStepSequenceIndex` field controls which step is shown. GLB parts spawn under `Preview Scaffold`, named by `part.id`.
- `MechanicsSceneVisualProfile_Default.asset` drives the scene camera/geometry preview settings.
- In play mode, the `SessionDriver` automatically starts a `MachineSessionController` session:
  - the console shows session lifecycle transitions, step state changes, and part state changes
  - the inspector on `Test Scene Setup` shows live runtime state (lifecycle, current assembly, current step, elapsed time, etc.)
  - **click a part** to select it (yellow highlight), its info appears in the part info panel
  - **drag a part** toward the ghost/target position and release — if within tolerance, the part snaps to target (green) and the step auto-completes; if too far, the part flashes red and returns to start
  - **ghost parts** appear at target positions when a step activates, showing placement targets with transparent blue material
  - right-click the `SessionDriver` component and use **Complete Current Step** to manually advance through steps
  - right-click the `PartInteractionBridge` and use **Place Selected Part at Target** as a debug shortcut
  - when a step completes, its required parts move to their assembled (play) positions and turn green; ghost parts are cleared
  - **touch input** works the same as mouse — touch to select, drag to place
  - the UI step and part info panels update live as steps activate and parts are selected
  - the session auto-advances through assemblies and completes when all steps are done
- Verbose logging is enabled so all `[Step]`, `[Session]`, and `[PartRuntime]` events appear in the console.
- If the package id is changed in the `SessionDriver` inspector, the session will use that package on next Play.

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
- `Assets/_Project/Scripts/UI/Root/PreviewSceneSetup.cs`
- `Assets/_Project/Scripts/UI/Root/PackagePartSpawner.cs`
- `Assets/_Project/Scripts/UI/Root/PartInteractionBridge.cs`
- `Assets/_Project/Scripts/UI/Root/MaterialHelper.cs`
- `Assets/_Project/Scripts/UI/Controllers/StepPanelController.cs`
- `Assets/_Project/Scripts/UI/Controllers/PartInfoPanelController.cs`
- `Assets/_Project/Scripts/UI/Presenters/StepPanelPresenter.cs`
- `Assets/_Project/Scripts/UI/Presenters/PartInfoPanelPresenter.cs`
- `Assets/_Project/Scripts/Content/Definitions/PackagePreviewConfig.cs`
- `Assets/_Project/Scripts/Runtime/Session/PartRuntimeController.cs`
- `Assets/_Project/Scripts/Runtime/Session/PlacementValidator.cs`
- `Assets/_Project/Scripts/Editor/PackageSyncTool.cs`
- `Assets/_Project/Scripts/Editor/PackageBrowserWindow.cs`

---

## Phase Status

### Current Completed Phase

- **Phase 9: Drag-and-Drop Placement and Validation Feedback**

Why this phase is considered complete:

- Mouse drag-to-place: click part → drag with mouse → release near ghost/target → validates placement via `PlacementValidator` with tolerance values from package `ValidationRuleDefinition`
- Touch drag-to-place: same flow via `Touchscreen.current` from Input System
- Snap-to-target: valid placements lerp-animate to exact target position/rotation/scale
- Visual feedback: grabbed parts turn orange, valid placements snap + turn green, invalid placements flash red and return to start position
- Tolerance-based validation: reads `positionToleranceMm` and `rotationToleranceDeg` from the step's validation rules
- Drag threshold (5px) distinguishes click-to-select from drag-to-place
- "Place Selected Part at Target" context menu remains as a debug shortcut
- Parts return to their start position on invalid placement
- All interaction routes through `PartRuntimeController` state machine (Selected → Grabbed → ValidPlacement/InvalidPlacement)

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
- **Invalid flash**: invalid placements flash red for 0.3 seconds, then the part returns to its start position and is deselected.
- **Visual feedback colors**: Selected = yellow (1.0, 0.85, 0.2), Grabbed = orange (1.0, 0.65, 0.1), Completed = green (0.3, 0.9, 0.4), Invalid flash = red (1.0, 0.2, 0.2).
- **Context menu preserved**: "Place Selected Part at Target" still works as a debug shortcut for exact placement.

### Package Authoring Workflow

- **Authoring home**: `Assets/_Project/Data/Packages/` — Unity imports and visualizes everything here including GLB/FBX/USD models.
- **Runtime home**: `Assets/StreamingAssets/MachinePackages/` — generated by the sync tool, never hand-edited.
- **In-editor loader**: `MachinePackageLoader` reads from the authoring folder during play-in-editor (`#if UNITY_EDITOR`). No sync step needed to press Play.
- **PackageSyncTool**: `OSE → Sync Packages to StreamingAssets` copies JSON and binary assets, skipping `.meta` files. Also runs automatically before every build via `IPreprocessBuildWithReport` so builds always ship the latest authored packages.
- **PackageBrowserWindow**: `OSE → Package Browser` — read-only tree view of all packages in the Data folder. Shows machine info, assemblies, steps, parts, tools. Clicking a part's `assetRef` pings the file in the Project window. Includes a Sync button.
- To **add a new package**: create a folder under `Assets/_Project/Data/Packages/<package-id>/`, add a `machine.json`, place any model files in an `assets/` subfolder. The browser and loader pick it up immediately.


### Next Formal Phase

- **Phase 10: XR Grab Interaction and Canonical Input Wiring**

This phase adds XR-native grab interaction and wires all input through the canonical action system.

Scope:

- **XR near/far grab**: parts become `XRGrabInteractable` objects. Controller or hand grab drags them, release near target validates placement. Routes through `XRIInteractionAdapter` → canonical actions → `PartRuntimeController`.
- **Wire `SelectionService`** to `PartRuntimeController` so canonical actions drive selection instead of raw pointer raycast in `PartInteractionBridge`.
- **Multi-target step support**: steps with multiple `requiredPartIds` and multiple `targetIds` — each part must be placed at its matching target before the step completes. Currently only the first target is matched.
- **Challenge mode metrics**: track hints used, failed placement attempts, and time per step via `MachineSessionState`.

---

## Recommended Next Tasks

1. Add `XRGrabInteractable` to spawned parts in play mode when XR is active.
2. Wire `XRIInteractionAdapter` grab/release events to `PartRuntimeController.GrabPart()` / `AttemptPlacement()`.
3. Wire `SelectionService` to `PartRuntimeController` for canonical input-driven selection.
4. Support steps with multiple required parts — only complete step when all parts are placed.
5. Track challenge metrics (hints, failed attempts, time) in `MachineSessionState`.
6. Test with `power_cube_frame_corner` package (3 parts, 2 steps, multiple targets).
7. Keep `Test_Assembly_Mechanics.unity` as the integration sandbox.

---

## Known Constraints

- Direct headless Unity validation may fail while the editor is already open.
- When that happens, use a compile-only verification path against the Unity assemblies and then verify visually in the open editor.
- The scene harness components (PreviewSceneSetup, PackagePartSpawner, PartInteractionBridge) are intentionally temporary and content-agnostic.
- `MechanicsSceneVisualProfile` now only controls camera position, floor appearance, and visibility flags. All part and target scene placement comes from `previewConfig` in the package JSON.
- The UI shell is still a foundation layer, not a complete runtime UI system.
- Part selection currently uses raw pointer raycast in `PartInteractionBridge`. Phase 10 should wire `SelectionService` for canonical input-driven selection.
- **Known Phase 9 drag bug**: Dragging uses `new Plane(Vector3.up, partPosition)` which constrains movement to a horizontal plane at the part's Y height. This feels "stuck on an axis" — the part can only slide horizontally and cannot be raised/lowered toward targets at different heights. The fix is to use a camera-facing plane instead: `new Plane(-cam.transform.forward, partPosition)` so the part follows the pointer naturally on the camera's view plane regardless of Y position. This bug was confirmed by the user but not yet fixed.
- Mouse and touch drag-to-place is implemented in Phase 9. XR grab interaction via XRI interactables is Phase 10.
- Multi-target steps (multiple parts per step) are not yet supported — only the first matching target is validated. Phase 10 should add this.
- Ghost parts use a basic transparent URP/Lit material via `MaterialHelper.ApplyGhost()`. URP shader graph ghost effects could be added later.
- `MaterialHelper.Apply()` replaces renderer materials with a fresh URP/Lit material. This works for dummy meshes but may strip textures/properties from production GLB assets with authored materials. Consider preserving existing Lit-shader materials and only overriding color properties when real assets are used.
- `StreamingAssets/MachinePackages/` is generated by `PackageSyncTool`. Do not hand-edit files there — edit the authoring copies in `Assets/_Project/Data/Packages/` instead.

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
