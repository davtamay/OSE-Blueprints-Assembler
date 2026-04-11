# Step Definition Schema Reference

Every assembly step in `machine.json` is a `StepDefinition` object inside the top-level `steps[]` array. This document lists every field, its type, phase, and authoring rules.

---

## Phase Legend

| Phase | Meaning |
|-------|---------|
| **Core** | Required or heavily used; present in all packages |
| **Legacy** | Flat field superseded by a payload; kept for backward compatibility |
| **Phase 3** | Grouped payload introduced in Phase 3; takes precedence over the matching Legacy field when present |
| **Planned** | Defined in code, not yet wired to runtime behavior |

---

## Top-Level Fields

| Field | Type | Phase | Description |
|-------|------|-------|-------------|
| `id` | `string` | Core | Unique step identifier within the package. Used as the primary key in runtime state, persistence, and event payloads. Convention: `step_<snake_case_name>`. |
| `name` | `string` | Core | Human-readable label for the step (editor UI, audit reports). Not shown to the learner directly. |
| `assemblyId` | `string` | Core | ID of the assembly section this step belongs to. Groups steps into modules for the assembly picker and progress tracking. |
| `subassemblyId` | `string` | Core | ID of the subassembly this step contributes to (e.g. `extruder`, `frame`). Used for subassembly proxy placement steps. |
| `sequenceIndex` | `int` | Core | Global zero-based index of this step across all assemblies. Determines playback order. Must be unique and contiguous. |
| `instructionText` | `string` | Legacy | Learner-facing instruction shown in the step panel. **Superseded by `guidance.instructionText` when the `guidance` payload is present.** |
| `whyItMattersText` | `string` | Legacy | Appended to instruction with "Why it matters: " prefix. **Superseded by `guidance.whyItMattersText`.** |
| `requiredPartIds` | `string[]` | Core | Part IDs that must be placed (snapped to targets) to complete this step. Each ID must match a `partPlacements[].partId` entry in `previewConfig`. |
| `requiredSubassemblyId` | `string` | Core | When set, the step expects the learner to place a completed subassembly proxy rather than individual parts. Activates the subassembly drag-dock interaction. |
| `optionalPartIds` | `string[]` | Core | Parts visible during this step but not required for completion (informational only). |
| `relevantToolIds` | `string[]` | Core | Tool IDs that should be highlighted / made available during this step. Informational; does not block completion unless `requiredToolActions` are present. |
| `targetIds` | `string[]` | Core | Target zone IDs that define acceptable snap positions. Each ID must match a `targetPlacements[].targetId` entry in `previewConfig`. |
| `completionType` | `string` | Legacy | **Deprecated.** How the step is completed. Accepted values: `"placement"`, `"tool_action"`, `"pipe_connection"`, `"confirmation"`. **Use `family` + `profile` instead.** Kept for backward compatibility — `ResolvedFamily` derives the family from this field when `family` is absent. |
| `family` | `string` | Core | Fundamental interaction shape. Accepted values: `"Place"`, `"Use"`, `"Connect"`, `"Confirm"`. Overrides `completionType` when present. |
| `profile` | `string` | Core | Refines behavior within a family. See profile table below. Null/empty = family default. |
| `viewMode` | `string` | Core | Camera framing mode override. Accepted values: `"SourceAndTarget"`, `"PairEndpoints"`, `"WorkZone"`, `"PathView"`, `"Overview"`, `"Inspect"`, `"ToolFocus"`. Null = resolved from family + profile via `ViewModeResolver`. |
| `validationRuleIds` | `string[]` | Legacy | Validation rule IDs applied at completion. **Superseded by `validation.validationRuleIds`.** |
| `hintIds` | `string[]` | Legacy | Hint definition IDs shown progressively. **Superseded by `guidance.hintIds`.** |
| `effectTriggerIds` | `string[]` | Legacy | Effect trigger IDs fired on step events. **Superseded by `feedback.effectTriggerIds`.** |
| `requiredToolActions` | `ToolActionDefinition[]` | Core | Ordered list of tool actions (weld, torque, cut, etc.) that must be performed to complete a `Use`-family step. See `ToolActionDefinition` below. |
| `removePersistentToolIds` | `string[]` | Core | Tool IDs whose persistent scene instances (clamps, fixtures) are removed when this step activates. Content-driven cleanup without code changes. |
| `allowSkip` | `bool` | Legacy | Whether the learner can skip this step. **Superseded by `difficulty.allowSkip`.** |
| `challengeFlags` | `StepChallengeFlagsDefinition` | Legacy | Per-step challenge tuning. **Superseded by `difficulty.challengeFlags`.** |
| `eventTags` | `string[]` | Planned | Semantic tags for analytics and telemetry grouping (e.g. `"safety"`, `"first-time-action"`). Not yet wired to runtime behavior. |
| `targetOrder` | `string` | Core | Controls whether targets are processed simultaneously or one at a time. `"parallel"` (default/null) or `"sequential"`. |
| `guidance` | `StepGuidancePayload` | Phase 3 | Grouped instruction payload. **Takes precedence over flat `instructionText`, `whyItMattersText`, `hintIds`.** |
| `validation` | `StepValidationPayload` | Phase 3 | Grouped validation payload. **Takes precedence over flat `validationRuleIds`.** |
| `feedback` | `StepFeedbackPayload` | Phase 3 | Grouped feedback payload. **Takes precedence over flat `effectTriggerIds`.** |
| `reinforcement` | `StepReinforcementPayload` | Phase 3 | Post-completion learning context. No flat-field equivalent. |
| `difficulty` | `StepDifficultyPayload` | Phase 3 | Difficulty and challenge tuning. **Takes precedence over flat `allowSkip`, `challengeFlags`.** |
| `measurement` | `StepMeasurementPayload` | Phase 3 | Anchor-to-anchor measurement data for `Use.Measure` steps. |
| `gesture` | `StepGesturePayload` | Phase 3 | Gesture override payload for `Use`-family steps. Overrides profile defaults. |
| `wireConnect` | `StepWireConnectPayload` | Phase 3 | Polarity-aware wire connection data for `Connect.WireConnect` steps. |
| `workingOrientation` | `StepWorkingOrientationPayload` | Phase 3 | Temporarily transforms the subassembly for this step (e.g., flip 180° to access underside). Reverts on step transition. Appends orientation hint to instruction text. |
| `animationCues` | `StepAnimationCuePayload` | Phase 3 | Data-driven animation cues played on step activation. Supports placement demonstrations, pose transitions, pulses, and subassembly orientation. Can defer preview spawning. |
| `taskOrder` | `TaskOrderEntry[]` | Planned | Explicit cross-section task sequence within a step. Null/empty = no explicit order. |

