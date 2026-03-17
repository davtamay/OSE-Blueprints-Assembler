# Interaction V2 — Implementation Plan & Handoff

## Status: ACTIVE IMPLEMENTATION

**Architecture doc**: `ose-xr-foundation/docs/INTERACTION_ARCHITECTURE_V2.md`
**This file**: Living implementation tracker. Update after each task.

---

## Strategic Implementation Order

The order is designed so each task delivers standalone value, can be tested immediately, and doesn't break anything existing. If tokens run out mid-phase, the next agent picks up from the last completed task.

---

## PHASE M1: Foundation (Zero Risk)

Creates types, config, and folder structure. No behavior change. No existing files touched.

### Task M1.1 — InteractionState enum + InteractionIntent struct ✅
- Create `Assets/_Project/Scripts/Interaction.V2/Core/InteractionState.cs`
- Create `Assets/_Project/Scripts/Interaction.V2/Core/InteractionIntent.cs`
- Pure data types, no dependencies

### Task M1.2 — InteractionSettings ScriptableObject ✅
- Create `Assets/_Project/Scripts/Interaction.V2/Core/InteractionSettings.cs`
- All toggles with sensible defaults (master switch `UseV2Interaction = false`)
- Create asset at `Assets/_Project/Data/Config/InteractionSettings_Default.asset` (must be done in Unity Editor — just create the SO script, document that the asset needs creating)

### Task M1.3 — IIntentProvider interface ✅
- Create `Assets/_Project/Scripts/Interaction.V2/Input/IIntentProvider.cs`
- Single method: `InteractionIntent Poll()`

### Task M1.4 — InteractionOrchestrator shell ✅
- Create `Assets/_Project/Scripts/Interaction.V2/Core/InteractionOrchestrator.cs`
- MonoBehaviour with serialized `InteractionSettings` reference
- Update() returns early if `!_settings.UseV2Interaction`
- Camera intent routing implemented (Orbit/Pan/Zoom/Focus/ResetView)
- Camera suppressed during DraggingPart state
- Public `InteractionState CurrentState` property

### Task M1.5 — Assembly definition ✅
- Create `Assets/_Project/Scripts/Interaction.V2/OSE.Interaction.V2.asmdef`
- References: `OSE.Core`, `OSE.Input`, `OSE.Interaction`, `OSE.Runtime`, `OSE.UI`, `Unity.InputSystem`, `Unity.XR.Interaction.Toolkit`

---

## PHASE M2: Camera Rig (Medium Risk — High Value)

This is the **highest-value deliverable** because no camera control exists today. Users currently have a fixed camera.

### Task M2.1 — CameraState + CameraConstraintSphere ✅
- `CameraState` struct with Yaw/Pitch/Distance/PivotPosition, ComputePosition(), ComputeRotation(), FromTransform()
- `CameraConstraintSphere` clamps distance + pitch, reads settings

### Task M2.2 — CameraSmoothing ✅
- `CameraSmoothing` with per-axis lerp speeds, Step() method

### Task M2.3 — CameraPivotResolver ✅
- `CameraPivotResolver` with PivotSource enum, Resolve() method, assembly bounds caching

### Task M2.4 — AssemblyCameraRig MonoBehaviour ✅
- Full implementation with ApplyOrbit/Pan/Zoom/FocusOn/FrameBounds/ResetToDefault/ApplyViewpoint/SetPivot
- LateUpdate smoothing + constraint enforcement
- InitializeFromCurrentTransform avoids visual jump

### Task M2.5 — DesktopIntentProvider ✅
- Full implementation: right-drag → Orbit, middle-drag → Pan, scroll → Zoom
- Left-click → Select/Drag with commit-early gesture classification
- Left-drag on empty → Orbit (trackpad-friendly fallback)
- F → Focus, Home → ResetView, Escape → Cancel, Alt+click → Inspect
- Raycast for HitTarget, UI gating via EventSystem

### Task M2.6 — Wire orchestrator to camera ✅
- `InteractionV2Bootstrap` MonoBehaviour created (RequireComponent InteractionOrchestrator)
- Creates platform-appropriate IIntentProvider
- Finds/creates AssemblyCameraRig on main camera
- Initializes rig from current camera transform
- Auto-disables when XR is active

---

## PHASE M3: Intent Orchestration + State Machine (Medium Risk)

### Task M3.1 — Full state machine in InteractionOrchestrator
- Implement all state transitions from architecture doc section 6
- Idle, PartHovered, PartSelected, DraggingPart, CameraOrbit, CameraPan, CameraZoom, InspectMode, UIInteraction

### Task M3.2 — LegacyBridgeAdapter ✅
- `LegacyBridgeAdapter` created — loosely typed ref to PartInteractionBridge
- Connect/Disconnect methods toggle ExternalControlEnabled via reflection
- Delegates SelectPart/GrabPart/ReleasePart through CanonicalActionBridge

### Task M3.3 — CanonicalActionBridge ✅
- `CanonicalActionBridge` created — bridges V2 events to InputActionRouter.InjectAction + SelectionService
- Methods: OnPartSelected, OnPartInspected, OnPartGrabbed, OnPartReleased, OnDeselected, OnHintRequested

