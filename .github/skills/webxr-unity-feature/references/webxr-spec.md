# W3C WebXR Specification Reference

Always fetch the **latest published URL** (`/TR/`) rather than the Editor's Draft (`/ED/`) unless you specifically need unreleased features.

---

## Core Spec

| Spec | URL | Status |
|------|-----|--------|
| WebXR Device API (core) | https://www.w3.org/TR/webxr/ | CR |
| WebXR AR Module | https://www.w3.org/TR/webxr-ar-module-1/ | CR |
| WebXR Hand Input | https://www.w3.org/TR/webxr-hand-input-1/ | CR |
| WebXR Anchors | https://www.w3.org/TR/webxr-anchors-module/ | WD |
| WebXR Depth Sensing | https://www.w3.org/TR/webxr-depth-sensing-1/ | WD |
| WebXR Hit Testing | https://www.w3.org/TR/webxr-hit-test-1/ | WD |
| WebXR DOM Overlays | https://www.w3.org/TR/webxr-dom-overlays-1/ | CR |
| WebXR Layers | https://www.w3.org/TR/webxr-layers-1/ | WD |
| WebXR Lighting Estimation | https://www.w3.org/TR/webxr-lighting-estimation-1/ | WD |
| WebXR Mesh Detection | https://www.w3.org/TR/webxr-mesh-detection/ | ED only |

Editor's Drafts base: `https://immersive-web.github.io/`

---

## Useful Anchors in the Core Spec

Append these fragments to `https://www.w3.org/TR/webxr/` to jump directly to a section:

| Topic | Fragment |
|-------|----------|
| Session lifecycle | `#xrsession-interface` |
| `requestSession()` | `#dom-navigator-xr-requestsession` |
| Reference spaces | `#xrreferencespace-interface` |
| `XRFrame` | `#xrframe-interface` |
| Input sources | `#xrinputsource-interface` |
| Pose / viewer pose | `#xrpose-interface` |
| Render state | `#xrrenderstate-interface` |
| Feature requirements | `#feature-requirements` |
| Security considerations | `#security` |

---

## Fetch Strategy

When the agent needs a specific section:
1. Fetch the full spec URL (or editor's draft) — the W3C specs render as single-page HTML
2. Search the returned HTML for the IDL block or algorithm near the topic
3. Extract: IDL interface definition, required steps, exceptions thrown

Example fetch targets:
```
# Core session init
https://www.w3.org/TR/webxr/#xrsession-interface

# Hand input joint enum
https://www.w3.org/TR/webxr-hand-input-1/#xrhand-interface

# AR hit test
https://www.w3.org/TR/webxr-hit-test-1/#hit-test
```

---

## Required Session Features (string tokens)
These are passed to `requestSession({ requiredFeatures: [...] })` in JS:

| Feature token | Spec |
|---------------|------|
| `"local"` | Core |
| `"local-floor"` | Core |
| `"bounded-floor"` | Core |
| `"unbounded"` | Core |
| `"viewer"` | Core |
| `"hand-tracking"` | Hand Input |
| `"hit-test"` | Hit Test |
| `"anchors"` | Anchors |
| `"depth-sensing"` | Depth Sensing |
| `"dom-overlay"` | DOM Overlays |
| `"light-estimation"` | Lighting Estimation |
| `"layers"` | Layers |
| `"mesh-detection"` | Mesh Detection |

---

## GitHub Source (Immersive Web CG)
All specs are maintained at: https://github.com/immersive-web/  
Useful repos for edge cases and test suites:
- https://github.com/immersive-web/webxr — core spec issues
- https://github.com/immersive-web/webxr-hand-input — hand input
- https://github.com/immersive-web/webxr-samples — sample code (canonical patterns)
