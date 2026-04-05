# OSE Blueprints Assembler

XR assembly training application. Users follow step-by-step guided instructions to assemble machines in 3D, with placement targets, tool actions, and real-time visual feedback. All machine content is data-driven — new machines and assembly sequences are authored in `machine.json` with no C# recompile required.

---

## Requirements

| Requirement | Version |
|---|---|
| Unity | 2022.3 LTS |
| Universal Render Pipeline | 14.x (included) |
| XR Interaction Toolkit | 2.5.x |
| XR Plugin Management | 4.x |
| Target platforms | WebGL, Windows (standalone) |

---

## Setup

1. **Clone the repository**
   ```
   git clone <repo-url>
   cd "OSE Blueprints Assembler"
   ```

2. **Open in Unity Hub**
   Add the cloned folder as a project. Unity will import assets and resolve packages on first open (~2–5 min).

3. **Open the test scene**
   `Assets/Scenes/Test_Assembly_Mechanics.unity`

4. **Hit Play**
   The session loads the default machine package (`d3d_v18_10`). Use the pointer/mouse to select and place parts.

> **Note:** On first import Unity may report errors while packages resolve. Wait for the import to complete fully before inspecting Console output.

---

## Project Structure

```
Assets/
  _Project/
    Scripts/
      Core/           OSE.Core — events, logging, shared interfaces
      Runtime/        OSE.Runtime — session, step, part, tool state machines
      Content/        OSE.Content — machine.json definitions and loading
      Interaction/    OSE.Interaction — input routing, drag, snap
      UI/             OSE.UI — MonoBehaviours, UIToolkit, visual feedback
      Persistence/    Save/load (PlayerPrefs-backed)
      Tests/          EditMode unit tests
    Data/
      Packages/       Machine package authoring folders (machine.json + meshes)
  Scenes/             Unity scenes
  StreamingAssets/    Runtime build output — DO NOT edit directly

Tools/                Python content-authoring scripts (mesh audit, pivot checks, etc.)
```

---

## Authoring Machine Content

Machine packages live in `Assets/_Project/Data/Packages/<packageId>/`. Each package contains:

- `machine.json` — steps, parts, tools, targets, and subassemblies
- Mesh assets (GLB/FBX) referenced by the JSON

**Always edit `machine.json` in the authoring folder.** The build pipeline copies to `StreamingAssets/` automatically. See [CLAUDE.md](CLAUDE.md) for the authoritative rule and [CONTRIBUTING.md](CONTRIBUTING.md) for the authoring workflow.

---

## Architecture

Dependency arrows flow inward — UI and Interaction depend on Runtime; Runtime depends on Content and Core; Core has no Unity UI or XR SDK knowledge.

```
OSE.Core  ←  OSE.Content  ←  OSE.Runtime  ←  OSE.App  ←  OSE.Bootstrap
                                                        ←  OSE.UI
                                                        ←  OSE.Interaction
```

Business logic (`StepController`, `ProgressionController`, `MachineSessionController`) is plain C# with no `MonoBehaviour` — fully unit-testable without the Unity runtime.

### Key Entry Points

| File | Role |
|------|------|
| `Scripts/Bootstrap/AppBootstrap.cs` | Scene entry — registers all core services in `Awake()` |
| `Scripts/App/ServiceRegistry.cs` | Service locator; header documents the `Awake/OnEnable/Start` init order |
| `Scripts/Runtime/Session/MachineSessionController.cs` | Top-level session orchestrator (load → step FSM → completion) |
| `Scripts/Runtime/Session/StepController.cs` | Step FSM with validated state transitions |
| `Scripts/Core/RuntimeEvents.cs` | All published events (all `readonly struct`, zero GC allocation) |
| `Scripts/Core/OseErrorCode.cs` | Numeric error code ranges for grepping logs |

### Service Bootstrap Convention

```
Awake()    — self-register via ServiceRegistry + local init only
OnEnable() — resolve cross-service dependencies via ServiceRegistry.TryGet<T>()
Start()    — multi-service orchestration
```
Never call `ServiceRegistry.TryGet<T>()` inside `Awake()` — Unity does not guarantee ordering across GameObjects.

---

## Adding Content

To add a new machine: create `Assets/_Project/Data/Packages/<newId>/machine.json`, add mesh assets alongside it, and follow `ose-xr-foundation/docs/DATA_SCHEMA.md`. No C# changes required.

To add a new step profile: add an entry to `ProfileLookup` in `StepDefinition.cs` and, if it needs a new handler, implement `IStepFamilyHandler`.

---

## Tests

Run via **Window › General › Test Runner** in the Unity Editor.
EditMode tests (no Unity runtime needed) live in `Scripts/Tests/EditMode/`.

| Test file | What it covers |
|-----------|---------------|
| `StepControllerTests.cs` | Full FSM — all valid and invalid transitions (~50 cases) |
| `SessionLifecycleTests.cs` | Assembly begin → step complete → assembly complete |
| `SessionStartTests.cs` | `StartSessionAsync` with injected stub loader |
| `PlayerPrefsPersistenceServiceTests.cs` | Save/load round-trip, corrupt-data recovery |
| `PlacementValidatorTests.cs` | Position and rotation tolerance boundary checks |
| `RuntimeEventBusTests.cs` | Subscribe, unsubscribe, multi-subscriber, type isolation |
| `MachinePackageValidatorTests.cs` | Schema validation rules |
