# Interaction Architecture V2 — PC & Mobile Assembly UX

## Status: Design Document (Pre-Implementation)

**Last Updated**: March 15, 2026
**Scope**: Desktop (Mouse+Keyboard) and Mobile (Touch) interaction systems
**Out of Scope**: XR rewrite — existing XRI logic remains untouched

---

## 1. High-Level Interaction Philosophy

The application is an assembly trainer. The user's primary task is placing parts onto targets. Camera navigation is secondary — it exists to serve assembly, not compete with it.

**Core Rule**: Pointer on a part → manipulates the part. Pointer on empty space → manipulates the camera.

This rule must hold on every platform. Ambiguity between "did I mean to orbit?" and "did I mean to drag?" is the single largest UX risk. The architecture resolves this through a state machine that commits to one intent early and holds it for the gesture's duration.

**Design Principles**:

1. **Intent-first**: Raw input becomes semantic intent before any system acts on it.
2. **Commit-early**: Once a gesture is classified (drag vs orbit), it locks for the gesture duration. No mid-gesture reclassification.
3. **Additive integration**: New systems wrap or extend existing logic. Nothing is deleted until the new path is validated.
4. **Toggle-gated**: Every optional behavior has a bool gate. Disabled features have zero runtime cost.
5. **Event-driven coupling**: Systems communicate through events and queries, not direct references.

---

## 2. Simplified Core Architecture (6 Systems)

```
┌─────────────────────────────────────────────────────────┐
│                  Unity Input System                      │
│            (Mouse / Touch / Keyboard)                    │
└───────────────┬─────────────────────────────────────────┘
                │
                ▼
┌───────────────────────────┐
│   2. Input Adapter Layer  │  ← Translates raw input → InteractionIntent
│   (IntentProvider)        │
└───────────┬───────────────┘
            │ InteractionIntent
            ▼
┌───────────────────────────┐
│  1. Interaction           │  ← State machine, arbitration, routing
│     Orchestrator          │  ← Decides: part? camera? UI?
└──┬────────┬────────┬──────┘
   │        │        │
   ▼        ▼        ▼
┌──────┐ ┌──────┐ ┌──────────────┐
│  3.  │ │  4.  │ │     5.       │
│Camera│ │Place-│ │  Step        │
│ Rig  │ │ment  │ │  Guidance    │
│System│ │Assist│ │  System      │
└──┬───┘ └──┬───┘ └──────┬───────┘
   │        │             │
   └────────┼─────────────┘
            ▼
┌───────────────────────────┐
│  6. Feedback Presenter    │  ← Visual-only: highlights, ghosts, paths
└───────────────────────────┘
```

### System Ownership

| # | System | Owns | Does NOT Own |
|---|--------|------|--------------|
| 1 | Interaction Orchestrator | State machine, intent routing, conflict resolution | Low-level math, rendering |
| 2 | Input Adapter Layer | Raw input → semantic intent translation | State decisions |
| 3 | Camera Rig System | Orbit, pan, zoom, framing, constraints | Part manipulation |
| 4 | Placement Assist System | Magnetic snap, corridors, alignment validation | Camera, UI |
| 5 | Step Guidance System | Step views, auto-framing requests, exploded preview | Camera math (delegates to Camera Rig) |
| 6 | Feedback Presenter | All visual feedback rendering | Behavior decisions |

---

## 3. Recommended Unity Folder Structure

All new code lives under a new `Interaction.V2` namespace/folder, parallel to existing code. This prevents any file conflicts and makes the boundary between old and new explicit.

```
Assets/_Project/Scripts/
├── Interaction.V2/                          ← NEW — all new systems
│   ├── Core/
│   │   ├── InteractionOrchestrator.cs       ← State machine + routing
│   │   ├── InteractionState.cs              ← State enum
│   │   ├── InteractionIntent.cs             ← Intent data struct
│   │   └── InteractionSettings.cs           ← ScriptableObject toggle config
│   │
│   ├── Input/
│   │   ├── IIntentProvider.cs               ← Interface for intent sources
│   │   ├── DesktopIntentProvider.cs          ← Mouse+keyboard → intents
│   │   ├── MobileIntentProvider.cs           ← Touch → intents
│   │   └── IntentProviderResolver.cs         ← Auto-selects provider by platform
│   │
│   ├── Camera/
│   │   ├── AssemblyCameraRig.cs             ← MonoBehaviour on camera
│   │   ├── CameraState.cs                   ← Orbit params struct
│   │   ├── CameraConstraintSphere.cs        ← Clamp logic (plain C#)
│   │   ├── CameraPivotResolver.cs           ← Dynamic pivot selection
│   │   ├── CameraFramingService.cs          ← Auto-frame / focus
│   │   ├── CameraSmoothing.cs               ← Interpolation utility
│   │   └── OrbitGizmoController.cs          ← Optional gizmo overlay
│   │
│   ├── Placement/
│   │   ├── PlacementAssistService.cs        ← Coordinator for placement features
│   │   ├── MagneticSnapSolver.cs            ← Proximity-based magnetic pull
│   │   ├── PlacementCorridorSolver.cs       ← Direction-aware approach validation
│   │   ├── GhostPathRenderer.cs             ← Visual path from part to target
│   │   └── AlignmentPreview.cs              ← Live alignment feedback data
│   │
│   ├── Guidance/
│   │   ├── StepGuidanceService.cs           ← Step-aware camera/view guidance
│   │   ├── StepViewpoint.cs                 ← Viewpoint definition struct
│   │   ├── ViewpointLibrary.cs              ← Standard views (front/side/iso)
│   │   └── ExplodedPreviewController.cs     ← Temporary exploded view
│   │
│   ├── Feedback/
│   │   ├── InteractionFeedbackPresenter.cs  ← MonoBehaviour coordinator
│   │   ├── HoverFeedback.cs                 ← Hover highlight logic
│   │   ├── SelectionFeedback.cs             ← Selection outline/color
│   │   ├── DragPreviewFeedback.cs           ← Drag ghost/trail
│   │   ├── PlacementFeedback.cs             ← Valid/invalid placement visuals
│   │   └── CorridorFeedback.cs              ← Corridor/path rendering
│   │
│   ├── Integration/
│   │   ├── LegacyBridgeAdapter.cs           ← Wraps existing PartInteractionBridge
│   │   ├── CanonicalActionBridge.cs         ← Connects V2 intents ↔ CanonicalAction
│   │   └── SelectionServiceBridge.cs        ← Syncs V2 selection ↔ SelectionService
│   │
│   └── Editor/
│       ├── InteractionSettingsInspector.cs   ← Custom inspector for toggles
│       └── InteractionDebugWindow.cs         ← Debug overlay for state/intents
│
├── Input/                                    ← EXISTING (untouched)
│   ├── InputActionRouter.cs
│   ├── DesktopMouseKeyboardInputAdapter.cs
│   └── MobileTouchInputAdapter.cs
│
├── Interaction/                              ← EXISTING (untouched)
│   ├── SelectionService.cs
│   ├── XRIInteractionAdapter.cs
│   └── XRRigModeSwitcher.cs
│
├── UI/Root/                                  ← EXISTING
│   ├── PartInteractionBridge.cs              ← Stays as-is during migration
│   ├── PackagePartSpawner.cs
│   ├── PreviewSceneSetup.cs
│   └── MaterialHelper.cs
│
└── ... (all other existing folders unchanged)
```

---

## 4. Recommended Class Structure

### 4.1 Interaction Orchestrator

```csharp
// InteractionState.cs — plain C# enum
public enum InteractionState
{
    Idle,
    PartHovered,
    PartSelected,
    DraggingPart,
    CameraOrbit,
    CameraPan,
    CameraZoom,
    InspectMode,
    UIInteraction
}
```

```csharp
// InteractionIntent.cs — plain C# struct
public readonly struct InteractionIntent
{
    public enum Kind
    {
        None,
        Select,          // Tap/click on target
        BeginDrag,       // Pointer down + moved threshold
        ContinueDrag,    // Pointer held + moving
        EndDrag,         // Pointer up after drag
        Orbit,           // Right-click drag / single-finger empty
        Pan,             // Middle-click drag / two-finger drag
        Zoom,            // Scroll / pinch
        Focus,           // F key / double-tap
        ResetView,       // Home key / shake gesture
        Inspect,         // Alt+click / long-press
        Cancel           // Escape / back
    }

    public Kind IntentKind { get; }
    public Vector2 ScreenPosition { get; }
    public Vector2 ScreenDelta { get; }
    public float ScrollDelta { get; }
    public float PinchDelta { get; }
    public GameObject HitTarget { get; }     // From raycast (null = empty space)
    public bool IsOverUI { get; }
}
```

