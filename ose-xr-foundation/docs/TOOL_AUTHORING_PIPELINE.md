# TOOL_AUTHORING_PIPELINE.md

> Complete reference for creating, importing, and deploying 3D tool assets in OSE XR training packages.
> Companion to [PART_AUTHORING_PIPELINE.md](PART_AUTHORING_PIPELINE.md) - same infrastructure, tool-specific conventions.
> Canonical physical-fidelity requirements live in [CONTENT_MODEL.md](CONTENT_MODEL.md) under `Physical Fidelity Standard`.

---

## 1. Purpose

Tools are interactive objects the user wields to perform assembly actions — measuring, cutting, welding, tightening. Unlike parts (which are placed and stay), tools are **held, used, and put away**. This pipeline covers:

1. Defining tool metadata in `machine.json`
2. Generating or sourcing 3D tool models (GLB)
3. Importing, normalizing, and deploying tool assets
4. Orientation and grip conventions for cross-platform use
5. Effect attachment points for particles, decals, and material transitions

---

## 2. Core Principles

1. **Data-driven** — Tool definitions live in `machine.json`, not in code. New tools require zero C# changes.
2. **Real-world proportions** — Every tool has cataloged dimensions (meters). Models are scaled to match reality.
3. **Progressive fidelity** — Same tool model renders on mobile, PC, and XR. Interaction complexity scales with device capability.
4. **Zero tech debt** — Extension points for grip, effects, and animations are defined in the schema now, wired later as needed.

---

## 3. Tool Definition Schema

Each tool in `machine.json` has this structure:

```json
{
  "id": "tool_torque_wrench",
  "name": "Torque Wrench",
  "category": "hand_tool",
  "purpose": "Tightens bolts to precise torque specification...",
  "usageNotes": "Set desired torque, pull smoothly until click...",
  "safetyNotes": "Never use to loosen bolts...",
  "assetRef": "assets/tools/tool_torque_wrench.glb",
  "defaultOrientationHint": "Hold horizontal, socket end forward..."
}
```

### Required Fields

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Unique identifier. Convention: `tool_<name>` |
| `name` | string | Display name shown in UI |
| `category` | enum | `measurement`, `hand_tool`, `power_tool`, `custom` |
| `purpose` | string | What the tool does — written for learners |
| `assetRef` | string | Relative path to GLB: `assets/tools/<id>.glb` |

### Optional Fields

| Field | Type | Description |
|-------|------|-------------|
| `usageNotes` | string | How to use it — practical guidance |
| `safetyNotes` | string | Safety warnings |
| `defaultOrientationHint` | string | Text description of how to hold it (UI/documentation only) |

### Extension Fields (schema-ready, not yet wired)

| Field | Type | Description |
|-------|------|-------------|
| `gripConfig` | object | XR hand grip attachment — see §8 |
| `effectAttachPoints` | object | Named points on the tool for particle/decal origins — see §9 |

---

## 4. Dimension Catalog

Every tool must have an entry in `PartDimensionCatalog.cs` before normalization can work.

```csharp
// Assets/_Project/Scripts/Editor/PartDimensionCatalog.cs
{ "tool_torque_wrench",  new Vector3(0.45f, 0.05f, 0.05f) },  // 18" long handle
{ "tool_tape_measure",   new Vector3(0.08f, 0.08f, 0.04f) },  // 3" round case
{ "tool_angle_grinder",  new Vector3(0.30f, 0.10f, 0.10f) },  // 12" grinder
```

**Dimension convention**: `Vector3(width, height, depth)` in **meters**.
- **Width (X)** — left-to-right when held naturally
- **Height (Y)** — top-to-bottom
- **Depth (Z)** — front-to-back (business end to grip end)

For elongated tools (wrenches, tape measures), the longest axis goes in the dimension that matches how the tool extends when held.

---

## 5. GLB Orientation Convention

All tool GLB models must follow this axis convention:

```
        +Y (up)
         |
         |
         +--- +X (right)
        /
       /
      +Z (forward = business end)
```

| Axis | Direction | Example |
|------|-----------|---------|
| **+Z** | Business end — points forward (away from user) | Wrench socket, drill bit, grinder disc, welder tip |
| **-Z** | Grip end — closest to user | Handle, trigger, grip area |
| **+Y** | Up | Top of tool when held naturally |
| **Origin** | Handle center / natural grip point | Where the hand would wrap around |

