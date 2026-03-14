# ARCHITECTURE.md

## Purpose

This document defines the technical architecture for a cross-platform XR assembly training application that teaches people how to build real-world machines using Open Source Ecology (OSE) blueprints.

The system should transform blueprint-driven machine documentation into interactive 3D assembly experiences that work across XR, mobile, and desktop devices.

This architecture must support:

- guided assembly learning
- modular machine content
- physical + virtual hybrid workflows
- future remote assistance
- open door for optimal multiplayer
- cross-platform interaction
- strong web performance
- scalable authoring for many machines and build types
- process-specific visual effects when instructionally useful

---

# 1. Product Intent

The application is not just a 3D viewer.

It is a spatial assembly learning system.

Its purpose is to help users:

- understand what a machine is made of
- understand how parts relate to one another
- learn assembly procedure
- identify required tools and accessories
- practice virtually
- transition to real physical construction
- retain knowledge through reinforcement and aha moments
- eventually gain practical mastery

The experience should be educational, practical, enjoyable, motivating, and worth returning to.

---

# 2. Primary Technical Direction

## Engine and Deployment

Primary implementation direction:

- Unity as the authoring and runtime engine
- De-Panther WebXR exporter for web XR deployment
- web deployment as the main distribution target
- WebGPU preferred when available
- WebGL fallback when necessary

## Why this direction

Unity is preferred because it provides:

- mature editor tooling
- strong authoring workflows
- XR Interaction Toolkit support
- hand/controller interaction systems
- Unity Input System support for cross-platform input abstraction
- faster iteration for structured training content
- easier content authoring for assembly logic than starting from scratch in Three.js

Three.js remains a useful future exploration path, especially if WebGPU + WebXR capabilities become significantly stronger for the intended needs, but the current foundation should prioritize Unity.

## Technology Policy

Use the latest stable and production-appropriate technology available. Prefer stable releases, supported package versions, proven integration paths, and maintainable workflows over unstable novelty unless there is a clear and justified benefit.

---

# 3. Architectural Principles

All systems must be designed with these principles:

1. modularity
2. data-driven content
3. device-adaptive interaction
4. performance-first web delivery
5. scalable authoring
6. educational clarity
7. future networking extensibility
8. separation of content from runtime logic
9. clear module ownership and delegation
10. stepwise reliability with regression awareness

No system should be tightly coupled to a specific machine, device type, or interaction method.

---

# 4. Core System Layers

The application should be split into the following high-level layers:

## 4.1 Content Layer

Defines what is being taught.

Includes:

- machine definitions
- assemblies
- subassemblies
- parts
- tools
- metadata
- instructional steps
- reinforcement content
- purchasing/search information
- challenge mode descriptors

This layer must be data-driven.

---

## 4.2 Runtime Assembly Engine

Defines how assembly content is executed.

Includes:

- step progression
- validation
- placement state
- visibility toggling
- virtual vs physical part tracking
- instruction sequencing
- contextual hints
- completion logic
- review and reinforcement hooks

This is the core logic system of the app.

---

## 4.3 Interaction Layer

Defines how users interact with the assembly system.

Includes:

- Unity Input System action maps
- universal input abstraction
- selection
- grabbing
- rotation
- placement
- inspection
- confirmation
- navigation
- hint requests

This layer must adapt to XR, mobile, and desktop.

---

## 4.4 Presentation Layer

Defines how the system communicates with the user visually.

Includes:

- UI panels
- step instructions
- part callouts
- tool overlays
- highlights
- progress indicators
- fun/rewarding feedback
- onboarding/tutorial flow
- challenge and score presentation

---

## 4.5 Asset Delivery Layer

Defines how 3D assets, textures, metadata, and content are loaded.

Includes:

- glTF / GLB loading
- KTX2 compressed textures
- progressive loading
- background decoding
- streaming
- caching
- manifest loading

---

## 4.6 Platform Adaptation Layer

Defines environment-specific behavior.

Includes:

- device detection
- capability detection
- render-path selection
- input profile selection
- performance tier selection
- effect quality selection

---

## 4.7 VFX and Instructional Effects Layer

Defines process-specific visual communication effects.

Includes:

- welding effects
- sparks
- fire or torch visuals when contextually required
- heat glow or emissive transitions
- cutting cues
- dust or smoke effects where useful
- placement feedback
- alignment guides
- state visualization shaders

This layer should be modular and support both high-quality and fallback implementations.

---

## 4.8 Future Collaboration Layer

Reserved for remote assistance and shared sessions.

Includes future support for:

- WebRTC
- synchronized state
- remote annotations
- expert guidance
- voice communication
- cooperative assembly
- competitive challenge modes

