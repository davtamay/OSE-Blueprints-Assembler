# UI_ARCHITECTURE.md

## Purpose

This document defines the UI architecture for the XR assembly training application.

Its goal is to ensure the UI remains:

- modular
- maintainable
- runtime-driven
- cross-platform
- compatible with desktop, mobile, and XR
- aligned with the Unity Input System
- aligned with Unity UI Toolkit
- safe for future multiplayer, challenge, and remote-assistance expansion

This document exists because UI can easily become a hidden source of state ownership, architectural drift, and messy coupling if not defined clearly.

This file should be used together with:

- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/INTERACTION_MODEL.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`
- `TASK_EXECUTION_PROTOCOL.md`

---

# 1. Core UI Principle

The UI is a **presentation layer**.

The UI must not own core assembly truth.

That means UI must not become the source of truth for:

- current machine state
- current step
- part placement truth
- validation truth
- challenge truth
- physical substitution truth
- multiplayer truth

Those belong to runtime systems.

The UI exists to:

- present state
- communicate instructions
- expose controls and confirmations
- show feedback
- support learning and clarity

---

# 2. Technology Choice

## Primary UI Framework

**Unity UI Toolkit (runtime)**

UI Toolkit is the primary UI framework for this project.

## Why UI Toolkit

UI Toolkit is preferred because it supports:

- structured runtime UI
- code-driven integration
- reusable panel architecture
- separation of presentation from runtime logic
- maintainable styling
- good fit for data-driven systems
- clean panel and controller patterns

The project may use:

- UXML for structure
- USS for styling
- C# for dynamic UI creation, binding, and runtime integration

The project is allowed to create UI programmatically in C# where that is the better fit.

---

# 3. UI Conceptual Model

The UI should fit into the larger system like this:

runtime state  
→ presenter / UI adapter  
→ UI controller  
→ UI Toolkit view  
→ user sees and interacts  
→ user action routed back through Unity Input System and interaction systems  
→ runtime updates state

This means:

- runtime owns truth
- presenters translate truth for UI
- UI controllers coordinate view behavior
- UI Toolkit renders the view
- user actions go back into runtime pathways, not around them

---

# 4. UI Ownership Model

## 4.1 Runtime Systems Own Truth

Examples:

- `MachineSessionController`
- `AssemblyRuntimeController`
- `StepController`
- `PlacementValidator`
- `ChallengeRunTracker`
- `SessionPersistenceService`

These systems own real state.

## 4.2 Presenters / Adapters Own Translation

Presenters translate runtime state into display-friendly models.

Examples:

- current step title
- part info text
- tool usage text
- challenge summary formatting
- progress percentage
- hint visibility state

Presenters do not create runtime truth.

## 4.3 UI Controllers Own Panel Coordination

UI controllers coordinate a panel or document.

Examples:

- show/hide panel
- refresh labels
- wire button callbacks
- update visibility states
- route user interface actions to the correct runtime or interaction system

## 4.4 UI Toolkit Views Own Presentation Structure

Views define:

- layout
- structure
- visual hierarchy
- style hooks

Views should not implement assembly logic.

---

# 5. Recommended UI Layers

The UI architecture should be separated into these layers.

## 5.1 View Layer

This is the UI Toolkit presentation layer.

Assets may include:

- UXML
- USS
- `UIDocument`
- visual element trees

Responsibilities:

- render the panel
- expose named UI elements
- host styles and layout

## 5.2 Panel Controller Layer

This is the runtime-side UI coordination layer.

Responsibilities:

- bind to view elements
- receive display models from presenters
- subscribe to runtime events
- update UI elements
- route button interactions

Examples:

- `StepPanelController`
- `PartInfoPanelController`
- `ToolInfoPanelController`
- `PauseMenuController`

## 5.3 Presenter / View-Model Layer

This is the transformation layer between runtime state and UI display.

Responsibilities:

- derive user-readable data
- reduce formatting logic inside UI controllers
- isolate display concerns from runtime systems

Examples:

- `StepPanelPresenter`
- `ProgressPresenter`
- `ChallengeSummaryPresenter`

## 5.4 UI Service / Root Coordination Layer

This layer coordinates multiple panels and UI lifecycles.

Responsibilities:

- show/hide major UI groups
- manage layer ordering
- manage active panel sets by context
- coordinate root UI mode switching

Example:

- `UIRootCoordinator`

---

# 6. UI States and Modes

UI behavior should respond to application context.

## 6.1 Primary UI Modes

- `FrontendMode`
- `TutorialMode`
- `AssemblyMode`
- `InspectionMode`
- `ChallengeMode`
- `PausedMode`

## 6.2 Future UI Modes

- `RemoteAssistMode`
- `MultiplayerSharedMode`
- `ReviewMode`

The UI root should know which high-level mode is active so it can show the correct panel groups.

---

# 7. Core Panel Set

The project should define a stable set of core panels.

## 7.1 Step Panel

Displays:

- current step title
- instruction text
- optional step number
- progress
- confirm/back/hint actions if appropriate

## 7.2 Part Info Panel

Displays:

- part name
- function
- material
- search terms
- structural role

## 7.3 Tool Info Panel

Displays:

- tool name
- purpose
- usage notes
- safety notes if provided

## 7.4 Hint Panel

Displays:

- hint text
- optional hint type
- contextual guidance
- dismiss action

## 7.5 Progress Panel / HUD

Displays:

- current progress
- current assembly/subassembly
- optional challenge timer
- optional milestone state

## 7.6 Pause / Settings Panel

Displays:

- pause actions
- resume
- settings
- restart or exit options where appropriate

## 7.7 Completion Summary Panel

Displays:

- completion status
- time
- retries
- hints used
- challenge result if active
- next recommended action

---

# 8. Panel Ownership Rules

Each panel must have a clear owner.

Examples:

- `StepPanelController` owns step panel coordination
- `PartInfoPanelController` owns part info panel coordination
- `ChallengeSummaryPanelController` owns challenge summary rendering

Do not let one giant UI script own every panel.

No `UIManager` god class should control all panel details directly unless it is a deliberately small root coordinator.

---

# 9. UI Toolkit Asset Structure

Recommended UI Toolkit structure:

```text
Assets/_Project/UI/
  Documents/
  UXML/
  USS/
  Themes/
  Panels/
  Debug/