```csharp
// InteractionOrchestrator.cs — MonoBehaviour (scene-bound, one per scene)
public sealed class InteractionOrchestrator : MonoBehaviour
{
    [SerializeField] private InteractionSettings _settings;

    // Dependencies (resolved via ServiceRegistry or serialized)
    private IIntentProvider _intentProvider;
    private AssemblyCameraRig _cameraRig;
    private PlacementAssistService _placementAssist;
    private StepGuidanceService _stepGuidance;
    private InteractionFeedbackPresenter _feedback;

    // State
    public InteractionState CurrentState { get; private set; }
    public GameObject HoveredPart { get; private set; }
    public GameObject SelectedPart { get; private set; }

    // Bridge to existing systems
    private LegacyBridgeAdapter _legacyBridge;

    private void Update()
    {
        var intent = _intentProvider.Poll();
        ProcessIntent(intent);
    }

    private void ProcessIntent(InteractionIntent intent) { /* state machine */ }
}
```

### 4.2 Input Adapter Layer

```csharp
// IIntentProvider.cs — plain C# interface
public interface IIntentProvider
{
    InteractionIntent Poll();
    bool IsActive { get; }
}
```

```csharp
// DesktopIntentProvider.cs — plain C# class (no MonoBehaviour needed)
public sealed class DesktopIntentProvider : IIntentProvider
{
    private readonly Camera _camera;
    private readonly LayerMask _partLayer;
    private readonly float _dragThreshold;

    // Tracks pointer state for commit-early gesture classification
    private Vector2 _pointerDownPosition;
    private bool _pointerDown;
    private bool _gestureClassified;
    private InteractionIntent.Kind _classifiedGesture;

    public InteractionIntent Poll() { /* read Mouse.current, Keyboard.current */ }
}
```

```csharp
// MobileIntentProvider.cs — plain C# class
public sealed class MobileIntentProvider : IIntentProvider
{
    // Tracks finger count, pinch state, gesture classification
    public InteractionIntent Poll() { /* read Touchscreen.current */ }
}
```

```csharp
// IntentProviderResolver.cs — plain C# factory
public static class IntentProviderResolver
{
    public static IIntentProvider Create(Camera camera, InteractionSettings settings)
    {
        #if UNITY_EDITOR
        return new DesktopIntentProvider(camera, settings);
        #elif UNITY_ANDROID || UNITY_IOS
        return new MobileIntentProvider(camera, settings);
        #else
        return new DesktopIntentProvider(camera, settings);
        #endif
    }
}
```

### 4.3 Camera Rig System

```csharp
// CameraState.cs — plain C# struct
public struct CameraState
{
    public Vector3 PivotPosition;
    public float Yaw;        // Horizontal angle (degrees)
    public float Pitch;      // Vertical angle (degrees)
    public float Distance;   // Distance from pivot
}
```

```csharp
// AssemblyCameraRig.cs — MonoBehaviour (on camera or camera parent)
public sealed class AssemblyCameraRig : MonoBehaviour
{
    [SerializeField] private InteractionSettings _settings;

    private CameraState _currentState;
    private CameraState _targetState;
    private CameraPivotResolver _pivotResolver;
    private CameraConstraintSphere _constraint;
    private CameraSmoothing _smoothing;

    // Public API called by Orchestrator
    public void ApplyOrbit(Vector2 delta);
    public void ApplyPan(Vector2 delta);
    public void ApplyZoom(float delta);
    public void FocusOn(Vector3 worldPosition, float distance);
    public void FrameBounds(Bounds bounds);
    public void SetPivot(Vector3 position);
    public void ResetToDefault();
    public void ApplyViewpoint(StepViewpoint viewpoint, bool animated);

    private void LateUpdate()
    {
        // Smooth interpolation + constraint enforcement
        _currentState = _smoothing.Step(_currentState, _targetState, Time.deltaTime);
        _currentState = _constraint.Clamp(_currentState);
        ApplyStateToTransform(_currentState);
    }
}
```

```csharp
// CameraConstraintSphere.cs — plain C# class
public sealed class CameraConstraintSphere
{
    public float MinDistance { get; set; }
    public float MaxDistance { get; set; }
    public float MinPitch { get; set; }   // e.g. -10° (can't go underground)
    public float MaxPitch { get; set; }   // e.g. 85° (can't flip over top)

    public CameraState Clamp(CameraState state) { /* enforce limits */ }
}
```

```csharp
// CameraPivotResolver.cs — plain C# class
public sealed class CameraPivotResolver
{
    public enum PivotSource { AssemblyCenter, SelectedPart, GhostTarget, StepTarget, Custom }

    public Vector3 ResolvePivot(PivotSource source, InteractionOrchestrator orchestrator)
    {
        return source switch
        {
            PivotSource.SelectedPart => orchestrator.SelectedPart?.transform.position ?? FallbackCenter(),
            PivotSource.GhostTarget => ResolveGhostTarget(),
            PivotSource.StepTarget => ResolveStepTarget(),
            PivotSource.AssemblyCenter => ComputeAssemblyCenter(),
            _ => FallbackCenter()
        };
    }
}
```

```csharp
// CameraFramingService.cs — plain C# class
public sealed class CameraFramingService
{
    public CameraState ComputeFramingState(Bounds targetBounds, Camera camera, float padding);
    public CameraState ComputeFocusState(Vector3 point, float viewDistance, CameraState current);
}
```

### 4.4 Placement Assist System

```csharp
// PlacementAssistService.cs — plain C# class (registered in ServiceRegistry)
public sealed class PlacementAssistService
{
    private readonly InteractionSettings _settings;
    private readonly MagneticSnapSolver _magneticSnap;
    private readonly PlacementCorridorSolver _corridorSolver;

    // Called every frame while dragging
    public PlacementAssistResult Evaluate(
        Vector3 partPosition,
        Quaternion partRotation,
        string partId,
        string targetId,
        Vector3 targetPosition,
        Quaternion targetRotation,
        float positionTolerance)
    {
        var result = new PlacementAssistResult { RawPosition = partPosition };

        if (_settings.EnableMagneticPlacement)
            result = _magneticSnap.Apply(result, targetPosition, targetRotation, positionTolerance);

        if (_settings.EnablePlacementCorridors)
            result.IsInCorridor = _corridorSolver.Evaluate(partPosition, targetPosition, targetRotation);

        return result;
    }
}
```

```csharp
// MagneticSnapSolver.cs — plain C# class
public sealed class MagneticSnapSolver
{
    // When part enters magnetic radius (larger than snap radius),
    // gradually pull it toward the target position.
    // Strength increases as distance decreases.
    public PlacementAssistResult Apply(
        PlacementAssistResult current,
        Vector3 targetPos,
        Quaternion targetRot,
        float tolerance)
    {
        float dist = Vector3.Distance(current.RawPosition, targetPos);
        float magneticRadius = tolerance * 2f;  // Magnetic field is 2x snap radius

        if (dist > magneticRadius) return current;

        float t = 1f - (dist / magneticRadius);  // 0 at edge, 1 at center
        t = t * t;  // Ease-in curve — gentle at edge, strong near target

        current.AssistedPosition = Vector3.Lerp(current.RawPosition, targetPos, t * 0.5f);
        current.AssistedRotation = Quaternion.Slerp(
            Quaternion.identity, targetRot, t * 0.3f);
        current.MagneticStrength = t;
        current.IsInMagneticField = true;

        return current;
    }
}
```

```csharp
// PlacementCorridorSolver.cs — plain C# class
public sealed class PlacementCorridorSolver
{
    public float CorridorHalfAngle { get; set; } = 45f;  // degrees

    // Returns true if the part is approaching the target from a valid direction.
    // "Valid" means the approach vector is within CorridorHalfAngle of the
    // target's expected approach direction (typically target's local up or forward).
    public bool Evaluate(Vector3 partPos, Vector3 targetPos, Quaternion targetRot)
    {
        Vector3 approachDir = (partPos - targetPos).normalized;
        Vector3 expectedDir = targetRot * Vector3.up;  // configurable per-target

        float angle = Vector3.Angle(approachDir, expectedDir);
        return angle <= CorridorHalfAngle;
    }
}
```

### 4.5 Step Guidance System

