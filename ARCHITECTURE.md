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

## ADR 006 — OSE.Interaction Must Not Be autoReferenced

**Problem:** `OSE.Interaction.asmdef` had `autoReferenced: true`, silently granting every assembly in the project access to all `OSE.Interaction` types without an explicit declaration. This made it impossible to know which assemblies actually depend on Interaction, and bypassed the compiler-enforced dependency graph.

**Decision:** Set `autoReferenced: false`. Add explicit `OSE.Interaction` references only to the three assemblies that legitimately depend on it: `OSE.UI`, `OSE.Editor`, `OSE.Tests.PlayMode`.

**Rule:** Every new assembly that needs `OSE.Interaction` types must declare the dependency explicitly in its `.asmdef`. No exceptions.

---

## ADR 007 — StepDefinition Legacy Field Migration

**Problem:** `StepDefinition` carried both legacy flat fields (`instructionText`, `whyItMattersText`, `hintIds`, `validationRuleIds`, `effectTriggerIds`, `allowSkip`, `challengeFlags`, `completionType`) and their grouped-payload replacements (`guidance`, `validation`, `feedback`, `difficulty`). New content could accidentally use legacy fields instead of payloads, and `BuildInstructionBody()` bypassed the payload resolver entirely.

**Decision:**
1. Mark all legacy flat fields `[Obsolete]` to surface accidental use at compile time.
2. Add `#pragma warning disable CS0618` only inside the `Resolved*` accessors that intentionally read the legacy field as a fallback, and in `ResolvedFamily` which reads `completionType`.
3. Fix all runtime callers (`StepUiContentUtility`, `HintManager`, `InteractionOrchestrator`, `StepPreflightValidator`, `ToolRuntimeController`) to use `Resolved*` accessors.
4. Fix `BuildInstructionBody()` to use `ResolvedInstructionText` / `ResolvedWhyItMattersText`.
5. Test files that test the backward-compat serialization path get a file-level `#pragma warning disable CS0618` with an explanatory comment.

**Future:** When all `machine.json` files have been migrated to payload format, run `PackageSchemaMigrator` to strip the flat fields, then remove the legacy field declarations and the `[Obsolete]` attributes.

---

## ADR 008 — ServiceRegistry Over DI Framework

**Problem:** The project needs a lightweight dependency injection mechanism to connect services (session controller, tool controller, persistence) without hard-wiring concrete types across assemblies.

**Decision:** Use a hand-rolled static `ServiceRegistry` (94 lines) instead of Zenject, VContainer, or Unity's built-in DI.

**Why not Zenject/VContainer:**
- Both require a `Container`/`VContainer` MonoBehaviour in every scene. Scene management in XR builds adds complexity when lifetimes must survive scene loads.
- Reflection-based injection adds startup cost and complicates AOT/IL2CPP builds for Quest targets.
- The project has a small, stable set of services (~6 at peak); a full DI framework's binding DSL is overhead for this scale.
- `ServiceRegistry.TryGet<T>()` returns a bool — callers degrade gracefully when optional services are absent, which is the correct XR pattern (services register in `Awake`, resolve in `OnEnable`).

**Rule:** Services must be registered in `AppBootstrap.Awake()` before any consumer's `OnEnable()` runs. Never call `ServiceRegistry.Get<T>()` from a constructor.

---

## ADR 009 — External Package Choices

**Problem:** Selecting third-party packages for runtime GLB loading and procedural curve rendering.

### glTFast (com.atteneder.gltfast)
**Chosen over:** UnityGLTF, Piglet glTF Importer.
- Official Khronos-spec compliant implementation; actively maintained by Unity and Atteneder.
- Allocation-free import path — meshes streamed directly into Unity native mesh buffers.
- No managed wrappers around vertex data: correct for Quest where GC pauses cause hitching.
- Async API (`IGltfImport.LoadingDone`, `ImportGameObject`) integrates cleanly with `Task`-based loading.

