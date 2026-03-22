# STEP_VIEW_FRAMING.md

## OSE XR Foundation -- Step View Framing

This document defines the **Step View Framing** concept -- the authoritative taxonomy for how the camera should frame each assembly step's spatial context during guided training.

Step View Framing is the sixth canonical concept in the project taxonomy:

| # | Concept | Answers | Authority Doc |
|---|---------|---------|---------------|
| 1 | Entity Role | What is this object? | INTERACTION_PATTERN_MATRIX.md |
| 2 | Step Family | What is the semantic meaning? | STEP_CAPABILITY_MATRIX.md |
| 3 | Interaction Pattern | How does the learner perform it? | INTERACTION_PATTERN_MATRIX.md |
| 4 | Profile | What specialized variation? | STEP_CAPABILITY_MATRIX.md |
| 5 | Payloads | What feedback/guidance/validation? | STEP_CAPABILITY_MATRIX.md |
| **6** | **Step View Framing** | **How should the step be visually framed?** | **STEP_VIEW_FRAMING.md** |

This file should be used together with:

- `docs/STEP_CAPABILITY_MATRIX.md`
- `docs/INTERACTION_PATTERN_MATRIX.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/DATA_SCHEMA.md`
- `docs/SOURCE_OF_TRUTH_MAP.md`

---

# 1. Purpose

The runtime has camera mechanics (orbital rig, pivot sources, viewpoint presets) but no camera **semantics** -- no classification of what each step type needs visually.

Step View Framing fills this gap by defining:

- **View Mode** -- a semantic classification of how the camera should frame a step's spatial context
- **Framing Behavior** -- how and when the camera transitions on step lifecycle events
- **Recovery Affordances** -- how the learner returns to a useful perspective after manual exploration

The goal is **guided, not free-roam**. Each step should present the learner with an intentional, recoverable perspective that reduces cognitive load and minimizes the need for manual camera manipulation -- especially on mobile.

---

# 2. Design Principles

## 2.1 Guided, Not Free-Roam

The camera serves the instructional intent of each step. The learner should not need to hunt for the right viewing angle before they can act. The system provides a useful default perspective on every step activation.

## 2.2 Recoverable

The learner can always return to a known-good perspective:

- **Back** -- return to the previous step's framing state
- **Step Home** -- return to the current step's authored/default framing

These affordances remove the anxiety of "losing" a good camera angle.

## 2.3 Non-Jarring

Camera transitions use soft animated movement, not hard cuts. The learner should always understand spatial continuity between steps. Transition speed adapts to the distance traveled -- short pivots are quick, large reframes are slower.

## 2.4 Selection Does Not Reposition Camera

Selecting a part (tap to highlight) should **not** trigger a full camera reframe. Selection may adjust the pivot point subtly, but the camera position and orientation remain stable. This prevents disorientation when the learner is exploring parts within a step.

Full reframing occurs only on **step activation** or explicit **recovery actions** (Back / Step Home).

## 2.5 Focus Is Intentional

Every camera movement must have a clear instructional reason. The system does not move the camera for decorative or cinematic purposes during active assembly. Camera transitions serve spatial comprehension, not spectacle.

---

# 3. View Modes

A view mode is a semantic classification that describes **what spatial context a step needs the learner to see**. It is not a camera angle -- it is a framing intent that the runtime resolves into camera parameters.

| View Mode | Description | Camera Behavior | Pivot Target |
|-----------|-------------|-----------------|--------------|
| **SourceAndTarget** | Frame the source part and its ghost/target zone together | Fit bounds of source + target | Midpoint of source and target |
| **PairEndpoints** | Frame both endpoints of a connection or measurement | Fit bounds of endpoint A + endpoint B | Midpoint of A and B |
| **WorkZone** | Frame the tool target area at working distance | Focus on target cluster centroid | Target centroid |
| **PathView** | Frame a linear work path (cut line, weld seam) | Frame path extent with slight pull-back | Path midpoint |
| **Overview** | Wide shot of the full assembly | Frame assembly bounds | Assembly center |
| **Inspect** | Close-up detail view for verification | Tight focus on inspection target | Inspection target |

### View Mode vs. Viewpoint

A **view mode** is a semantic classification (e.g. "show source and target together"). A **viewpoint** is a concrete camera parameterization (yaw, pitch, distance, pivot offset). The runtime resolves view modes into viewpoints using spatial data from the step's targets, parts, and ghost positions.

The existing `ViewpointLibrary` presets (Front, Side, Top, Iso, Detail) remain available as manual overrides or as starting orientations that the view mode system refines with step-specific spatial data.

---

# 4. Family-to-View-Mode Default Mapping

View modes resolve from family + profile using the same pattern as interaction patterns. When a step does not declare an explicit `viewMode`, the runtime resolves it automatically.

### Family Defaults

| Family | Default View Mode | Rationale |
|--------|-------------------|-----------|
| Place | SourceAndTarget | Learner needs to see both the part and where it goes |
| Use | WorkZone | Learner needs the tool target area at working distance |
| Connect | PairEndpoints | Learner needs to see both connection endpoints |
| Confirm | Overview | Confirmation steps are review/acknowledgment -- wide context |

### Profile Overrides

| Profile | Family | View Mode Override | Rationale |
|---------|--------|-------------------|-----------|
| Place.Default | Place | SourceAndTarget | -- |
| Place.Clamp | Place | SourceAndTarget | -- |
| Use.Default | Use | WorkZone | -- |
| Use.Torque | Use | WorkZone | Bolt cluster at working distance |
| Use.Weld | Use | PathView | Weld seam requires path framing |
| Use.Cut | Use | PathView | Cut line requires path framing |
| Use.Measure | Use | PairEndpoints | Measurement spans two anchors |
| Connect.Cable | Connect | PairEndpoints | Cable connects two port endpoints |
| Confirm.Default | Confirm | Overview | -- |