---

## Family × Profile Matrix

| Family | Profile | Interaction | Notes |
|--------|---------|-------------|-------|
| `Place` | *(none)* | Drag-and-snap part to target zone | Default placement |
| `Place` | `Clamp` | Place with tool-hold confirmation | Clamp fixture persists until removed |
| `Place` | `AxisFit` | Constrained axis-slide placement | Anchored side stays fixed; only axis direction moves |
| `Use` | *(none)* | Generic tool-use action | Resolves gesture from profile registry |
| `Use` | `Torque` | Rotary torque gesture | Wrench, screwdriver interactions |
| `Use` | `Weld` | Arc-weld gesture + particle effect | Welding rod, MIG gun |
| `Use` | `Cut` | Linear cut gesture | Angle grinder, hacksaw |
| `Use` | `Strike` | Impact strike gesture | Hammer, mallet |
| `Use` | `Measure` | Tape measure anchor-to-anchor | Requires `measurement` payload |
| `Use` | `SquareCheck` | L-square alignment check | Framing square verification |
| `Connect` | *(none)* | Generic port-to-port connection | Pipe, hose |
| `Connect` | `Cable` | Spline cable routing | Requires spline path data in previewConfig |
| `Connect` | `WireConnect` | Polarity-aware wire connection | Requires `wireConnect` payload |
| `Confirm` | *(none)* | Explicit learner confirmation | Button-press / voice-confirm |

---

## Payload Field Reference

### `StepGuidancePayload` (Phase 3)

| Field | Type | Description |
|-------|------|-------------|
| `instructionText` | `string` | Learner-facing instruction text. Overrides flat `instructionText`. |
| `whyItMattersText` | `string` | Appended context after instruction. Overrides flat `whyItMattersText`. |
| `hintIds` | `string[]` | Progressive hint IDs. Overrides flat `hintIds`. |
| `contextualDiagramRef` | `string` | Reference to a diagram asset shown alongside instruction (Planned). |

### `StepValidationPayload` (Phase 3)

| Field | Type | Description |
|-------|------|-------------|
| `validationRuleIds` | `string[]` | Validation rule IDs applied at completion. Overrides flat `validationRuleIds`. |

### `StepFeedbackPayload` (Phase 3)