---

# 5. Core Runtime Concept

The runtime should be thought of as a state-driven assembly tutor.

At any moment, the application knows:

- which machine is loaded
- which assembly/subassembly is active
- which step is current
- which parts are required
- which parts are already placed
- whether each part is virtual or physical
- what tool is needed
- what optional reinforcement or explanation is available
- whether the user has satisfied the completion conditions

The runtime should not depend on scene-specific hardcoded logic.

Instead, the scene is a host environment and the assembly engine drives behavior through structured content data.

---

# 6. Content Model

The system should use a hierarchical machine content model.

## 6.1 Machine

A machine is the top-level learning object.

A machine contains:

- metadata
- one or more assemblies
- introduction content
- prerequisites
- difficulty rating
- estimated build time
- tool requirements
- learning objectives
- optional challenge mode definitions

---

## 6.2 Assembly

An assembly is a major construction group within a machine.

Examples:

- frame
- wheel system
- engine mount
- hydraulic subsystem

Each assembly contains:

- ordered subassemblies or steps
- dependencies
- required parts and tools

---

## 6.3 Subassembly

A subassembly groups a smaller meaningful construction unit.

Examples:

- axle mount
- seat bracket
- hinge support

Subassemblies can provide intermediate mastery milestones.

---

## 6.4 Part

A part is an individual build item.

Each part should support:

- unique id
- display name
- category
- material
- function
- associated tool(s)
- quantity
- geometry asset reference
- metadata panel text
- purchasing search terms
- placement rules
- orientation rules
- whether physical substitution is allowed
- whether special process effects are associated with this part or step

---

## 6.5 Tool

A tool is a supporting item required for assembly.

Each tool should support:

- name
- category
- purpose
- usage notes
- optional search terms
- optional safety notes
- optional effect profile reference where relevant

---

## 6.6 Step

A step is the executable learning unit of the system.

A step defines:

- instruction text
- required parts
- required tools
- target placement locations
- allowed completion conditions
- hints
- optional animations
- validation logic
- next-step transition rules
- optional process VFX requirements
- optional challenge timing hooks

---

# 7. Step System Design

The step engine is central to the product.

Each step should support:

## 7.1 Step States

- locked
- available
- active
- completed
- skipped
- reviewed

---

## 7.2 Step Actions

A step may require one or more of the following:

- inspect a part
- identify a part
- identify a tool
- place a part
- align a part
- confirm a physical part already exists
- view an explanation
- complete a small quiz/checkpoint
- verify understanding
- trigger or observe a process effect when instructionally required

---

## 7.3 Step Completion Modes

A step may complete by:

- exact virtual placement
- accepted tolerance placement
- explicit user confirmation
- physical substitution confirmation
- multi-part completion
- instructor override (future remote mode)

---

## 7.4 Step Reinforcement

To maximize learning retention, steps should optionally include:

- exploded view previews
- part highlighting
- contextual why-this-matters notes
- fastener or tool tips
- error correction hints
- repetition checkpoints
- review mode before advancing
- process visualization where useful

---

# 8. Learning Design Strategy

The architecture must support instructional scaffolding.

The content system should support:

## 8.1 Progressive Complexity

Users should begin with simple builds and smaller concepts before confronting large machines.

The architecture should allow content authors to define:

- beginner mode
- guided mode
- standard mode
- challenge mode
- expert review mode

---

## 8.2 Zone of Proximal Development Support

The system should help users stretch beyond what they already know without overwhelming them.

This means steps should support variable assistance such as:

- auto-highlight
- ghost placement
- animated hint path
- optional explanation panels
- reduced guidance on repeated success

---

## 8.3 Aha Moments

The runtime should support moments where structure becomes clear.

Examples:

- exploded-to-assembled transitions
- subassembly completion reveals
- visual relation maps
- before/after comparisons
- structural purpose explanations

---

# 9. Fun and Motivation Layer

The app must not feel dry or purely instructional.

It should feel enjoyable, satisfying, and worth returning to.

Possible motivational support:

- progress milestones
- satisfying placement feedback
- completion celebrations
- unlocking larger assemblies
- visual polish and delight
- collection or mastery markers
- time-to-complete improvements
- confidence-building tutorial wins
- optional challenge and speed-run experiences
- global or social ranking systems if implemented responsibly

Fun should reinforce learning rather than distract from it.

---

# 10. Physical + Virtual Hybrid Workflow

A core product requirement is support for real-world assembly alongside virtual guidance.

The system must support two placement modes for compatible parts:

## 10.1 Virtual Placement