### Resolution Order

1. If the step declares an explicit `viewMode`, use it directly.
2. If `viewMode` is absent but a profile override exists in the table above, use the profile override.
3. Otherwise, use the family default.

---

# 5. Framing Behavior

Framing behavior defines **when and how** the camera transitions during the step lifecycle.

## 5.1 On Step Activation

When a new step becomes active, the runtime resolves the step's view mode and applies a **soft assist** transition:

1. Resolve the view mode (family + profile + optional override).
2. Compute the target viewpoint from the view mode and the step's spatial data (ghost positions, target positions, part bounds).
3. Animate the camera from its current position to the target viewpoint over a short duration.
4. Store the target viewpoint as the step's **home framing** for recovery.

The transition is always animated, never a hard cut. If the camera is already close to the target framing (e.g. consecutive steps in the same work area), the transition is minimal.

## 5.2 On Part Selection

Selecting a part within an active step does **not** trigger a full reframe. The system may:

- Adjust the orbit pivot point toward the selected part (subtle, non-disruptive).
- Keep the camera position and orientation stable.

This ensures the learner can tap parts to learn about them without losing spatial context.

## 5.3 On Step Completion

When a step completes, the camera holds its current position. The next step's activation (5.1) handles the transition. There is no intermediate "celebration camera" -- completion feedback is visual effects and UI, not camera movement.

## 5.4 Recovery: Back

**Back** returns the camera to the **previous step's home framing**. This is the framing that was computed when the previous step was activated. The transition is animated.

Use case: the learner advances to a new step, the camera reframes, but they want to review the previous work area.

## 5.5 Recovery: Step Home

**Step Home** returns the camera to the **current step's home framing**. This is the framing computed during step activation (5.1).

Use case: the learner manually orbited/zoomed the camera and wants to return to the system's recommended perspective for the current step.

---

# 6. Schema Integration

## 6.1 Optional `viewMode` Field

Steps may declare an explicit `viewMode` in machine.json to override the resolved default:

```json
{
  "id": "step_final_square_check",
  "family": "Confirm",
  "viewMode": "Inspect"
}
```

When `viewMode` is absent, the runtime resolves it from family + profile using the tables in section 4.

## 6.2 What `viewMode` Is Not

`viewMode` is **not** an authored camera angle. It does not specify yaw, pitch, distance, or pivot offset. It is a semantic classification that the runtime resolves into camera parameters using the step's spatial data.

Content authors should think: "what does the learner need to see?" not "what camera angle should we use?"

## 6.3 Valid Values

- `SourceAndTarget`
- `PairEndpoints`
- `WorkZone`
- `PathView`
- `Overview`
- `Inspect`

When the field is absent or null, the runtime resolves the view mode from family + profile. Unknown values fall back to the family default.

---

# 7. Recovery Affordances

Recovery affordances are learner-facing actions that restore a known-good camera perspective.

| Affordance | Target Framing | When Available |
|------------|----------------|----------------|
| **Back** | Previous step's home framing | When there is a previous step in the sequence |
| **Step Home** | Current step's home framing | Always during an active step |

### Implementation Notes

- The runtime maintains a stack of home framings (one per activated step).
- **Back** pops to the previous entry. Multiple Back actions walk backward through the stack.
- **Step Home** returns to the top of the stack (current step's entry) without modifying the stack.
- Recovery transitions are animated using the same soft-assist timing as step activation transitions.

---

# 8. Existing Runtime Infrastructure

The runtime already has the camera mechanics that Step View Framing will orchestrate:

| Component | Role | File |
|-----------|------|------|
| `AssemblyCameraRig` | Orbital camera with `FocusOn()`, `FrameBounds()`, `ApplyViewpoint()`, `SetPivot()` | `Interaction.V2/Camera/AssemblyCameraRig.cs` |
| `StepViewpoint` | Struct: Yaw, Pitch, Distance, PivotOffset, Label | `Interaction.V2/Guidance/StepViewpoint.cs` |
| `ViewpointLibrary` | 5 presets: Front, Side, Top, Iso, Detail | `Interaction.V2/Guidance/StepViewpoint.cs` |
| `CameraPivotResolver` | PivotSource enum: AssemblyCenter, SelectedPart, GhostTarget, StepTarget, Custom | `Interaction.V2/Camera/CameraPivotResolver.cs` |
| `StepGuidanceService` | Bridge between step activation and camera commands | `Interaction.V2/Guidance/StepGuidanceService.cs` |
| `InteractionSettings` | Feature toggles: EnableAutoFraming, EnableStepViewGuidance, EnablePivotToTarget, EnableSuggestedViews | `Interaction.V2/Core/InteractionSettings.cs` |

Step View Framing adds the **semantic layer** on top of these mechanics. The view mode classification tells the camera system *what to frame*; the existing infrastructure handles *how to frame it*.

---

# 9. Companion Documents

| Document | Relationship |
|----------|-------------|
| `STEP_CAPABILITY_MATRIX.md` | Defines families and profiles; view modes map from these |
| `INTERACTION_PATTERN_MATRIX.md` | Defines interaction patterns; view modes complement patterns (pattern = how to interact, view mode = what to see) |
| `DATA_SCHEMA.md` | Schema definition for the optional `viewMode` field on steps |
| `ASSEMBLY_RUNTIME.md` | Step activation lifecycle where view mode resolution occurs |
| `SOURCE_OF_TRUTH_MAP.md` | Authority entry for Step View Framing |