### Task M3.4 — ExternalControlEnabled on PartInteractionBridge
- Add single bool field to existing PartInteractionBridge
- When true, skip Update() input polling
- Minimal change: 3-4 lines added to existing file

### Task M3.5 — Part selection via V2 orchestrator
- Update DesktopIntentProvider to emit Select/BeginDrag/ContinueDrag/EndDrag intents
- Orchestrator routes through LegacyBridgeAdapter → PartInteractionBridge public methods
- Toggle UseV2Interaction switches between old and new input path

---

## PHASE M4: Mobile Intent Provider

### Task M4.1 — MobileIntentProvider ✅
- Full implementation with touch gesture classification
- Single-finger on part = drag, on empty = orbit
- Two-finger = always camera (pan/pinch)
- Long-press = inspect, double-tap = focus/reset
- Uses EnhancedTouch for reliable multi-touch
- Commit-early: gesture classified on touch-down, locked for duration
- Two-finger override: adding second finger cancels part drag → camera

### Task M4.2 — IntentProviderResolver ✅
- Integrated into InteractionV2Bootstrap via #if UNITY_ANDROID || UNITY_IOS
- Auto-selects MobileIntentProvider on Android/iOS, DesktopIntentProvider otherwise

---

## PHASE M5: Placement Assist

### Task M5.1 — PlacementAssistService + MagneticSnapSolver ✅
- `PlacementAssistService` coordinates all placement features, toggle-gated
- `MagneticSnapSolver` with quadratic ease-in pull (0.5× position, 0.3× rotation)
- `PlacementAssistResult` struct for per-frame results
- Registered in ServiceRegistry

### Task M5.2 — PlacementCorridorSolver ✅
- `PlacementCorridorSolver` validates approach direction within configurable cone angle
- Supports custom approach direction override per target

### Task M5.3 — GhostPathRenderer
- NOT YET IMPLEMENTED — needs LineRenderer + bezier curve from part to target
- Low priority, visual-only

### Task M5.4 — AlignmentPreview
- NOT YET IMPLEMENTED — real-time position/rotation error display
- Low priority, visual-only

---

## PHASE M6: Step Guidance + Feedback

### Task M6.1 — StepGuidanceService + ViewpointLibrary ✅
- `StepGuidanceService` subscribes to StepActivated, delegates to camera rig
- `ViewpointLibrary` with Front/Side/Top/Isometric/Detail presets
- `StepViewpoint` struct for viewpoint definition
- `ApplySuggestedView(label)` for UI buttons
- `AutoFrameStepTarget(position)` for external callers

### Task M6.2 — StepViewpoint data + suggested view UI buttons
- NOT YET IMPLEMENTED — needs UI Toolkit panel with view buttons
- Depends on UIRootCoordinator integration

### Task M6.3 — ExplodedPreviewController
- NOT YET IMPLEMENTED — needs part transform management + animation
- Lower priority, advanced feature

### Task M6.4 — InteractionFeedbackPresenter + sub-feedback classes ✅
- `InteractionFeedbackPresenter` MonoBehaviour coordinator
- `InteractionFeedbackData` struct for per-frame state snapshot
- `HoverFeedback` class (cyan tint)
- `SelectionFeedback` class (yellow tint)
- UpdateFeedback() called by orchestrator each frame

---

## PHASE M7: Legacy Cleanup (Deferred — weeks later)

---

## Implementation Rules

1. **Every task must compile without errors** (test with `dotnet build` or Unity compile check)
2. **No existing file modifications until M3.4** (the one-line ExternalControlEnabled addition)
3. **All new code in `Assets/_Project/Scripts/Interaction.V2/`**
4. **Namespace: `OSE.Interaction.V2`** (or sub-namespaces like `OSE.Interaction.V2.Camera`)
5. **Test each task by entering Play mode** — existing behavior must be unchanged
6. **Update this file** after completing each task (check the box, add notes)

## Key Files to Read Before Implementing

An agent picking this up should read these files first:
1. `ose-xr-foundation/docs/INTERACTION_ARCHITECTURE_V2.md` — full architecture
2. `ose-xr-foundation/docs/APP_CURRENT_PROGRESS_FOR_AGENT.md` — project context
3. `Assets/_Project/Scripts/Core/CoreEnums.cs` — existing enums (CanonicalAction, InputContext, etc.)
4. `Assets/_Project/Scripts/Core/CoreInterfaces.cs` — existing service contracts
5. `Assets/_Project/Scripts/Core/RuntimeEvents.cs` — event bus pattern
6. `Assets/_Project/Scripts/UI/Root/PartInteractionBridge.cs` — the 1690-line file we're wrapping
7. `Assets/_Project/Scripts/Input/InputActionRouter.cs` — existing action dispatcher
8. `Assets/_Project/Scripts/Interaction/SelectionService.cs` — existing selection tracking
9. `Assets/_Project/Scripts/App/ServiceRegistry.cs` — service locator pattern
10. `Assets/_Project/Scripts/UI/Root/PreviewSceneSetup.cs` — current camera setup