### Unity Splines (com.unity.splines)
**Chosen over:** BezierSolution, custom Hermite splines.
- Built-in Unity package — no additional dependency, zero licence risk.
- `SplineContainer` + `SplineInstantiate` cover cable-routing use case with no custom tooling.
- Editor tooling (spline handles in Scene view) lets content authors adjust cable paths without code.
- Version-gated via `UNITY_SPLINES` define symbol in `OSE.UI.asmdef` so the package compiles without Splines on platforms where it's absent.

---

## ADR 010 — Cursor-Gate for "Required This Step" Visual Telegraphs

**Problem:** Multiple UI systems independently answer the question "what does the trainee need to do right now?" — tray emission pulse, ghost previews, selection highlights, step hint display, camera framing bounds. If one surface uses `step.requiredPartIds` and another uses `TaskCursor.OpenTasks`, they disagree during unordered spans and strict-sequential spans where only a subset is actionable. The disagreement IS the bug: under `step_top_place_bars` (4 sequential singletons), the tray pulsed all 4 bars as "pick me" even though only the first was cursor-open; the trainee picked bar #2, the state-machine silently rejected it, no ghost appeared, no feedback. UX looked broken because two visual systems claimed different scopes for the same signal.

**Decision:** Every visual layer that telegraphs "which required parts/actions are live *right now*" must consult `TaskCursor.OpenTasks` when a cursor exists. `step.requiredPartIds` / `step.targetIds` is a fallback only for the no-session bootstrap path.

**Canonical resolution pattern:**
```csharp
if (ServiceRegistry.TryGet<IMachineSessionController>(out var session)
    && session?.AssemblyController?.StepController?.CurrentTaskCursor is { IsComplete: false } cursor)
{
    foreach (var entry in cursor.OpenTasks)
    {
        if (entry.kind == "part") results.Add(TaskInstanceId.ToPartId(entry.id));
        // else if (entry.kind == "toolAction") resolve via step.requiredToolActions[].targetId
    }
}
else
{
    // Fallback: step.requiredPartIds / step.targetIds
}
```

**Surfaces that must follow this rule:**
- [`PlaceStepAnimator.UpdateRequiredPartPulse`](Assets/_Project/Scripts/UI/StepHandlers/PlaceStepAnimator.cs) — tray emission
- [`PreviewSpawnManager.SpawnPreviewsForCurrentSpan`](Assets/_Project/Scripts/UI/Spawning/PreviewSpawnManager.cs) — ghost previews
- [`PlaceStepAnimator.UpdatePreviewSelectionPulse`](Assets/_Project/Scripts/UI/StepHandlers/PlaceStepAnimator.cs) — selection pulses
- [`StepFocusComputer.CollectStepFocusPartIds`](Assets/_Project/Scripts/UI/StepHandlers/StepFocusComputer.cs) — camera framing bounds
- Any future guidance pointer, hint pulse, or per-step visual highlight — apply the same rule up-front.

**Why:** A behavior change that narrows the cursor's actionable scope but isn't matched on a visual surface creates a fresh variant of this bug. The invariant MUST be applied at every telegraph-drawing site.

**Enforcement:** No structural test today; adherence is by convention + code review. A future improvement is an analyzer or integration test that invokes each telegraph function with a mock cursor and asserts the scope.

---

## ADR 011 — EditorWindow Post-Reload State Recovery via Single EnsureLoaded Guard

**Problem:** `ToolTargetAuthoringWindow` (TTAW) carries serialized state (`_pkgId`) across Unity domain reloads but not live state (`_pkg`, `_targets`). Three places in the window's lifecycle defensively loaded the package when state was missing: a fresh-open fallback in `OnEnable`, a lazy retry at the top of the IMGUI callback, and an inline `LoadPkg` at the top of `OnSpawnerPartsReady`. Each had slightly different guards (`_pkg == null` vs `_targets == null`), and a race (spawner's `SpawnerPartsReady` fires before TTAW's first OnGUI) slipped through them — the tool preview GameObject would disappear after every C# compile until the author manually re-selected the task. Five consecutive commits tried to patch the race by widening guards in each block.

**Decision:** One idempotent `EnsureLoaded()` guard. Every lifecycle entry point calls it. No ad-hoc `if (_pkg == null) LoadPkg(_pkgId)` lines anywhere else.