```csharp
// StepViewpoint.cs — plain C# struct
public struct StepViewpoint
{
    public string Label;        // "Front", "Side", "Detail"
    public float Yaw;
    public float Pitch;
    public float Distance;
    public Vector3 PivotOffset; // Relative to step target
}
```

```csharp
// ViewpointLibrary.cs — plain C# static class
public static class ViewpointLibrary
{
    public static StepViewpoint Front => new() { Label = "Front", Yaw = 0, Pitch = 15, Distance = 1.5f };
    public static StepViewpoint Side  => new() { Label = "Side",  Yaw = 90, Pitch = 15, Distance = 1.5f };
    public static StepViewpoint Top   => new() { Label = "Top",   Yaw = 0, Pitch = 80, Distance = 2.0f };
    public static StepViewpoint Iso   => new() { Label = "Iso",   Yaw = 45, Pitch = 35, Distance = 1.8f };
    // Detail: closer view focused on connection point
    public static StepViewpoint Detail => new() { Label = "Detail", Yaw = 30, Pitch = 25, Distance = 0.8f };
}
```

```csharp
// StepGuidanceService.cs — plain C# class (registered in ServiceRegistry)
public sealed class StepGuidanceService
{
    private readonly InteractionSettings _settings;
    private readonly AssemblyCameraRig _cameraRig;

    // Subscribed to RuntimeEventBus.StepActivated
    public void OnStepActivated(StepActivated evt)
    {
        if (!_settings.EnableStepViewGuidance) return;

        // Look up recommended viewpoint from step data or compute default
        var viewpoint = ResolveViewpoint(evt.StepId);

        if (_settings.EnableAutoFraming)
            _cameraRig.ApplyViewpoint(viewpoint, animated: true);
    }

    // Called by UI when user taps a suggested view button
    public void ApplySuggestedView(string viewLabel)
    {
        var vp = viewLabel switch
        {
            "Front" => ViewpointLibrary.Front,
            "Side" => ViewpointLibrary.Side,
            "Top" => ViewpointLibrary.Top,
            "Iso" => ViewpointLibrary.Iso,
            "Detail" => ViewpointLibrary.Detail,
            _ => ViewpointLibrary.Front
        };
        _cameraRig.ApplyViewpoint(vp, animated: true);
    }
}
```

```csharp
// ExplodedPreviewController.cs — MonoBehaviour (manages part transforms temporarily)
public sealed class ExplodedPreviewController : MonoBehaviour
{
    private readonly Dictionary<GameObject, Vector3> _originalPositions = new();
    private bool _isExploded;

    public void ShowExploded(List<GameObject> parts, Vector3 center, float spread);
    public void RestorePositions();
}
```

### 4.6 Feedback Presenter

```csharp
// InteractionFeedbackPresenter.cs — MonoBehaviour (scene-bound coordinator)
public sealed class InteractionFeedbackPresenter : MonoBehaviour
{
    [SerializeField] private InteractionSettings _settings;

    private HoverFeedback _hover;
    private SelectionFeedback _selection;
    private DragPreviewFeedback _dragPreview;
    private PlacementFeedback _placement;
    private CorridorFeedback _corridor;

    // Called by Orchestrator each frame with current state
    public void UpdateFeedback(InteractionState state, InteractionFeedbackData data);
}
```

```csharp
// InteractionFeedbackData.cs — plain C# struct
public struct InteractionFeedbackData
{
    public GameObject HoveredPart;
    public GameObject SelectedPart;
    public GameObject DraggedPart;
    public Vector3? MagneticTargetPosition;
    public float MagneticStrength;        // 0–1
    public bool IsInCorridor;
    public bool IsValidPlacement;
    public Vector3? GhostPathStart;
    public Vector3? GhostPathEnd;
}
```

Each sub-feedback class (`HoverFeedback`, `SelectionFeedback`, etc.) is a plain C# class that receives the data and applies visuals. They are not MonoBehaviours — the presenter owns their lifecycle.

---

## 5. Responsibility Breakdown per Class

| Class | Type | Responsibility | References | Referenced By |
|-------|------|---------------|------------|---------------|
| `InteractionOrchestrator` | MonoBehaviour | State machine, intent routing, conflict resolution | IIntentProvider, AssemblyCameraRig, PlacementAssistService, InteractionFeedbackPresenter, LegacyBridgeAdapter | Scene root |
| `InteractionSettings` | ScriptableObject | All feature toggles and tuning values | Nothing | Everything reads it |
| `InteractionState` | Enum | State definitions | Nothing | Orchestrator, Feedback |
| `InteractionIntent` | Struct | Semantic input data | Nothing | IntentProviders → Orchestrator |
| `IIntentProvider` | Interface | Contract for intent sources | Nothing | Orchestrator |
| `DesktopIntentProvider` | Plain C# | Mouse+KB → intents | Camera, Mouse, Keyboard | Orchestrator (via interface) |
| `MobileIntentProvider` | Plain C# | Touch → intents | Camera, Touchscreen | Orchestrator (via interface) |
| `IntentProviderResolver` | Static C# | Factory for platform intent provider | Settings | Orchestrator initialization |
| `AssemblyCameraRig` | MonoBehaviour | Camera orbit/pan/zoom/frame | CameraConstraintSphere, CameraPivotResolver, CameraSmoothing | Orchestrator, StepGuidanceService |
| `CameraState` | Struct | Camera orbit parameters | Nothing | CameraRig internals |
| `CameraConstraintSphere` | Plain C# | Clamp camera to valid range | Nothing | AssemblyCameraRig |
| `CameraPivotResolver` | Plain C# | Dynamic pivot selection | Orchestrator (for selected part) | AssemblyCameraRig |
| `CameraFramingService` | Plain C# | Compute framing for bounds/points | Camera | AssemblyCameraRig, StepGuidance |
| `CameraSmoothing` | Plain C# | Interpolation utility | Nothing | AssemblyCameraRig |
| `OrbitGizmoController` | MonoBehaviour | Optional gizmo overlay | AssemblyCameraRig | UI layer |
| `PlacementAssistService` | Plain C# | Coordinate placement features | MagneticSnapSolver, CorridorSolver, Settings | Orchestrator |
| `MagneticSnapSolver` | Plain C# | Proximity-based pull toward target | Nothing | PlacementAssistService |
| `PlacementCorridorSolver` | Plain C# | Direction-aware approach validation | Nothing | PlacementAssistService |
| `GhostPathRenderer` | MonoBehaviour | Visual path from part to target | LineRenderer | FeedbackPresenter |
| `StepGuidanceService` | Plain C# | Step-aware camera/view requests | AssemblyCameraRig, RuntimeEventBus, Settings | Orchestrator, UI |
| `ViewpointLibrary` | Static C# | Standard view definitions | Nothing | StepGuidanceService |
| `ExplodedPreviewController` | MonoBehaviour | Temporary exploded view | Part transforms | StepGuidanceService |
| `InteractionFeedbackPresenter` | MonoBehaviour | Coordinates all visual feedback | Sub-feedback classes, MaterialHelper | Orchestrator |
| `LegacyBridgeAdapter` | Plain C# | Wraps existing PartInteractionBridge | PartInteractionBridge, SelectionService | Orchestrator |
| `CanonicalActionBridge` | Plain C# | Syncs V2 intents ↔ CanonicalAction | InputActionRouter | Orchestrator |
| `SelectionServiceBridge` | Plain C# | Syncs V2 selection ↔ SelectionService | SelectionService | Orchestrator |

---

## 6. Interaction State Machine

```
                    ┌──────────────────────────────────────────────┐
                    │                                              │
                    ▼                                              │
              ┌──────────┐                                         │
         ┌───▶│   Idle   │◀──── Cancel / Deselect ────────────────┤
         │    └────┬─────┘                                         │
         │         │                                               │
         │         │ Raycast hit?                                  │
         │         │                                               │
         │    ┌────┴──────────────────┐                            │
         │    │                       │                            │
         │    ▼ (hit part)            ▼ (hit empty)               │
         │  ┌──────────────┐    ┌─────────────────┐               │
         │  │ Part Hovered │    │ Begin camera     │               │
         │  └──────┬───────┘    │ gesture classify │               │
         │         │            └───┬────┬────┬────┘               │
         │    Tap? │ Drag?         │    │    │                     │
         │    ┌────┴────┐     Orbit│ Pan│ Zoom│                    │
         │    ▼         ▼         ▼    ▼    ▼                     │
         │ ┌──────┐ ┌────────┐ ┌─────┐ ┌───┐ ┌────┐             │
         │ │Select│ │Dragging│ │Orbit│ │Pan│ │Zoom│              │
         │ │ Part │ │  Part  │ └──┬──┘ └─┬─┘ └──┬─┘              │
         │ └──┬───┘ └───┬────┘    │      │      │                │
         │    │         │         └──────┴──────┘                 │
         │    │    pointer up          pointer up                  │
         │    │    ┌────┴────┐              │                      │
         │    │    ▼ valid?  ▼ invalid?     │                      │
         │    │  [Snap]   [Flash]           │                      │
         │    │    │         │              │                      │
         │    └────┴─────────┴──────────────┘                      │
         │                                                         │
         └─────────────────────────────────────────────────────────┘
```