The user manipulates and places the virtual representation.

---

## 10.2 Physical Placement

The user indicates the real part is physically present or already installed.

When a physical placement is confirmed:

- the virtual placeholder may be hidden
- the step progresses using physical-state logic
- later validation must respect the physical state

This architecture allows the system to function as both:

- a training simulator
- a real-world assembly assistant

---

# 11. Universal Input Architecture

The app must not hardcode behavior around device-specific input.

Instead, build a universal action layer on top of the Unity Input System.

## 11.1 Canonical Actions

The system should support a shared set of actions such as:

- select
- inspect
- grab
- move
- rotate
- place
- confirm
- cancel
- navigate
- zoom
- orbit
- request hint
- advance
- go back

---

## 11.2 Input Adapters

Each platform should map native input into canonical actions.

### XR adapter
- hand tracking
- pinch/grab
- controllers
- ray + direct interaction

### Mobile adapter
- tap
- drag
- pinch
- two-finger rotate
- touch-confirm patterns

### Desktop adapter
- left click
- click-drag
- right click orbit
- mouse wheel zoom
- keyboard shortcuts as needed

---

## 11.3 Interaction Contexts

Actions should behave differently depending on context:

- assembly mode
- inspection mode
- onboarding mode
- menu mode
- remote-assistance mode (future)
- challenge mode

Use context-sensitive routing rather than scattered conditional logic.

---

# 12. Rendering and Platform Strategy

## 12.1 Render Path

Preferred:
- WebGPU

Fallback:
- WebGL

The runtime should select the best available rendering path based on platform support.

---

## 12.2 XR Browser Support

Quest and mobile XR browser support must be treated as capability-based, not assumption-based.

The architecture should allow:

- capability probing
- feature flags
- fallback behavior

Do not assume all XR + WebGPU + WebXR combinations behave the same.

---

## 12.3 Performance Tiers

Define platform tiers, for example:

- Tier A: desktop high capability
- Tier B: standalone XR headset
- Tier C: modern mobile
- Tier D: low-end fallback

These tiers can adjust:

- texture resolution
- model complexity
- effect quality
- simultaneous visible parts
- animation density
- shader complexity

---

# 13. Asset Architecture

## 13.1 Preferred Formats

- glTF / GLB for models
- KTX2 / Basis for textures

---

## 13.2 Asset Requirements

Assets should be:

- lightweight
- web-friendly
- streamable
- modular
- reusable across multiple steps
- metadata-linked

---

## 13.3 Asset Origin

Assets may come from:

- Hyperhuman generated 3D assets
- Blender-authored geometry
- CAD conversions
- simplified instructional reconstructions

---

## 13.4 Asset Separation

Visual assets must remain separate from instructional data.

A part asset should not contain hardcoded instructional flow.

Instead:

- geometry is one concern
- step logic is another concern
- metadata is another concern
- effect profile references are another concern

---

# 14. Performance Architecture

Performance must be treated as a first-class architectural requirement.

## 14.1 Main Thread Discipline

The main thread should remain focused on:

- rendering
- immediate interaction
- UI updates
- lightweight orchestration

Avoid heavy blocking work on the main thread.

---

## 14.2 WebWorkers

Use WebWorkers for tasks such as:

- metadata preprocessing
- instruction dataset parsing
- asset manifest processing
- background assembly graph preparation
- non-Unity-side web integration tasks where possible

---

## 14.3 WebAssembly

Use WASM only where it provides real value.

Potential use cases:

- geometry processing
- constraint solving
- custom validation logic
- compression/decompression
- heavy parsing
- search/indexing workloads

WASM should be a performance tool, not a default requirement.

---

## 14.4 Streaming

Use asynchronous and staged loading for:

- machine manifests
- subassembly content
- part metadata
- textures
- model data

Load what is needed first, then progressively deepen detail.

---

## 14.5 Caching

Cache frequently reused assets and metadata where possible to minimize reload cost and improve responsiveness.

---

# 15. Effects Architecture

Process and instructional effects should be handled by dedicated modular systems.

## 15.1 Supported Effect Types

Potential effect categories:

- welding sparks
- welding glow
- torch or flame visuals
- smoke or dust
- cutting indicators
- placement snap feedback
- ghost placement cues
- heat or material-state shaders
- focus/highlight effects
- dissolve/confirm transitions

---

## 15.2 Implementation Methods

Use the appropriate implementation for the job:

- HLSL shaders for custom material-state, glow, thermal, dissolve, highlight, and alignment visualization
- particle systems for sparks, fire, smoke, dust, and process bursts
- lighter fallback variants for lower-performance tiers

---