### Why This Convention

The runtime uses `Quaternion.LookRotation(camera.forward, camera.up)` to orient the tool preview. This aligns the tool's local +Z with the camera's forward direction — like holding a gun. The business end points into the scene, the handle stays near the user.

### Generation Prompt Tip

Include orientation in your Rodin prompt:
> "...tool pointing forward along the Z axis, handle closest to viewer, Y axis up"

If the generated model has the wrong orientation, it will appear sideways or upside-down in-game. Re-generate rather than adding rotation hacks.

---

## 6. 3D Model Generation Pipeline

Tools follow the same four-stage pipeline as parts:

```
Generate → Import → Normalize Scale → Verify
```

### 6.1 Prerequisites

Same as PART_AUTHORING_PIPELINE.md §28.2:
- Python 3.10+ with `requests`
- Rodin skill scripts in `.github/skills/rodin3d-skills/`
- API key (`vibecoding` for free tier, or paid key)
- glTFast package in Unity

### 6.2 Generation Methods

#### Text-to-3D (Rodin)

Best for: Generic tools with well-known shapes (socket sets, clamps).

```bash
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py \
  --prompt "Professional torque wrench, 1/2 inch drive, chrome vanadium steel shaft, \
            black rubber grip handle, ratchet head with socket end. \
            Approximately 18 inches long, 2 inches wide, 2 inches deep. \
            Tool pointing forward along the Z axis, handle closest to viewer. \
            No text, no logos, no humans. Clean industrial design." \
  --geometry-file-format glb \
  --quality medium \
  --tier Sketch \
  --output ./generated_models \
  --api-key vibecoding
```

#### Image-to-3D (Rodin)

**Preferred for recognizable tools** — a wrench should look like a specific wrench, not a generic one. Reference photos produce more accurate geometry.

```bash
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py \
  --image reference_images/torque_wrench_front.jpg \
  --geometry-file-format glb \
  --quality high \
  --tier Regular \
  --output ./generated_models \
  --api-key vibecoding
```

**Finding reference images:**
- OSE Wiki: https://wiki.opensourceecology.org/
- Manufacturer product pages (use for reference, not for branding)
- Photograph the actual tool if available

**Image-to-3D tips:**
- Clean background (white or neutral)
- Single tool, centered, well-lit
- Front-facing or 3/4 angle — avoid top-down
- Remove text/logos from image if possible
- **Single image works best** — composites or multi-angle collages get fused into a blob
- Rodin minimum resolution is 512×512; pad smaller crops if needed
- A clean single-object photograph consistently outperforms text-to-3D for recognizable tools

#### Prompt Guidelines — Lessons Learned

1. **Always include dimensions**: "approximately 18 inches long, 2 inches wide, 2 inches deep"
2. **Include aspect ratio**: "longest dimension is length at 18 inches"
3. **State orientation**: "tool pointing forward along Z axis, handle closest to viewer"
4. **Describe material**: "chrome vanadium steel", "rubber grip", "cast iron body"
5. **Negative prompts**: "No text, no logos, no humans, no background"
6. **Be specific about the tool type**: "1/2 inch drive torque wrench" not just "wrench"
7. **Image-to-3D over text-to-3D for tools with distinctive shapes** — text prompts struggle with specific mechanical geometry (e.g. ratchet heads, wrench jaws)
8. **Expect diagonal mesh orientation** — Rodin models are frequently baked at arbitrary angles; `ComputeUprightCorrection` handles this at runtime via vertex-based principal axis detection
9. **Use Regular tier for image-to-3D** — produces better detail than Sketch for photo references
10. **Rotating the reference photo** (e.g. to vertical) has minimal impact on output orientation — the model will orient however it wants

### 6.3 Import

```powershell
# Find the generated GLB
$glb = Get-ChildItem ".\generated_models\*\*.glb" | Sort-Object LastWriteTime | Select-Object -Last 1

# Copy to the authoring folder (source of truth)
Copy-Item $glb.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\tools\tool_torque_wrench.glb" -Force
```

The `PackageAssetPostprocessor` auto-syncs to StreamingAssets on import. Do **not** manually copy to StreamingAssets.

### 6.4 Normalize Scale

In Unity Editor: **OSE → Normalize Package Model Scales**