### State Transitions

| From | Trigger | To | Guard |
|------|---------|-----|-------|
| Idle | Pointer over part | PartHovered | Raycast hit on part layer |
| PartHovered | Pointer leaves part | Idle | No raycast hit |
| PartHovered | Tap/click | PartSelected | — |
| PartHovered | Drag threshold exceeded | DraggingPart | Part is Available/Selected |
| PartSelected | Tap empty space | Idle | — |
| PartSelected | Drag on selected part | DraggingPart | — |
| PartSelected | F key / double-tap | InspectMode | — |
| DraggingPart | Pointer up (valid) | PartSelected → step advance | Within snap tolerance |
| DraggingPart | Pointer up (invalid) | PartSelected | Outside tolerance |
| Idle | Right-drag / 1-finger empty | CameraOrbit | Pointer on empty space |
| Idle | Middle-drag / 2-finger drag | CameraPan | — |
| Idle | Scroll / pinch | CameraZoom | — |
| CameraOrbit | Pointer up | Idle | — |
| CameraPan | Pointer up / fingers lift | Idle | — |
| CameraZoom | Scroll stop / pinch end | Idle | — |
| InspectMode | Escape / tap | PartSelected | — |
| Any | UIInteraction detected | UIInteraction | EventSystem reports UI hit |
| UIInteraction | Pointer up outside UI | Idle | — |

### Critical Rule: No Camera During Drag

When `CurrentState == DraggingPart`, all camera intents are suppressed. The orchestrator simply does not route Orbit/Pan/Zoom intents in this state. This is enforced at the state machine level, not at the input level — input still generates intents, but the orchestrator drops them.

---

## 7. PC Input Model

### Input Mapping

| Input | Action | Context |
|-------|--------|---------|
| **Left-click** on part | Select part | Always |
| **Left-drag** on part | Drag part (after 5px threshold) | Part is selectable |
| **Left-click** on empty | Deselect | Part is selected |
| **Right-drag** anywhere | Orbit camera | Not dragging a part |
| **Middle-drag** anywhere | Pan camera | Not dragging a part |
| **Scroll wheel** | Zoom camera | Not dragging a part |
| **Scroll wheel** while dragging | Adjust part depth | Dragging a part |
| **Shift+drag** while dragging | Adjust part depth (vertical) | Dragging a part |
| **F** key | Focus camera on selected part / step target | Part selected or step active |
| **Home** key | Reset camera to default view | Always |
| **1-5** keys | Apply suggested viewpoint | Step active, StepViewGuidance enabled |
| **Escape** | Cancel / deselect / exit inspect | Context-dependent |
| **Alt+click** on part | Inspect part (detailed info) | Part is visible |

### Why This Model Works

- **Left button = interact with what's under cursor.** This is the universal desktop convention. Part under cursor → manipulate part. Empty space → deselect.
- **Right button = camera.** This matches 3D modeling tools (Blender, Maya), CAD tools, and most 3D viewers. Users who work with 3D content expect right-drag = orbit.
- **Middle button = pan.** Same convention as above. Consistent with professional 3D tools.
- **Scroll = zoom.** Universal expectation.
- **No modifier keys required for basic interaction.** Select and drag are unmodified left-click. Camera is unmodified right-click. No Ctrl/Alt/Shift needed for core workflows.
- **Scroll while dragging = depth.** This repurposes scroll contextually — when not dragging, it zooms the camera. When dragging, it pushes/pulls the part. This avoids needing a separate depth control UI.

### Gesture Classification (Commit-Early)

On left pointer-down:
1. Raycast from cursor position.
2. If hit is on a part → prepare for Select or Drag.
3. If pointer moves > 5px before pointer-up → classify as Drag (commit).
4. If pointer-up before 5px → classify as Select (tap).
5. Once classified, the gesture is locked. A drag cannot become a select mid-gesture.

On right pointer-down:
1. Always → prepare for Orbit (commit immediately on first delta).
2. No raycast needed — right-drag is always camera.

---

## 8. Mobile Input Model

### Input Mapping

| Gesture | Object Under Finger | Empty Space | While Dragging Part |
|---------|---------------------|-------------|---------------------|
| **Tap** | Select part | Deselect | — |
| **Single-finger drag** | Drag part | Orbit camera | Move part |
| **Two-finger drag** | Pan camera | Pan camera | — (ignored) |
| **Pinch** | Zoom camera | Zoom camera | Adjust part depth |
| **Long-press** (0.4s) | Inspect part | — | — |
| **Double-tap** | Focus on part | Reset camera | — |

### How Gesture Conflicts Are Prevented

**The single-finger drag is the critical conflict zone.** On desktop, left-click selects/drags parts and right-click orbits — two separate buttons, no conflict. On mobile, there's only one finger for both.

**Resolution: Raycast on touch-down decides the gesture.**

```
Touch Down
    │
    ├─ Raycast hits part? ──► Single-finger = DRAG PART
    │                         (camera is locked, no orbit)
    │
    └─ Raycast hits empty? ──► Single-finger = ORBIT CAMERA
                               (no part interaction)
```

Once classified at touch-down, the gesture is locked for its entire duration. This prevents the frustrating "I meant to orbit but I grabbed a part" problem.

**Two-finger gestures are always camera** regardless of what's under either finger. If a user puts down one finger on a part and then adds a second finger, the gesture reclassifies to camera (pan/pinch). This is consistent with iOS/Android map conventions.

**Specific conflict scenarios:**

| Scenario | What Happens | Why |
|----------|-------------|-----|
| Finger down on part, moves | Part drags | Committed on touch-down |
| Finger down on empty, moves | Camera orbits | Committed on touch-down |
| Finger on part, second finger added | Cancels part drag → camera pan/zoom | Two-finger always = camera |
| Pinch while dragging part | Adjusts part depth (push/pull) | Contextual repurpose |
| Tap on part | Selects part | No drag threshold exceeded |
| Tap on empty | Deselects | No drag threshold exceeded |

**Why this works for low precision:**
- No modifier keys or hold-to-orbit needed.
- No ambiguous "was that a tap or a drag?" — 8px threshold resolves quickly.
- Camera access never requires first tapping empty space — just touch empty space and drag.
- Two-finger is always camera, so camera pan/zoom is always available regardless of what's under the fingers.

---

## 9. Camera Rig Design

### Core Architecture

The camera operates on a **constraint sphere** model: the camera position is always computed as:

```
cameraPosition = pivotPosition + sphericalToCartesian(yaw, pitch, distance)
```

This means the camera is always on the surface of a sphere centered on the pivot. Orbit changes yaw/pitch. Zoom changes distance. Pan moves the pivot.

### Constraint Sphere

```csharp
public sealed class CameraConstraintSphere
{
    // Distance limits
    public float MinDistance = 0.3f;    // Can't zoom closer than this
    public float MaxDistance = 10.0f;   // Can't zoom farther than this

    // Vertical angle limits
    public float MinPitch = -10f;      // Slightly below horizon (prevents underground)
    public float MaxPitch = 85f;       // Near-top-down (prevents flip)

    // Collision (optional)
    public bool EnableCollision = false;
    public LayerMask CollisionMask;

    public CameraState Clamp(CameraState state)
    {
        state.Distance = Mathf.Clamp(state.Distance, MinDistance, MaxDistance);
        state.Pitch = Mathf.Clamp(state.Pitch, MinPitch, MaxPitch);
        // Yaw wraps naturally (0-360)

        if (EnableCollision)
            state.Distance = ResolveCollision(state);

        return state;
    }
}
```

### Dynamic Pivot

The pivot is not fixed at world origin. It updates based on context:

| Context | Pivot Position | When |
|---------|---------------|------|
| Default | Assembly bounds center | No selection, no active step |
| Step active | Step target position | Step becomes active (if EnableAutoFraming) |
| Part selected | Part world position | User selects a part |
| Ghost target | Ghost target position | User starts dragging toward ghost |
| Focus command | Explicitly specified | User presses F or double-taps |

