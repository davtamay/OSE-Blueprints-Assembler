# UNITY_PROJECT_STRUCTURE.md

## Purpose

This document defines the recommended Unity project structure for the XR assembly application.

The goal is to keep the codebase:

- clean
- modular
- scalable
- testable
- agent-friendly
- reliable under incremental changes

This structure should support:

- cross-platform XR, mobile, and desktop delivery
- Unity WebXR deployment through De-Panther
- the Unity Input System as the canonical input foundation
- Unity UI Toolkit as the primary UI framework
- latest stable Unity technologies
- Unity 6.3 as the current engine baseline
- future stable version upgrades with explicit documentation
- modular runtime systems
- future multiplayer
- future remote assistance
- effects, shaders, and process visuals
- data-driven machine packages
- safe auto stage and commit workflows after meaningful validated changesets

The project should avoid becoming a typical Unity “everything in one folder” project.

---

# 1. Structural Philosophy

Unity projects often become hard to maintain because logic, content, prefabs, materials, and experimental scripts get mixed together.

This project must avoid that from day one.

Use these structural rules:

1. separate runtime, authoring, and content
2. separate systems by responsibility
3. separate shared abstractions from platform adapters
4. separate visuals from instructional logic
5. separate machine content from engine code
6. keep experiments isolated
7. keep generated data and exported machine packages clearly identified
8. keep UI Toolkit views, controllers, and styling organized and distinct

The folder structure should reflect architectural boundaries, not just asset types.

---

# 2. Recommended Top-Level Structure

Suggested Unity project layout inside `Assets/`:

```text
Assets/
  _Project/
    Art/
    Audio/
    Content/
    Data/
    Materials/
    Models/
    Prefabs/
    Scenes/
    Settings/
    Shaders/
    VFX/
    Scripts/
    UI/
    XR/
  Plugins/
  ThirdParty/
  StreamingAssets/
```

Use `_Project/` as the main owned project root so team and agent changes stay organized.

---

# 3. Detailed Folder Structure

## 3.1 Scripts

Recommended script layout:

```text
Assets/_Project/Scripts/
  App/
  Bootstrap/
  Core/
  Content/
  Runtime/
  Input/
  Interaction/
  Validation/
  Presentation/
  UI/
  Effects/
  Assets/
  Persistence/
  Networking/
  Challenge/
  Platform/
  Authoring/
  Utilities/
  Editor/
  Tests/
```

This should be the main codebase organization.

---

## 3.2 Content

Runtime content definitions and machine packages:

```text
Assets/_Project/Content/
  MachinePackages/
  Templates/
  SampleMachines/
  TutorialMachines/
```

This folder holds authored content assets or source content before export.

---

## 3.3 Data

Shared structured data:

```text
Assets/_Project/Data/
  ScriptableObjects/
  Registries/
  Definitions/
  Localization/
```

Use this for Unity-side definitions that are not machine-specific scene logic.

---

## 3.4 Shaders and Effects

Process and feedback visuals:

```text
Assets/_Project/Shaders/
  Surface/
  Feedback/
  Process/
  Utilities/

Assets/_Project/VFX/
  Particles/
  Graphs/
  Prefabs/
  Profiles/
```

Examples:

- welding glow shader
- sparks shader support
- heat glow
- torch/fire visual
- ghost placement material
- hint highlight material
- error material

---

## 3.5 XR and Platform Layers

```text
Assets/_Project/XR/
  InteractionProfiles/
  Hands/
  Controllers/
  WebXR/
  DeviceProfiles/
```

This keeps XR-specific setup away from general runtime logic.

---

## 3.6 UI Toolkit Structure

Because the project uses **Unity UI Toolkit** as the primary UI framework, keep UI assets intentionally organized.

```text
Assets/_Project/UI/
  Documents/
  UXML/
  USS/
  Themes/
  Panels/
  Debug/
```

Suggested responsibilities:

- `Documents/` for `UIDocument` prefabs or related setup assets
- `UXML/` for UI structure definitions
- `USS/` for stylesheets
- `Themes/` for shared visual themes
- `Panels/` for panel-specific assets
- `Debug/` for developer or diagnostics UI

