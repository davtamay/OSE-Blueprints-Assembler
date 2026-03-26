# INTERACTION_MODEL.md

## Purpose

This document defines the universal interaction language for the XR assembly training application.

Its goal is to prevent inconsistent interaction design across:

- desktop
- mobile
- XR hands
- XR controllers

It also ensures that the **Unity Input System** can be used as the canonical input foundation while keeping runtime behavior device-agnostic.

This document also establishes **Unity XR Interaction Toolkit (XRI)** as the primary XR interaction framework so the XR implementation remains vendor-agnostic by default.

This file should be used together with:

- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/VERTICAL_SLICE_SPEC.md`
- `TASK_EXECUTION_PROTOCOL.md`

---

# 1. Core Principle

The application must not be designed around raw hardware input.

It must be designed around **user intent**.

That means the runtime should think in terms of actions like:

- inspect
- grab
- rotate
- place
- confirm
- request hint
- navigate

and not in terms of:

- left mouse button
- two-finger touch
- XR trigger
- hand pinch

The hardware-specific layer exists only to translate device input into shared interaction actions.

---

# 2. Conceptual Model

The interaction stack should work like this:

native device input  
→ Unity Input System  
→ platform adapter  
→ canonical action  
→ interaction context router  
→ runtime behavior

The XR implementation baseline should fit into this model like this:

XR device signals  
→ Unity Input System  
→ XRI-compatible interaction adapters / interactors  
→ canonical action  
→ interaction context router  
→ runtime behavior

The UI presentation layer should fit into this model cleanly:

runtime state  
→ UI controller / presenter  
→ UI Toolkit view

This gives the project:

- cross-platform consistency
- cleaner runtime logic
- easier testing
- easier extension to new devices
- better future multiplayer compatibility
- a cleaner separation between interaction and UI presentation
- a vendor-agnostic XR interaction baseline

---

# 3. XR Interaction Framework Baseline

## Primary XR Interaction Technology

**Unity XR Interaction Toolkit (XRI)**

XRI is the primary XR interaction framework for this project.

## Why XRI

XRI is preferred because it supports:

- vendor-agnostic XR interaction design
- structured interaction workflows for direct, ray, select, hover, and grab behavior
- good alignment with the Unity Input System
- cleaner portability than using a vendor-specific SDK as the default baseline
- a stronger long-term foundation for cross-headset maintenance

## Vendor SDK Policy

The project should **not** assume Meta Interaction SDK as a required baseline dependency.

Vendor-specific SDKs may be layered in later only when a clear, justified need exists and the architectural cost is understood.

## Architectural Rule

XR behavior should be expressed in terms of:

- user intent
- canonical actions
- interaction contexts
- runtime ownership

not in terms of vendor-specific APIs.

---

# 4. UI Framework Alignment

## Primary UI Technology

**Unity UI Toolkit (runtime)**

UI Toolkit is the primary UI framework for the project.

## UI Interaction Routing

The interaction flow should be understood as:

native input  
→ Unity Input System  
→ platform adapter  
→ canonical action  
→ interaction context router  
→ runtime systems and UI routing  
→ UI Toolkit presentation where relevant

## UI Responsibility Rule

UI Toolkit panels are responsible for presenting things like:

- step instructions
- part information
- tool information
- hints
- confirmation prompts
- challenge summaries
- settings and pause menus

UI Toolkit panels do **not** own:

- assembly validation
- step truth
- placement truth
- machine state truth

Those remain in runtime systems.

## XR UI Note

UI Toolkit may be used for:

- screen-space UI on desktop/mobile
- world-space or spatially presented UI surfaces in XR, where supported by the chosen implementation approach

---

# 5. Canonical Action Vocabulary

These are the shared interaction actions the runtime should understand.

## 5.1 Primary World Actions

- `Select`
- `Inspect`
- `Grab`
- `Move`
- `Rotate`
- `Place`
- `Confirm`
- `Cancel`

## 5.2 Navigation Actions

- `Navigate`
- `Orbit`
- `Pan`
- `Zoom`
- `RecenterView`

## 5.3 Guidance Actions

- `RequestHint`
- `OpenPartInfo`
- `OpenToolInfo`
- `AdvanceStep`
- `GoBackStep`
- `TogglePhysicalMode`

## 5.4 Session / Utility Actions

- `Pause`
- `Resume`
- `RestartChallenge`
- `OpenMenu`
- `CloseMenu`

These actions are canonical. Devices should map into them.

## 5.5 Step-Level Interaction Patterns

The canonical actions above (Select, Grab, Place, Confirm, etc.) are **atomic** — they describe a single user intent. During step execution, these atomic actions compose into **interaction patterns**: reusable, step-level interaction contracts that define the full learner-facing physical interaction for completing a step.

For example, the **SelectPair** pattern composes two Select actions into a tap-A-then-tap-B sequence used by both measurement (Use.Measure) and cable connection (Connect.Cable) steps.

Interaction patterns are distinct from step families (semantic meaning) and profiles (behavioral refinement). The runtime resolves the correct pattern automatically from family + profile + step data.

For the full pattern catalog and family-to-pattern mapping, see `INTERACTION_PATTERN_MATRIX.md`.

---

# 6. Interaction Contexts

The same action can mean different things depending on context.

The interaction system must therefore support explicit contexts.

## 6.1 Core Contexts

- `NavigationContext`
- `InspectionContext`
- `AssemblyPlacementContext`
- `UIContext`
- `TutorialContext`
- `ChallengeContext`
- `PhysicalSubstitutionContext`

## 6.2 Future Contexts

- `RemoteAssistContext`
- `MultiplayerSharedContext`
- `ProcessActionContext`

## 6.3 Why Contexts Matter

Without contexts, input handling becomes scattered and fragile.

Example:

- a drag in navigation mode may orbit the camera
- a drag while holding a selected part may move the part
- a drag inside a menu may scroll UI

The context router must decide which behavior owns the action.

---

# 7. Interaction States

Within a context, objects and the user may move through states.

## 7.1 Object Interaction States

- `Idle`
- `HoverCandidate`
- `Selected`
- `Inspecting`
- `Manipulating`
- `PlacementCandidate`
- `Placed`
- `Blocked`
- `PhysicallySubstituted`

## 7.2 User Flow States

- `Browsing`
- `LearningStep`
- `InspectingPart`
- `ManipulatingPart`
- `ReviewingHint`
- `ConfirmingAction`
- `Paused`
- `ChallengeRunning`

These states help the runtime and presentation systems stay coordinated.

---

# 8. Platform Mapping Strategy

The same runtime actions must be reachable from desktop, mobile, and XR.

## 8.1 Desktop Mapping

Likely mappings include:

- mouse move → hover / aim / orbit depending on context
- left click → select / confirm / grab
- right click drag → orbit / alternative navigation
- scroll wheel → zoom / step through inspect detail where relevant
- keyboard keys → utility commands, step navigation, pause, hints

## 8.2 Mobile Mapping

Likely mappings include:

- single-finger tap → select / confirm
- drag → manipulate or orbit depending on context
- pinch → zoom
- two-finger drag → pan
- long press → inspect or open contextual detail

## 8.3 XR Mapping

XR should be implemented with **XRI-first** interactions.

Likely mappings include:

- direct hand/controller selection → select / grab
- ray interaction where appropriate → UI select / distant selection
- hand/controller pose-based manipulation → move / rotate
- confirm gestures/buttons → confirm / place
- utility bindings → hints / pause / menu

The important rule is that XRI interaction events still resolve into the same canonical runtime action vocabulary.

---

# 9. Canonical XR Routing Rule

XR interaction code must not bypass the core interaction model.

That means:

XRI interactors / interaction callbacks  
→ adapter / router  
→ canonical action  
→ interaction context router  
→ runtime behavior

Do not bury runtime truth inside a headset-specific interactor script.

---

# 10. Platform Adapter Responsibilities

Platform adapters are the device-specific translation layer.

## 10.1 Suggested Adapters

- `DesktopMouseKeyboardInputAdapter`
- `MobileTouchInputAdapter`
- `XRInputAdapter`

## 10.2 Responsibilities

Adapters should:

- listen to native input sources
- map to canonical actions
- pass intent into the interaction router
- avoid owning feature logic
- avoid owning step progression or validation logic

In XR, the adapter layer may translate XRI events and interaction state into canonical actions for the runtime.

---

# 11. Interaction Router Responsibilities

The interaction router should:

- determine active interaction context
- dispatch canonical actions to the correct subsystem
- prevent conflicting handlers from responding at once
- separate UI-targeted actions from world-targeted actions
- keep action handling deterministic and inspectable

This is the point where the system chooses whether an input is:

- a camera/navigation action
- a UI action
- a part inspection action
- a part manipulation action
- a placement confirmation action
- a hint or tutorial action

---

# 12. UI vs World Interaction Rule

The system must clearly distinguish UI interaction from world interaction.

## 12.1 UI Context

When the user is interacting with a panel, menu, button, or other UI affordance:

- UI owns the event
- world interaction should not also fire unless deliberately allowed

## 12.2 World Context

When the user is manipulating or inspecting assembly content:

- world interaction owns the event
- UI should not steal focus unexpectedly

This must remain true across desktop, mobile, and XR.

---

# 13. Physical Substitution Interaction Rule

The app supports a hybrid virtual/physical workflow.

That means interaction must support marking a part as physically present.

Possible canonical flow:

- inspect part
- choose physical substitution mode
- confirm that real-world part is present
- hide or reduce the virtual part representation
- continue progression using runtime truth

This should be a first-class interaction path, not a hack layered on top later.

---

# 14. Hint and Reinforcement Interaction Rule

Hints and reinforcement should be interaction-driven, not passive-only.

Users should be able to:

- request a hint
- inspect why a step matters
- review what a part does
- open tool info
- replay or re-check a critical explanation

These actions should map into the canonical action model and remain available across supported platforms.

---

# 15. Challenge Interaction Rule

Challenge and speed-run modes may later add competitive pressure.

However, they must still work inside the same canonical interaction system.

Challenge mode may emphasize:

- faster confirmation
- fewer hints
- fewer invalid placements
- cleaner timing

But it must not invent a separate input model.

---

# 16. Interaction Feedback Rules

Every important interaction should provide feedback.

Examples:

- hover feedback
- selection feedback
- inspection highlight
- placement preview
- valid/invalid placement indication
- success confirmation
- blocked action feedback

Feedback can be visual, audio, or UI-driven, but the interaction model should assume that meaningful actions are acknowledged.

---

# 17. Accessibility and Tolerance Considerations

The interaction system should remain teachable and forgiving.

That means:

- avoid unnecessary precision when possible
- use generous tutorial tolerances early
- allow hints to reduce friction
- make target states visible
- support readable interaction feedback

This is especially important in XR and mobile contexts.

---

# 18. Multiplayer-Open Interaction Requirement

The interaction model must stay open to future multiplayer.

That means interactions that matter should be representable as sync-safe intent or state transitions, such as:

- selected part
- grabbed part
- placement attempt
- placement success
- hint requested
- confirmation action
- physical substitution confirmation

The interaction layer does not need networking now, but it must not make networking impossible later.

---

# 19. Validation Questions

Before approving a new interaction behavior, ask:

- Is this expressed as user intent or raw hardware?
- Does it route through the canonical action model?
- Does it fit an explicit interaction context?
- Does it preserve UI vs world separation?
- Does it remain compatible across desktop, mobile, and XR?
- In XR, is this aligned with XRI-first implementation rather than vendor lock-in?
- Does it preserve runtime ownership?
- Does it remain open for future multiplayer?
- Does it provide understandable user feedback?

If these are unclear, the interaction behavior is not stable enough.

---

# 20. Final Guidance

The correct interaction strategy is not:

“make each device work however is convenient.”

The correct strategy is:

- define intent first
- translate devices into canonical actions
- let contexts decide meaning
- keep runtime ownership explicit
- keep UI separate from world interaction
- keep the XR baseline XRI-first and vendor-agnostic
- keep the app teachable and forgiving
- preserve portability and future multiplayer compatibility

That is how the interaction model stays coherent as the project grows.