| Field | Type | Description |
|-------|------|-------------|
| `effectTriggerIds` | `string[]` | Effect trigger IDs fired on step events. Overrides flat `effectTriggerIds`. |
| `completionEffectColor` | `string` | Hex color for completion pulse (e.g. `"#33FF66"`). Null = profile default. |
| `completionPulseScale` | `float` | Scale multiplier for completion pulse. `0` = profile default. |
| `completionParticleId` | `string` | Named particle effect on completion (e.g. `"torque_sparks"`, `"weld_glow"`). Null = none. |

### `StepReinforcementPayload` (Phase 3)

| Field | Type | Description |
|-------|------|-------------|
| `milestoneMessage` | `string` | Shown after step completion (e.g. "Rail secured — frame is now rigid"). |
| `consequenceText` | `string` | What happens if this step is skipped or done wrong. |
| `safetyNote` | `string` | Safety reminder shown at step completion. |
| `counterfactualText` | `string` | "What if you hadn't done this?" — deepens learning. |

### `StepDifficultyPayload` (Phase 3)

| Field | Type | Description |
|-------|------|-------------|
| `allowSkip` | `bool` | Whether the learner can skip. Overrides flat `allowSkip`. |
| `challengeFlags` | `StepChallengeFlagsDefinition` | Per-step challenge penalties. Overrides flat `challengeFlags`. |
| `timeLimitSeconds` | `float` | Step time limit in seconds. `0` = no limit. |
| `hintAvailability` | `string` | `"always"` (default), `"limited"`, `"none"`. |
| `gestureMode` | `string` | `"easy"` / null = click-to-complete, `"standard"` = relaxed gesture, `"realistic"` = strict. |

### `StepMeasurementPayload` (Phase 3 — `Use.Measure` only)

| Field | Type | Description |
|-------|------|-------------|
| `startAnchorTargetId` | `string` | Target ID for the tape hook end. |
| `endAnchorTargetId` | `string` | Target ID for the measurement mark. |
| `expectedValueMm` | `float` | Correct measurement in millimeters. |
| `toleranceMm` | `float` | Acceptable error in millimeters. `0` = no validation. |
| `displayUnit` | `string` | Display unit: `"inches"`, `"mm"`, `"cm"`, `"ft"`. |

### `StepGesturePayload` (Phase 3 — `Use`-family only)

| Field | Type | Description |
|-------|------|-------------|
| `gestureType` | `string` | Explicit override: `"Tap"`, `"RotaryTorque"`, `"LinearPull"`, `"SteadyHold"`, `"PathTrace"`, `"ImpactStrike"`. Null = profile default. |
| `targetAngleDegrees` | `float` | Target rotation for `RotaryTorque`. Default: 90°. |
| `targetPullDistance` | `float` | Target pull distance (world units) for `LinearPull`. Default: 0.3. |
| `holdDurationSeconds` | `float` | Hold time for `SteadyHold`. Default: 2.0s. |
| `pathControlPointIds` | `string[]` | Ordered waypoint target IDs for `PathTrace`. |
| `showGestureGuide` | `bool` | Whether to show the gesture overlay. Default: `true`. |

### `StepWorkingOrientationPayload` (Phase 3)

Temporarily transforms the subassembly for the duration of this step (e.g., flip 180° to expose the underside for fastener insertion). The orientation reverts automatically on step transition.

| Field | Type | Description |
|-------|------|-------------|
| `subassemblyRotation` | `SceneFloat3` | Euler angles (degrees) applied to the subassembly proxy root relative to its authored fabrication pose. |
| `subassemblyPositionOffset` | `SceneFloat3` | Optional position offset (meters) in PreviewRoot local space, applied after rotation. Useful for keeping a flipped assembly at a comfortable working height. |
| `hint` | `string` | Optional human-readable explanation shown to the learner. When null/empty, a default message is auto-generated. |
| `partOverrides` | `StepPartPoseOverride[]` | Optional per-part pose overrides for non-rigid adjustments that can't be expressed as a single subassembly rotation. |

#### `StepPartPoseOverride`

| Field | Type | Description |
|-------|------|-------------|
| `partId` | `string` | Part ID to override. |
| `positionOffset` | `SceneFloat3` | Additive offset applied to the part's assembled position. |
| `rotationOverride` | `SceneFloat3` | Euler degrees — replaces the part's assembled rotation when non-zero. |

### `StepAnimationCuePayload` (Phase 3)

Data-driven animation cues played when the step activates. Supports placement demonstrations (bolt drill-down), pose transitions, emission pulses, and subassembly orientation flips.