```

Recommended script structure:

```text
Assets/_Project/Scripts/UI/
  Controllers/
  Presenters/
  Root/
  Bindings/
  Utilities/
```

Examples:

- `Controllers/StepPanelController.cs`
- `Presenters/StepPanelPresenter.cs`
- `Root/UIRootCoordinator.cs`
- `Bindings/UIDocumentBootstrap.cs`

This keeps UI maintainable.

---

# 10. Runtime-to-UI Data Flow

The UI should consume data in a predictable direction.

Recommended flow:

runtime event or state change  
→ presenter derives display model  
→ panel controller applies display model to UI Toolkit view  
→ UI updates

For example:

step changes  
→ `StepController` updates runtime state  
→ `StepPanelPresenter` creates display model  
→ `StepPanelController` updates labels/buttons  
→ user sees new instruction

This keeps runtime logic out of the panel.

---

# 11. UI-to-Runtime Action Flow

When the user clicks or activates a UI control, the flow should be:

UI Toolkit event  
→ panel controller  
→ appropriate runtime / interaction action  
→ runtime updates state  
→ presenters refresh UI

Do not bypass runtime systems.

For example:

user presses “Request Hint”  
→ `HintPanelController` or `StepPanelController` routes action  
→ runtime hint service or interaction action is invoked  
→ hint state updates  
→ hint presenter updates UI

---

# 12. UI and Interaction Integration

The UI must integrate cleanly with the interaction model.

## 12.1 UI vs World Routing

If the user is clearly interacting with UI:

- UI owns the event

If the user is interacting with the assembly world:

- world interaction systems own the event

The routing must be explicit and reliable.

## 12.2 Input System Rule

UI actions still live within the broader architecture:

Unity Input System  
→ interaction routing / UI routing  
→ runtime and UI response

Do not create ad hoc input logic directly inside random UI elements.

---

# 13. UI Presentation Rules

The UI should be readable and low-friction.

## 13.1 Clarity Rules

- clear hierarchy
- minimal clutter
- obvious primary action
- platform-appropriate sizing
- strong contrast where needed
- clear status changes
- explicit feedback when actions succeed or fail

## 13.2 Teaching Rules

The UI should support learning by making it easy to understand:

- what the user is doing
- why the current step matters
- what tool is required
- what went wrong if validation fails
- how to continue

The UI should reinforce the educational goals, not distract from them.

---

# 14. Cross-Platform UI Rules

The same application must support desktop, mobile, and XR.

## 14.1 Shared Design Principle

Keep the same information architecture where possible, but allow layout adaptation by platform.

## 14.2 Desktop UI Rules

Desktop can support:

- side panels
- hover affordances
- denser debug information during development
- mouse-friendly controls

## 14.3 Mobile UI Rules

Mobile requires:

- larger buttons
- simpler layouts
- fewer simultaneous panels
- less clutter
- touch-friendly spacing

## 14.4 XR UI Rules

XR UI must be readable in spatial context.

Rules:

- panels should be comfortably placed
- important text must remain readable
- controls must be large enough for XR interaction
- avoid excessive panel layering
- avoid requiring fine precision for core menu actions

---

# 15. XR UI Strategy

UI Toolkit is the primary UI framework, but XR requires intentional presentation choices.

## 15.1 XR UI Goals

XR UI should support:

- step instructions
- confirmations
- part/tool information
- hint display
- progress
- pause/settings

## 15.2 XR Panel Placement

Panels may be:

- attached to a stable world anchor
- attached to the user relative space when appropriate
- summoned contextually

Avoid panels that constantly drift or obstruct the work area.

## 15.3 XR Interaction Rule

XR panels must remain compatible with the chosen XR interaction path and the broader interaction routing model.

The UI architecture should remain flexible enough to support:

- ray-based interaction
- poke/direct interaction
- future hand/controller refinement

---

# 16. Screen-Space vs World-Space UI

The project may use both.

## 16.1 Screen-Space Use Cases

Best for:

- desktop
- mobile
- menus
- onboarding
- debug overlays
- challenge summaries

## 16.2 World-Space / Spatial UI Use Cases

Best for:

- XR step panels
- contextual tool/part information
- anchored guidance panels
- assembly-local prompts

## 16.3 Rule

The same panel purpose may need multiple presentation strategies depending on platform.

The information model should stay the same even if layout differs.

---

# 17. UI Lifecycle

UI should have a predictable lifecycle.

## 17.1 Suggested Lifecycle

- initialize root UI
- load required documents
- bind panel controllers
- subscribe to runtime events
- receive initial state
- update in response to state changes
- unbind / dispose cleanly on scene unload or mode change

## 17.2 Lifecycle Rule

UI controllers must unsubscribe and clean up properly.

Do not leak event subscriptions across sessions or scene transitions.

---

# 18. Debug UI and Developer UI

The project should support debug UI, but keep it separate from player-facing UI.

Recommended separation:

- player panels in normal UI folders
- debug panels in `UI/Debug/`
- debug toggles behind explicit dev-only flags

Examples:

- runtime state inspector
- current step debug view
- content load diagnostics
- validation result debug view
- platform capability debug display

This helps development without polluting player UI.

---

# 19. Challenge UI

Challenge and speed-run features need dedicated UI seams.

## 19.1 Challenge UI Needs

- timer display
- retry count
- hint penalty display
- completion summary
- best run comparison later
- optional leaderboard state later

## 19.2 Rule

Challenge UI must remain optional.

Do not make the entire UI architecture depend on challenge mode being active.

---

# 20. Multiplayer and Remote Assistance UI Readiness

The UI should stay open for future collaboration features.

Potential future UI needs:

- remote expert presence indicators
- shared progress markers
- annotation overlays
- co-op step state
- spectator/instructor prompts

The current UI architecture should not hardcode purely single-user assumptions into every panel.

However, these features should not destabilize the current architecture.

---

# 21. Performance Rules for UI

The UI must remain efficient for web delivery.

## 21.1 Rules

- avoid excessive dynamic hierarchy churn
- avoid rebuilding whole documents unnecessarily
- update only what changed when practical
- keep styling manageable
- avoid overly heavy visual complexity on constrained devices

## 21.2 XR/Mobile Caution

Be especially careful with:

- too many simultaneous panels
- expensive visual effects in UI
- tiny unreadable text that forces extra UI complexity
- layouts that require excessive updates per frame

---

# 22. Recommended First UI Implementation Order

Implement UI in this order:

1. establish UI Toolkit root setup
2. create root coordinator
3. create step panel
4. create part info panel
5. create tool info panel
6. create hint panel
7. create progress HUD
8. create pause/settings panel
9. create completion summary panel
10. add debug UI where needed
11. refine XR/world-space panel strategy
12. add challenge UI seams
13. preserve future multiplayer seams

This keeps the UI grounded in the actual user journey.

---

# 23. Validation Questions

Before approving a UI feature, ask:

- Is runtime still the source of truth?
- Is this panel owned by the correct controller?
- Is display formatting separated from runtime logic?
- Does the UI remain compatible with desktop, mobile, and XR?
- Does this respect the Unity Input System and interaction routing model?
- Is the panel readable and discoverable?
- Does it support learning clearly?
- Did we avoid turning the UI into hidden gameplay logic?
- Does this remain open for future multiplayer and challenge features?

If any answer is unclear, the UI architecture is not stable yet.

---

# 24. Final Guidance

The correct UI strategy is not:

“put all behavior in the panel scripts and make it work somehow.”

The correct UI strategy is:

- keep runtime as the source of truth
- use presenters and panel controllers as clear intermediaries
- let UI Toolkit focus on presentation and interaction surfaces
- keep panels modular
- adapt presentation per platform without changing information ownership
- keep the UI clean, instructional, and scalable
- leave the door open for future XR refinement, multiplayer overlays, and challenge systems

That is how the UI stays reliable as the project grows.
