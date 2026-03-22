# STEP_CAPABILITY_MATRIX.md

## Purpose

This document defines the **Step Capability Matrix** — the authoritative taxonomy for authored assembly steps in the XR training application.

Its goal is to replace ad-hoc, one-off interaction mechanics with a small set of **families** and **profiles** that the runtime can dispatch generically.

Every authored step must map to exactly one family. A step may optionally declare a family-scoped profile that refines its behavior within that family.

This file should be used together with:

- `docs/DATA_SCHEMA.md`
- `docs/CONTENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/SOURCE_OF_TRUTH_MAP.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`
- `docs/INTERACTION_PATTERN_MATRIX.md`
- `docs/STEP_VIEW_FRAMING.md`

---

# 1. Core Principle

The runtime must not contain a unique code path for every kind of assembly action.

Instead, every step is classified by:

1. **Family** — the semantic meaning of the step (what it achieves)
2. **Profile** — a family-scoped refinement that selects specific behavior, effects, or validation within that family
3. **Interaction Pattern** — the reusable learner-facing physical interaction that implements the step (see `INTERACTION_PATTERN_MATRIX.md`)

The runtime dispatches on family first, then applies profile-specific adjustments. The interaction pattern is resolved automatically from family + profile + step data — it is not authored explicitly.

Unknown profiles fall back to the family default — no crashes, no one-off code.

---

# 2. Families

A family defines the fundamental user interaction that completes a step.

## 2.1 Place

The user moves a source object to a target position and releases it.

**Interaction contract:**
- One or more ghost targets are spawned
- The user grabs a part, moves it toward a target, and releases
- Validation checks position tolerance, rotation tolerance, and part identity
- On success the part snaps to the target

**Examples:**
- Place a bracket on a frame corner
- Insert a bolt into a hole
- Seat a bearing into a housing

**Default interaction pattern:** PlaceOnZone (see `INTERACTION_PATTERN_MATRIX.md` §3.1)

**Default view mode:** SourceAndTarget (see `STEP_VIEW_FRAMING.md` §4)

**Current runtime mapping:** `completionType: "placement"`

---

## 2.2 Use

The user wields a tool against one or more targets.

**Interaction contract:**
- A tool is equipped (auto-equip or manual)
- The user activates the tool on each required target
- Validation checks that all targets have been acted upon
- Tool identity must match the step requirement

**Examples:**
- Tighten bolts with a torque wrench
- Weld a joint with a welder
- Cut a plate with an angle grinder
- Measure a gap with a tape measure

**Default interaction pattern:** TargetHit; varies by profile -- see `INTERACTION_PATTERN_MATRIX.md` §4

**Default view mode:** WorkZone; varies by profile -- see `STEP_VIEW_FRAMING.md` §4

**Current runtime mapping:** `completionType: "tool_action"`

---

## 2.3 Connect

The user links two endpoints to form a connection.

**Interaction contract:**
- Two port markers are spawned (portA, portB)
- The user selects the first endpoint, then the second
- On completion a visual connection is rendered (spline, cable, hose)
- Validation checks that both endpoints are selected in a valid pairing

**Examples:**
- Attach a hydraulic hose between two fittings
- Route an electrical cable between terminals
- Connect a rigid pipe between flanges

**Default interaction pattern:** SelectPair (see `INTERACTION_PATTERN_MATRIX.md` §3.2)

**Default view mode:** PairEndpoints (see `STEP_VIEW_FRAMING.md` §4)

**Current runtime mapping:** `completionType: "pipe_connection"`

---

## 2.4 Confirm

The user acknowledges, verifies, or continues without a spatial interaction.

**Interaction contract:**
- No ghost targets, no tool equip, no port markers
- The user reads instructional content and presses Continue / Confirm
- Validation is implicit — the confirmation action itself completes the step
- May be used for safety warnings, pre-assembly checks, or instructional pauses

**Examples:**
- Acknowledge a safety warning before proceeding
- Verify a measurement reading matches a spec
- Review a completed subassembly before the next phase

**Default interaction pattern:** SingleConfirm (see `INTERACTION_PATTERN_MATRIX.md` §3.7)

**Default view mode:** Overview (see `STEP_VIEW_FRAMING.md` §4)

**Current runtime mapping:** `completionType: "confirmation"`