```csharp
private bool _pendingLoadRetry;

private void EnsureLoaded()
{
    if (_pkg != null && _targets != null) { _pendingLoadRetry = false; return; }
    if (string.IsNullOrEmpty(_pkgId))     { _pendingLoadRetry = false; return; }
    try
    {
        LoadPkg(_pkgId, restoring: true);
        _pendingLoadRetry = _pkg == null || _targets == null;
    }
    catch (Exception e)
    {
        OseLog.Warn($"[TTAW.Lifecycle] EnsureLoaded threw '{e.Message}'. Will retry.");
        _pendingLoadRetry = true;
    }
}
```

**Myth this debunks:** Earlier code deferred loading to the first OnGUI "because AssetDatabase must be ready." `PackageJsonUtils.LoadPackage` uses `File.ReadAllText`, and `MachinePackageNormalizer.Normalize` is pure data — neither touches AssetDatabase. The deferral was defensive overreach that created the race window.

**Where it's called:**
- `OnEnable` — close the domain-reload race before any event fires.
- IMGUI callback (first block) — catch `_pendingLoadRetry`.
- `OnSpawnerPartsReady` — the spawner event may arrive before first OnGUI.
- Any other event handler that touches `_pkg` / `_targets`.

**Rule:** If you catch yourself writing `if (_pkg == null) LoadPkg(...)` in a new lifecycle hook, route through `EnsureLoaded()` instead. Scattered defensive blocks drift apart and regenerate this bug.

---

## ADR 012 — MaterialPropertyBlock for Per-Instance Visual Overrides

**Problem:** [`MaterialHelper.SetEmission`](Assets/_Project/Scripts/Core/MaterialHelper.cs) and `SetMaterialColor` mutated `renderer.sharedMaterials[m].SetColor(...)` directly. Because 12 frame-bar parts share one GLB prefab (one source Material asset), setting emission on one bar's renderer flashed the emission on all 12. The old code partially hid this with `bool useShared = !Application.isPlaying` — in play mode it read `renderer.materials` (cloning per-renderer, no cross-bleed) but leaked Material instances in edit mode, so the edit-mode path kept `sharedMaterials` and shipped the bug visibly.

**Decision:** Transient per-renderer visual overrides (emission color, base color tint pulse) write to a shared reusable `MaterialPropertyBlock`, never to `sharedMaterial`. The property block is per-renderer; it never mutates the underlying Material asset, so sibling instances that share the same source material are unaffected. No Material instances are leaked in either edit mode or play mode.

```csharp
private static readonly MaterialPropertyBlock s_propBlock = new MaterialPropertyBlock();

public static void SetEmission(GameObject target, Color emissionColor)
{
    foreach (var renderer in GetRenderers(target))
    {
        if (hasEmission) EnsureEmissionKeywords(renderer); // one-time keyword flag
        renderer.GetPropertyBlock(s_propBlock);
        s_propBlock.SetColor(ID_EmissionColor, emissionColor);
        s_propBlock.SetColor(ID_EmissiveFactor, emissionColor);
        renderer.SetPropertyBlock(s_propBlock);
    }
}
```

**Keyword caveat:** `MaterialPropertyBlock` cannot toggle shader keywords (`_EMISSION`, `_EMISSIVE` on URP/Lit and glTFast Shader Graph). `EnsureEmissionKeywords` enables them once on each shared material the first time that renderer wants emission. The keyword-on state has no visible effect with black emission color; color writes stay per-instance.

**What keeps `sharedMaterial` mutation:**
- Wholesale material swaps (`ApplyTint` puts a cached `tintMat` in every slot) — swap, not mutate, and cached tintMat is keyed by color so shared-across-instances is fine.
- Fallback material injection for GLBs with null material slots (one-time assignment at spawn time).

**Rule:** Any per-frame or per-interaction color/emission write must go through `MaterialPropertyBlock`. Do not call `.sharedMaterial.SetColor(...)` or mutate `renderer.sharedMaterials[i]` for transient effects. If you need a new transient override, add a property ID constant alongside `ID_BaseColor` / `ID_EmissionColor` and route through `s_propBlock`.

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
