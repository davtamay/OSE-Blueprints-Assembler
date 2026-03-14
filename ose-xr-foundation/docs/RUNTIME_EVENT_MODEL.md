
# RUNTIME_EVENT_MODEL.md

## Purpose

This document defines the runtime event architecture used by the XR assembly application.

The goal is to ensure all major systems communicate through a **clear, decoupled event model** rather than tightly coupled direct calls.

This keeps the system:

- modular
- testable
- multiplayer-ready
- UI‑agnostic
- scalable as more machines are added

The runtime event system acts as the **central nervous system** connecting:

- assembly logic
- validation
- UI
- effects
- tool usage
- analytics
- persistence
- future multiplayer sync

---

# Core Principle

Runtime systems must **not directly control each other**.

Instead they:

1. emit events
2. subscribe to events
3. react to events

This prevents hard dependencies between modules.

---

# Event Categories

Events are grouped into several categories.

## Interaction Events

Triggered by user interaction.

Examples:

- PartGrabStarted
- PartGrabEnded
- PartHoverStarted
- PartHoverEnded
- ToolActivated
- ToolDeactivated

These originate from the **Input + Interaction system**.

---

## Assembly Events

Triggered when the user manipulates assembly components.

Examples:

- PartPlacementAttempted
- PartPlacementValidated
- PartPlacementRejected
- PartPlacedSuccessfully
- StepCompleted

These are emitted by the **AssemblyRuntime** system.

---

## Step Flow Events

These control progression through assembly instructions.

Examples:

- StepStarted
- StepHintRequested
- StepHintDisplayed
- StepCompleted
- SubAssemblyCompleted
- AssemblyCompleted

---

## Validation Events

Validation events confirm correct placement or action.

Examples:

- ValidationStarted
- ValidationPassed
- ValidationFailed
- DependencyMissing

These events drive both UI messaging and feedback effects.

---

## UI Events

UI should subscribe to runtime state changes rather than poll systems.

Examples:

- DisplayPartInfo
- DisplayToolInfo
- UpdateStepInstruction
- ShowHint
- ShowCompletionPanel
- ShowErrorMessage

These events are handled by the **UI Toolkit presenter layer**.

---

## Effects Events

Visual feedback should be triggered by events rather than embedded logic.

Examples:

- PlayPlacementEffect
- PlaySuccessEffect
- PlayErrorEffect
- PlaySparkEffect
- PlayHeatEffect

These events are handled by the **EffectsSystem**.

Effects may use:

- particle systems
- shader effects (HLSL)
- audio cues

---

## Challenge / Score Events

For future challenge modes.

Examples:

- TimerStarted
- TimerStopped
- ScoreUpdated
- SpeedRunCompleted

---

## Multiplayer Sync Events (Future)

These events enable synchronization later.

Examples:

- PlayerJoinedSession
- PlayerLeftSession
- PlayerPickedPart
- PlayerPlacedPart
- SharedAssemblyUpdated

Even if multiplayer is not implemented initially, the event architecture must support it.

---

# Event Flow Example

Example sequence when a user installs a bolt.

User grabs bolt  
→ Interaction Event: `PartGrabStarted`

User moves bolt near target  
→ Interaction Event: `PartHoverStarted`

User attempts placement  
→ Assembly Event: `PartPlacementAttempted`

Validation system checks constraints  
→ Validation Event: `ValidationStarted`

Validation passes  
→ Validation Event: `ValidationPassed`

Assembly system confirms placement  
→ Assembly Event: `PartPlacedSuccessfully`

Step system updates progress  
→ Step Flow Event: `StepCompleted`

UI updates instruction panel  
→ UI Event: `UpdateStepInstruction`

Effects system triggers feedback  
→ Effects Event: `PlaySuccessEffect`

This entire chain occurs without systems directly controlling each other.

---

# Event Bus Design

The runtime should include a central **EventBus**.

Responsibilities:

- register event listeners
- dispatch events
- allow systems to subscribe/unsubscribe
- remain lightweight

Example conceptual structure:

EventBus
  subscribe(eventType, listener)
  unsubscribe(eventType, listener)
  publish(event)

The EventBus must be extremely lightweight because it will be used frequently.

---

# Event Payload Design

Each event should contain minimal structured data.

Example payload:

PartPlacedSuccessfully

{
  partId
  stepId
  assemblyId
  position
  rotation
  userId (optional future multiplayer)
}

Payloads should avoid carrying heavy data structures.

---

# Event Naming Rules

Event names should be:

- descriptive
- past-tense for completed actions
- present-tense for attempts
- consistent across systems

Examples:

Correct:
- PartPlacementAttempted
- PartPlacedSuccessfully
- ValidationFailed

Incorrect:
- DoPlacePart
- ActionPlacement

---

# Avoid Event Abuse

Events should represent **meaningful state changes**.

Do not emit events for:

- every frame update
- temporary animation states
- micro‑state transitions that no system needs

The event model should remain **semantic**, not noisy.

---

# System Responsibilities

Each runtime module should own a specific responsibility.

Interaction System

- emit interaction events

Assembly Runtime

- interpret user actions
- emit assembly events

Validation System

- verify placement rules
- emit validation results

Step System

- track assembly progress
- emit step flow events

UI System (UI Toolkit)

- subscribe to runtime events
- update UI panels

Effects System

- subscribe to effect events
- trigger particles, shaders, audio

Challenge System

- subscribe to step and timer events
- compute scores

Persistence System

- subscribe to completion events
- save state

Multiplayer System (future)

- mirror relevant events across network

---

# Event Logging

Events should optionally support logging for debugging.

This is useful for:

- diagnosing broken step logic
- validating machine packages
- tracking user flow
- multiplayer debugging

Logging should be toggleable in development builds.

---

# Testing Strategy

Event-driven systems must be tested independently.

Tests should validate:

- correct event emission
- correct subscription behavior
- correct event order
- no duplicate event dispatch
- no missing event listeners

---

# Benefits of This Model

A clean runtime event model provides:

- system decoupling
- UI independence
- easier debugging
- multiplayer compatibility
- easier content integration
- easier testing
- scalable architecture

This is essential for a project expected to grow into:

- many machines
- many assemblies
- cross-platform runtime
- future collaborative XR experiences.

