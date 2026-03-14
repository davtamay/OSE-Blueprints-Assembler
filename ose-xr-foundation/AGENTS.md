# AGENTS.md

## Project Overview

This project is a cross-platform XR assembly training application that teaches people how to build real-world machines using Open Source Ecology (OSE) blueprints.

The system will convert machine blueprints into interactive 3D assembly experiences that guide users step-by-step through construction, understanding, practice, and eventual real-world application.

Primary goals:

- teach real-world construction and fabrication
- enable users to understand complex machines through interactive spatial learning
- bridge virtual instruction with physical building
- make learning engaging, fun, rewarding, and worth returning to
- preserve an open door for optimal multiplayer and future remote assistance
- keep performance strong on the web
- keep the codebase modular, delegated, scalable, and maintainable

The experience should feel playful, satisfying, motivating, and confidence-building so that users enjoy opening the web app and progressing through builds.

---

## Product Vision

The long-term vision is to create a 3D guidance XR application, with the door open for future remote assistance and multiplayer, that teaches people how to construct machines, tools, and eventually larger systems from the Open Source Ecology ecosystem by using their documented blueprints.

The application should help people:

- understand how assemblies fit together
- understand why parts exist and what they do
- learn what tools and accessories are required
- practice virtually before building physically
- retain knowledge through reinforcement and aha moments
- eventually gain practical mastery

The system should scale from simple starter builds to more complex machines and eventually even large construction systems such as structures or houses.

---

## Core Experience

Users select a machine blueprint and follow a guided assembly process.

The system should:

- load modular 3D assemblies
- present step-by-step build instructions
- highlight parts and tools
- allow users to place components virtually
- allow switching between virtual and physical placement
- provide contextual part and tool information
- provide reinforcement and review moments
- make progression feel enjoyable and satisfying

If the user has the real part, they can mark it as physically placed, and the virtual object will disappear while the assembly progresses.

---

## Educational Goals

The application should maximize understanding, retention, and mastery.

Design principles:

- scaffold learning within the Zone of Proximal Development
- provide clear aha moments
- reinforce concepts through interaction, repetition, and context
- avoid overwhelming users too early

Approach:

1. begin with a simpler onboarding build or tutorial
2. teach the interaction model and assembly vocabulary
3. teach tools and part relationships
4. introduce subassemblies and larger systems
5. progress toward complex OSE machines and eventually large builds

Learning should feel interactive, empowering, practical, and enjoyable.

---

## Part and Tool Metadata

Each part or tool should support contextual information such as:

- name
- function
- material
- quantity
- required tool
- search keywords for purchasing
- safety notes if relevant
- assembly role or purpose

Avoid hardcoded purchasing links when possible because retail links change.
Prefer stable search phrases users can copy.

Example:

Part: M8 Hex Bolt  
Tool: 13mm wrench  
Material: steel  
Search: "M8 hex bolt zinc plated"

---

## Physical-Digital Hybrid Workflow

Users may possess real components.

The system should support:

- virtual placement of digital parts
- physical substitution when the user owns the real part
- hiding the virtual placeholder after physical confirmation
- continuing assembly flow using physical-state logic

This allows the app to act as both:

- a training simulator
- a real-world construction assistant

---

## Platform Goals

The application must run across:

- XR headsets
- mobile devices
- desktop browsers

Interaction should adapt automatically and reliably.

---

## Input Foundation

Use the Unity Input System as the canonical cross-platform input foundation.

The project should build a device-agnostic action layer on top of the Unity Input System so that XR, mobile, and desktop all route into shared canonical actions.

Canonical actions may include:

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

Input adapters should map platform-specific controls into these actions.

### XR Devices

Interaction methods:

- hand tracking
- controllers
- direct interaction
- ray-based interaction where appropriate

### Mobile

Interaction methods:

- touch
- drag
- pinch
- rotate
- gesture patterns similar to map navigation

### Desktop

Interaction methods:

- mouse
- click drag
- right-click orbit
- scroll zoom
- keyboard shortcuts where helpful

---

## Technology Direction

Primary engine:

Unity

Web deployment:

De-Panther WebXR exporter

Rendering targets:

- WebGPU preferred
- WebGL fallback

Use the latest stable and production-appropriate technology available. Prefer modern stable releases, stable workflows, and maintainable tools over experimental novelty unless there is a justified reason.

---

## Performance Requirements

The system should maximize web performance.

Use:

- WebWorkers for suitable background processing
- WebAssembly when it provides real performance value
- asynchronous and staged asset loading
- streaming assets where appropriate
- platform-aware quality scaling

Goals:

- fast startup
- smooth interaction
- minimal blocking on main thread
- scalable support for larger assemblies

WASM is not mandatory everywhere. Use it intentionally for workloads that benefit from it.

---

## Asset Pipeline

Preferred formats:

- 3D models: glTF / GLB
- textures: KTX2 / Basis

Potential asset origins:

- Hyperhuman-generated 3D assets
- Blender-authored assets
- CAD conversions
- simplified instructional reconstructions

Assets must support:

- streaming
- modular assembly
- metadata annotations
- reuse across steps and machines

---

## VFX, HLSL, and Process Effects

The app should support process-specific visual effects when needed during assembly or explanation.

Examples:

- welding effects
- sparks
- heat glow
- cutting indicators
- fire or torch effects where contextually appropriate
- dust, smoke, or particulate cues when instructional value is improved
- material state highlights
- alignment guides and placement feedback

These effects should be authored responsibly and used only when they improve clarity, realism, delight, or instruction.

Implementation options:

- HLSL shaders for custom material, heat, glow, dissolve, alignment, or instructional visualization effects
- particle systems for sparks, smoke, fire, welding bursts, dust, and impact cues
- lightweight fallback effects for lower-performance devices

Effects should remain modular and delegated to the appropriate systems instead of being hardcoded into unrelated gameplay scripts.

---

## Multiplayer and Remote Assistance

The architecture should preserve an open door for optimal multiplayer and future remote assistance.

Possible future capabilities:

- co-present assembly sessions
- expert-guided remote instruction
- shared state synchronization
- spatial annotations
- voice communication
- instructor override or assistance

Multiplayer should also allow fun and motivating challenge modes when appropriate.

Examples:

- cooperative assembly
- time challenges
- speed runs on a machine
- global scoreboards or leaderboards
- friendly competition around efficient completion

Challenge systems must support learning rather than distract from it.

---

## Codebase Quality Requirements

The codebase should be:

- well structured
- delegated to appropriate modules and scripts
- scalable
- maintainable
- easy to reason about
- separated by responsibility

Avoid monolithic scripts, hidden coupling, scene-specific hacks, and logic that only works for a single machine or platform.

Agents should prefer:

- modular systems
- composition over inheritance where appropriate
- data-driven content
- separation of authoring and runtime concerns
- clean interfaces between systems

---

## Agent Responsibilities

Agents assisting development should prioritize:

- clean architecture
- modular design
- performance
- cross-platform compatibility
- maintainability
- educational effectiveness
- reliable task completion
- double-checking that nothing broke after each meaningful change

Agents should work incrementally, one validated step at a time.

After every meaningful validated changeset, agents should:

- stage relevant files
- create a clear commit
- keep the repo history understandable

After every completed phase, agents should:

- update `docs/APP_CURRENT_PROGRESS_FOR_AGENT.md`
- update the test scene (`Test_Assembly_Mechanics.unity`) so the phase's work is visible

### Phase Visualization Requirement

Every completed phase must leave behind something observable in the test scene.

The test scene (`Assets/Scenes/Test_Assembly_Mechanics.unity`) is the project's living proof-of-work. It should grow incrementally alongside the codebase so that anyone opening the scene can see and interact with what has been built so far.

This means:

- if the phase adds runtime logic, add a lightweight scene driver or harness component that exercises it on Play and exposes state in the inspector
- if the phase adds UI, make sure it renders in the scene
- if the phase adds interaction, make sure there is a way to trigger it in the scene
- if the phase adds content loading, make sure the scene loads and displays content
- if the phase adds effects, make sure at least one effect plays in the scene

Scene drivers and harness components are **diagnostic bridges**, not runtime authorities. They exist only to make the underlying systems observable. They must not own gameplay truth, alter the architecture, or introduce coupling that would not exist without them. They read state, call public APIs, and display results.

When a later phase replaces a driver's responsibility with a real system, the driver should be simplified or removed.

This discipline ensures:

- progress is always demonstrable
- regressions are immediately visible
- agents can verify their work without guessing
- the test scene is a reliable handoff artifact

Agents should avoid quick hacks and focus on scalable solutions.

---

## Recommended Agent Skills

Agents should be capable in:

- Unity XR development
- Unity Input System
- WebXR architecture
- WebGPU/WebGL rendering strategy
- glTF and KTX2 pipelines
- Web performance optimization
- WASM and WebWorker usage when justified
- educational UX and instructional design
- multiplayer architecture planning
- modular system architecture

---

## Recommended MCP Integrations

Recommended when available:

- Unity MCP
- Meta XR MCP
- GitHub MCP
- Perplexity MCP or equivalent technical research support

Use research tools to validate current platform support, stable package versions, and compatibility assumptions.

---

## Development Philosophy

This is a large project. Build it one meaningful step at a time.

Priorities:

- keep the foundation correct
- verify each step
- avoid regressions
- preserve future flexibility
- use the latest stable and appropriate technology
- make the experience both educational and enjoyable

The system should eventually be able to grow into a broad open-hardware training platform delivered through immersive spatial learning.
