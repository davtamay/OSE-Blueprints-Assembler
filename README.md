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

See [ARCHITECTURE.md](ARCHITECTURE.md) for:
- Layer model (OSE.Core → Runtime → Content → Interaction → UI)
- Bootstrap sequence
- Architectural Decision Records (ADR 001–005)
- Key file reference table

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for:
- Adding a new machine package
- Adding a new step family handler
- Adding a new runtime event
- Code standards and pre-submit checklist