Pivot changes are **animated** (smooth transition) to avoid jarring camera jumps. The `CameraSmoothing` utility handles this:

```csharp
public sealed class CameraSmoothing
{
    public float OrbitSmoothing = 8f;    // Responsiveness for orbit
    public float PanSmoothing = 8f;
    public float ZoomSmoothing = 6f;
    public float PivotSmoothing = 4f;    // Slower for pivot transitions

    public CameraState Step(CameraState current, CameraState target, float dt)
    {
        return new CameraState
        {
            Yaw = Mathf.LerpAngle(current.Yaw, target.Yaw, OrbitSmoothing * dt),
            Pitch = Mathf.Lerp(current.Pitch, target.Pitch, OrbitSmoothing * dt),
            Distance = Mathf.Lerp(current.Distance, target.Distance, ZoomSmoothing * dt),
            PivotPosition = Vector3.Lerp(current.PivotPosition, target.PivotPosition, PivotSmoothing * dt)
        };
    }
}
```

### Camera Assist Features (Toggle-Gated)

| Feature | Toggle | Behavior |
|---------|--------|----------|
| Auto-frame step | `EnableAutoFraming` | On step activate, frame the step target |
| Focus selected | `EnableSmartPivot` | On part select, shift pivot to part |
| Visibility correction | `EnableVisibilitySolver` | If selected part is occluded, nudge camera |
| Suggested views | `EnableSuggestedViews` | Show view buttons in UI (Front/Side/Top/Iso/Detail) |
| Orbit gizmo | `EnableOrbitGizmo` | Show draggable orbit ring on screen |
| Vision Pro mode | `EnableVisionProInteractionModel` | Guided camera: auto-orbit, reduced manual control |
| Constraint sphere | `EnableCameraConstraintSphere` | Enforce min/max distance and pitch limits |

### Performance Note

Camera computation runs in `LateUpdate` (after all transforms are finalized). The sphere position calculation is trivial — one sin/cos pair. Smoothing is one lerp per axis. No per-frame raycasts unless collision avoidance is enabled.

---

## 10. Placement Assist Design

### Existing System (Preserved)

The current `PartInteractionBridge` already handles:
- Ghost part spawning at target positions
- Snap zone detection (`SnapZoneRadius = 0.8f`)
- Snap animation (lerp to target)
- Invalid placement flash
- Multi-target step completion gating

**These stay as-is during migration.** The new Placement Assist System sits alongside and is consulted by the orchestrator when its toggles are enabled.

### New Capabilities

#### Magnetic Placement (`EnableMagneticPlacement`)

Extends the existing snap zone with a larger "magnetic field" zone. When the dragged part enters the magnetic radius (2× snap radius), it gets a gentle pull toward the target. The pull strength increases as the part gets closer, following a quadratic ease-in curve.

This helps users on mobile who lack precision. The part "wants" to go to the right place.

```
Magnetic field visualization:

         ┌─── Magnetic radius (2× snap) ───┐
         │                                   │
         │     ┌── Snap radius (1×) ──┐     │
         │     │                       │     │
         │     │    ┌─ Target ─┐      │     │
         │     │    │   ████   │      │     │
         │     │    └──────────┘      │     │
         │     │   Strong pull (t²)   │     │
         │     └───────────────────────┘     │
         │       Gentle pull (t²)            │
         └───────────────────────────────────┘
           No effect outside this radius
```

#### Placement Corridors (`EnablePlacementCorridors`)

Some parts should only be placed from a specific direction (e.g., a bolt goes in from above, not from the side). The corridor solver validates the approach direction:

- Define an expected approach direction per target (stored in package data or defaulted to local up).
- When the part is within the snap zone, check if the approach angle is within the corridor half-angle.
- If outside the corridor → placement is invalid even if position is correct.
- Visual feedback shows the corridor as a translucent cone.

#### Ghost Path Guidance (`EnableGhostPathGuidance`)

Shows a visual path (curved line or arrow) from the selected part to its ghost target. This helps users understand where the part needs to go, especially when the target is off-screen or behind other parts.

Implementation: `GhostPathRenderer` uses a `LineRenderer` with a bezier curve between part position and target position. The midpoint is elevated to create an arc. The path fades as the part gets closer.

#### Alignment Preview (`AlignmentPreview`)

While dragging, shows real-time alignment feedback:
- Position error (distance to target)
- Rotation error (angle to target orientation)
- Combined validity indicator

This data feeds into `PlacementFeedback` for visual rendering.

### Integration with Existing Snap Logic

The placement assist system does **not** replace existing snap validation. Instead:

1. During drag, the orchestrator calls `PlacementAssistService.Evaluate()` each frame.
2. The result adjusts the dragged part's visual position (magnetic pull).
3. On release, the existing `PlacementValidator` still performs the final validation.
4. If corridors are enabled, corridor validity is ANDed with position validity.

This is purely additive — disable the toggles and you get exactly the current behavior.

---

## 11. Step Guidance Design

### Existing System (Preserved)

The current system has:
- `StepActivated` events via RuntimeEventBus
- Step panel showing step instructions
- Hint system with world bubble
- Session HUD with progress bar

### New Capabilities

#### Step-Driven View Guidance (`EnableStepViewGuidance`)

When a step activates, the guidance service can:
1. Compute a recommended viewpoint based on the step's target position and the assembly geometry.
2. Optionally auto-frame the camera to that viewpoint (`EnableAutoFraming`).
3. Show suggested view buttons in the UI (Front / Side / Top / Iso / Detail) that users can tap.

**Data source**: Step definitions in the package JSON could include an optional `recommendedViewpoint` field. If absent, the system computes a default based on target position relative to assembly bounds.

#### Exploded Step Preview (`EnableExplodedStepPreview`)

Before the user begins interacting with a step, temporarily push surrounding parts outward from the connection point to reveal the assembly structure. This is a common technique in industrial training (NASA assembly guides, Boeing maintenance manuals).

Implementation:
1. On step activate, if enabled, compute explosion vectors for nearby completed parts.
2. Animate parts outward over 0.5s.
3. Hold for 2s (or until user taps).
4. Animate parts back to assembled positions.
5. Resume normal interaction.

The explosion center is the step's target position. Explosion distance scales with part proximity — closer parts move more.

#### Suggested Viewpoints (`EnableSuggestedViews`)

The UI presents small view buttons (Front / Side / Top / Iso / Detail) that apply pre-defined camera orientations relative to the current step target. Each button calls `StepGuidanceService.ApplySuggestedView(label)` → `AssemblyCameraRig.ApplyViewpoint()`.

This reduces the need for manual camera navigation, especially on mobile where orbit/pan is harder.

---

## 12. Feedback Presentation Design

### Architecture

The Feedback Presenter is **presentation-only**. It receives state from the orchestrator and renders visuals. It never decides behavior.

```
Orchestrator.CurrentState + InteractionFeedbackData
              │
              ▼
   InteractionFeedbackPresenter
              │
    ┌─────────┼──────────┬───────────┬──────────────┐
    ▼         ▼          ▼           ▼              ▼
  Hover    Selection   DragPreview  Placement    Corridor
 Feedback   Feedback    Feedback    Feedback     Feedback
```

### Feedback Types

| Feedback | Visual | Trigger |
|----------|--------|---------|
| Hover | Subtle cyan tint on part | PartHovered state |
| Selection | Yellow highlight + outline | PartSelected state |
| Drag preview | Part follows cursor, slightly transparent | DraggingPart state |
| Magnetic field | Ghost target glows brighter as part approaches | MagneticStrength > 0 |
| Valid placement | Green glow on ghost target | IsValidPlacement && within tolerance |
| Invalid placement | Red flash on part (0.3s) | Placement attempted, invalid |
| Snap animation | Part lerps to target | Valid placement committed |
| Corridor | Translucent cone from target | EnablePlacementCorridors && dragging |
| Ghost path | Curved line from part to target | EnableGhostPathGuidance && part selected |
| Step complete | Green flash + toast | Step completed |

### Integration with Existing Visuals

The current `PartInteractionBridge` already applies colors via `MaterialHelper`:
- Selected = yellow (1.0, 0.85, 0.2)
- Grabbed = orange (1.0, 0.65, 0.1)
- Completed = green (0.3, 0.9, 0.4)
- Invalid = red (1.0, 0.2, 0.2)