## 15.3 Rules for Use

Effects should:

- support clarity and learning
- improve delight without becoming noisy
- scale with platform capability
- remain data-driven where practical
- be delegated to dedicated effect systems instead of embedded across unrelated modules

---

# 16. Authoring Architecture

The system should eventually support content creation by authors inside Unity.

Authors should be able to:

- import machine assets
- define part metadata
- create steps
- assign tools
- set validations
- add hints
- define optional challenge behavior
- assign effect profiles
- publish machine packages

This suggests an eventual internal authoring workflow with data exporters.

---

# 17. Data-Driven Machine Packages

Each machine should ideally be distributable as a package of:

- manifest
- part metadata
- tool metadata
- step definitions
- asset references
- optional audio/visual hints
- optional tutorial mode config
- optional challenge mode config
- optional effect profile references

This allows modular growth of the content library.

---

# 18. UI Architecture

The UI should be modular and contextual.

Core UI regions may include:

- current step panel
- part info panel
- tool info panel
- progress tracker
- hint panel
- completion/review overlay
- device-specific control cues
- challenge timer or score panel where applicable

The UI must remain readable and low-friction across XR, mobile, and desktop.

---

# 19. Tutorial / Onboarding Architecture

The app should not assume the user already knows how to interact with the system.

A first-run tutorial should teach:

- navigation
- object inspection
- placement behavior
- virtual vs physical mode
- hint usage
- confirmation flow
- optional challenge awareness

This tutorial should use a simpler mini-build before larger OSE machines.

---

# 20. Future Remote Assistance and Multiplayer Architecture

The architecture should reserve a future extension path for collaborative guidance and multiplayer.

Future capabilities may include:

- instructor joins session
- shared step state
- part highlighting by remote expert
- spatial annotations
- voice support
- co-presence guidance
- cooperative assembly
- competitive speed-run style assembly sessions
- global scoreboards or rankings

This implies that state synchronization boundaries should already be designed cleanly even if networking is deferred initially.

---

# 21. Suggested Internal Modules

A clean internal module split might include:

- MachineCatalog
- MachineLoader
- AssemblyRuntime
- StepController
- PlacementValidator
- PartRegistry
- ToolRegistry
- InputRouter
- InputActionProfileResolver
- PlatformProfileResolver
- AssetStreamingManager
- HintSystem
- ProgressTracker
- PhysicalPlacementState
- TutorialFlowController
- EffectProfileRegistry
- EffectPlaybackSystem
- ChallengeModeController
- LeaderboardBridge
- RemoteAssistBridge
- MultiplayerSyncBridge

These names are conceptual and may be refined during implementation.

---

# 22. Suggested Development Priorities

## Phase 1
Foundational architecture and first vertical slice

- machine manifest format
- part/tool metadata format
- step engine
- Unity Input System foundation
- universal input abstraction
- one simple tutorial assembly
- one simple OSE-inspired build
- part metadata panels
- virtual/physical placement toggle
- basic performance-aware asset loading
- regression checks after each meaningful step

## Phase 2
Cross-platform hardening

- desktop/mobile/XR input refinement
- performance tiering
- content authoring improvements
- stronger placement validation
- progression systems
- more engaging feedback systems
- initial effect system integration

## Phase 3
Scale and collaboration

- machine package pipeline
- larger machines
- subassembly progression
- multiplayer-ready boundaries
- remote assistance architecture
- shared session support
- challenge modes and scoring hooks

---

# 23. Reliability and Repo Hygiene

Development should proceed one meaningful step at a time.

For each meaningful validated changeset:

- verify nothing broke
- stage the relevant files
- create a clear commit
- preserve understandable history

The system should be developed incrementally and safely.

---

# 24. Agent Guidance

Agents working on this codebase should:

- preserve modularity
- prefer data-driven systems over scene-specific hardcoding
- avoid hacks that couple runtime logic to one machine or one platform
- keep learning UX central to technical decisions
- optimize for scalability and maintainability
- expose assumptions clearly
- separate authoring concerns from runtime concerns
- treat web performance as essential
- use the latest stable and appropriate technology
- double-check that each meaningful change did not break existing behavior
- stage and commit after each meaningful validated changeset

Agents should help build a foundation that can grow from a small assembly tutor into a broad open-hardware spatial learning platform.

---

# 25. Long-Term Vision

This system should be able to grow beyond a single machine or tutorial.

The long-term goal is an open, scalable platform where people can learn how to build:

- tools
- machines
- fabrication systems
- infrastructure
- housing systems

through immersive, modular, web-delivered spatial instruction.

The architecture should always support that future, even when building the smallest first prototype.