## 3.7 Scenes

```text
Assets/_Project/Scenes/
  Boot/
  Frontend/
  Machines/
  Sandbox/
  Testbeds/
```

Use minimal scenes.

Do not put business logic into scenes.

Scenes should compose prefabs and runtime bootstrap objects.

---

# 4. Suggested Assembly Definitions for Scripts

Below is a recommended script breakdown matching the architecture documents.

## 4.1 App and Bootstrap

```text
Assets/_Project/Scripts/App/
Assets/_Project/Scripts/Bootstrap/
```

Purpose:

- app entry
- service registration
- environment setup
- capability loading
- initial configuration
- startup diagnostics

Possible scripts:

- `AppBootstrap.cs`
- `AppContext.cs`
- `ServiceRegistry.cs`
- `BuildConfiguration.cs`

---

## 4.2 Core

```text
Assets/_Project/Scripts/Core/
```

Purpose:

- core abstractions
- interfaces
- shared types
- common enums
- result models
- events
- contracts

Possible scripts:

- `Result.cs`
- `MachineSessionState.cs`
- `RuntimeStepState.cs`
- `PlacementValidationResult.cs`
- `CapabilityTier.cs`

Avoid putting feature logic here.

---

## 4.3 Content

```text
Assets/_Project/Scripts/Content/
```

Purpose:

- machine manifest models
- content parsers
- package version validation
- content registries
- assembly definitions
- step definitions
- tool and part definitions

Possible scripts:

- `MachineDefinition.cs`
- `AssemblyDefinition.cs`
- `StepDefinition.cs`
- `PartDefinition.cs`
- `ToolDefinition.cs`
- `EffectDefinition.cs`
- `MachinePackageLoader.cs`

---

## 4.4 Runtime

```text
Assets/_Project/Scripts/Runtime/
```

Purpose:

- machine session orchestration
- assembly progression
- step execution
- state transitions

Possible scripts:

- `MachineSessionController.cs`
- `AssemblyRuntimeController.cs`
- `StepController.cs`
- `ProgressionController.cs`
- `MachineRuntimeStateStore.cs`

---

## 4.5 Input

```text
Assets/_Project/Scripts/Input/
```

Purpose:

- Unity Input System configuration
- input action wrappers
- device adapters
- canonical action dispatching

Possible scripts:

- `InputActionRouter.cs`
- `RuntimeInputAction.cs`
- `XRInputAdapter.cs`
- `MobileTouchInputAdapter.cs`
- `DesktopMouseKeyboardInputAdapter.cs`
- `InputContextMap.cs`

This folder should treat the **Unity Input System** as the source of native input truth.

Do not scatter direct input polling across the codebase.

---

## 4.6 Interaction

```text
Assets/_Project/Scripts/Interaction/
```

Purpose:

- selection
- inspection
- grabbing
- rotation
- placement interactions
- context-sensitive behavior

Possible scripts:

- `InteractionContextController.cs`
- `SelectionService.cs`
- `InspectionController.cs`
- `ManipulationController.cs`
- `PlacementInteractionController.cs`

---

## 4.7 Validation

```text
Assets/_Project/Scripts/Validation/
```

Purpose:

- placement validation
- completion conditions
- tolerances
- dependency checks
- challenge-specific strictness

Possible scripts:

- `PlacementValidator.cs`
- `ConstraintValidationService.cs`
- `StepCompletionEvaluator.cs`
- `ToleranceProfile.cs`

---

## 4.8 Presentation

```text
Assets/_Project/Scripts/Presentation/
```

Purpose:

- instructional messaging
- hints
- metadata overlays
- aha moments
- progress feedback

Possible scripts:

- `InstructionPresenter.cs`
- `HintSystem.cs`
- `PartInfoPresenter.cs`
- `ToolInfoPresenter.cs`
- `ProgressHUDController.cs`
- `MilestoneFeedbackController.cs`

---

## 4.9 UI

```text
Assets/_Project/Scripts/UI/
```

Purpose:

- UI Toolkit panel controllers
- UI binding logic
- view-model style state adapters where needed
- menus
- platform-friendly UI behavior