The new feedback system uses the **same `MaterialHelper`** for consistency. New feedback types (hover, magnetic glow, corridor) add to the existing visual vocabulary without replacing it.

During migration, the `LegacyBridgeAdapter` can forward visual state to either the old or new feedback path based on toggle state.

---

## 13. Integration Strategy for Existing Codebase

### Principle: Wrap Before Replace

Every integration point follows the same pattern:

1. **Identify** the existing behavior.
2. **Wrap** it with an adapter that exposes the same behavior but can also route to new logic.
3. **Toggle** between old and new paths.
4. **Validate** the new path matches or improves on the old.
5. **Remove** the old path only when confident.

### Specific Integration Points

#### 13.1 PartInteractionBridge (1690 lines — the big one)

**Current role**: Handles all pointer input, drag, snap, validation, visual feedback, ghost management.

**Strategy**: Do NOT rewrite. Instead:

1. Create `LegacyBridgeAdapter` that holds a reference to the existing `PartInteractionBridge`.
2. The `InteractionOrchestrator` checks `_settings.UseV2Interaction`:
   - If **false**: orchestrator is passive. `PartInteractionBridge` runs exactly as today.
   - If **true**: orchestrator handles state machine and routing. It calls `PartInteractionBridge` methods for specific operations (ghost spawning, snap animation, color application) but bypasses its `Update()` input polling.
3. `PartInteractionBridge` gets a single new public bool: `ExternalControlEnabled`. When true, its `Update()` input polling is skipped, but all its public/context-menu methods still work.

```csharp
// Addition to existing PartInteractionBridge.cs (minimal change)
[Header("V2 Integration")]
public bool ExternalControlEnabled;

private void Update()
{
    if (ExternalControlEnabled) return;  // V2 orchestrator handles input
    // ... existing Update code unchanged ...
}
```

This is a **one-line addition** to the existing file. Everything else stays.

#### 13.2 InputActionRouter + SelectionService

**Strategy**: The new `CanonicalActionBridge` listens to V2 intents and calls `InputActionRouter.InjectAction()` and `SelectionService.NotifySelected()`. This keeps all downstream subscribers (PartRuntimeController, UI panels, etc.) working without changes.

```csharp
// CanonicalActionBridge.cs
public sealed class CanonicalActionBridge
{
    private readonly InputActionRouter _router;
    private readonly SelectionService _selectionService;

    public void OnPartSelected(GameObject part)
    {
        _selectionService.NotifySelected(part);
        _router.InjectAction(CanonicalAction.Select);
    }

    public void OnPartGrabbed(GameObject part)
    {
        _router.InjectAction(CanonicalAction.Grab);
    }

    public void OnPartReleased()
    {
        _router.InjectAction(CanonicalAction.Place);
    }
}
```

#### 13.3 Camera (Currently None)

No camera controller exists. `PreviewSceneSetup` sets a static camera position from `MechanicsSceneVisualProfile`. The new `AssemblyCameraRig` replaces this static setup:

1. Add `AssemblyCameraRig` component to the camera GameObject.
2. Set its initial state from `MechanicsSceneVisualProfile` values (position → compute yaw/pitch/distance from pivot).
3. `PreviewSceneSetup` continues to create the camera; `AssemblyCameraRig` attaches to it.
4. If `EnableCameraAssist` is false, the rig applies the initial state and then does nothing (static camera, same as today).

#### 13.4 XR Path (Untouched)

The V2 systems have **zero XR dependencies**. The `IIntentProvider` interface is implemented only by `DesktopIntentProvider` and `MobileIntentProvider`. XR interaction continues through `XRIInteractionAdapter → InputActionRouter → CanonicalAction`. The V2 orchestrator does not intercept XR events.

When XR is active, the orchestrator can detect it and disable itself:

```csharp
// In InteractionOrchestrator
private void OnEnable()
{
    if (XRSettings.isDeviceActive)
    {
        enabled = false;  // XR uses its own interaction path
        return;
    }
}
```

#### 13.5 RuntimeEventBus Integration

The V2 systems **subscribe to existing events** — they don't create a parallel event system:

- `StepGuidanceService` subscribes to `RuntimeEventBus.Subscribe<StepActivated>`.
- `PlacementAssistService` uses data from `PartRuntimeController` (via ServiceRegistry).
- `InteractionFeedbackPresenter` subscribes to `RuntimeEventBus.Subscribe<PartStateChanged>`.

#### 13.6 ServiceRegistry Integration

New services register in `AppBootstrap` alongside existing services:

```csharp
// Addition to AppBootstrap.cs
if (settings.UseV2Interaction)
{
    ServiceRegistry.Register(new PlacementAssistService(settings));
    ServiceRegistry.Register(new StepGuidanceService(settings, cameraRig));
}
```

---

## 14. Toggle Strategy

### Where Toggles Live

All toggles live in a single `InteractionSettings` ScriptableObject asset:

```csharp
// InteractionSettings.cs — ScriptableObject
[CreateAssetMenu(fileName = "InteractionSettings", menuName = "OSE/Interaction Settings")]
public sealed class InteractionSettings : ScriptableObject
{
    [Header("V2 System Master Switch")]
    [Tooltip("When false, the existing PartInteractionBridge handles all interaction.")]
    public bool UseV2Interaction = false;

    [Header("Camera")]
    public bool EnableCameraAssist = true;
    public bool EnableAutoFraming = true;
    public bool EnableVisibilitySolver = false;
    public bool EnableSmartPivot = true;
    public bool EnableSuggestedViews = true;
    public bool EnableOrbitGizmo = false;
    public bool EnableVisionProInteractionModel = false;
    public bool EnableCameraConstraintSphere = true;

    [Header("Camera Tuning")]
    public float OrbitSensitivity = 0.3f;
    public float PanSensitivity = 0.005f;
    public float ZoomSensitivity = 0.1f;
    public float OrbitSmoothing = 8f;
    public float ZoomSmoothing = 6f;
    public float MinCameraDistance = 0.3f;
    public float MaxCameraDistance = 10f;
    public float MinPitch = -10f;
    public float MaxPitch = 85f;

    [Header("Placement")]
    public bool EnableMagneticPlacement = true;
    public bool EnablePlacementCorridors = false;
    public bool EnableGhostPathGuidance = false;
    public float MagneticRadiusMultiplier = 2.0f;
    public float CorridorHalfAngle = 45f;

    [Header("Step Guidance")]
    public bool EnableStepViewGuidance = true;
    public bool EnableExplodedStepPreview = false;

    [Header("Input")]
    public float DragThresholdPixels = 5f;
    public float LongPressDuration = 0.4f;
    public float DoubleTapWindow = 0.3f;

    [Header("Feedback")]
    public float SnapLerpSpeed = 12f;
    public float InvalidFlashDuration = 0.3f;
}
```

### Why ScriptableObject

1. **Inspector-editable**: Designers and developers can tune values without code changes.
2. **Asset-based**: Can create multiple presets (e.g., `InteractionSettings_Desktop`, `InteractionSettings_Mobile`, `InteractionSettings_Debug`).
3. **Serialized**: Settings persist across Play mode entries.
4. **Swappable at runtime**: Change the active settings asset to switch presets.
5. **No scene dependency**: The asset lives in `Assets/_Project/Data/Config/`, not in a scene.

### Toggle Evaluation Pattern

Systems check toggles **at decision time**, not on startup:

```csharp
// Good — checked when needed
public void OnStepActivated(StepActivated evt)
{
    if (!_settings.EnableStepViewGuidance) return;  // Zero cost when disabled
    // ... compute viewpoint ...
}

// Bad — don't cache toggle state at startup
private bool _guidanceEnabled;  // Stale if settings change at runtime
```

This allows runtime toggle changes (e.g., from a debug window) to take effect immediately.

### Debug Window

`InteractionDebugWindow.cs` (Editor window, accessible via `OSE → Interaction Debug`) shows:
- Current `InteractionState`
- Active `IIntentProvider` type
- Last 10 intents (rolling log)
- All toggle states with runtime flip buttons
- Camera state (yaw, pitch, distance, pivot)
- Placement assist state (magnetic strength, corridor status)

---

## 15. Event / Data Flow

### Intent Flow (Input → Action)

