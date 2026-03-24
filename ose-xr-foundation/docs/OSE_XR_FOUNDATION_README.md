# OSE XR Foundation – Documentation Guide

Welcome to the **OSE XR Foundation** architecture documentation.

This repository defines the technical foundation for building a **cross-platform XR training platform** capable of teaching real-world machine assembly through immersive interaction.

The system is designed to support:

- XR headsets (WebXR / Quest browser)
- Desktop browsers
- Mobile browsers

The architecture emphasizes:

- data-driven machine content
- deterministic runtime validation
- scalable machine libraries
- performance-first design
- future multiplayer support

This README helps developers and AI agents quickly understand **where architectural truth lives**.

---

# How to Navigate the Documentation

## Start Here (Core Authority)

These documents define the **mission, stack, and ownership model**.

1. **AGENTS.md**  
   High-level project mission and development philosophy.

2. **TECH_STACK.md**  
   Technology choices and upgrade policy.

3. **SOURCE_OF_TRUTH_MAP.md**  
   Defines which systems own which runtime state and which documents are authoritative.

4. **SYSTEM_BOUNDARIES.md**  
   Defines allowed dependencies between subsystems and enforces the architecture firewall:

Input → Runtime → Presentation

This prevents tight coupling and protects long-term scalability.

5. **APP_CURRENT_PROGRESS_FOR_AGENT.md**  
   Live implementation-state handoff for agents and developers.  
   Use this first when resuming work to see the current completed phase, the active visual validation path, and the next recommended phase.

---

# System Architecture

These documents define how the runtime system is structured.

- **ARCHITECTURE.md**  
  Overall system decomposition and module boundaries.

- **ASSEMBLY_RUNTIME.md**  
  Defines the machine session lifecycle and step progression system.

- **RUNTIME_EVENT_MODEL.md**  
  Describes the event-driven communication model used between runtime systems.

---

# Interaction Systems

- **INTERACTION_MODEL.md**

Defines canonical interaction actions across platforms and the interaction-stack direction.

- **INTERACTION_PATTERN_MATRIX.md**

Defines reusable step-level interaction patterns (PlaceOnZone, SelectPair, TargetHit, etc.), the family-to-pattern mapping, and entity role classification. Patterns are the learner-facing physical interaction contracts that implement step execution.

The current XR interaction baseline is:

- **Unity XR Interaction Toolkit (XRI)** as the primary XR interaction framework

The interaction model remains platform-agnostic and intent-driven:

| Platform | Input |
|--------|--------|
| XR | hands / controllers |
| Desktop | mouse |
| Mobile | touch |

Interaction semantics must remain platform-agnostic even while XR implementation is XRI-first.

---

# View System

- **STEP_VIEW_FRAMING.md**

Defines how the camera should frame each assembly step's spatial context. Establishes the sixth canonical concept alongside Entity Role, Step Family, Interaction Pattern, Profile, and Payloads.

Key concepts:

- **View Modes** -- semantic classifications (SourceAndTarget, PairEndpoints, WorkZone, PathView, Overview, Inspect)
- **Framing Behavior** -- soft animated transitions on step activation; selection does not reframe
- **Recovery Affordances** -- Back (previous step's framing) and Step Home (current step's authored framing)
- **Resolution** -- view mode resolved from family + profile defaults, or explicitly authored per step

---

# UI System

- **UI_ARCHITECTURE.md**

Defines the UI architecture using **Unity UI Toolkit**.

Key principles:

- UI is presentation only
- runtime owns truth
- presenters translate runtime state to UI

Runtime → Presenter → UI

---

# Content System

Machine experiences are defined through **data-driven packages**.

Documents:

- **CONTENT_MODEL.md**  
  Conceptual content hierarchy for machines, assemblies, subassemblies, parts, tools, and steps.

- **DATA_SCHEMA.md**  
  Canonical machine package format.

- **STACKING_ARCHITECTURE.md**  
  Canonical model for moving a previously completed subassembly as one rigid unit while preserving part identity.

- **PART_AUTHORING_PIPELINE.md**  
  Workflow for turning real-world machines into digital training content.

---

# Unity Project Structure

- **UNITY_PROJECT_STRUCTURE.md**

Defines:

- folder layout
- module boundaries
- `.asmdef` organization

Maintaining clear module boundaries is critical for scalability.

---

# Performance Architecture

- **PERFORMANCE_ARCHITECTURE.md**

Defines performance rules for:

- WebXR deployments
- mobile browsers
- desktop browsers

Includes profiling workflows using:

- Unity Profiler
- Web browser tools
- **Perfetto MCP CPU/GPU profiling**

---

# Testing Strategy

- **TEST_STRATEGY.md**

Defines:

- unit tests
- system tests
- runtime integration tests
- schema validation
- performance regression testing

Testing protects the architecture from accidental breakage.

---

# Implementation Order

- **IMPLEMENTATION_CHECKLIST.md**

Defines the recommended order for implementing the system from:

1. runtime core
2. interaction layer
3. validation systems
4. UI layer
5. content pipeline
6. vertical slice

Keep **APP_CURRENT_PROGRESS_FOR_AGENT.md** updated alongside this checklist.  
It tracks what has already been completed in the real codebase and what the next active phase should be.

---

# Working Protocol

This repository also includes:

- **TASK_EXECUTION_PROTOCOL.md**

This file defines the canonical agent workflow for:

- Plan Mode
- task sizing
- technical validation
- visual validation
- commit discipline

It is the operational companion to **CLAUDE_TASK_PROMPT.md**.

---

# Vertical Slice Definition

- **VERTICAL_SLICE_SPEC.md**

Defines the first full end-to-end learning experience used to validate the architecture.

---

# Machine Selection Research

- **MACHINE_SELECTION_RESEARCH.md**

Research on candidate machines suitable for the first training experience.

---

# Operator Prompt

This repository also contains:

**CLAUDE_TASK_PROMPT.md**

This file is a prompt template for AI agents assisting with development.  
It ensures agents follow architecture rules and consult the correct documentation before making changes.

It is not architectural truth itself.

---

# Architectural Principles

The project follows several strict rules:

### 1. Runtime Owns Truth

Gameplay and machine state are owned by runtime systems.

UI must never be the source of truth.

### 2. Data Drives Content

Machine experiences are defined through structured data packages.

### 3. Systems Communicate Through Events

Subsystems communicate through the event system rather than direct coupling.

### 4. Performance First

XR and browser deployments require strict performance discipline.

### 5. Documentation Is Authoritative

Architecture decisions must be reflected in documentation to prevent drift.

### 6. Architecture Boundaries Are Enforced

Subsystem dependencies must respect the rules defined in:

SYSTEM_BOUNDARIES.md

This prevents runtime logic leaking into UI or scene objects becoming hidden sources of truth.

### 7. XR Runtime Must Remain Vendor-Agnostic by Default

The current XR interaction baseline is **XRI-first**, not vendor-SDK-first.

This helps keep the runtime portable and maintainable across headset ecosystems.

---

# Repository Philosophy

OSE XR Foundation is intended to become a **long-term platform for XR technical education**.

The architecture prioritizes:

- clarity
- scalability
- deterministic runtime behavior
- data-driven learning experiences
- agent-assisted development workflows

Maintaining clear system ownership, strict subsystem boundaries, vendor-agnostic runtime foundations, and documentation discipline ensures the platform can grow safely.