Possible scripts:

- `MachineSelectPanelController.cs`
- `StepPanelController.cs`
- `PartInfoPanelController.cs`
- `ToolInfoPanelController.cs`
- `ChallengeSummaryPanelController.cs`
- `PauseMenuController.cs`
- `UIDocumentBootstrap.cs`

Keep UI controllers separate from runtime state logic.

UI Toolkit should remain a presentation layer that consumes runtime state.

---

## 4.10 Effects

```text
Assets/_Project/Scripts/Effects/
```

Purpose:

- effect lookup
- playback
- lifetime management
- quality-tier switching
- shader and particle coordination

Possible scripts:

- `EffectRuntimeController.cs`
- `EffectDefinitionRegistry.cs`
- `EffectPlaybackService.cs`
- `EffectQualityResolver.cs`

This is where process visuals like welding, sparks, heat glow, torch/fire, dust, and guidance cues should be coordinated.

---

## 4.11 Assets

```text
Assets/_Project/Scripts/Assets/
```

Purpose:

- asset streaming
- caching
- visual spawning
- lightweight loading orchestration

Possible scripts:

- `MachineAssetLoader.cs`
- `StreamingAssetCoordinator.cs`
- `RuntimeAssetCache.cs`
- `PartVisualSpawner.cs`

---

## 4.12 Persistence

```text
Assets/_Project/Scripts/Persistence/
```

Purpose:

- save and restore session state
- settings persistence
- challenge result persistence
- resilience on restart

Possible scripts:

- `SessionPersistenceService.cs`
- `UserSettingsStore.cs`
- `ChallengeRunPersistence.cs`

---

## 4.13 Networking

```text
Assets/_Project/Scripts/Networking/
```

Purpose:

- future multiplayer readiness
- sync-safe adapters
- remote assistance boundaries
- no direct transport coupling yet unless phase requires it

Possible scripts:

- `AssemblySyncStateAdapter.cs`
- `RemoteAssistBridge.cs`
- `NetworkStateSnapshot.cs`

Even if networking is not implemented immediately, keep this folder reserved so the architecture remains open for optimal multiplayer.

---

## 4.14 Challenge

```text
Assets/_Project/Scripts/Challenge/
```

Purpose:

- challenge mode rules
- timing
- scoring
- leaderboard-ready payloads
- speed-run metrics

Possible scripts:

- `ChallengeRunTracker.cs`
- `ChallengeRules.cs`
- `LeaderboardSubmissionPayload.cs`
- `ChallengeScoreCalculator.cs`

---

## 4.15 Platform

```text
Assets/_Project/Scripts/Platform/
```

Purpose:

- capability probing
- device profiles
- quality tier selection
- WebGL / WebGPU / XR conditional behavior boundaries

Possible scripts:

- `CapabilityProfileService.cs`
- `DeviceProfileResolver.cs`
- `PerformanceTierResolver.cs`
- `RenderPathPolicy.cs`

---

## 4.16 Authoring

```text
Assets/_Project/Scripts/Authoring/
```

Purpose:

- future authoring workflows
- content validation tools
- machine package exporters
- step authoring helpers

Possible scripts:

- `MachinePackageExporter.cs`
- `StepAuthoringTool.cs`
- `PartMetadataAuthoringTool.cs`
- `MachinePackageValidator.cs`

---

## 4.17 Utilities

```text
Assets/_Project/Scripts/Utilities/
```

Purpose:

- tightly scoped utility classes only
- no dumping ground

If a utility becomes feature-specific, move it to the proper module.

---

## 4.18 Editor

```text
Assets/_Project/Scripts/Editor/
```

Purpose:

- editor-only inspectors
- content validators
- menu items
- packaging helpers
- build automation

Possible scripts:

- `MachinePackageBuildWindow.cs`
- `ContentValidationMenu.cs`
- `ProjectSetupValidator.cs`
- `UIThemePreviewWindow.cs`

---

## 4.19 Tests

```text
Assets/_Project/Scripts/Tests/
  EditMode/
  PlayMode/
```

Purpose:

- test runtime state transitions
- test content parsing
- test validators
- test persistence
- test challenge rules
- test UI state binding at useful seams

Use Unity Test Framework where appropriate.

---

# 5. Assembly Definition Ownership

A key rule:

**instructional data must not live inside random scene objects.**

Preferred ownership:

- machine data in content package definitions
- runtime state in runtime systems
- visuals in prefabs/materials/VFX assets
- UI in UI Toolkit documents/controllers
- scene objects as presentation hosts only

Avoid:

- hidden truth inside inspector-only scene references
- machine logic embedded into prefab scripts
- single-machine assumptions inside generic systems

---

# 6. Scene Strategy

Use a small number of intentionally designed scenes.

## Suggested scenes

### Boot Scene
Contains:
- bootstrap object
- diagnostics setup
- settings init
- initial routing

### Frontend Scene
Contains:
- machine selection UI
- settings
- intro/tutorial entry points
- root UI Toolkit documents for menus

### Machine Runtime Scene
Contains:
- machine host environment
- runtime composition roots
- camera / XR origin
- UI anchors or UI Toolkit document roots
- machine root spawn points

### Sandbox/Testbed Scenes
Contains:
- isolated testing for placement
- effects testing
- input testing
- shader testing
- challenge loop testing
- UI Toolkit interaction testing

This prevents the main runtime scene from becoming a dumping ground.

---

# 7. Prefab Structure

Suggested prefab layout:

```text
Assets/_Project/Prefabs/
  App/
  Runtime/
  UI/
  Parts/
  Assemblies/
  Effects/
  XR/
  Interaction/
```

Examples:

- `MachineRuntimeRoot.prefab`
- `InstructionPanelRoot.prefab`
- `PartGhostPreview.prefab`
- `PlacementTarget.prefab`
- `WeldSparkEffect.prefab`
- `UIDocumentRoot.prefab`

Prefabs should remain composable and low-responsibility.

---

# 8. Script Assembly Definitions

Use Assembly Definition Files (`.asmdef`) to keep compile boundaries clear.

Suggested assembly groups:

- `Project.Core`
- `Project.Content`
- `Project.Runtime`
- `Project.Input`
- `Project.Interaction`
- `Project.Validation`
- `Project.Presentation`
- `Project.UI`
- `Project.Effects`
- `Project.Assets`
- `Project.Persistence`
- `Project.Platform`
- `Project.Challenge`
- `Project.Networking`
- `Project.Authoring`
- `Project.Editor`
- `Project.Tests`

Benefits:

- faster iteration
- clearer dependencies
- better modularity
- less accidental coupling

Keep references directional and intentional.

---

# 9. Input System Structure

The **Unity Input System** must be structured clearly.

Suggested ownership:

```text
Assets/_Project/XR/InteractionProfiles/
Assets/_Project/Scripts/Input/
Assets/_Project/Settings/Input/
```

Recommended assets:

- `RuntimeInputActions.inputactions`
- control schemes for:
  - desktop
  - mobile
  - XR hands
  - XR controllers

Do not let different systems create conflicting ad hoc input bindings.

All runtime features should use centralized input definitions.

---

# 10. UI Toolkit Structure Guidance

Because UI Toolkit is central to the project, keep the structure explicit.

Suggested separation:

```text
Assets/_Project/UI/UXML/
Assets/_Project/UI/USS/
Assets/_Project/Scripts/UI/
```

Recommended pattern:

- UXML for structure where useful
- USS for style
- C# for dynamic panel control, binding, and integration with runtime systems

UI may also be built programmatically in C# where that is the better fit.

The critical rule is that UI structure and UI state wiring remain organized and readable.

---

# 11. Shader and VFX Structure

Because process visuals are important to assembly learning, keep shader and effect development structured.

Suggested organization:

```text
Assets/_Project/Shaders/Process/
  WeldHeatGlow.shader
  GhostPlacement.shader
  ErrorHighlight.shader

Assets/_Project/VFX/Prefabs/
  WeldSparkEffect.prefab
  TorchFireEffect.prefab
  DustBurstEffect.prefab
  PlacementSuccessEffect.prefab
```

Also keep:

- common shader includes
- effect profiles per quality tier
- material presets
- test scene coverage for all important effects

Effects should have low-end fallbacks for web delivery.

---

# 12. StreamingAssets and Machine Packages

For machine packages intended for runtime loading:

```text
Assets/StreamingAssets/
  MachinePackages/
    onboarding_tutorial/
    power_cube/
```

Each package can include:

- manifest JSON
- assembly definitions
- part metadata
- tool metadata
- optional challenge config
- optional effect definitions
- asset references

This supports web-friendly dynamic content loading and future package updates.

---

# 13. Third-Party and Plugin Management

Keep third-party code isolated.

```text
Assets/Plugins/
Assets/ThirdParty/
```

Examples:

- De-Panther WebXR exporter
- external glTF import tooling if used
- other stable dependencies

Rules:

- do not modify third-party code casually
- wrap plugin usage behind project adapters where possible
- document versions clearly
- prefer latest stable compatible releases only

---

# 14. Branch and Workflow Strategy

Suggested branch categories:

- `docs/architecture-foundation`
- `feature/runtime-step-engine`
- `feature/unity-input-system-foundation`
- `feature/ui-toolkit-foundation`
- `feature/placement-validation`
- `feature/tutorial-vertical-slice`
- `feature/effects-process-visuals`
- `feature/machine-package-loader`
- `experiment/multiplayer-sync`
- `experiment/webgpu-research`

This makes agent collaboration safer.

---

# 15. Commit Discipline

A critical requirement for this repo:

**auto stage and commit after every meaningful validated changeset**

A meaningful changeset is one that:

- compiles
- does not obviously break architecture boundaries
- passes the intended validation for that scope
- has a clear purpose

Commit style should be structured and descriptive.

Examples:

- `feat(runtime): add machine session controller skeleton`
- `feat(input): add Unity Input System canonical action routing`
- `feat(ui): add UI Toolkit step panel controller`
- `feat(validation): add placement validator result model`
- `feat(effects): add welding effect runtime hooks`
- `docs(architecture): update project structure for UI Toolkit and Unity 6.3`

Do not batch unrelated work into one commit.

---

# 16. Reliability Rules for Agents

Agents modifying this project should follow strict rules.

## 16.1 Structural Rules

- place files in the correct module
- do not create duplicate systems in random folders
- do not put business logic inside view scripts
- do not put scene-specific hacks in generic systems
- preserve assembly definition boundaries
- keep names explicit and domain-relevant

---

## 16.2 Implementation Rules

- prefer composition over inheritance
- expose ownership clearly
- document assumptions in code where necessary
- keep runtime models serializable where needed
- avoid singletons unless deliberately justified and isolated

---

## 16.3 Validation Rules

After each meaningful change:

- check compile health
- check no namespace collisions
- check no broken references introduced
- stage and commit the validated changeset

This is essential for reliable agent-driven iteration.

---

# 17. Suggested First Implementation Order

A clean development order:

1. bootstrap and folder structure
2. assembly definitions
3. Unity Input System foundation
4. UI Toolkit foundation
5. machine package definition models
6. machine session controller
7. step controller
8. placement validator
9. instruction presenter
10. simple tutorial vertical slice
11. effect runtime basics
12. persistence
13. challenge hooks
14. multiplayer-ready sync boundaries

This order reduces architectural drift.

---

# 18. Minimal Naming Conventions

Use names that expose responsibility clearly.

Examples:

Good:
- `PlacementValidator`
- `MachineSessionController`
- `EffectPlaybackService`
- `StepPanelController`
- `ToolInfoPanelController`

Avoid:
- `GameManager`
- `Helper`
- `SystemUtils`
- `StuffController`

Folders, classes, and assets should explain what they own.

---

# 19. Long-Term Goal of This Structure

This Unity structure should let the project grow cleanly from:

- a small onboarding build
- to a Power Cube guided assembly flow
- to more OSE machines
- to challenge modes
- to cross-platform XR/mobile/desktop delivery
- to remote assistance
- to multiplayer collaboration and competitive speed-run features

The structure should help agents make reliable changes without breaking architecture or scattering logic across the project.