```
1. Unity Input System fires callbacks
       │
2. IIntentProvider.Poll() reads raw state, performs raycast, classifies gesture
       │
3. Returns InteractionIntent struct to Orchestrator
       │
4. Orchestrator evaluates state machine transitions
       │
5. Based on new state, Orchestrator routes to appropriate system:
       │
       ├─ DraggingPart → PlacementAssistService.Evaluate()
       │                → CanonicalActionBridge.OnPartGrabbed()
       │                → InteractionFeedbackPresenter.UpdateFeedback()
       │
       ├─ CameraOrbit  → AssemblyCameraRig.ApplyOrbit(delta)
       │
       ├─ PartSelected → CanonicalActionBridge.OnPartSelected()
       │                → CameraPivotResolver (if SmartPivot enabled)
       │                → InteractionFeedbackPresenter.UpdateFeedback()
       │
       └─ Idle         → InteractionFeedbackPresenter.UpdateFeedback()
```

### Event Flow (Runtime → V2 Systems)

```
RuntimeEventBus publishes:
       │
       ├─ StepActivated ──────► StepGuidanceService.OnStepActivated()
       │                            │
       │                            └─► AssemblyCameraRig.ApplyViewpoint() (if auto-framing)
       │
       ├─ PartStateChanged ───► InteractionFeedbackPresenter (visual update)
       │                   ───► PlacementAssistService (state awareness)
       │
       ├─ StepStateChanged ───► ExplodedPreviewController (if exploded preview)
       │
       └─ SessionCompleted ───► AssemblyCameraRig.FrameBounds() (show final result)
```

### Feedback Data Flow

```
Orchestrator collects state each frame:
       │
       ├─ CurrentState (enum)
       ├─ HoveredPart (from intent raycast)
       ├─ SelectedPart (from state)
       ├─ DraggedPart (from state)
       ├─ PlacementAssistResult (from PlacementAssistService)
       │
       ▼
InteractionFeedbackData (struct)
       │
       ▼
InteractionFeedbackPresenter.UpdateFeedback(state, data)
       │
       ├─ HoverFeedback.Apply(data.HoveredPart)
       ├─ SelectionFeedback.Apply(data.SelectedPart)
       ├─ DragPreviewFeedback.Apply(data.DraggedPart, data.MagneticTargetPosition)
       ├─ PlacementFeedback.Apply(data.IsValidPlacement, data.MagneticStrength)
       └─ CorridorFeedback.Apply(data.IsInCorridor, corridorParams)
```

### Decoupling Strategy

- **Orchestrator → Systems**: Direct method calls (orchestrator holds references). This is intentional — the orchestrator is the coordinator and needs to call systems synchronously within a single frame.
- **Systems → Orchestrator**: Never. Systems do not call back into the orchestrator.
- **Systems → Systems**: Never directly. If StepGuidance needs to tell the camera to frame, it calls `AssemblyCameraRig.ApplyViewpoint()` directly (camera is a dependency of guidance). This is a one-way dependency, not circular.
- **Runtime → V2**: Via RuntimeEventBus subscriptions (decoupled, existing pattern).
- **V2 → Runtime**: Via ServiceRegistry to get PartRuntimeController, then direct method calls (same pattern as existing PartInteractionBridge).

---

## 16. Migration / Rollout Plan

### Phase M1: Foundation (Low Risk)

**Goal**: Add V2 folder structure, InteractionSettings, and debug tooling. Zero behavior change.

**Steps**:
1. Create `Assets/_Project/Scripts/Interaction.V2/` folder structure.
2. Create `InteractionSettings.cs` ScriptableObject with all toggles defaulted to **false**.
3. Create `InteractionSettings_Default.asset` in `Assets/_Project/Data/Config/`.
4. Create `InteractionDebugWindow.cs` (Editor window showing current state).
5. Create `InteractionState.cs` and `InteractionIntent.cs` (data types only).
6. No scene changes. No existing code modified.

**Validation**: Project compiles. Existing behavior unchanged. Debug window opens.

### Phase M2: Camera Rig (Medium Risk)

**Goal**: Add camera orbit/pan/zoom that works alongside the existing static camera.

**Steps**:
1. Implement `AssemblyCameraRig.cs`, `CameraState.cs`, `CameraConstraintSphere.cs`, `CameraSmoothing.cs`.
2. Implement `DesktopIntentProvider.cs` with camera-only intents (Orbit, Pan, Zoom). No part interaction yet.
3. Add `AssemblyCameraRig` component to the camera in `Test_Assembly_Mechanics.unity`.
4. `AssemblyCameraRig` reads initial position from `MechanicsSceneVisualProfile` and converts to yaw/pitch/distance.
5. Set `EnableCameraAssist = true` in settings.
6. Existing `PartInteractionBridge` still handles all part interaction.

**Validation**: Camera orbits with right-drag, pans with middle-drag, zooms with scroll. Part interaction unchanged.

### Phase M3: Intent Orchestration (Medium Risk)

**Goal**: Add the orchestrator with state machine. Part interaction still via legacy bridge.

**Steps**:
1. Implement `InteractionOrchestrator.cs` with full state machine.
2. Implement `LegacyBridgeAdapter.cs` that wraps `PartInteractionBridge`.
3. Implement `CanonicalActionBridge.cs` and `SelectionServiceBridge.cs`.
4. Add `InteractionOrchestrator` to scene.
5. With `UseV2Interaction = false` (default): orchestrator is passive, no behavior change.
6. With `UseV2Interaction = true`: orchestrator drives state, camera uses V2 rig, part interaction still routes through `PartInteractionBridge` via legacy bridge.
7. Add `ExternalControlEnabled` bool to `PartInteractionBridge` (one-line change).

**Validation**: Toggle `UseV2Interaction` at runtime. Both paths produce identical part interaction behavior. Camera works in both modes.

### Phase M4: Mobile Intent Provider (Medium Risk)

**Goal**: Add touch gesture classification for mobile.

**Steps**:
1. Implement `MobileIntentProvider.cs` with gesture classification.
2. Implement `IntentProviderResolver.cs` to auto-select provider.
3. Test on mobile device or Unity Remote.

**Validation**: Single-finger on part = drag. Single-finger on empty = orbit. Pinch = zoom. Two-finger = pan. No conflicts.

### Phase M5: Placement Assist (Low Risk)

**Goal**: Add magnetic snap and corridor validation as optional enhancements.

**Steps**:
1. Implement `PlacementAssistService.cs`, `MagneticSnapSolver.cs`, `PlacementCorridorSolver.cs`.
2. Register in ServiceRegistry.
3. Orchestrator calls `PlacementAssistService.Evaluate()` during drag.
4. Enable toggles one at a time.

**Validation**: With `EnableMagneticPlacement = true`, parts get gently pulled toward targets when close. With `EnablePlacementCorridors = true`, approaching from wrong angle fails.

### Phase M6: Step Guidance + Feedback (Low Risk)

**Goal**: Add step-aware camera guidance and enhanced visual feedback.

**Steps**:
1. Implement `StepGuidanceService.cs`, `ViewpointLibrary.cs`.
2. Implement `InteractionFeedbackPresenter.cs` and sub-feedback classes.
3. Implement `ExplodedPreviewController.cs` (optional).
4. Implement `GhostPathRenderer.cs` (optional).
5. Enable toggles one at a time.

**Validation**: On step activate, camera auto-frames target. Suggested view buttons appear. Ghost path shows when enabled.

### Phase M7: Cleanup (After Validation)

**Goal**: Once V2 is validated and stable, simplify.

**Steps**:
1. Move remaining logic out of `PartInteractionBridge` into V2 systems (ghost spawning, snap animation).
2. Reduce `PartInteractionBridge` to a thin adapter or remove it.
3. Remove `LegacyBridgeAdapter`.
4. Set `UseV2Interaction = true` as default.

**This phase is optional and should only happen after weeks of validation.**

---

## 17. Performance / Scalability Notes

### Raycasts

**Current**: `PartInteractionBridge` raycasts every frame during pointer interaction.
**V2**: `IIntentProvider.Poll()` raycasts once per frame (single `Physics.Raycast` from camera through pointer position). The result is cached in `InteractionIntent.HitTarget` and reused by all systems.

**For large assemblies**: Use a dedicated `PartLayer` layer mask to limit raycast to part colliders only. Avoid raycasting against environment geometry.

### Update Loops

| System | Update Frequency | Method |
|--------|-----------------|--------|
| InteractionOrchestrator | Every frame (`Update`) | Polls intent, runs state machine |
| AssemblyCameraRig | Every frame (`LateUpdate`) | Applies smoothed camera state |
| PlacementAssistService | Every frame while dragging only | Called by orchestrator, not self-updating |
| StepGuidanceService | On event only | Subscribes to StepActivated |
| InteractionFeedbackPresenter | Every frame | Updates visuals from state |

