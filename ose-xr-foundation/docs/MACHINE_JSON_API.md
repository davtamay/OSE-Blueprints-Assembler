# machine.json API Reference

Complete keyword reference for `machine.json` — the single data file that defines an assembly training package. Every JSON key maps 1:1 to a C# field via Unity's `JsonUtility`. Unknown keys are silently ignored; missing keys default to zero/null/false.

**Authoring path:** `Assets/_Project/Data/Packages/<packageId>/machine.json`

---

## Table of Contents

- [Root Object](#root-object)
- [machine](#machine)
- [assemblies](#assemblies)
- [subassemblies](#subassemblies)
- [partTemplates](#parttemplates)
- [parts](#parts)
- [tools](#tools)
- [targets](#targets)
- [steps](#steps)
- [hints](#hints)
- [validationRules](#validationrules)
- [effects](#effects)
- [challengeConfig](#challengeconfig)
- [assetManifest](#assetmanifest)
- [previewConfig](#previewconfig)
- [Shared Value Types](#shared-value-types)
- [String Token Reference](#string-token-reference)

---

## Root Object

`MachinePackageDefinition` — the top-level JSON object.

| Key | Type | Required | Description |
|-----|------|----------|-------------|
| `schemaVersion` | `string` | Yes | Schema version (e.g. `"1.0"`). |
| `packageVersion` | `string` | Yes | Content version (e.g. `"0.6.4"`). |
| `machine` | `MachineDefinition` | Yes | Machine metadata. |
| `assemblies` | `AssemblyDefinition[]` | Yes | Assembly groupings. |
| `subassemblies` | `SubassemblyDefinition[]` | No | Subassembly groupings. |
| `partTemplates` | `PartTemplateDefinition[]` | No | Reusable part templates. Parts reference via `templateId`. |
| `parts` | `PartDefinition[]` | Yes | All parts in the package. |
| `tools` | `ToolDefinition[]` | No | All tools in the package. |
| `steps` | `StepDefinition[]` | Yes | Assembly step sequence. |
| `validationRules` | `ValidationRuleDefinition[]` | No | Validation rules referenced by steps. |
| `effects` | `EffectDefinition[]` | No | Visual/audio effect definitions. |
| `hints` | `HintDefinition[]` | No | Hint definitions referenced by steps. |
| `targets` | `TargetDefinition[]` | No | Target zone metadata (extends placement data in previewConfig). |
| `challengeConfig` | `ChallengeConfigDefinition` | No | Global challenge mode settings. |
| `assetManifest` | `AssetManifestDefinition` | No | Asset reference manifest. |
| `previewConfig` | `PackagePreviewConfig` | Yes | Spatial placement data for the 3D scene. |

---

## machine

`MachineDefinition` — metadata about the machine being assembled.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Machine identifier. |
| `name` | `string` | Internal name. |
| `displayName` | `string` | User-facing name (preferred over `name` when present). |
| `description` | `string` | Machine description shown on intro screen. |
| `difficulty` | `string` | Difficulty label (e.g. `"beginner"`, `"intermediate"`, `"advanced"`). |
| `estimatedBuildTimeMinutes` | `int` | Estimated real-world build time. |
| `learningObjectives` | `string[]` | Learning objectives displayed on intro screen. |
| `recommendedMode` | `string` | Suggested session mode (e.g. `"guided"`, `"tutorial"`). |
| `entryAssemblyIds` | `string[]` | Assembly IDs available at session start. |
| `prerequisiteNotes` | `string[]` | Notes about required prior knowledge. |
| `sourceReferences` | `SourceReferenceDefinition[]` | Links to external documentation/CAD sources. |
| `introImageRef` | `string` | Asset reference for the intro screen image. |

### SourceReferenceDefinition

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique reference ID. |
| `type` | `string` | Reference type (e.g. `"documentation"`, `"cad_source"`, `"video"`). |
| `title` | `string` | Display title. |
| `referencePath` | `string` | URL or relative path. |
| `notes` | `string` | Author notes. |

---

## assemblies

`AssemblyDefinition[]` — top-level groupings for the assembly picker.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique assembly ID. Referenced by `steps[].assemblyId`. |
| `name` | `string` | Display name. |
| `description` | `string` | Assembly description. |
| `machineId` | `string` | Parent machine ID. |
| `subassemblyIds` | `string[]` | Subassemblies belonging to this assembly. |
| `stepIds` | `string[]` | Legacy step ID list. Runtime derives steps from `steps[].assemblyId` instead. |
| `dependencyAssemblyIds` | `string[]` | Assemblies that must be completed before this one. |
| `learningFocus` | `string` | Learning focus description for this assembly section. |

---

## subassemblies

`SubassemblyDefinition[]` — groups of parts that form a placeable unit.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique subassembly ID. Referenced by `steps[].subassemblyId` and `steps[].requiredSubassemblyId`. |
| `name` | `string` | Display name. |
| `assemblyId` | `string` | Parent assembly ID. |
| `description` | `string` | Subassembly description. |
| `partIds` | `string[]` | Member part IDs. |
| `stepIds` | `string[]` | Steps that build this subassembly. |
| `milestoneMessage` | `string` | Message shown when subassembly is completed. |

**Two subassembly modes:**
- **Fabrication** (`subassemblyId` on steps): Parts are built individually within a subassembly context. No proxy object. Parts remain as individual GOs.
- **Stacking** (`requiredSubassemblyId` on a step): A completed subassembly becomes a single draggable proxy. Requires a `subassemblyPlacements` entry in `previewConfig`.

---

## partTemplates

`PartTemplateDefinition[]` — reusable templates. Parts inherit fields from their template via `templateId`. Only override fields that differ.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique template ID. Referenced by `parts[].templateId`. |
| `name` | `string` | Template name. |
| `displayName` | `string` | Default display name for parts using this template. |
| `category` | `string` | Part category. See [Part Categories](#part-categories). |
| `material` | `string` | Material description. |
| `function` | `string` | Functional description. |
| `structuralRole` | `string` | Structural role. See [Structural Roles](#structural-roles). |
| `quantity` | `int` | Default quantity. |
| `toolIds` | `string[]` | Default associated tools. |
| `assetRef` | `string` | Default 3D model asset reference (GLB filename without extension). |
| `searchTerms` | `string[]` | Search keywords. |
| `allowPhysicalSubstitution` | `bool` | Whether a real-world substitute is acceptable. |
| `defaultOrientationHint` | `string` | Default orientation hint text. |
| `tags` | `string[]` | Freeform tags. |

---

## parts

`PartDefinition[]` — every physical part in the package.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique part ID. Convention: `<descriptive_snake_case>`. Referenced by steps, targets, and previewConfig. |
| `templateId` | `string` | Optional template ID. When set, null/empty fields inherit from the template. |
| `name` | `string` | Internal name. |
| `displayName` | `string` | User-facing name. |
| `category` | `string` | Part category. See [Part Categories](#part-categories). |
| `material` | `string` | Material description (e.g. `"steel"`, `"aluminum"`, `"ABS plastic"`). |
| `function` | `string` | What this part does. |
| `structuralRole` | `string` | Structural role. See [Structural Roles](#structural-roles). |
| `quantity` | `int` | Quantity needed. |
| `toolIds` | `string[]` | Tool IDs associated with this part. |
| `assetRef` | `string` | 3D model asset reference (GLB filename without extension). |
| `searchTerms` | `string[]` | Search keywords. |
| `allowPhysicalSubstitution` | `bool` | Whether a real-world substitute is acceptable. |
| `defaultOrientationHint` | `string` | Orientation hint text. |
| `tags` | `string[]` | Freeform tags. |
| `grabConfig` | `PartGrabConfig` | XR grab metadata. |

### PartGrabConfig

| Key | Type | Description |
|-----|------|-------------|
| `gripPoint` | `SceneFloat3` | Local offset from mesh origin to the natural grab point. |
| `gripRotation` | `SceneFloat3` | Euler correction for natural held orientation. |
| `handedness` | `string` | `"right"`, `"left"`, or `"either"`. |
| `poseHint` | `string` | `"power_grip"`, `"pinch"`, or `"two_hand"`. |

---

## tools

`ToolDefinition[]` — tools used during assembly.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique tool ID. Convention: `tool_<name>`. Referenced by steps and tool actions. |
| `name` | `string` | Display name. |
| `category` | `string` | Tool category (e.g. `"hand_tool"`, `"power_tool"`, `"measurement"`). |
| `purpose` | `string` | What this tool is for. |
| `usageNotes` | `string` | Usage instructions. |
| `safetyNotes` | `string` | Safety warnings. |
| `searchTerms` | `string[]` | Search keywords. |
| `assetRef` | `string` | 3D model asset reference. |
| `useOrientationOverride` | `bool` | When `true`, use `orientationEuler` instead of auto-detection. |
| `orientationEuler` | `Vector3` | Legacy Euler override `{"x":0,"y":0,"z":0}`. Superseded by `toolPose.cursorRotation`. |
| `scaleOverride` | `float` | Scale multiplier for the tool preview cursor. `0` or `1` = default. |
| `persistent` | `bool` | When `true`, tool remains on workpiece after use (clamps, fixtures). |
| `toolPose` | `ToolPoseConfig` | Spatial metadata for grip, tip, and cursor. |

### ToolPoseConfig

| Key | Type | Description |
|-----|------|-------------|
| `gripPoint` | `SceneFloat3` | Local offset to center of hand grip. |
| `gripRotation` | `SceneFloat3` | Euler correction for held orientation. |
| `tipPoint` | `SceneFloat3` | Local offset to business end (nozzle, socket, blade). |
| `cursorOffset` | `SceneFloat3` | Additional offset for desktop/mobile cursor. |
| `cursorRotation` | `SceneFloat3` | Euler override for cursor preview orientation. |
| `handedness` | `string` | `"right"`, `"left"`, or `"either"`. |
| `poseHint` | `string` | `"power_grip"`, `"pinch"`, `"precision"`, or `"two_hand"`. |
| `tipAxis` | `SceneFloat3` | Explicit tip direction (model-local). Preferred over derived `tipPoint - gripPoint`. |
| `actionAxis` | `SceneFloat3` | Local axis for torque/insertion motion (e.g. Allen key short leg). |

---

## targets

`TargetDefinition[]` — metadata for snap targets. Spatial placement is in `previewConfig.targetPlacements`.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique target ID. Convention: `target_<name>`. Must match a `previewConfig.targetPlacements[].targetId`. |
| `name` | `string` | Display name. |
| `anchorRef` | `string` | Anchor reference for positioning. |
| `description` | `string` | Target description. |
| `associatedPartId` | `string` | Part ID this target accepts. |
| `associatedSubassemblyId` | `string` | Subassembly ID this target accepts. |
| `tags` | `string[]` | Freeform tags. |
| `weldAxis` | `SceneFloat3` | Direction of weld/cut line in local space (normalized). Zero = point target. |
| `weldLength` | `float` | Length of weld/cut line in scene units. `0` = default (0.03). |
| `useToolActionRotation` | `bool` | When `true`, use `toolActionRotation` for tool orientation during action. |
| `toolActionRotation` | `SceneFloat3` | Euler override for tool orientation at this target. |

---

## steps

`StepDefinition[]` — the assembly step sequence. See [STEP_SCHEMA.md](STEP_SCHEMA.md) for the full field reference, family/profile matrix, and payload documentation.

### Core Fields

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique step ID. Convention: `step_<snake_case>`. |
| `name` | `string` | Human-readable label (editor/audit only). |
| `assemblyId` | `string` | Parent assembly ID. |
| `subassemblyId` | `string` | Parent subassembly ID (fabrication context). |
| `sequenceIndex` | `int` | Ordering index. Must be unique per assembly and ordered. |
| `family` | `string` | Interaction family. Values: `"Place"`, `"Use"`, `"Connect"`, `"Confirm"`. |
| `profile` | `string` | Family-scoped profile. Values: `"Clamp"`, `"AxisFit"`, `"Torque"`, `"Weld"`, `"Cut"`, `"Strike"`, `"Measure"`, `"SquareCheck"`, `"Cable"`, `"WireConnect"`. Omit for family default. |
| `viewMode` | `string` | Camera framing. Values: `"SourceAndTarget"`, `"PairEndpoints"`, `"WorkZone"`, `"PathView"`, `"Overview"`, `"Inspect"`, `"ToolFocus"`. Null = auto-resolved. |
| `targetOrder` | `string` | `"parallel"` (default/null) or `"sequential"`. |
| `requiredPartIds` | `string[]` | Parts that must be placed to complete this step. |
| `requiredSubassemblyId` | `string` | Subassembly proxy to place (stacking step). |
| `optionalPartIds` | `string[]` | Parts visible but not required. |
| `relevantToolIds` | `string[]` | Tools highlighted during this step. |
| `targetIds` | `string[]` | Target zone IDs for this step. |
| `requiredToolActions` | `ToolActionDefinition[]` | Tool actions required for `Use`-family steps. |
| `removePersistentToolIds` | `string[]` | Persistent tools to remove when step activates. |
| `eventTags` | `string[]` | Semantic tags for analytics (planned). |
| `taskOrder` | `TaskOrderEntry[]` | Explicit task sequence within step (planned). |

### Step Payloads (Phase 3)

| Key | Type | Description |
|-----|------|-------------|
| `guidance` | object | Instruction text, why-it-matters, hints. |
| `validation` | object | Validation rule IDs. |
| `feedback` | object | Effect trigger IDs, completion effects. |
| `reinforcement` | object | Post-completion learning context. |
| `difficulty` | object | Skip, challenge flags, time limit, hint availability. |
| `measurement` | object | Anchor-to-anchor measurement data (`Use.Measure`). |
| `gesture` | object | Gesture type and thresholds (`Use` family). |
| `wireConnect` | object | Polarity-aware wire data (`Connect.WireConnect`). |
| `workingOrientation` | object | Temporary subassembly rotation for this step. |
| `animationCues` | object | Data-driven animation cues on step activation. |

See [STEP_SCHEMA.md](STEP_SCHEMA.md) for full payload field tables.

### Legacy Flat Fields (do not add to new steps)

| Key | Type | Superseded By |
|-----|------|---------------|
| `instructionText` | `string` | `guidance.instructionText` |
| `whyItMattersText` | `string` | `guidance.whyItMattersText` |
| `hintIds` | `string[]` | `guidance.hintIds` |
| `validationRuleIds` | `string[]` | `validation.validationRuleIds` |
| `effectTriggerIds` | `string[]` | `feedback.effectTriggerIds` |
| `allowSkip` | `bool` | `difficulty.allowSkip` |
| `challengeFlags` | object | `difficulty.challengeFlags` |
| `completionType` | `string` | `family` + `profile` |

### ToolActionDefinition

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique ID within the step. |
| `toolId` | `string` | Tool to use. Must match `tools[].id`. |
| `actionType` | `string` | Action kind. Values: `"tighten"`, `"weld_pass"`, `"grind_pass"`, `"strike"`, `"measure"`. |
| `targetId` | `string` | Target to apply tool to. Must match `previewConfig.targetPlacements[].targetId`. |
| `requiredCount` | `int` | Times the action must be performed. Default: `1`. |
| `successMessage` | `string` | Message on completion. |
| `failureMessage` | `string` | Message on wrong-tool/wrong-target attempt. |

### TaskOrderEntry (Planned)

| Key | Type | Description |
|-----|------|-------------|
| `kind` | `string` | `"part"`, `"toolAction"`, `"wire"`, or `"target"`. |
| `id` | `string` | ID of the referenced part, action, or target. |

---

## hints

`HintDefinition[]` — progressive hints referenced by `guidance.hintIds`.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique hint ID. |
| `type` | `string` | Hint type (e.g. `"text"`, `"highlight"`, `"arrow"`). |
| `title` | `string` | Hint title. |
| `message` | `string` | Hint message text. |
| `targetId` | `string` | Optional target to highlight. Makes this hint unique (don't reuse). |
| `partId` | `string` | Optional part to highlight. Makes this hint unique. |
| `toolId` | `string` | Optional tool to highlight. |
| `priority` | `string` | Display priority. |

**Reuse rule:** Hints with no `targetId`/`partId`/`toolId` (generic reminders) should be defined once and referenced by many steps. Only create a unique hint when it carries a specific ID for runtime highlighting.

---

## validationRules

`ValidationRuleDefinition[]` — validation rules referenced by `validation.validationRuleIds`.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique rule ID. |
| `type` | `string` | Validation type. |
| `targetId` | `string` | Target to validate against. |
| `expectedPartId` | `string` | Expected part at the target. |
| `positionToleranceMm` | `float` | Position tolerance in millimeters. |
| `rotationToleranceDeg` | `float` | Rotation tolerance in degrees. |
| `requiredStepIds` | `string[]` | Steps that must be complete before this rule applies. |
| `requiredPartIds` | `string[]` | Parts that must be placed. |
| `modeOverrides` | `ValidationModeOverrideDefinition` | Per-mode tolerance overrides. |
| `failureMessage` | `string` | Message on validation failure. |
| `correctionHintId` | `string` | Hint ID shown on failure. |

### ValidationModeOverrideDefinition

| Key | Type | Description |
|-----|------|-------------|
| `tutorialPositionToleranceMm` | `float` | Tutorial mode position tolerance. |
| `tutorialRotationToleranceDeg` | `float` | Tutorial mode rotation tolerance. |
| `guidedPositionToleranceMm` | `float` | Guided mode position tolerance. |
| `guidedRotationToleranceDeg` | `float` | Guided mode rotation tolerance. |
| `challengePositionToleranceMm` | `float` | Challenge mode position tolerance. |
| `challengeRotationToleranceDeg` | `float` | Challenge mode rotation tolerance. |

---

## effects

`EffectDefinition[]` — visual/audio effects referenced by `feedback.effectTriggerIds`.

| Key | Type | Description |
|-----|------|-------------|
| `id` | `string` | Unique effect ID. |
| `type` | `string` | Effect type. |
| `assetRef` | `string` | Asset reference. |
| `shaderRef` | `string` | Shader reference. |
| `fallbackRef` | `string` | Fallback asset reference. |
| `triggerPolicy` | `string` | When to trigger. |
| `notes` | `string` | Author notes. |

---

## challengeConfig

`ChallengeConfigDefinition` — global challenge mode settings.

| Key | Type | Description |
|-----|------|-------------|
| `enabled` | `bool` | Whether challenge mode is available. |
| `trackTime` | `bool` | Track completion time. |
| `trackRetries` | `bool` | Track retry count. |
| `trackHintUsage` | `bool` | Track hint usage. |
| `leaderboardReady` | `bool` | Whether scores can be submitted. |
| `strictValidationModeAvailable` | `bool` | Enable stricter tolerances in challenge. |

### StepChallengeFlagsDefinition

Per-step challenge tuning (used in `difficulty.challengeFlags`).

| Key | Type | Description |
|-----|------|-------------|
| `penalizeHintUsage` | `bool` | Deduct score on hint use. |
| `penalizeInvalidPlacement` | `bool` | Deduct score on invalid placement. |
| `stricterToleranceAvailable` | `bool` | Enable tighter placement tolerance. |

---

## assetManifest

`AssetManifestDefinition` — declares all referenced assets.

| Key | Type | Description |
|-----|------|-------------|
| `modelRefs` | `string[]` | 3D model asset references. |
| `textureRefs` | `string[]` | Texture asset references. |
| `effectRefs` | `string[]` | Effect asset references. |
| `uiRefs` | `string[]` | UI asset references. |

---

## previewConfig

`PackagePreviewConfig` — spatial placement data for the 3D assembly scene.

| Key | Type | Description |
|-----|------|-------------|
| `defaultAssemblyScaleMultiplier` | `float` | Scale for the entire preview. `1.0` = authored size. |
| `targetRotationFormat` | `string` | `"mesh"` = direct mesh rotation. Null = legacy approach-vector format. |
| `partPlacements` | `PartPreviewPlacement[]` | Per-part start and assembled transforms. |
| `targetPlacements` | `TargetPreviewPlacement[]` | Per-target placement transforms. |
| `subassemblyPlacements` | `SubassemblyPreviewPlacement[]` | Fabrication-space frames for stacking subassemblies. |
| `constrainedSubassemblyFitPlacements` | `ConstrainedSubassemblyFitPreviewPlacement[]` | Axis-constrained fit data. |
| `completedSubassemblyParkingPlacements` | `SubassemblyPreviewPlacement[]` | Parking frames for finished subassemblies. |
| `integratedSubassemblyPlacements` | `IntegratedSubassemblyPreviewPlacement[]` | Final member poses after integration. |

### PartPreviewPlacement

| Key | Type | Description |
|-----|------|-------------|
| `partId` | `string` | Part ID. Must match `parts[].id`. |
| `startPosition` | `SceneFloat3` | Position before step (floating). |
| `startRotation` | `SceneQuaternion` | Rotation before step. |
| `startScale` | `SceneFloat3` | Scale before step. |
| `color` | `SceneFloat4` | Representative RGBA color. |
| `assembledPosition` | `SceneFloat3` | Final assembled position. |
| `assembledRotation` | `SceneQuaternion` | Final assembled rotation. |
| `assembledScale` | `SceneFloat3` | Final assembled scale. |
| `splinePath` | `SplinePathDefinition` | Optional spline data for tubular parts (hoses, cables). |

### SplinePathDefinition

| Key | Type | Description |
|-----|------|-------------|
| `radius` | `float` | Tube radius in meters. |
| `segments` | `int` | Radial segments for cross-section. |
| `metallic` | `float` | PBR metallic (0 = rubber, 0.8 = steel). |
| `smoothness` | `float` | PBR smoothness (0 = rough, 0.5 = polished). |
| `color` | `SceneFloat4` | Optional RGBA override. Alpha > 0 to activate. |
| `knots` | `SceneFloat3[]` | Ordered spline control points in PreviewRoot local space. |

### TargetPreviewPlacement

| Key | Type | Description |
|-----|------|-------------|
| `targetId` | `string` | Target ID. Must match `targets[].id`. |
| `position` | `SceneFloat3` | Target position. |
| `rotation` | `SceneQuaternion` | Target rotation. |
| `scale` | `SceneFloat3` | Target scale. |
| `color` | `SceneFloat4` | Target marker RGBA color. |
| `portA` | `SceneFloat3` | Port A position (pipe/cable connections). |
| `portB` | `SceneFloat3` | Port B position (pipe/cable connections). |

### SubassemblyPreviewPlacement

| Key | Type | Description |
|-----|------|-------------|
| `subassemblyId` | `string` | Subassembly ID. |
| `position` | `SceneFloat3` | Reference frame position. |
| `rotation` | `SceneQuaternion` | Reference frame rotation. |
| `scale` | `SceneFloat3` | Reference frame scale. |

### ConstrainedSubassemblyFitPreviewPlacement

| Key | Type | Description |
|-----|------|-------------|
| `subassemblyId` | `string` | Subassembly ID. |
| `targetId` | `string` | Anchor target ID. |
| `fitAxisLocal` | `SceneFloat3` | Local axis for constrained slide. |
| `minTravel` | `float` | Minimum travel distance. |
| `maxTravel` | `float` | Maximum travel distance. |
| `completionTravel` | `float` | Travel distance at which fit is complete. |
| `snapTolerance` | `float` | Snap tolerance. |
| `drivenPartIds` | `string[]` | Parts that slide along the axis. |

### IntegratedSubassemblyPreviewPlacement

| Key | Type | Description |
|-----|------|-------------|
| `subassemblyId` | `string` | Subassembly ID. |
| `targetId` | `string` | Integration target ID. |
| `memberPlacements` | `IntegratedMemberPreviewPlacement[]` | Per-member canonical poses. |

### IntegratedMemberPreviewPlacement

| Key | Type | Description |
|-----|------|-------------|
| `partId` | `string` | Member part ID. |
| `position` | `SceneFloat3` | Integrated position. |
| `rotation` | `SceneQuaternion` | Integrated rotation. |
| `scale` | `SceneFloat3` | Integrated scale. |

---

## Shared Value Types

### SceneFloat3

Three-component vector for positions, scales, and Euler angles.

```json
{ "x": 0.0, "y": 1.0, "z": 0.0 }
```

### SceneFloat4

Four-component vector for RGBA colors.

```json
{ "r": 1.0, "g": 0.5, "b": 0.0, "a": 1.0 }
```

### SceneQuaternion

Quaternion rotation. Identity = `{ "x": 0, "y": 0, "z": 0, "w": 1 }`. Zero (all 0) is also treated as identity.

```json
{ "x": 0.0, "y": 0.7071, "z": 0.0, "w": 0.7071 }
```

### Vector3

Unity engine Vector3 (used only on `ToolDefinition.orientationEuler`).

```json
{ "x": 0, "y": 0, "z": 0 }
```

**Float precision:** All spatial values use 4 decimal places max (0.1 mm / 0.01 deg). Run `OSE / Package Builder / Normalize Float Precision` after bulk edits.

---

## String Token Reference

Quick-lookup for every accepted string value across all definition types.

### Step Family

| Token | Meaning |
|-------|---------|
| `"Place"` | Spatial placement onto targets |
| `"Use"` | Tool activation on targets |
| `"Connect"` | Two-endpoint connection |
| `"Confirm"` | Non-spatial acknowledgement |

### Step Profile

| Token | Family | Meaning |
|-------|--------|---------|
| `"Clamp"` | Place | Clamp-assisted placement |
| `"AxisFit"` | Place | Constrained axis-slide |
| `"Torque"` | Use | Rotational tightening |
| `"Weld"` | Use | Linear weld/solder pass |
| `"Cut"` | Use | Linear cut/grind pass |
| `"Strike"` | Use | Impact strike |
| `"Measure"` | Use | Anchor-to-anchor measurement |
| `"SquareCheck"` | Use | Alignment verification |
| `"Cable"` | Connect | Spline cable routing |
| `"WireConnect"` | Connect | Polarity-aware wire connection |

### Step View Mode

| Token | Framing |
|-------|---------|
| `"SourceAndTarget"` | Start position + target |
| `"PairEndpoints"` | Both connection endpoints |
| `"WorkZone"` | Active work area |
| `"PathView"` | Cable/pipe path |
| `"Overview"` | Full assembly |
| `"Inspect"` | Close-up inspection |
| `"ToolFocus"` | Tool action zone |

### Tool Action Type

| Token | Meaning |
|-------|---------|
| `"tighten"` | Rotational tightening (drill, wrench) |
| `"weld_pass"` | Linear weld/solder pass |
| `"grind_pass"` | Linear cut/grind pass |
| `"strike"` | Impact strike (hammer, mallet) |
| `"measure"` | Anchor-to-anchor measurement |

### Target Order

| Token | Meaning |
|-------|---------|
| `"parallel"` | All targets visible at once (default) |
| `"sequential"` | One target at a time, in array order |

### Animation Cue Type

| Token | Meaning |
|-------|---------|
| `"demonstratePlacement"` | Lerp start to assembled with optional bolt spin |
| `"poseTransition"` | Arbitrary from/to pose animation |
| `"pulse"` | Emission color pulse |
| `"orientSubassembly"` | Rotate subassembly to expose work area |

### Animation Cue Trigger

| Token | Meaning |
|-------|---------|
| `"onActivate"` | Play when step activates (default) |
| `"afterDelay"` | Play after `delaySeconds` |

### Animation Cue Easing

| Token | Curve |
|-------|-------|
| `"smoothStep"` | Mathf.SmoothStep (default) |
| `"linear"` | Linear interpolation |
| `"easeInOut"` | Hermite ease-in-out |

### Animation Cue Target Mode

| Token | Meaning |
|-------|---------|
| `"part"` | Animate the real spawned part (default) |
| `"ghost"` | Create transparent clone, animate that |

### Gesture Type

| Token | Meaning |
|-------|---------|
| `"Tap"` | Single tap |
| `"RotaryTorque"` | Rotational gesture |
| `"LinearPull"` | Linear pull gesture |
| `"SteadyHold"` | Hold steady for duration |
| `"PathTrace"` | Trace a path through waypoints |
| `"ImpactStrike"` | Impact strike gesture |

### Wire Polarity Type

| Token | Signal |
|-------|--------|
| `"+12V"` | +12V power |
| `"+5V"` | +5V power |
| `"+"` | Positive |
| `"GND"` | Ground |
| `"-"` | Negative |
| `"-12V"` | -12V power |
| `"signal"` | Generic signal |
| `"pwm"` | PWM signal |
| `"enable"` | Enable line |
| `"thermistor"` | Thermistor |
| `"fan"` | Fan |
| `"endstop"` | Endstop |

### Wire Connector Type

| Token | Physical connector |
|-------|--------------------|
| `"dupont_1pin"` | Dupont 1-pin |
| `"dupont_2pin"` | Dupont 2-pin |
| `"dupont_3pin"` | Dupont 3-pin |
| `"jst_xh_2pin"` | JST XH 2-pin |
| `"jst_xh_3pin"` | JST XH 3-pin |
| `"screw_terminal"` | Screw terminal |
| `"spade"` | Spade connector |
| `"barrel_jack"` | Barrel jack |
| `"bare_wire"` | Bare wire |
| `"molex"` | Molex connector |

### Measurement Display Unit

| Token | Unit |
|-------|------|
| `"inches"` | Inches |
| `"mm"` | Millimeters |
| `"cm"` | Centimeters |
| `"ft"` | Feet |

### Handedness

| Token | Meaning |
|-------|---------|
| `"right"` | Right hand |
| `"left"` | Left hand |
| `"either"` | Either hand |

### Pose Hint

| Token | Meaning |
|-------|---------|
| `"power_grip"` | Full-hand grip |
| `"pinch"` | Pinch grip |
| `"precision"` | Precision grip (tools only) |
| `"two_hand"` | Two-handed grip |

### Difficulty Hint Availability

| Token | Meaning |
|-------|---------|
| `"always"` | Hints always available (default) |
| `"limited"` | Limited hint count |
| `"none"` | No hints |

### Difficulty Gesture Mode

| Token | Meaning |
|-------|---------|
| `"easy"` | Click-to-complete (default) |
| `"standard"` | Relaxed gesture |
| `"realistic"` | Strict gesture |

### Task Order Kind

| Token | References |
|-------|-----------|
| `"part"` | `requiredPartIds[]` entry |
| `"toolAction"` | `requiredToolActions[].id` |
| `"wire"` | `targetIds[]` entry (Cable/WireConnect) |
| `"target"` | `targetIds[]` entry (Place/Use) |

### Legacy Completion Type (do not use for new steps)

| Token | Maps to Family |
|-------|----------------|
| `"placement"` | `Place` |
| `"tool_action"` | `Use` |
| `"pipe_connection"` | `Connect` |
| `"confirmation"` | `Confirm` |

### Part Categories

Freeform string. Common values used in existing packages:

`"fastener"`, `"structural"`, `"bearing"`, `"motor"`, `"electronic"`, `"cable"`, `"bracket"`, `"rod"`, `"plate"`, `"pulley"`, `"coupler"`, `"sensor"`, `"connector"`

### Structural Roles

Freeform string. Common values:

`"frame"`, `"axis"`, `"carriage"`, `"mount"`, `"guide"`, `"clamp"`, `"spacer"`, `"bearing_seat"`