| Field | Type | Description |
|-------|------|-------------|
| `cues` | `AnimationCueEntry[]` | Ordered list of animation cues to play. |
| `previewDelaySeconds` | `float` | When > 0, preview ghosts are deferred until this many seconds after step activation. Lets orientation/demonstration cues play before ghosts appear. `0` = spawn immediately (default). |

#### `AnimationCueEntry`

| Field | Type | Description |
|-------|------|-------------|
| `type` | `string` | Animation type key. Accepted values: `"demonstratePlacement"`, `"poseTransition"`, `"pulse"`, `"orientSubassembly"`. |
| `targetPartIds` | `string[]` | Part IDs to animate (resolved via spawned part lookup). |
| `targetToolIds` | `string[]` | Tool IDs to animate (resolved via ToolCursorManager / PersistentToolController). |
| `targetSubassemblyId` | `string` | Subassembly ID to animate (resolved via proxy root or fabrication group). |
| `trigger` | `string` | When to start: `"onActivate"` (default) or `"afterDelay"`. |
| `delaySeconds` | `float` | Delay in seconds when trigger is `"afterDelay"`. |
| `durationSeconds` | `float` | Duration in seconds. `0` = type default. |
| `loop` | `bool` | When `true`, animation restarts on completion instead of stopping. |
| `easing` | `string` | Easing curve: `"smoothStep"` (default), `"linear"`, `"easeInOut"`. |
| `target` | `string` | `"part"` (default) = animate the actual spawned part/tool. `"ghost"` = create a transparent clone and animate that instead. |
| `fromPose` | `AnimationPose` | Explicit start pose for `poseTransition`. Null = use start transform. |
| `toPose` | `AnimationPose` | Explicit end pose for `poseTransition`. Null = use assembled transform. |
| `subassemblyRotation` | `SceneFloat3` | Euler rotation for `orientSubassembly`. |
| `pulseColorA` | `SceneFloat4` | Pulse color A (RGBA). Alpha > 0 activates override; default is blue `(0, 0.6, 1, 1)`. |
| `pulseColorB` | `SceneFloat4` | Pulse color B (RGBA). Alpha > 0 activates override; default is gold `(1, 0.85, 0, 1)`. |
| `pulseSpeed` | `float` | Pulse speed in rad/s. Default: `3.0`. |
| `spinRevolutions` | `float` | Number of full rotations during `demonstratePlacement` (bolt screw effect). `0` = no spin. e.g., `4` = bolt makes 4 turns while traveling to assembled pose. |
| `spinAxis` | `SceneFloat3` | Local axis for spin rotation. Defaults to `(0,1,0)` = Y-up (bolt shaft). |
| `animationClipName` | `string` | Reserved for future GLB-embedded animation support. When set, the player would play the named clip instead of procedural lerp. Not implemented in Phase 1. |

#### `AnimationPose`

| Field | Type | Description |
|-------|------|-------------|
| `position` | `SceneFloat3` | World or local position. |
| `rotation` | `SceneQuaternion` | Orientation quaternion. |
| `scale` | `SceneFloat3` | Scale. |

### `StepWireConnectPayload` (Phase 3 — `Connect.WireConnect` only)

| Field | Type | Description |
|-------|------|-------------|
| `wires` | `WireConnectEntry[]` | Per-wire polarity + connector definitions, parallel to `targetIds`. |
| `enforcePortOrder` | `bool` | When `true`, portA must be clicked before portB. Default: `false`. |

#### `WireConnectEntry`

| Field | Type | Description |
|-------|------|-------------|
| `targetId` | `string` | Target ID this entry applies to. Null = matched by array index. |
| `portAPolarityType` | `string` | Signal at portA: `"+12V"`, `"+5V"`, `"+"`, `"GND"`, `"-"`, `"-12V"`, `"signal"`, `"pwm"`, `"enable"`, `"thermistor"`, `"fan"`, `"endstop"`. |
| `portBPolarityType` | `string` | Signal at portB. Same token set as portA. |
| `portAConnectorType` | `string` | Physical connector at portA: `"dupont_1pin"`, `"dupont_2pin"`, `"dupont_3pin"`, `"jst_xh_2pin"`, `"jst_xh_3pin"`, `"screw_terminal"`, `"spade"`, `"barrel_jack"`, `"bare_wire"`, `"molex"`. |
| `portBConnectorType` | `string` | Physical connector at portB. Same token set. |
| `polarityOrderMatters` | `bool` | When `true`, swapping portA/B is a violation. Default: `true`. |