This reads the GLB's native bounding box, looks up the target dimensions in `PartDimensionCatalog`, and writes the correct uniform scale to `machine.json`.

**Uniform scaling** preserves the model's proportions. If the model looks wrong after normalization, the problem is in the model's proportions — fix at generation time, not scaling time.

### 6.4A CAD-Derived Tool Hardening Via FreeCAD + Blender CLI

When a tool has a real CAD source, use the CAD-first CLI pipeline documented in
[PART_AUTHORING_PIPELINE.md §9.11](PART_AUTHORING_PIPELINE.md#911-freecad--blender-cli-pipeline--end-to-end).
The scripts, folder structure, material conventions, and lessons learned are identical for parts and tools.

Tool-specific notes:

- Use `center` mode (not `base_center`) for handheld tools — the pivot should be at the grip center.
- Deploy approved GLBs to `assets/tools/` instead of `assets/parts/`.
- The tool's `assetRef` in `machine.json` is just the filename (e.g. `tool_clamp_approved.glb`).

### 6.5 Verify Proportions

```bash
python verify_proportions.py tool_torque_wrench "Assets\_Project\Data\Packages\power_cube_frame\assets\tools\tool_torque_wrench.glb"
```

| Result | Meaning | Action |
|--------|---------|--------|
| **PASS** (≤20% deviation) | Proportions match catalog | Deploy |
| **WARN** (20-40%) | Noticeable distortion | Consider re-generating |
| **FAIL** (>40%) | Severely wrong proportions | Must re-generate |

---

## 7. Quality Checklist — Per Tool

Before marking a tool asset as complete:

- [ ] GLB is a real 3D model (not a placeholder cube)
- [ ] Dimensions in `PartDimensionCatalog.cs` match real-world tool size
- [ ] `verify_proportions.py` returns PASS or WARN
- [ ] Orientation: business end along +Z, handle along -Z, Y-up
- [ ] Scale normalized via OSE menu (uniform scale in `machine.json`)
- [ ] Tool equips in-game and preview renders with correct size
- [ ] Preview visual: semi-transparent with original textures, renders on top of scene (ZTest Always)
- [ ] Preview turns green (ready state color) when near valid target; restores original textures when moving away
- [ ] Tool texture/materials visible through preview transparency
- [ ] No Z-fighting, no inverted normals, no missing faces

---

## 8. XR Hand Grip Convention (Extension Point)

> **Status**: Schema-defined, not yet wired at runtime. Include `gripConfig` in tool definitions now; it will be ignored until the grip system is implemented.

Each tool can define how XR hand models attach to it:

```json
"gripConfig": {
  "gripPoint": { "x": 0.0, "y": 0.0, "z": -0.15 },
  "gripRotation": { "x": 0, "y": 0, "z": 0 },
  "handedness": "right",
  "poseHint": "power_grip"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `gripPoint` | vec3 | Local offset from tool origin where hand center attaches |
| `gripRotation` | vec3 | Euler angles for hand orientation adjustment |
| `handedness` | enum | `right`, `left`, `either` — preferred hand |
| `poseHint` | enum | `power_grip`, `pinch`, `precision`, `two_hand` — drives hand animation |

### Pose Hints

| Pose | Description | Tools |
|------|-------------|-------|
| `power_grip` | Full fist around cylindrical handle | Torque wrench, angle grinder, welder |
| `pinch` | Thumb + index finger, small object | Wire crimper, multimeter probe |
| `precision` | Fingertip control, delicate hold | Tape measure hook, line wrench |
| `two_hand` | Both hands required | Large welder, framing square |

### Authoring Grip Points

When creating/generating a tool GLB, keep the model origin at the natural grip center. This means:
- For a wrench: origin at the middle of the handle, not at the socket end
- For a grinder: origin at the main grip, not at the disc
- The `gripPoint` offset in `gripConfig` provides fine-tuning on top of this

Infrastructure already in place:
- `com.unity.xr.hands` 1.7.3 with `HandVisualizer` samples
- `XRGrabInteractable` pattern used for parts (extends to tools)
- `XR Interaction Toolkit` 3.3.1 grab flow

---

## 9. Tool Effects Architecture (Extension Point)

> **Status**: Schema-defined, not yet wired at runtime. Effects will be implemented progressively.

Tool actions can trigger visual feedback through **three composable layers**:

### Layer 1: Particle Systems (transient — during action)

Active effects that play while the tool is in use:

| Effect | Tools | Description |
|--------|-------|-------------|
| Spark cone | Welder, grinder | Orange/white particles, conical emission from contact point |
| Arc glow | Welder | Bright point light + soft emissive particle at weld point |
| Smoke trail | Welder, grinder | Low-alpha grey particles rising from contact |
| Dust cloud | Grinder, saw | Heavier grey particles with gravity |
| Heat shimmer | Welder | Distortion/refraction effect near contact |

**Convention**: Particle prefabs stored in `assets/effects/` within the package. Referenced by `prefabRef` in the effect definition.

### Layer 2: Decal Projectors (persistent — after action)

Surface marks left by tool actions:

| Effect | Tools | Description |
|--------|-------|-------------|
| Weld seam | Welder | Linear bead texture along joint |
| Cut line | Grinder, saw | Thin line marking where material was cut |
| Scorch mark | Welder, grinder | Darkened area near contact point |
| Measurement mark | Tape measure, soapstone | Temporary line or mark on surface |

**Implementation**: URP Decal Projectors positioned at the tool target's transform. Decal textures stored in `assets/effects/`.

### Layer 3: Mesh/Material Effects (state transitions)

Structural or visual changes to parts:

| Effect | Tools | Description |
|--------|-------|-------------|
| Emissive fade | Welder | Fresh weld glows orange → cools to grey over N seconds |
| Color shift | Grinder | Surface changes from rough to smooth finish at grind point |
| Mesh swap | Grinder, saw | Whole tube replaced with pre-cut version |
| Line renderer | Tape measure | Visible line between two measurement points |
| Readout display | Multimeter, torque wrench | Floating UI showing value (PSI, ft-lbs) |

### Effect Composition

A single tool action composes multiple layers. Example — **welding a joint**:

```
1. User holds on weld target
   → Layer 1: spark particles + arc glow play at contact point
   
2. Action completes (duration met or click count reached)
   → Layer 1: particles stop
   → Layer 2: weld bead decal appears along joint
   → Layer 3: emissive orange glow on bead, fades to grey over 3 seconds

3. Step advances
   → Decal persists (weld is permanent)
   → Emissive effect has already faded
```

### Effect Schema (per-action in step definitions)

```json
"effects": [
  {
    "layer": "particle",
    "prefabRef": "assets/effects/weld_sparks.prefab",
    "attachPoint": "tip",
    "duration": 0
  },
  {
    "layer": "decal",
    "textureRef": "assets/effects/weld_bead.png",
    "size": { "x": 0.02, "y": 0.15, "z": 0.02 },
    "persistAfterStep": true
  },
  {
    "layer": "material",
    "type": "emissive_fade",
    "color": { "r": 1.0, "g": 0.5, "b": 0.1 },
    "fadeDuration": 3.0
  }
]
```

### Asset Convention

```
Assets/_Project/Data/Packages/<package_id>/assets/
├── parts/          ← Part GLBs
├── tools/          ← Tool GLBs
└── effects/        ← Particle prefabs, decal textures, swap meshes
```

---

## 10. Tool Interaction Tiers

The same tool and effects work across all platforms. Only the **trigger mechanism** varies:

| Tier | Platform | Interaction | Effects |
|------|----------|-------------|---------|
| **Simple** | Mobile, PC | Tap/click on target → instant action | All 3 layers play |
| **Duration** | PC, XR | Hold pointer/trigger on target for N seconds | Particles ramp with progress; decal + material on complete |
| **Gesture** | XR (hand tracking) | Tracked hand motion matches tool-specific pattern | Same effects + haptic feedback |

### Gesture Patterns (future, for reference)

| Tool | Gesture | Description |
|------|---------|-------------|
| Torque wrench | Twist | Clockwise rotation of wrist |
| Tape measure | Pull | Linear hand extension |
| Welder | Steady hold | Hold position stable for duration |
| Angle grinder | Sweep | Linear motion along cut line |
| Wire crimper | Squeeze | Pinch gesture closing |

The tiered system is designed so that **effects are independent of interaction method**. A weld always sparks — whether triggered by a tap or a hand gesture.

### 10.1 Tool Animation System (Extension Point)

> **Status**: Interface defined (`IToolAnimator`), runtime wiring not yet implemented. Attach animators to tool prefabs now; they will be discovered and called when the hooks are wired.

Tool animations are **model-level behaviors** that animate the tool itself (distinct from environmental particle/decal effects in §9). Examples:

| Tool | Animation | Triggered By |
|------|-----------|-------------|
| Torque wrench | Ratchet head clicks, slight rotation | `OnActionExecuted` |
| Tape measure | Tape blade extends from case to target point | `OnReadyStateEnter` / `OnActionExecuted` |
| Angle grinder | Disc spins up on approach, full speed on action | `OnReadyStateEnter` / `OnActionExecuted` |
| Wire crimper | Jaws close progressively | `OnActionExecuted` (with `actionProgress`) |
| Multimeter | Display shows reading value | `OnActionExecuted` |
| Welder | Tip glows orange on approach | `OnReadyStateEnter` |

#### IToolAnimator Interface

```csharp
public interface IToolAnimator
{
    void OnToolEquipped(GameObject toolPreview);
    void OnReadyStateEnter(Vector3 targetWorldPos);
    void OnReadyStateExit();
    void OnActionExecuted(Vector3 targetWorldPos, float actionProgress, bool isComplete);
    void OnToolUnequipped();
}
```

Implement on a MonoBehaviour attached to the tool GLB prefab or instantiated alongside it. Multiple animators can coexist on one tool (e.g. spin animation + sound trigger + haptic pulse).

#### Schema Extension (future)

```json
"animationConfig": {
  "idleClip": "assets/tools/anims/wrench_idle.anim",
  "readyClip": "assets/tools/anims/wrench_ready.anim",
  "actionClip": "assets/tools/anims/wrench_tighten.anim",
  "actionDuration": 0.5,
  "loopIdle": true
}
```

#### How Animations Compose with Effects

```
1. Tool approaches target
   → IToolAnimator.OnReadyStateEnter() — tool-level animation (disc spins up)
   → Preview turns green (existing visual feedback)
   
2. User taps/clicks
   → IToolAnimator.OnActionExecuted() — tool animation plays (ratchet clicks)
   → Layer 1 effects fire (particles)
   → Layer 2 effects apply (decals)
   → Layer 3 effects trigger (material changes)
   
3. Action complete
   → IToolAnimator.OnToolUnequipped() — cleanup
   → Step advances
```

#### Implementation Notes for Later

- Wire lifecycle calls in `PartInteractionBridge`: query `_toolPreviewIndicator.GetComponents<IToolAnimator>()` at spawn, call methods at ready-state transitions and action execution
- For tape measure: use a `LineRenderer` in `OnReadyStateEnter` from tool tip to `targetWorldPos`; works on mobile/PC/XR
- For ratchet tools: simple `Transform.Rotate` coroutine on the head sub-object
- For grinder: `ParticleSystem.Play/Stop` on the disc sub-object

---

## 11. Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| Tool appears as capsule | `assetRef` missing or GLB not found | Check path in machine.json, verify GLB exists |
| Tool is giant/tiny | Missing catalog entry or normalizer not run | Add to `PartDimensionCatalog.cs`, run OSE → Normalize |
| Tool is sideways | GLB orientation wrong (+Z not forward) | Re-generate with orientation prompt, don't add rotation hacks |
| Tool has no texture through preview | MaterialHelper strips all materials | Expected — preview shows base color + emission. Original textures show when tool is used on target |
| Proportions look wrong | AI didn't respect dimension prompt | Re-generate with more explicit proportions, try image-to-3D |
| `verify_proportions.py` says FAIL | Model aspect ratio >40% off catalog | Re-generate. Include "approximately X inches wide, Y inches tall, Z inches deep" in prompt |

---

## 12. Power Cube Tool Manifest

10 tools in the `power_cube_frame` package:

| Tool ID | Category | Dimensions (m) | Generation Method | Status |
|---------|----------|-----------------|-------------------|--------|
| `tool_tape_measure` | measurement | 0.08 × 0.08 × 0.04 | Text-to-3D (Sketch) | **Done** |
| `tool_framing_square` | measurement | 0.40 × 0.30 × 0.005 | Text-to-3D | Placeholder |
| `tool_clamp` | hand_tool | 0.25 × 0.10 × 0.05 | Text-to-3D | Placeholder |
| `tool_welder` | power_tool | 0.40 × 0.30 × 0.25 | Image-to-3D recommended | Placeholder |
| `tool_angle_grinder` | power_tool | 0.30 × 0.10 × 0.10 | Image-to-3D recommended | Placeholder |
| `tool_torque_wrench` | hand_tool | 0.45 × 0.05 × 0.05 | Image-to-3D (single photo) | **Done** |
| `tool_socket_set` | hand_tool | 0.30 × 0.08 × 0.15 | Text-to-3D | Placeholder |
| `tool_line_wrench` | hand_tool | 0.20 × 0.03 × 0.02 | Text-to-3D | Placeholder |
| `tool_wire_crimper` | hand_tool | 0.22 × 0.06 × 0.02 | Text-to-3D | Placeholder |
| `tool_multimeter` | measurement | 0.08 × 0.15 × 0.04 | Text-to-3D | Placeholder |

### Recommended Prompts

| Tool ID | Prompt |
|---------|--------|
| `tool_torque_wrench` | "Professional 1/2 inch drive torque wrench, chrome vanadium steel shaft, black rubber grip handle, ratchet head with socket end. Approximately 18 inches long, 2 inches wide, 2 inches deep. Tool pointing forward along Z axis, handle closest to viewer. No text, no logos, no humans. Clean industrial design." |
| `tool_tape_measure` | "Retractable tape measure, closed compact case only. Yellow plastic round case, chrome belt clip on back, small metal hook visible at tape slot. NO extended tape blade, tape is fully retracted inside. Approximately 3 inches wide, 3 inches tall, 1.5 inches deep. Compact round puck shape. No text, no logos, no humans." |
| `tool_framing_square` | "Steel framing square (L-shaped), 24 inch long arm and 16 inch short arm, flat profile, industrial brushed steel finish. Approximately 16 inches wide, 12 inches tall, very thin. No text, no logos, no humans." |
| `tool_clamp` | "Quick-grip bar clamp, 12 inch capacity, orange/black plastic handles with steel bar, trigger mechanism. Approximately 10 inches long, 4 inches tall, 2 inches deep. No text, no logos, no humans." |
| `tool_welder` | "MIG welding gun/torch, curved neck, brass contact tip, trigger grip handle, blue insulated cable visible. Approximately 16 inches long, 12 inches tall, 10 inches deep. Tool pointing forward, handle toward viewer. No text, no logos, no humans." |
| `tool_angle_grinder` | "DeWalt 4.5 inch angle grinder, yellow/black body, side handle, grinding disc visible, paddle switch. Approximately 12 inches long, 4 inches wide, 4 inches deep. Disc end pointing forward. No text, no logos, no humans." |
| `tool_socket_set` | "Single 1/2 inch drive socket and ratchet combination, chrome vanadium, ratchet handle with socket attached. Approximately 12 inches long, 3 inches wide, 6 inches deep. No text, no logos, no humans." |
| `tool_line_wrench` | "Flare-nut (line) wrench, 3/8 inch, chrome vanadium steel, open-end with slot cut, flat profile. Approximately 8 inches long, 1 inch wide, very thin. No text, no logos, no humans." |
| `tool_wire_crimper` | "Ratcheting wire terminal crimper, red/black handles, multiple die positions, compound leverage. Approximately 9 inches long, 2.5 inches wide, 1 inch deep. No text, no logos, no humans." |
| `tool_multimeter` | "Digital multimeter, yellow/black case, LCD display, two test probes, rotary selector dial. Approximately 3 inches wide, 6 inches tall, 1.5 inches deep. Display facing forward. No text, no logos, no humans." |

---

## 13. Complete Walkthrough — Single Tool

End-to-end example for `tool_torque_wrench`:

### Step 1: Verify catalog dimensions exist

```bash
python -c "
from verify_proportions import CATALOG
dims = CATALOG.get('tool_torque_wrench')
print(f'Catalog: W={dims[0]}m, H={dims[1]}m, D={dims[2]}m')
"
```

### Step 2: Generate 3D model

**Preferred: Image-to-3D** (for tools with distinctive shapes):

```bash
# Use a clean single-object photo (512x512 minimum)
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py \
  --image reference_images/torque_wrench.jpg \
  --geometry-file-format glb --quality high --tier Regular \
  --output ./generated_models --api-key vibecoding
```

**Fallback: Text-to-3D** (for generic shapes):

```bash
python .github/skills/rodin3d-skills/skills/rodin3d-skill/scripts/generate_3d_model.py \
  --prompt "Professional 1/2 inch drive torque wrench, chrome vanadium steel shaft, ..." \
  --geometry-file-format glb --quality medium --tier Sketch \
  --output ./generated_models --api-key vibecoding
```

> **Note**: `--image` and `--prompt` are mutually exclusive in Rodin. Image-to-3D consistently produces more recognizable tool shapes than text prompts. Expect the output mesh to be oriented at an arbitrary angle — the runtime `ComputeUprightCorrection` handles alignment automatically.

### Step 3: Verify proportions before importing

```bash
$glb = Get-ChildItem ".\generated_models\*\*.glb" | Sort-Object LastWriteTime | Select-Object -Last 1
python verify_proportions.py tool_torque_wrench $glb.FullName
```

If FAIL → re-generate with adjusted prompt. If PASS/WARN → proceed.

### Step 4: Import to package

```powershell
Copy-Item $glb.FullName "Assets\_Project\Data\Packages\power_cube_frame\assets\tools\tool_torque_wrench.glb" -Force
```

### Step 5: Normalize scale in Unity

Menu: **OSE → Normalize Package Model Scales**

### Step 6: Verify in-game

1. Enter Play mode
2. Navigate to a step that requires the torque wrench
3. Equip the tool from the tool dock
4. Confirm: correct size, orientation (business end forward), preview effect with textures
5. Confirm: green ready state when near tool targets

---

## 14. Anti-Patterns

| Don't | Do instead |
|-------|-----------|
| Add rotation offsets to fix a bad model | Re-generate, or rely on `ComputeUprightCorrection` for runtime alignment |
| Use per-axis scaling on organic tool shapes | Use uniform scaling; fix proportions at generation time |
| Manually copy GLBs to StreamingAssets | Let `PackageAssetPostprocessor` auto-sync |
| Skip proportion verification | Always run `verify_proportions.py` before deploying |
| Hardcode effect behavior in C# per tool | Define effects in `machine.json`, implement generic players |
| Generate without dimensions in the prompt | Always include "approximately X inches wide, Y tall, Z deep" |
| Use placeholder descriptions for grip | Document actual grip style even before runtime wiring |
| Use multi-angle composites for image-to-3D | Use a single clean photo — composites get fused into one blob |
| Use text-to-3D for mechanically distinctive tools | Use image-to-3D with a reference photo for better shape accuracy |

---

## 15. File Reference

| File | Purpose |
|------|---------|
| `Assets/_Project/Data/Packages/<pkg>/machine.json` | Tool definitions, effect configs |
| `Assets/_Project/Data/Packages/<pkg>/assets/tools/` | Tool GLB models |
| `Assets/_Project/Data/Packages/<pkg>/assets/effects/` | Particle prefabs, decal textures |
| `Assets/_Project/Scripts/Editor/PartDimensionCatalog.cs` | Real-world dimensions for all tools |
| `Assets/_Project/Scripts/Editor/PackageModelNormalizer.cs` | Uniform scale computation |
| `Assets/_Project/Scripts/UI/Root/PartInteractionBridge.cs` | Tool preview rendering, orientation correction, interaction |
| `Assets/_Project/Scripts/UI/Root/MaterialHelper.cs` | Preview transparency, ready-state color, overlay rendering |
| `Assets/_Project/Scripts/Runtime/Session/ToolRuntimeController.cs` | Tool equip/action logic |
| `verify_proportions.py` | CLI proportion validation |
| `.github/skills/rodin3d-skills/` | Rodin 3D generation scripts |

---

## 16. Relationship to Part Pipeline

This pipeline **shares infrastructure** with the part pipeline:

| Shared | Tool-Specific |
|--------|--------------|
| `PartDimensionCatalog` (includes tools) | Orientation: +Z forward, -Z grip |
| `PackageModelNormalizer` (processes tools) | `gripConfig` extension point |
| `verify_proportions.py` (validates tools) | Three-layer effect system |
| `PackageAssetPostprocessor` (auto-syncs) | Interaction tiers (simple/duration/gesture) |
| Same GLB format and import flow | `assets/tools/` subfolder convention |
| Same Rodin generation scripts | Tool-specific prompt patterns |

The separation into two documents reflects the **different authoring concerns** — parts are placed and snapped; tools are held and used. The underlying technical pipeline is identical.
