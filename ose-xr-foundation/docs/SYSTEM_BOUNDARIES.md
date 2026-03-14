
# SYSTEM_BOUNDARIES.md

## Purpose

This document defines **allowed dependencies between major subsystems** in the OSE XR Foundation architecture.

It acts as a **dependency firewall** that prevents architectural drift and tight coupling.

If a system attempts to directly depend on a forbidden subsystem, the architecture is considered broken.

---

# Core Principle

Dependencies must flow in **one direction**:

Input → Runtime → Presentation

Data packages feed the runtime but do not depend on runtime systems.

UI and visual systems must never own gameplay state.

---

# High Level System Layers

Layer order (top cannot be depended on by lower layers):

1. Input Layer
2. Runtime Layer
3. Presentation Layer
4. Content Data Layer
5. Platform / Engine Layer

Dependencies may only flow **downward**.

---

# Allowed Dependencies

## Input Layer

Responsibilities:

- device input collection
- action mapping
- input adapters

May depend on:

- Unity Input System
- platform APIs

Must NOT depend on:

- UI Toolkit panels
- runtime controllers
- validation logic

Input emits actions that the runtime consumes.

---

## Runtime Layer

Responsibilities:

- machine session state
- step progression
- placement validation
- challenge tracking
- persistence

Runtime may depend on:

- content data models
- validation services
- event system

Runtime must NOT depend on:

- UI Toolkit
- visual effects systems
- scene-only logic

Runtime must remain **pure logic**.

---

## Presentation Layer

Responsibilities:

- UI Toolkit panels
- presenters
- visual effects
- highlights
- audio

Presentation may depend on:

- runtime state (read-only)
- event system

Presentation must NOT:

- mutate runtime state directly
- implement validation logic
- implement progression logic

All runtime changes must go through **runtime controllers**.

---

## Content Data Layer

Responsibilities:

- machine packages
- step definitions
- hints
- validation configuration
- effects references

Content may depend on:

- schema definitions

Content must NOT depend on:

- runtime classes
- UI classes
- scene objects

Content must remain **engine-agnostic structured data**.

---

# Module Dependency Map

Allowed module directions:

Input
→ Runtime
→ Presentation

Content
→ Runtime

Presentation
→ Runtime (read-only)

Runtime must never depend on:

- UI
- scene visuals
- device hardware APIs

---

# Forbidden Dependency Examples

These patterns are not allowed:

❌ UI script directly modifying StepController

❌ Scene object storing authoritative placement state

❌ Validation logic embedded inside UI

❌ Content package referencing runtime classes

❌ Runtime depending on Unity UI Toolkit

---

# Event System Role

The event system acts as a **decoupling layer** between subsystems.

Example flow:

User input
→ input action
→ runtime event
→ validation
→ runtime state change
→ presentation update

Events ensure layers remain independent.

---

# Dependency Enforcement

Developers should enforce boundaries through:

- `.asmdef` module separation
- code review
- architecture validation tests

Suggested module split:

Scripts/
  Input/
  Runtime/
  Validation/
  Presentation/
  Content/

---

# When Boundaries Need to Change

If a new feature appears to require cross-layer access:

1. stop
2. evaluate architecture impact
3. update this document
4. update SOURCE_OF_TRUTH_MAP.md

Never silently bypass boundaries.

---

# Summary

OSE XR Foundation remains scalable by enforcing:

- strict runtime ownership
- presentation-only UI
- data-driven content
- event-driven communication
- controlled module dependencies

If these boundaries remain intact, the system can safely scale to:

- large machine libraries
- multiplayer learning sessions
- remote instructors
- community-authored machines
