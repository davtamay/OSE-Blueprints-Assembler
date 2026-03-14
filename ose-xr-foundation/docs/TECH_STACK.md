# TECH_STACK.md

## Purpose

This document defines the practical technology stack for the XR assembly training application.

Its job is to remove ambiguity so human developers and coding agents do not guess about:

- engine choice
- web deployment path
- input architecture
- rendering policy
- UI framework
- content formats
- package strategy
- stability rules
- what is required now
- what is optional later

This stack is based on what is realistically usable today, not on tools that may become available later.

---

# 1. Stack Philosophy

The project should be built on **latest stable, production-appropriate technology**.

The stack must prioritize:

- stability
- cross-platform delivery
- maintainability
- modular architecture
- web performance
- XR readiness
- future multiplayer readiness

The stack should not depend on beta-only workflows to make progress.

Development must continue even without Unity AI access.

---

# 2. Current Production Baseline

The current practical baseline is:

- **Unity 6.3** as the main engine and authoring environment
- **De-Panther WebXR exporter** for browser-based XR deployment
- **Unity Input System** as the canonical input foundation
- **Unity UI Toolkit (runtime)** as the primary UI framework
- **C#** for runtime systems and tooling
- **WebXR** as the main XR delivery path
- **WebGL** as the dependable baseline render/export path
- **WebGPU** as a preferred future path when stable and compatible enough
- **glTF / GLB** for machine and part asset delivery where appropriate
- **KTX2 / Basis** for texture compression and efficient web delivery
- **HLSL shaders + Unity particle/VFX workflows** for process and instructional effects

This is the stack agents should assume unless explicitly updated.

---

# 3. Engine Choice

## Selected Engine

**Unity 6.3**

## Why Unity 6.3

Unity 6.3 is the selected foundation because it provides:

- strong editor authoring workflows
- mature C# scripting
- established asset/prefab workflows
- practical support for interaction-driven 3D applications
- good modularity potential
- compatibility with De-Panther WebXR export workflows
- a realistic path for desktop, mobile, and XR support from one codebase
- the Unity Input System, which is critical for universal input design
- a stable modern baseline for continued project growth

## Engine Strategy

Unity is the **core runtime and authoring foundation**.

The architecture should not depend on Unity AI features being available.

Future agent tooling can help development, but the app itself must remain buildable and maintainable with normal Unity workflows.

---

# 4. Version Evolution Policy

## Current Locked Baseline

The project currently targets:

- **Unity 6.3**
- latest stable compatible versions of the selected Unity packages and exporter tools

## Update Policy

The project should remain open to upgrading as **new stable versions become available**.

However, upgrades must be deliberate, not casual.

A version upgrade should happen only when:

- the new version is stable
- it is compatible with the current architecture
- it provides meaningful benefit
- migration risk is understood
- the upgrade is documented before broad adoption

## Upgrade Rule

Do not let agents silently drift the project to a new engine or package version.

When versions change, update:

- `TECH_STACK.md`
- any affected architecture docs
- package version notes
- any migration checklists needed for the upgrade

This keeps the codebase and documentation aligned.

---

# 5. Web Deployment Path

## Primary Delivery Goal

The app is intended for **web delivery** so it can reach users across:

- desktop browsers
- mobile browsers
- XR browsers

## Current Deployment Strategy

Use:

- **De-Panther WebXR exporter**
- **WebXR-first thinking**
- **Unity Input System-based interaction**
- **UI Toolkit-driven runtime UI**
- **WebGL-safe baseline behavior**

## Practical Policy

Do not block the project waiting for perfect WebGPU + WebXR maturity.

Instead:

- ship against the most stable path that works
- keep architecture open for WebGPU growth
- preserve clean fallbacks

---

# 6. Input Foundation

## Canonical Input Technology

**Unity Input System**

This is a core architectural decision.

The Unity Input System must be the single canonical input layer for all supported platforms.

## Why It Matters

The project needs to support:

- desktop mouse/keyboard
- mobile touch gestures
- XR hands
- XR controllers

If input is implemented ad hoc per platform, the project will become brittle.

Instead, input should follow this model:

native device input  
→ Unity Input Actions  
→ platform adapter  
→ canonical runtime actions  
→ interaction context router  
→ runtime systems

## Required Rule

Runtime systems must consume **canonical actions**, not raw device-specific checks.

This means runtime code should think in terms of:

- select
- inspect
- grab
- rotate
- place
- confirm
- cancel
- navigate
- orbit
- zoom
- request hint
- toggle physical mode

not in terms of:

- mouse button
- screen touch count
- XR trigger directly inside feature scripts

## Input Policy

The Unity Input System is not optional for this project.

It is part of the core architecture.

---

# 7. UI Framework

## Primary UI Technology

**Unity UI Toolkit (runtime)**

This is the primary UI framework for the project.

## Why UI Toolkit

UI Toolkit is preferred because it supports:

- UI defined and integrated cleanly in code
- strong runtime UI composition in C#
- structured UI architecture
- good maintainability for system-driven panels
- separation between runtime state and presentation
- consistency across desktop, mobile, and XR UI surfaces

## Intended UI Responsibilities

UI Toolkit should be used for:

- assembly instructions
- step progression UI
- part information panels
- tool information panels
- confirmation prompts
- challenge timers and summaries
- settings and pause menus
- debug UI where appropriate

## UI Architecture Rule

UI is a **presentation layer only**.

It must not own core assembly truth.

Runtime systems remain the source of truth for:

- current machine state
- current step
- placement state
- validation state
- challenge state
- physical substitution state

## XR UI Note

When needed, UI Toolkit panels may be used in:

- screen-space contexts for desktop/mobile
- world-space contexts for XR

---

# 8. Platform Strategy

## Supported Targets

The codebase should be designed for:

- desktop
- mobile
- XR

## Recommended Development Order

Build in this order:

1. desktop-first vertical slice
2. mobile adaptation
3. XR hardening

This does **not** mean XR is unimportant.

It means desktop is the fastest place to prove:

- content loading
- step progression
- validation
- persistence
- modular runtime structure

Then mobile and XR are mapped into the same canonical action system.

## Platform Rule

Do not create three separate app architectures.

Create one runtime architecture with platform adapters.

---

# 9. Rendering Policy

## Current Baseline

**WebGL** is the dependable baseline for web export behavior.

## Preferred Direction

**WebGPU** is preferred when it is stable and realistically supported for the target deployment path.

## Practical Rule

Do not assume that WebGPU + WebXR is always fully ready everywhere the app needs to run.

The codebase must remain compatible with the stable fallback path.

## Rendering Decision Policy

When choosing rendering features:

- prefer stable cross-platform behavior
- avoid relying on unstable experimental-only features
- isolate render-path assumptions behind capability checks
- keep performance tiers explicit

---

# 10. Asset Pipeline

## Model Formats

Preferred:

- **glTF**
- **GLB**

These should be used where practical for modular machine content and web-friendly asset delivery.

## Texture Formats

Preferred:

- **KTX2**
- **Basis Universal**

These are important for:

- smaller downloads
- improved web delivery
- compressed texture workflows
- better runtime memory/performance balance

## Asset Sources

Likely asset sources include:

- simplified machine reconstructions
- blueprint-derived models
- Blender-authored assets
- Hyperhuman-generated assets where useful
- hand-authored instructional geometry

## Asset Rule

Visual assets, metadata, and instructional flow must remain separate concerns.

Do not bake machine logic into prefab-only assumptions.

---

# 11. Effects and Visual Process Communication

## Required Capability

The app must support process-specific and feedback effects such as:

- welding
- sparks
- heat glow
- torch/fire
- dust
- cutting or grinding cues
- placement success feedback
- milestone celebration
- error highlight
- ghost guidance

## Preferred Technology

Use a modular combination of:

- **HLSL shaders**
- **Unity particle systems**
- **Unity VFX Graph**, only where stable and appropriate for target platforms
- quality-tier fallbacks for constrained devices

## Effects Rule

Effects are not only cosmetic.

They help communicate assembly process and reinforce learning.

However, effects must degrade gracefully on weaker devices.

---

# 12. Runtime Language and Code Architecture

## Primary Language

**C#**

## Code Architecture Policy

The codebase must be:

- modular
- delegated to the correct module scripts
- explicit in ownership
- cleanly separated by responsibility
- aligned with the architecture docs

## Required Structural Rules

- no giant god managers
- no random dumping into utilities
- no business logic hidden in view scripts
- no hardcoded machine-specific logic inside generic runtime systems
- no scene-only truth for critical runtime state

## Expected Module Areas

The stack assumes module separation such as:

- bootstrap
- content
- runtime
- input
- interaction
- validation
- presentation
- UI
- effects
- persistence
- platform
- challenge
- networking
- authoring

---

# 13. Content Delivery Model

## Content Strategy

Machine experiences should be packaged as structured, data-driven content.

This likely includes:

- machine manifest
- assemblies
- subassemblies
- parts
- tools
- steps
- validation rules
- effect definitions
- challenge metadata

