# OSE Blueprints Assembler — Architecture

## Overview

XR assembly training app. Users follow step-by-step instructions to assemble machines in 3D, guided by placement targets, tool actions, and visual feedback. All machine content is data-driven — defined in `machine.json` packages, no C# recompile required to add new machines or steps.

---

## Layer Model

```
┌─────────────────────────────────┐
│  OSE.UI          (Presentation) │  MonoBehaviours, UIToolkit, visual feedback
├─────────────────────────────────┤
│  OSE.Interaction (Application)  │  Orchestration, input routing, drag/snap
├─────────────────────────────────┤
│  OSE.Runtime     (Domain)       │  Session, step, part, tool state machines
├─────────────────────────────────┤
│  OSE.Content     (Data)         │  Definitions, loading, validation, migration
├─────────────────────────────────┤
│  OSE.Core        (Foundation)   │  Events, logging, enums, shared interfaces
└─────────────────────────────────┘
```

Dependencies flow **inward only**. UI may depend on Runtime; Runtime may never depend on UI.

---

## Bootstrap Sequence

`AppBootstrap.Awake()` registers services in this order:

1. `MachineSessionController` — top-level session orchestrator
2. `PartRuntimeController` — part placement state
3. `ToolRuntimeController` — tool action state
4. `IPlacementValidator` — snap/placement validation
5. `IPersistenceService` — save/load

Services resolve cross-dependencies in `OnEnable()` (guaranteed all registered by then).

```
AppBootstrap
  └── ServiceRegistry.Register(...)       ← Awake
        └── MachineSessionController      ← resolves deps in OnEnable
              └── StartSessionAsync()     ← Start
```

---

## ADR 001 — Step Family Handler Pattern

**Problem:** Different step types (Place, Use, Connect, Confirm) require fundamentally different pointer handling, visual feedback, and completion logic.

**Decision:** Define `IStepFamilyHandler` with a lifecycle contract. Each step family gets its own handler class. `StepExecutionRouter` routes to the active handler at runtime based on `StepDefinition.ResolvedFamily`.

```csharp
interface IStepFamilyHandler {
    void OnStepActivated(in StepHandlerContext ctx);
    bool TryHandlePointerAction(in StepHandlerContext ctx);
    bool TryHandlePointerDown(in StepHandlerContext ctx, Vector2 screenPos);
    void Update(in StepHandlerContext ctx, float deltaTime);
    void OnStepCompleted(in StepHandlerContext ctx);
    void Cleanup();
}
```

**Adding a new step family:**
1. Add enum value to `StepFamily` and `StepProfile`
2. Create `FooStepHandler : IStepFamilyHandler`
3. Register in `StepExecutionRouter.Initialize()`
4. No other files need changes

---

## ADR 002 — RuntimeEventBus

**Problem:** Systems (UI, Runtime, Interaction) need to react to each other's state changes without direct references.

**Decision:** Static type-safe event bus using struct events. Publishers fire; subscribers handle. No direct coupling between domains.

```csharp
RuntimeEventBus.Publish(new StepActivated { StepId = step.id, ... });
RuntimeEventBus.Subscribe<StepActivated>(HandleStepActivated);    // OnEnable
RuntimeEventBus.Unsubscribe<StepActivated>(HandleStepActivated);  // OnDisable
```

**Rules:**
- Events must be `struct` (no heap allocation)
- Always unsubscribe in `OnDisable` / `OnDestroy`
- Never publish from a constructor

---

## ADR 003 — Machine Package Content

**Problem:** Adding a new machine should not require C# changes.

**Decision:** All machine content lives in `machine.json` under `Assets/_Project/Data/Packages/<packageId>/`. The schema is validated on load by `MachinePackageValidator`. Schema migrations are handled by `PackageSchemaMigrator` — add a new `MigrationStep` to extend.

**Key types:**
- `MachinePackageDefinition` — root definition
- `StepDefinition` — one assembly step
- `PartDefinition` — a physical part
- `ToolDefinition` — a tool (torque wrench, clamp, etc.)
- `TargetDefinition` — a snap/placement target

**Authoring path (Editor):** `Assets/_Project/Data/Packages/<id>/machine.json`
**Runtime path (Build):** `Assets/StreamingAssets/MachinePackages/<id>/machine.json`

Never edit the StreamingAssets copy directly. The build pipeline syncs it.

---

## ADR 004 — Navigation Cooldown (Elapsed-Time)

**Problem:** After step navigation (back/forward), the pointer-down event that triggered the button can be processed in the same `Update()` tick as the navigation itself, re-completing the step immediately.

**Decision:** Record `LastNavigationTime = Time.realtimeSinceStartup` when navigation ends. `StepController.CompleteStep()` rejects completion within a 50ms window.

**Why time, not frames:** Frame-count checks are fragile under frame-rate variance and async stalls. 50ms is ~3 frames at 60 fps and imperceptible to users.

---

## ADR 005 — XRI Coupling in OSE.UI (Deferred Extraction)

**Problem:** Four files in `OSE.UI` depend directly on XR Interaction Toolkit (XRI) types (`XRGrabInteractable`, `XRBaseInteractable`, grab affordances). This violates the layer model — XRI is an input/interaction concern that belongs in `OSE.Interaction`, not presentation.

Affected files:
- `PackagePartSpawner.cs` — adds `XRGrabInteractable` + affordance components at spawn time
- `PartVisualFeedbackManager.cs` — reads `XRGrabInteractable` to gate hover visuals
- `PartInteractionBridge.cs` — queries `XRBaseInteractable` for interaction state
- `RepositionGrabProxy.cs` — bridges XRI grab events to the OSE interaction pipeline

**Decision:** Defer extraction. The coupling is load-bearing and pervasive — a partial refactor would leave the boundary worse than the current state. The correct path is:

1. Define an `IXrInteractable` abstraction in `OSE.Interaction`
2. Move `XRGrabInteractable` configuration into a new `XrGrabConfigurator` class in `OSE.Interaction`
3. `PackagePartSpawner` calls the configurator via the interface; `PartVisualFeedbackManager` and `PartInteractionBridge` query the interface
4. Remove `XRI` assembly reference from `OSE.UI.asmdef`

**Why deferred:** This is an L-effort task (~4 files, ~3 new types, integration test coverage required). It has no user-visible impact and is safe to defer until a dedicated refactor pass.

**Constraint until resolved:** Do not add new direct XRI references in `OSE.UI`. Route new XRI-dependent behavior through `OSE.Interaction` from the start.

---

## Key Files

| File | Role |
|------|------|
| `AppBootstrap.cs` | Entry point, service registration |
| `ServiceRegistry.cs` | Static service locator |
| `RuntimeEvents.cs` | All event struct definitions |
| `MachineSessionController.cs` | Top-level session orchestrator |
| `StepController.cs` | Step state machine |
| `ProgressionController.cs` | Step ordering and advancement |
| `PartRuntimeController.cs` | Part placement state |
| `ToolRuntimeController.cs` | Tool action state |
| `InteractionOrchestrator.cs` | Input → action routing |
| `PartInteractionBridge.cs` | Scene-level interaction facade |
| `IStepFamilyHandler.cs` | Step handler contract |
| `StepExecutionRouter.cs` | Routes to active step handler |
| `MachinePackageLoader.cs` | Loads and validates machine.json |
| `MachinePackageValidator.cs` | Content integrity checks |
| `PackageSchemaMigrator.cs` | Schema version upgrades |