### `ToolActionDefinition`

| Field | Type | Description |
|-------|------|-------------|
| `id` | `string` | Unique ID within the step's `requiredToolActions` list. |
| `toolId` | `string` | Tool that must be used (must match a `tools[].id` in the package). |
| `actionType` | `string` | Action kind: `"tighten"`, `"weld_pass"`, `"grind_pass"`, `"strike"`, `"measure"`. |
| `targetId` | `string` | Target the tool must be applied to. Must match a `targetPlacements[].targetId`. |
| `requiredCount` | `int` | Number of times the action must be performed. Default: 1. |
| `successMessage` | `string` | Message logged/displayed on action completion. |
| `failureMessage` | `string` | Message shown on wrong-tool or wrong-target attempt. |

### `StepChallengeFlagsDefinition`

| Field | Type | Description |
|-------|------|-------------|
| `penalizeHintUsage` | `bool` | Deducts score when hints are used. |
| `penalizeInvalidPlacement` | `bool` | Deducts score on invalid placement attempts. |
| `stricterToleranceAvailable` | `bool` | Enables tighter placement tolerance in challenge mode. |

---

## Payload-First Resolution Rules

When both a payload and its flat-field equivalent exist, the **payload always wins**:

| Resolved accessor | Payload field | Flat field fallback |
|-------------------|---------------|---------------------|
| `ResolvedInstructionText` | `guidance.instructionText` | `instructionText` |
| `ResolvedWhyItMattersText` | `guidance.whyItMattersText` | `whyItMattersText` |
| `ResolvedHintIds` | `guidance.hintIds` | `hintIds` |
| `ResolvedValidationRuleIds` | `validation.validationRuleIds` | `validationRuleIds` |
| `ResolvedEffectTriggerIds` | `feedback.effectTriggerIds` | `effectTriggerIds` |
| `ResolvedAllowSkip` | `difficulty.allowSkip` | `allowSkip` |
| `ResolvedChallengeFlags` | `difficulty.challengeFlags` | `challengeFlags` |
| `ResolvedFamily` | `family` string → enum | `completionType` → legacy mapping |

**Rule:** New authoring should use payload fields. Legacy flat fields are never removed — migrating is optional, not forced.

---

## Authoring Checklist for a New Step

- [ ] `id` is unique within the package
- [ ] `sequenceIndex` is contiguous with adjacent steps
- [ ] `assemblyId` matches an entry in `assemblies[]`
- [ ] Every `targetIds[]` entry exists in `previewConfig.targetPlacements[].targetId`
- [ ] Every `requiredPartIds[]` entry exists in `previewConfig.partPlacements[].partId`
- [ ] `family` is set (preferred over `completionType`)
- [ ] `profile` is set when using a non-default interaction (Torque, Weld, WireConnect, etc.)
- [ ] `guidance.instructionText` is non-empty
- [ ] `guidance.hintIds` has at least one entry
- [ ] `requiredToolActions` is non-empty for all `Use`-family steps
- [ ] `wireConnect` payload present for `Connect.WireConnect` steps
- [ ] `measurement` payload present for `Use.Measure` steps
- [ ] Float precision normalized (run `OSE / Package Builder / Normalize Float Precision`)

---

## Part Staging Positions (Agent Authoring Rule)

Staging positions — where a part floats before the trainee places it — are **not authored in steps**.
They live on the part definition itself:

```json
// parts[] in the assembly file or machine.json
{
  "id": "part_frame_rail_tl",
  "templateId": "template_frame_rail",
  "stagingPose": {
    "position": { "x": -0.3, "y": 0.4, "z": 0.1 },
    "rotation": { "x": 0, "y": 0, "z": 0, "w": 1 }
  }
}
```

**Rules:**
- `parts[].stagingPose` is the agent-authored source of truth for start positions (PreviewRoot local space).
- `previewConfig.partPlacements[].startPosition/startRotation/startScale/color` are **derived** — baked from `stagingPose` by `MachinePackageNormalizer.BakeStagingPoses()` at load time. **Do not edit them directly.**
- `previewConfig.partPlacements[].assembledPosition/assembledRotation` and all `stepPoses[]` data inside previewConfig are **TTAW/Blender-generated** — never authored by agents.
- `scale` in `stagingPose` may be omitted; zero means "use Vector3.one".
- `color` in `stagingPose` may be omitted; zero alpha means "use the ColAuthored default set in TTAW".