## Delivery Location

Use runtime-loadable machine packages through a controlled content pipeline.

## Content Rule

The runtime interprets content.

It should not require hardcoded content behavior per machine.

---

# 14. Performance Strategy

## Core Policy

Performance must be considered from the beginning.

Especially for web delivery, avoid building a desktop-only architecture that later becomes too expensive for mobile or XR.

## Main Thread Policy

Keep the main thread focused on:

- rendering
- immediate input
- UI
- lightweight orchestration

## Background Work

Where it adds real value, use:

- **WebWorkers**
- **WebAssembly**

for heavy or isolatable workloads.

## Important Rule

Do not add WebAssembly just because it sounds advanced.

Use it only where it clearly improves performance or architecture.

Potential candidates later may include:

- heavy parsing
- geometric processing
- validation helpers
- indexing/search workloads
- challenge replay analysis

## Streaming Policy

Use staged asset/content loading where possible.

---

# 15. Multiplayer-Ready Policy

## Current State

The project does **not** need full multiplayer immediately.

## Architectural Requirement

The codebase **must remain open for optimal multiplayer later**.

That means current systems should keep clean ownership around:

- session state
- step state
- placement state
- effect trigger events where relevant
- challenge timing state
- physical substitution confirmations

## Rule

Do not hardwire single-player-only assumptions into the architecture.

Keep authoritative state explicit and serializable where practical.

---

# 16. Challenge and Competitive Layer

## Current Policy

Challenge and speed-run features are optional motivational layers, not the core educational foundation.

## Architecture Requirement

The stack should stay open for:

- best time tracking
- global score/leaderboard systems later
- co-op challenge modes later
- competitive machine completion later

## Rule

Fun and competition should reinforce learning, not replace it.

---

# 17. Agent Tooling Policy

## Current Available Tooling

At the moment, do **not** assume Unity AI access.

The project should proceed without it.

## Planned Future Tooling

Once available and useful, the project may integrate workflows involving:

- **Unity MCP**
- **Meta MCP**
- **Porfeto MCP**

These should be treated as **development accelerators**, not core runtime dependencies.

## Rule

The app architecture must remain valid even if no MCP tooling is available.

---

# 18. Explicit Non-Blockers

These must **not** block current development:

- lack of Unity AI beta access
- lack of Unity MCP access today
- lack of Meta MCP access today
- lack of Porfeto MCP access today
- incomplete future multiplayer implementation
- incomplete future leaderboard implementation
- incomplete future remote assistance implementation

The correct move is to build the stable foundation now.

---

# 19. Version Pinning Policy

## Rule

Use the **latest stable compatible versions** of the chosen stack components.

Document them once selected.

## Must Be Pinned

Pin at minimum:

- Unity version
- De-Panther WebXR exporter version
- Unity Input System version
- UI Toolkit runtime approach assumptions if relevant
- any XR-related package versions that matter
- key content/runtime dependency versions

## Why

Without pinning, agents may make incompatible assumptions.

---

# 20. Stability Policy

## Golden Rule

Prefer **stable and production-safe** over newer but uncertain.

Use newer technology only when it is:

- stable
- compatible
- justified
- documented

## Practical Meaning

- WebGPU should be adopted carefully, not blindly
- VFX-heavy features should have fallbacks
- experimental rendering assumptions should be isolated
- package choices should be documented before broad use
- Unity upgrades should be deliberate and documented, even when the goal is to stay current

---

# 21. Recommended Immediate Stack Lock

If the team had to lock the stack **today**, it should be:

- Unity 6.3
- De-Panther WebXR exporter
- Unity Input System
- Unity UI Toolkit (runtime)
- C#
- WebXR-first deployment
- WebGL-safe baseline
- WebGPU investigated selectively
- glTF / GLB asset pipeline
- KTX2 / Basis compression pipeline
- HLSL + particle/VFX-based effect pipeline
- modular runtime architecture
- future MCP workflows added only when available and useful

---

# 22. Final Stack Guidance

The right strategy is:

- do not wait for beta tooling
- build on stable Unity foundations
- use Unity 6.3 now
- remain open to future stable Unity upgrades
- use the Unity Input System to unify desktop, mobile, and XR
- use UI Toolkit as the primary UI layer
- use De-Panther WebXR for current web XR delivery
- keep the render path practical and stable
- keep the codebase modular and delegated
- preserve performance awareness from the start
- keep the door open for multiplayer, challenge modes, and future agent tooling

This stack should let the project move forward immediately while staying compatible with future improvements.