**Design note:** Confirm is a distinct family because it has no spatial intent. Collapsing it into Place would be semantically incorrect — Place implies a source object, a target, and positional validation. Confirm implies acknowledgement, verification, or continuation.

---

# 3. Profiles

A profile is a **family-scoped** refinement that selects specific behavior, effects, or validation constraints within a family.

Profiles are not global. A profile name is always qualified by its family:

- `Place.Clamp` — placement where a clamp secures the part
- `Use.Weld` — tool use with welding effects and hold-duration validation
- `Use.Cut` — tool use with cutting effects and path-following validation
- `Use.Torque` — tool use with torque feedback and click-confirmation
- `Connect.Cable` — connection with flexible cable rendering

This scoping prevents ambiguity. `Place.Clamp` (a fixture that secures a part during placement) is unrelated to a hypothetical future `Use.Clamp` (using a clamp tool).

## 3.1 Initial Profile Set

These profiles are defined based on authored content that exists or is planned.

### Place profiles

| Profile | Behavior refinement | Default Pattern | Default View Mode |
|---|---|---|---|
| *(default)* | Standard ghost-target drag-and-snap | PlaceOnZone | SourceAndTarget |
| `Clamp` | Placement where a clamp or fixture secures the part; may involve a secondary confirmation | PlaceOnZone | SourceAndTarget |

### Use profiles

| Profile | Behavior refinement | Default Pattern | Default View Mode |
|---|---|---|---|
| *(default)* | Generic tool-on-target tap interaction | TargetHit | WorkZone |
| `Torque` | Wrench on bolt; may show torque gauge; completion on click/threshold | TargetHit / OrderedTargets | WorkZone |
| `Measure` | Tape measure between two anchors; distance label display | SelectPair | PairEndpoints |
| `Weld` | Welder on joint; hold-duration validation; welding effects (sparks, glow) | HoldOnTarget *(future)* | PathView |
| `Cut` | Grinder/saw on material; path-following validation; cutting effects (sparks, dust) | PathProgress *(future)* | PathView |

### Connect profiles

| Profile | Behavior refinement | Default Pattern | Default View Mode |
|---|---|---|---|
| *(default)* | Two-click endpoint connection with spline rendering | SelectPair | PairEndpoints |
| `Cable` | Flexible cable/hose rendering; sag physics; flow animation on completion | SelectPair | PairEndpoints |

### Confirm profiles

No profiles are defined for Confirm at this time. The family default covers all current authored content.

## 3.2 Rules for Adding Profiles

1. A new profile must be authored in at least one machine package before it is added to this document.
2. A new profile must be scoped to exactly one family.
3. The profile name must describe the *real-world process*, not the runtime implementation.
4. Every family must have a sensible default behavior when no profile is specified.
5. A profile must not require a new runtime code path — it should parameterize existing family behavior. If it cannot, it may indicate a missing family.

---

# 4. Capability Shape

Every step in the system has the following authored shape. Note that **interaction pattern** is not an authored field — it is resolved at runtime from family + profile + step data (see `INTERACTION_PATTERN_MATRIX.md` §6).

```
Step
 ├─ family       : Place | Use | Connect | Confirm
 ├─ profile      : family-scoped (optional, defaults to family default)
 ├─ guidance     : instructional payload
 ├─ validation   : correctness-checking payload
 ├─ feedback     : immediate interaction-level payload
 ├─ reinforcement: learning-level payload
 └─ difficulty   : challenge-tuning settings
```

## 4.1 Guidance Payload

Pre-action instruction that prepares the user.

Contents:

- `instructionText` — what to do
- `whyItMattersText` — why this step exists
- `hintIds` — progressive hints available on request
- `contextualDiagramRef` — optional reference to a diagram or overlay (future)

Guidance is always available regardless of family or profile.

## 4.2 Validation Payload

Correctness checking that determines whether the step is complete.

Contents:

- `validationRuleIds` — references to validation rule definitions
- Position tolerance, rotation tolerance, part identity (Place family)
- Tool identity, target coverage, order constraints (Use family)
- Endpoint pairing, connection validity (Connect family)
- Implicit — confirmation action itself (Confirm family)

Validation behavior is family-dependent. Profiles may tighten or relax constraints but do not change the fundamental validation shape.

