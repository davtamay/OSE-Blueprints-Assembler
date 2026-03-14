# unity-webxr-export Repository Guide

Repo: https://github.com/De-Panther/unity-webxr-export  
Raw file base URL: `https://raw.githubusercontent.com/De-Panther/unity-webxr-export/main/`

---

## Repository Structure

```
unity-webxr-export/
├── Packages/
│   └── webxr/                         ← UPM package root
│       ├── package.json
│       ├── Runtime/
│       │   ├── Scripts/
│       │   │   ├── WebXRManager.cs       ← central session + frame management
│       │   │   ├── WebXRController.cs    ← XR controller / input source
│       │   │   ├── WebXRHand.cs          ← hand tracking joints
│       │   │   ├── WebXRCamera.cs        ← stereo rendering setup
│       │   │   └── WebXRInputSystem.cs   ← Unity Input System bridge (if present)
│       │   └── Plugins/
│       │       └── WebGL/
│       │           ├── webxr.jslib       ← main JS↔C# bridge (session, frame, pose)
│       │           ├── webxr-input.jslib ← controller + hand input bridge
│       │           └── webxr-*.jslib     ← one file per feature module
│       └── Editor/
│           └── Scripts/
│               └── WebXRBuildProcessor.cs
├── Assets/
│   └── WebXR/
│       ├── Prefabs/                   ← ready-made XR Origin/Camera Rig
│       └── Samples/                   ← example scenes
└── WebGLTemplates/
    └── WebXR/
        └── index.html                 ← html template that boots the WebXR session
```

---

## Key File: `webxr.jslib`

Fetch: `https://raw.githubusercontent.com/De-Panther/unity-webxr-export/main/Packages/webxr/Runtime/Plugins/WebGL/webxr.jslib`

Patterns to follow:
- All exported functions live in `mergeInto(LibraryManager.library, { ... })`
- Function names: `WebXR_FeatureName_ActionVerb` (PascalCase segments)
- Sending data to C#: `SendMessage('WebXRManager', 'MethodName', value)` or writing to a `HEAPF32`/`HEAP32` buffer whose pointer was passed from C#
- Guards: always check the session/feature exists before calling browser APIs

---

## Key File: `WebXRManager.cs`

Fetch: `https://raw.githubusercontent.com/De-Panther/unity-webxr-export/main/Packages/webxr/Runtime/Scripts/WebXRManager.cs`

Patterns:
- `[DllImport("__Internal")] static extern void WebXR_*()` declarations — one per jslib function
- All `[DllImport]` calls wrapped in `#if UNITY_WEBGL && !UNITY_EDITOR`
- Public static events (`OnControllerUpdate`, `OnHandUpdate`, etc.) that game code subscribes to
- `WebXRState` enum: `NORMAL`, `VR`, `AR`

---

## How to Explore the Repo Without Cloning

| Goal | URL pattern |
|------|-------------|
| Browse directory | `https://github.com/De-Panther/unity-webxr-export/tree/main/<path>` |
| Read a file (raw) | `https://raw.githubusercontent.com/De-Panther/unity-webxr-export/main/<path>` |
| Search code | `https://github.com/search?q=repo:De-Panther/unity-webxr-export+<term>&type=code` |
| Latest release | `https://github.com/De-Panther/unity-webxr-export/releases/latest` |
| Open issues | `https://github.com/De-Panther/unity-webxr-export/issues` |
| Changelog | `https://raw.githubusercontent.com/De-Panther/unity-webxr-export/main/CHANGELOG.md` |

---

## Data Flow: JS → Unity

```
Browser WebXR API
      ↓
.jslib function (JS)
      ↓  (either)
SendMessage('WebXRManager', 'CSharpMethod', data)   ← simple scalar / string
      or
HEAPF32[ptr >> 2] = value  (shared memory buffer)   ← bulk pose/joint data
      ↓
WebXRManager.cs public method / event invocation
      ↓
C# events (OnControllerUpdate, OnHandUpdate, …)
      ↓
Game logic / Unity components
```

---

## Existing Features (use as implementation templates)

| Feature | JS file | C# class | Notes |
|---------|---------|----------|-------|
| Session lifecycle | `webxr.jslib` | `WebXRManager` | `requestSession`, `end`, transitions |
| Stereo rendering | `webxr.jslib` | `WebXRCamera` | projection matrices, view offsets |
| Controller input | `webxr-input.jslib` | `WebXRController` | grip/ray pose, button axes |
| Hand tracking | `webxr-input.jslib` | `WebXRHand` | 25 joint poses via HEAPF32 buffer |
| AR mode | `webxr.jslib` | `WebXRManager` | `immersive-ar` session mode |

---

## Adding a New Feature — Checklist

- [ ] Read spec IDL; list every method/attribute you'll call
- [ ] Create `webxr-<feature>.jslib` in `Packages/webxr/Runtime/Plugins/WebGL/`  
      (or extend the closest existing `.jslib` if the addition is small)
- [ ] Export `[DllImport("__Internal")]` declarations in `WebXRManager.cs` (or a new companion class)
- [ ] Define a public C# event/delegate for the feature output
- [ ] Add a session feature token to `requestSession` in `webxr.jslib` (`requiredFeatures` / `optionalFeatures`)
- [ ] Guard behind `#if UNITY_WEBGL && !UNITY_EDITOR` everywhere
- [ ] Add a sample scene or update an existing one in `Assets/WebXR/Samples/`