**Total always-on MonoBehaviours**: 3 (Orchestrator, CameraRig, FeedbackPresenter). This is fewer than the current setup.

### Dirty Flags

- `CameraPivotResolver`: Only recomputes pivot when selection changes (dirty flag on `OnSelected`).
- `CameraFramingService`: Only computes bounds when requested, not every frame.
- `PlacementAssistService`: Only runs when `CurrentState == DraggingPart`.
- `StepGuidanceService`: Only activates on `StepActivated` events.

### Caching

- `InteractionIntent.HitTarget`: Cached per frame, avoids duplicate raycasts.
- `CameraState`: The target state is set instantly; the current state interpolates toward it. No recomputation of target until new input arrives.
- `PlacementAssistResult`: Computed once per frame during drag, consumed by feedback.
- Assembly bounds: Computed once on package load, cached in `CameraPivotResolver`.

### Memory

- All intents, states, and results are **structs** (no heap allocation per frame).
- Feedback classes reuse materials (via `MaterialHelper` existing pattern).
- `GhostPathRenderer` reuses a single `LineRenderer` component.

---

## 18. Recommended Default Toggle Configuration

### Desktop Default

```
UseV2Interaction          = true
EnableCameraAssist        = true
EnableAutoFraming         = true
EnableVisibilitySolver    = false    // Not yet implemented
EnableSmartPivot          = true
EnableSuggestedViews      = true
EnableOrbitGizmo          = false    // Power user feature
EnableVisionProInteractionModel = false
EnableCameraConstraintSphere = true
EnableMagneticPlacement   = false    // Desktop users have precision
EnablePlacementCorridors  = false    // Advanced, enable later
EnableStepViewGuidance    = true
EnableExplodedStepPreview = false    // Experimental
EnableGhostPathGuidance   = false    // Experimental
```

### Mobile Default

```
UseV2Interaction          = true
EnableCameraAssist        = true
EnableAutoFraming         = true
EnableVisibilitySolver    = true     // Mobile users get lost more easily
EnableSmartPivot          = true
EnableSuggestedViews      = true     // Critical for mobile — reduces manual orbit
EnableOrbitGizmo          = false
EnableVisionProInteractionModel = false
EnableCameraConstraintSphere = true  // Prevents disorientation
EnableMagneticPlacement   = true     // Mobile needs precision assist
EnablePlacementCorridors  = false
EnableStepViewGuidance    = true
EnableExplodedStepPreview = false
EnableGhostPathGuidance   = true     // Helps find off-screen targets
```

### Debug / Development

```
UseV2Interaction          = true
[All features enabled for testing]
```

---

## 19. Risks / Tradeoffs / Alternatives

### Risk 1: PartInteractionBridge Complexity

**Risk**: The existing `PartInteractionBridge` is 1690 lines and handles too many concerns. Wrapping it is safer than rewriting it, but the wrapper may need to understand internal state.

**Mitigation**: The `ExternalControlEnabled` flag is the thinnest possible integration point — it just skips the `Update()` polling. All public methods remain callable. This avoids needing to understand internal state.

**Alternative considered**: Refactoring PartInteractionBridge into smaller classes first. Rejected because it's high-risk churn with no user-visible benefit before V2 is ready.

### Risk 2: Two Input Paths

**Risk**: During migration, both the old input path (PartInteractionBridge.Update) and the new path (IIntentProvider → Orchestrator) exist. If both are active simultaneously, inputs would be processed twice.

**Mitigation**: The `UseV2Interaction` toggle is a **mutual exclusion switch**. When true, `PartInteractionBridge.ExternalControlEnabled = true` disables its input polling. Only one path processes input at any time.

### Risk 3: Camera State Initialization

**Risk**: The current camera is positioned by `PreviewSceneSetup` from a `MechanicsSceneVisualProfile`. If `AssemblyCameraRig` doesn't read the same initial values, the camera will jump on Play.

**Mitigation**: `AssemblyCameraRig.Initialize()` reads the camera's current transform (set by `PreviewSceneSetup`) and reverse-computes the initial `CameraState` (yaw, pitch, distance from a default pivot). No jump because the initial state matches the current transform.

### Risk 4: Mobile Gesture Misclassification

**Risk**: Touch-down raycast hits a part → classified as part drag. But user intended to orbit and happened to touch near a part.

**Mitigation**: The 8px drag threshold helps — if the user lifts before the threshold, it's a tap (select), not a drag. If they wanted to orbit, they can use two fingers (always camera) or touch empty space. The key insight is that **intentional part drags are rare compared to camera orbits** — most gestures are navigation. The system should slightly favor camera over part interaction on ambiguous single-finger gestures. This can be tuned by increasing the drag threshold on mobile (e.g., 15px instead of 5px).

**Alternative considered**: Long-press to grab. Rejected because it adds latency to the primary task (assembly). The user should be able to grab and drag in one fluid gesture.

### Risk 5: Camera Orbit Conflicts with UI

**Risk**: Right-drag on desktop over a UI panel should interact with UI, not orbit the camera.

**Mitigation**: `IIntentProvider.Poll()` checks `EventSystem.current.IsPointerOverGameObject()` before classifying gestures. If pointer is over UI, the intent is `UIInteraction` and all camera/part intents are suppressed.

### Risk 6: Performance on Large Assemblies

**Risk**: Per-frame raycast against hundreds of parts.

**Mitigation**: Use layer-based raycasting (`PartLayer` only). Parts use simple colliders (box or convex mesh). For very large assemblies (500+ parts), consider spatial partitioning or only enabling colliders on visible/nearby parts.

### Tradeoff: ScriptableObject vs Runtime Config

**Chosen**: ScriptableObject for toggles.
**Alternative**: `static class InteractionConfig` with runtime-only values.
**Rationale**: ScriptableObject survives Play mode, is inspector-editable, supports multiple presets, and is the established pattern in the project (`MechanicsSceneVisualProfile` is already a ScriptableObject). Runtime overrides can still be applied via the debug window.

### Tradeoff: Single Orchestrator vs Per-System State

**Chosen**: Single `InteractionOrchestrator` owns the state machine.
**Alternative**: Each system manages its own state (camera knows it's orbiting, placement knows it's dragging).
**Rationale**: Centralized state prevents the "camera orbits while dragging a part" conflict. Distributed state requires each system to check every other system's state, creating hidden coupling. One state machine is simpler to debug and reason about.

### Tradeoff: Struct Intents vs Event-Based Intents

**Chosen**: Struct-based `InteractionIntent` returned from `Poll()` each frame.
**Alternative**: Event callbacks from Input System → Orchestrator.
**Rationale**: Polling gives the orchestrator full control over when and how to process input. Event callbacks can arrive at unpredictable times and require buffering. The struct is stack-allocated (no GC pressure). The orchestrator already needs per-frame updates for drag tracking and camera smoothing, so a poll model fits naturally.

---

## Appendix A: InteractionSettings Quick Reference

```
Feature                          System              Default (Desktop)  Default (Mobile)
─────────────────────────────────────────────────────────────────────────────────────────
UseV2Interaction                 Orchestrator         true               true
EnableCameraAssist               Camera Rig           true               true
EnableAutoFraming                Camera Rig           true               true
EnableVisibilitySolver           Camera Rig           false              true
EnableSmartPivot                 Camera Rig           true               true
EnableSuggestedViews             Step Guidance        true               true
EnableOrbitGizmo                 Camera Rig           false              false
EnableVisionProInteractionModel  Camera Rig           false              false
EnableCameraConstraintSphere     Camera Rig           true               true
EnableMagneticPlacement          Placement Assist     false              true
EnablePlacementCorridors         Placement Assist     false              false
EnableStepViewGuidance           Step Guidance        true               true
EnableExplodedStepPreview        Step Guidance        false              false
EnableGhostPathGuidance          Placement Assist     false              true
```

## Appendix B: File Count Summary

| Category | New Files | Modified Existing Files |
|----------|-----------|------------------------|
| Core (types, orchestrator) | 4 | 0 |
| Input | 4 | 0 |
| Camera | 6 | 0 |
| Placement | 5 | 0 |
| Guidance | 4 | 0 |
| Feedback | 6 | 0 |
| Integration | 3 | 1 (PartInteractionBridge: +1 line) |
| Config | 1 | 1 (AppBootstrap: +5 lines) |
| Editor | 2 | 0 |
| **Total** | **35** | **2** |

Only **2 existing files** are modified, and both changes are minimal (one bool field, one registration block). All other code is additive in a new folder.