## 4.3 Feedback Payload

**Immediate, interaction-level** responses that occur during or right after the user's action.

Contents:

- `effectTriggerIds` — references to effect definitions
- Ghost/target highlights
- Magnetic snap behavior
- Valid/invalid color transitions (green/red)
- Sparks, glow, dust (process effects tied to the profile)
- Spline preview on first endpoint click (Connect family)
- Progress tick per target (Use family)

Feedback is about the *current interaction moment*. It tells the user "you are doing this right/wrong *right now*."

## 4.4 Reinforcement Payload

**Learning-level** responses that deepen understanding after the action.

Contents:

- Structural consequence explanation — what this step contributes to the whole
- Safety implication — what goes wrong if done incorrectly
- "What would happen if..." counterfactual note (optional)
- Milestone celebration — visual/audio reward on completion
- `milestoneMessage` — text summary of what was achieved

Reinforcement is about *transfer of learning*. It tells the user "here's why this matters for the real build."

## 4.5 Difficulty Settings

Per-step challenge tuning that adjusts the step's demands based on mode.

Contents:

- Tolerance overrides (tighter for challenge, looser for tutorial)
- Time limit (optional, challenge mode only)
- Hint availability (always/limited/none)
- Allow skip flag
- Challenge flags (score multiplier, penalty rules)

---

# 5. Capability Matrix

This table shows which runtime systems and payloads activate for each family.

| Capability | Place | Use | Connect | Confirm |
|---|:---:|:---:|:---:|:---:|
| Ghost targets spawned | yes | — | — | — |
| Tool auto-equip | — | yes | — | — |
| Port markers spawned | — | — | yes | — |
| Continue button only | — | — | — | yes |
| Guidance payload | yes | yes | yes | yes |
| Validation payload | spatial | tool+target | endpoint | implicit |
| Feedback payload | snap+color | process effects | spline preview | — |
| Reinforcement payload | yes | yes | yes | yes |
| Difficulty settings | yes | yes | yes | limited |
| Profile modifies behavior | optional | optional | optional | — |

### Profile refinements within families

| Family.Profile | Validation adjustment | Feedback adjustment |
|---|---|---|
| Place.Clamp | May add secondary confirmation | Clamp-engage visual/audio |
| Use.Torque | Click/threshold completion | Torque gauge overlay |
| Use.Weld | Hold-duration timer | Welding sparks + heat glow |
| Use.Cut | Path-following check | Cutting sparks + dust |
| Connect.Cable | Standard endpoint pairing | Flexible cable sag + flow animation |

---

# 6. Legacy Mapping

The current runtime uses `completionType` as a string field on `StepDefinition`.

The following mapping defines how legacy values translate to the capability matrix:

| `completionType` value | Family | Default profile |
|---|---|---|
| `placement` | Place | *(default)* |
| `tool_action` | Use | *(default)* |
| `pipe_connection` | Connect | Cable |
| `confirmation` | Confirm | *(default)* |

When the runtime migrates to family-based dispatch:

1. If `family` is present on a step, use it directly.
2. If `family` is absent, derive it from `completionType` using the table above.
3. If both are absent, default to `Place` (preserves current behavior where empty `completionType` means placement).
4. `completionType` remains supported indefinitely as a legacy alias. It is never removed from the schema — only marked as deprecated in documentation.

---

# 7. Authoring Examples

## 7.1 Place step — legacy vs target

**Legacy (current):**
```json
{
  "id": "step_place_plate",
  "completionType": "placement",
  "requiredPartIds": ["frame_plate_a"],
  "targetIds": ["target_plate_slot_a"],
  "validationRuleIds": ["validation_plate_alignment"],
  "hintIds": ["hint_check_plate_edge"],
  "effectTriggerIds": ["effect_valid_placement_pulse"]
}
```

**Target shape:**
```json
{
  "id": "step_place_plate",
  "family": "Place",
  "profile": null,
  "requiredPartIds": ["frame_plate_a"],
  "targetIds": ["target_plate_slot_a"],
  "guidance": {
    "instructionText": "Place the frame plate onto the marked slot.",
    "whyItMattersText": "The plate forms the structural base of the corner.",
    "hintIds": ["hint_check_plate_edge"]
  },
  "validation": {
    "validationRuleIds": ["validation_plate_alignment"]
  },
  "feedback": {
    "effectTriggerIds": ["effect_valid_placement_pulse"]
  },
  "reinforcement": {
    "milestoneMessage": "Base plate secured — the corner now has a foundation."
  }
}
```