## Current Progress

**Last completed task**: Full state machine + scene integration wiring (March 15, 2026)
**Phases fully implemented**: M1, M2, M3, M4, M5 (core), M6 (core)
**System status**: FUNCTIONAL — ready for scene testing
**Blocking issues**: None

### What Is Done (23 files created, 0 existing files modified)

All core systems have implementations:
- **Core**: InteractionState, InteractionIntent, InteractionSettings (SO), InteractionOrchestrator, InteractionV2Bootstrap
- **Input**: IIntentProvider, DesktopIntentProvider, MobileIntentProvider
- **Camera**: CameraState, CameraConstraintSphere, CameraSmoothing, CameraPivotResolver, AssemblyCameraRig
- **Placement**: PlacementAssistResult, MagneticSnapSolver, PlacementCorridorSolver, PlacementAssistService
- **Guidance**: StepViewpoint, ViewpointLibrary, StepGuidanceService
- **Feedback**: InteractionFeedbackData, InteractionFeedbackPresenter (+ HoverFeedback, SelectionFeedback)
- **Integration**: CanonicalActionBridge, LegacyBridgeAdapter
- **Config**: OSE.Interaction.V2.asmdef

### What Was Completed in Session 2 (March 15, 2026)

**All core systems are now functional:**
1. ✅ **M3.1 — Full state machine** in InteractionOrchestrator with all transitions (Idle, PartHovered, PartSelected, DraggingPart, CameraOrbit/Pan/Zoom, InspectMode). Camera return-to-idle when camera intent stream stops.
2. ✅ **M3.4 — `ExternalControlEnabled`** added to PartInteractionBridge (1 field + 1 guard line). Only existing file modified.
3. ✅ **M3.5 — Full wiring**: InteractionV2Bootstrap auto-discovers InputActionRouter, SelectionService, PartInteractionBridge and creates CanonicalActionBridge + LegacyBridgeAdapter.
4. ✅ **Debug context menus**: "V2 Debug/Log Current State", "V2 Debug/Reset to Idle", "V2 Debug/Toggle UseV2Interaction" on InteractionOrchestrator.
5. ✅ **Runtime initialization**: AssemblyCameraRig, InteractionFeedbackPresenter, PlacementAssistService all auto-created/wired when UseV2Interaction = true.

**Existing file changes (total: 1 file, 4 lines added):**
- `PartInteractionBridge.cs`: Added `ExternalControlEnabled` field + guard in `HandlePointerInput()`

### What Still Needs Doing

**User must do in Unity Editor (one-time setup):**
1. Create `InteractionSettings_Default.asset`: right-click `Assets/_Project/Data/Config/` → Create → OSE → Interaction Settings
2. In `Test_Assembly_Mechanics.unity`, create empty GameObject named `Interaction V2`
3. Add `InteractionOrchestrator` component → assign settings asset
4. `InteractionV2Bootstrap` auto-adds via `[RequireComponent]` → assign same settings asset
5. Set `UseV2Interaction = true` in the settings asset to test V2 path
6. With `UseV2Interaction = false` (default): everything works exactly as before

**Lower priority enhancements (visual polish):**
- M5.3 — GhostPathRenderer (LineRenderer bezier path from part to target)
- M5.4 — AlignmentPreview (real-time position/rotation error display)
- M6.2 — Suggested view UI buttons (Front/Side/Top/Iso/Detail in step panel)
- M6.3 — ExplodedPreviewController (temporary exploded view on step activate)
- DragPreviewFeedback, PlacementFeedback, CorridorFeedback sub-classes
- InteractionDebugWindow (Editor window with state/intent log)

### What to Test

With `UseV2Interaction = true`:
- **Right-drag** → camera orbits (NEW — first camera control in the app!)
- **Middle-drag** → camera pans (NEW)
- **Scroll wheel** → camera zooms (NEW)
- **F key** → focus camera on selected part (NEW)
- **Home key** → reset camera to default view (NEW)
- **Left-click on part** → selects (routed through V2 → CanonicalActionBridge → SelectionService)
- **Left-drag on part** → drags to target (routed through V2 → grab/place canonical actions)
- **Left-click empty** → deselects
- **Escape** → cancel/deselect
- **Ghost spawning, snap animation, step completion** → unchanged (PartInteractionBridge still handles these via Update sub-methods)

With `UseV2Interaction = false`:
- Everything works exactly as before. Zero behavior change.

### Setup Instructions for Next Agent

1. Read this file and `INTERACTION_ARCHITECTURE_V2.md`
2. All V2 code is in `Assets/_Project/Scripts/Interaction.V2/` (23 files)
3. Only 1 existing file modified: `PartInteractionBridge.cs` (ExternalControlEnabled field + guard)
4. The master switch is `InteractionSettings.UseV2Interaction` (defaults to false)
5. Bootstrap auto-discovers all scene systems — no manual wiring needed beyond adding components
6. Debug via right-click InteractionOrchestrator → "V2 Debug/..." context menus
