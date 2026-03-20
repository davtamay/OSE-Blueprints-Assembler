# CONTENT_MODEL.md
## OSE XR Foundation – Conceptual Content Model

This document describes the conceptual structure of machine training content.

For canonical schema definitions see:

DATA_SCHEMA.md

---

# Purpose

The content model defines how Open Source Ecology blueprints are transformed into structured XR training experiences.

The model supports:

- modular machine assemblies
- reusable components
- scalable machine libraries
- deterministic assembly validation

---

# Content Hierarchy

Machine content is structured as:

Machine
 └ Assembly
     └ Subassembly
         └ Step
             ├ Part
             ├ Tool
             └ Instruction

---

# Machine

Represents a complete buildable machine.

Examples:

- Brick Press
- Tractor
- CNC Torch Table
- Power Cube

Contains:

- metadata
- assemblies
- references
- asset manifests

---

# Assembly

Major subsystem of a machine.

Examples:

- frame
- hydraulic system
- electrical system
- engine mount

---

# Subassembly

Mechanical grouping inside an assembly.

Examples:

- wheel hub
- pump mount
- frame cross‑brace

---

# Step

A single instructional action.

Every step belongs to one of four **families** that define its fundamental interaction shape:

- **Place** — move a part to a target position (e.g. place component, insert bolt)
- **Use** — wield a tool against targets (e.g. tighten fastener, weld joint)
- **Connect** — link two endpoints (e.g. attach hose, route cable)
- **Confirm** — acknowledge or verify without spatial interaction (e.g. safety check, review)

A step may optionally declare a **profile** — a family-scoped refinement that selects specific behavior, effects, or validation within its family. Examples: `Place.Clamp`, `Use.Torque`, `Use.Weld`, `Connect.Cable`.

For the full step capability taxonomy see `STEP_CAPABILITY_MATRIX.md`.

Each step defines:

- instructions
- required parts
- required tools
- validation rules
- five capability payloads: guidance, validation, feedback, reinforcement, difficulty

---

# Parts

Physical components used during assembly.

Examples:

- bolts
- plates
- bearings
- hoses

Parts may include:

- 3D assets
- metadata
- purchasing references

---

# Tools

Instruments used during assembly.

Examples:

- wrench
- drill
- welder
- socket

---

# Instructions

Guidance provided to the learner.

May include:

- text
- diagrams
- animations
- hints
- validation feedback

---

# Runtime Relationship

During runtime:

- content packages are loaded
- assembly steps drive the training session
- validation confirms correct actions
- UI presents contextual information

---

# Design Principles

Modularity – machines decomposed into reusable pieces.

Scalability – supports simple tools up to complex builds.

Educational clarity – steps scaffold learning.

Data‑driven design – machines defined by structured data, not hardcoded logic.

---

# Relationship to DATA_SCHEMA.md

CONTENT_MODEL.md describes the conceptual structure.

DATA_SCHEMA.md defines:

- canonical JSON schema
- field definitions
- validation constraints
- package structure