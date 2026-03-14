---
name: webxr-unity-feature
description: "Use when implementing, extending, or debugging WebXR features in Unity using the unity-webxr-export package (De-Panther/unity-webxr-export). Triggers: WebXR, hand tracking, AR module, hit testing, anchors, depth sensing, XR session, XRFrame, WebGL context, jslib interop, XR controller. Fetches W3C WebXR spec sections and explores the unity-webxr-export GitHub codebase to guide correct implementation."
argument-hint: "Describe the WebXR feature to implement (e.g. 'hand tracking', 'AR hit testing', 'DOM overlays')"
---

# WebXR Unity Feature Implementation

## Purpose
Implement or extend WebXR features inside a Unity project that uses the [unity-webxr-export](https://github.com/De-Panther/unity-webxr-export) package. Grounds every decision in the authoritative W3C spec and the existing package patterns.

## When to Use
- Adding a new WebXR API feature (hand input, anchors, depth sensing, layers, hit testing, DOM overlays, etc.)
- Bridging a new browser WebXR API into Unity via `.jslib` interop
- Debugging WebXR session behaviour or controller mapping
- Updating the package to support a newer draft of the spec

---

## Procedure

### 1. Clarify the Feature
Ask (or infer from context):
- Which WebXR module covers this feature? (core, AR, hand input, anchors, hit test, depth, layers, DOM overlays)
- Is the feature already partially implemented in the package?
- Target platform: VR headset, AR (phone/tablet), or both?

### 2. Read the W3C Spec
Load [references/webxr-spec.md](./references/webxr-spec.md) for the URL map.  
Fetch the relevant spec section(s) using the fetch tool:
- Identify the IDL interface(s) involved
- Note which attributes/methods are required vs optional
- Check the step-by-step algorithm the browser must follow (use to drive JS interop call order)
- Look for security/privacy notes that affect what can be exposed to Unity

### 3. Explore the unity-webxr-export Codebase
Load [references/unity-webxr-export.md](./references/unity-webxr-export.md) for repo structure.  
Fetch or search relevant files on GitHub (`https://raw.githubusercontent.com/De-Panther/unity-webxr-export/main/<path>`):
- Find the closest existing feature as a pattern reference (e.g. hand tracking → `webxr-input.jslib`)
- Identify the C# side (`WebXRManager`, `WebXRController`, `WebXRHand`, etc.)
- Identify the JS side (`.jslib` files that call browser WebXR APIs)
- Note how data flows: JS → `SendMessage` / shared buffer → C# script → Unity events

### 4. Locate Workspace Files
Search the current workspace for:
- Any existing partial implementation of the feature
- The Unity package folder (`Packages/webxr/` or `Assets/WebXR/`)
- Existing `.jslib` files and `WebXRManager.cs`

### 5. Implement

#### JS side (`.jslib`)
- Add a new exported function following existing naming conventions (`WebXR_<Feature>_<Action>`)
- Use the exact IDL method/attribute names from the spec
- Pass data back to Unity via `SendMessage` or a shared `UnityHeap` buffer (match whichever pattern the nearby code uses)
- Guard with feature detection: `if (!session.<feature>)` before calling

#### C# side
- Mirror the JS function in the appropriate manager/controller class
- Raise a C# event or update a serialized field so Unity components can subscribe
- Use `[DllImport("__Internal")]` for the jslib entry point (WebGL only); wrap in `#if UNITY_WEBGL && !UNITY_EDITOR`
- Follow the existing null-safety and coroutine patterns

#### Editor / build
- Ensure the new `.jslib` file lives inside a folder named `Plugins/WebGL/` so Unity picks it up
- If the feature requires a new browser permission or feature policy header, document it in a comment

### 6. Validate
- Build for WebGL and test in a WebXR-capable browser (Chrome with WebXR emulator or a real headset)
- Confirm the browser console shows no spec violation warnings
- Check that the Unity side receives clean events with no null refs

---

## Key Concepts

| Concept | Notes |
|---------|-------|
| `XRSession` | Entry point; `mode` is `immersive-vr`, `immersive-ar`, or `inline` |
| `XRFrame` | Per-frame callback; only valid during `requestAnimationFrame` |
| `XRReferenceSpace` | Coordinate system; prefer `local-floor` for VR, `unbounded` or `local` for AR |
| `XRInputSource` | Controller or hand; check `targetRayMode` and `hand` property |
| `jslib` interop | Unity WebGL calls browser JS; data crossing the boundary must be primitives or typed arrays |
| Shared buffer pattern | JS writes into `HEAPF32`/`HEAP32` at a pointer Unity allocated; avoids string marshalling |

---

## References
- [WebXR Spec Map](./references/webxr-spec.md)
- [unity-webxr-export Repo Guide](./references/unity-webxr-export.md)