## 7.2 Use step — legacy vs target

**Legacy (current):**
```json
{
  "id": "step_tighten_bolts",
  "completionType": "tool_action",
  "relevantToolIds": ["tool_torque_wrench"],
  "requiredToolActions": [
    { "targetId": "target_bolt_a", "toolId": "tool_torque_wrench" },
    { "targetId": "target_bolt_b", "toolId": "tool_torque_wrench" }
  ],
  "hintIds": ["hint_torque_sequence"],
  "effectTriggerIds": ["effect_bolt_tighten_click"]
}
```

**Target shape:**
```json
{
  "id": "step_tighten_bolts",
  "family": "Use",
  "profile": "Torque",
  "relevantToolIds": ["tool_torque_wrench"],
  "requiredToolActions": [
    { "targetId": "target_bolt_a", "toolId": "tool_torque_wrench" },
    { "targetId": "target_bolt_b", "toolId": "tool_torque_wrench" }
  ],
  "guidance": {
    "instructionText": "Tighten both bolts to specification using the torque wrench.",
    "whyItMattersText": "Proper torque prevents the bracket from loosening under load.",
    "hintIds": ["hint_torque_sequence"]
  },
  "validation": {
    "validationRuleIds": ["validation_bolt_torque"]
  },
  "feedback": {
    "effectTriggerIds": ["effect_bolt_tighten_click"]
  },
  "reinforcement": {
    "milestoneMessage": "Bolts torqued — the joint is now secure under rated load."
  }
}
```

## 7.3 Connect step — legacy vs target

**Legacy (current):**
```json
{
  "id": "step_connect_hose",
  "completionType": "pipe_connection",
  "targetIds": ["target_port_supply", "target_port_return"],
  "hintIds": ["hint_check_hose_routing"]
}
```

**Target shape:**
```json
{
  "id": "step_connect_hose",
  "family": "Connect",
  "profile": "Cable",
  "targetIds": ["target_port_supply", "target_port_return"],
  "guidance": {
    "instructionText": "Connect the hydraulic supply hose between the pump and the manifold.",
    "whyItMattersText": "This hose carries pressurized fluid to the actuator circuit.",
    "hintIds": ["hint_check_hose_routing"]
  },
  "validation": {
    "validationRuleIds": ["validation_hose_endpoints"]
  },
  "feedback": {
    "effectTriggerIds": ["effect_hose_connected_pulse"]
  },
  "reinforcement": {
    "milestoneMessage": "Supply line connected — hydraulic circuit is taking shape."
  }
}
```

## 7.4 Confirm step — legacy vs target

**Legacy (current):**
```json
{
  "id": "step_safety_check",
  "completionType": "confirmation",
  "instructionText": "Verify that all bolts are visible and no parts are loose before continuing."
}
```

**Target shape:**
```json
{
  "id": "step_safety_check",
  "family": "Confirm",
  "profile": null,
  "guidance": {
    "instructionText": "Verify that all bolts are visible and no parts are loose before continuing.",
    "whyItMattersText": "A loose part under load can cause structural failure or injury."
  },
  "reinforcement": {
    "milestoneMessage": "Safety check passed — assembly is structurally sound so far."
  }
}
```

---

# 8. Migration Path

The migration from `completionType` to the full capability matrix proceeds in phases.

## Phase 1 — Docs alignment ✅

- Define the matrix concept in this document.
- Update architecture docs to reference it.
- No runtime or schema code changes.
- `completionType` remains the active runtime field.

## Phase 2 — Schema bridge ✅ (Phase 14a in IMPLEMENTATION_CHECKLIST.md)

- Add `family` and `profile` as optional string fields to `StepDefinition.cs`.
- Add `ResolvedFamily` resolver: if `family` is null, derive from `completionType` using §6 rules.
- Add `IsConfirm` convenience property.
- Add `family` and valid profile values to `MachinePackageValidator.cs`.
- Existing `machine.json` files continue to work without changes.
- Validate zero behavior change.

## Phase 3 — Payload grouping ✅

- Five payload classes created: `StepGuidancePayload`, `StepValidationPayload`, `StepFeedbackPayload`, `StepReinforcementPayload`, `StepDifficultyPayload`.
- Added as optional fields on `StepDefinition`.
- Legacy flat fields (`hintIds`, `validationRuleIds`, `effectTriggerIds`, `allowSkip`, `challengeFlags`) remain and work unchanged.
- `Resolved*` accessor properties read payload first, fall back to flat fields.
- `MachinePackageValidator` validates cross-references within payloads.
- Runtime consumers not yet migrated to resolvers — they continue reading flat fields.

## Phase 4 — Profile-aware dispatch ✅

- `StepFamily` enum created (Place, Use, Connect, Confirm).
- `ResolvedFamily` returns `StepFamily` enum instead of string.
- All `Is*` boolean properties (`IsPlacement`, `IsToolAction`, `IsConfirmation`, `IsPipeConnection`, `IsConfirm`) now derive from `ResolvedFamily` enum instead of raw `completionType` string comparisons.
- All 9 runtime dispatch sites automatically use family-keyed dispatch through the updated `Is*` properties.
- One raw `completionType` string comparison in `UIRootCoordinator.cs` migrated to `step.IsToolAction`.
- `MachinePackageValidator.ValidateStepProfile` uses `StepFamily` enum switch.
- Profile is accessible at all dispatch sites via `step.profile` for future per-profile behavior.
- Unknown profiles fall back to family default.

## Phase 5 — First profile-aware handler (Use.Torque) ✅

- `UseStepHandler` reads `profile` from the current step definition and stores it as `_activeProfile`.
- Profile sync happens in `RefreshToolActionTargets` (the actual call site from `PartInteractionBridge`), not only in `OnStepActivated`.
- Torque profile triggers a green "click pop" effect (`ToolActionClickEffect`) on successful tool action — proof that profile-specific visual feedback works.
- Null or unknown profiles fall back to the existing default behavior — zero regression.
- Three torque-wrench steps in `power_cube_frame/machine.json` now declare `"family": "Use", "profile": "Torque"`.
- All other `tool_action` steps remain legacy (`completionType` only) and work unchanged via fallback.
- **Key discovery:** In editor, machine.json loads from `Assets/_Project/Data/Packages/` (authoring folder), not `StreamingAssets`. Both copies must have the fields. Documented in `CLAUDE.md`.

## Phase 6 — Router wiring + Feedback payload runtime (Use.Torque slice) ✅

- `StepExecutionRouter` wired into `PartInteractionBridge` — `OnStepActivated` and `OnStepCompleted` now fire through the router on genuine step transitions (not fail-retries).
- `TryBuildHandlerContext` helper builds `StepHandlerContext` from current session state.
- `StepFeedbackPayload` extended with `completionEffectColor` (hex string) and `completionPulseScale` (float).
- `UseStepHandler.OnStepActivated` caches feedback payload values — color parsed via `ColorUtility.TryParseHtmlString`, scale checked `> 0f`.
- `ToolActionClickEffect.Spawn` now accepts `Color` and `float pulseScale` params instead of hardcoded constants.
- Fallback chain: `feedback payload → profile default → family default (green, 1.8x)`.
- Two torque steps authored with distinct feedback payloads (#33FF66/2.5x and #66CCFF/2.0x); third torque step has no feedback to prove fallback.
- First proof that **authored payload data drives runtime presentation**.

---

# 9. Rules for Extending the Matrix

## Adding a new profile

1. Write at least one machine.json step that uses the new profile.
2. Confirm the profile is scoped to exactly one family.
3. Confirm the profile name describes the real-world process, not the implementation.
4. Confirm the family default still works if the profile is stripped.
5. Add the profile to §3.1 and the capability matrix in §5.
6. Update `MachinePackageValidator.cs` to accept the new value.

## Adding a new family

This should be rare. A new family means a fundamentally new interaction shape — not a variation of an existing one.

1. Confirm the new shape cannot be expressed as a profile within an existing family.
2. Define the interaction contract, validation shape, and spawning behavior.
3. Add the family to §2 and the capability matrix in §5.
4. Add the legacy mapping in §6 (if a `completionType` value exists).
5. Implement family dispatch in the runtime.
6. Update `MachinePackageValidator.cs`.
