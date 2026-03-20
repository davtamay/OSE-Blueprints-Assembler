# CLAUDE_TASK_PROMPT.md

## Purpose

This document is a **working prompt template** for AI agents (Claude, GPT, etc.) that will assist with development inside the `ose-xr-foundation` project.

It ensures agents:

- follow the architecture
- respect the source-of-truth ownership model
- avoid architectural drift
- execute tasks safely
- update the correct documentation when needed
- always provide a practical way for the user to visually test and validate visible results

This prompt is **for operator use** and does not define architecture itself.  
Architecture truth lives in the docs listed below.

---

# 1. Project Context

You are assisting with development of **OSE XR Foundation**.

This project is a **cross-platform XR training platform** designed to teach real-world machine assembly using immersive interaction.

The system must run on:

- XR headsets (WebXR / Quest browser)
- Desktop browsers
- Mobile browsers

The architecture must support:

- assembly training experiences
- modular machine content packages
- validation-based learning
- challenge modes
- future multiplayer synchronization
- scalable machine libraries

The project is implemented using **Unity 6.3** and will upgrade to **new stable Unity releases when appropriate**.

---

# 2. Core Technology Stack

The technology choices are defined in:

`docs/TECH_STACK.md`

Key decisions:

- Unity **6.3 baseline**
- **Stable upgrade policy**
- **UI Toolkit** for UI
- **Unity Input System**
- **Unity XR Interaction Toolkit (XRI)** as the primary XR interaction framework
- **De-Panther WebXR exporter**
- WebGL / WebXR web deployment
- GPU texture compression pipeline
- performance-first architecture

Agents must not change the tech stack without explicit approval.

Do **not** assume Meta Interaction SDK as a baseline dependency unless the project explicitly adds it later for a justified reason.

---

# 3. Authoritative Documentation

The following documents define the architecture and system behavior.

Agents must consult them **before implementing or modifying systems**.

## Primary Authority

1. `AGENTS.md`
2. `docs/TECH_STACK.md`
3. `docs/SOURCE_OF_TRUTH_MAP.md`
4. `docs/SYSTEM_BOUNDARIES.md`

These define:

- mission
- stack
- architectural ownership rules
- dependency firewall rules

## Core Architecture

5. `docs/ARCHITECTURE.md`
6. `docs/ASSEMBLY_RUNTIME.md`
7. `docs/RUNTIME_EVENT_MODEL.md`

These define:

- system decomposition
- runtime ownership
- event flow
- session lifecycle

## Interaction Systems

8. `docs/INTERACTION_MODEL.md`

Defines:

- canonical interaction actions
- XR / desktop / mobile mapping
- physical substitution interaction
- XRI-aligned, vendor-agnostic XR interaction direction

## UI Systems

9. `docs/UI_ARCHITECTURE.md`

Defines:

- UI Toolkit layering
- panel ownership rules
- presentation-only constraint

## Data and Content

10. `docs/CONTENT_MODEL.md`
11. `docs/DATA_SCHEMA.md`
12. `docs/PART_AUTHORING_PIPELINE.md`

Defines:

- conceptual content hierarchy
- machine content package format
- step metadata
- validation rules
- asset references

## Project Structure

13. `docs/UNITY_PROJECT_STRUCTURE.md`

Defines:

- module boundaries
- folder layout
- `.asmdef` structure

## Performance Rules

14. `docs/PERFORMANCE_ARCHITECTURE.md`

Defines:

- CPU/GPU boundaries
- streaming strategy
- effect cost rules
- WebXR performance budgets
- Perfetto MCP profiling workflow

## Testing Rules

15. `docs/TEST_STRATEGY.md`

Defines:

- unit tests
- system tests
- runtime integration tests
- schema validation
- regression protection
- visible validation expectations

## Implementation Order

16. `docs/IMPLEMENTATION_CHECKLIST.md`

Defines the **correct order for building systems**.

## Working Method

17. `TASK_EXECUTION_PROTOCOL.md`

Defines:

- task sizing
- validation rules
- visual validation requirements
- commit discipline
- Plan Mode behavior

---

# 4. Runtime Ownership Rules

The system follows a strict **source-of-truth ownership model**.

Defined in:

`docs/SOURCE_OF_TRUTH_MAP.md`

Key rules:

### UI is not authoritative

UI Toolkit panels may only own:

- presentation state
- panel visibility
- animation state
- input callbacks

UI must **not own gameplay truth**.

### Runtime systems own behavior

Authoritative runtime systems include:

- `MachineSessionController`
- `StepController`
- `AssemblyRuntimeController`
- `PlacementValidator`
- `ChallengeRunTracker`
- `SessionPersistenceService`

### Scene objects are not truth

Scene objects may own:

- transforms
- rendering state
- anchors

But must not be the only source of truth for:

- step progression
- validation results
- machine state

---

# 5. Interaction Model

Interactions must follow the canonical action set defined in:

`docs/INTERACTION_MODEL.md`

Examples:

- Grab
- Release
- Place
- Inspect
- Rotate
- UI Select

These actions must work across:

| Platform | Input |
|--------|--------|
| XR | hands / controllers |
| Desktop | mouse |
| Mobile | touch |

Do not introduce device-specific interaction logic outside the input layer.

For XR interaction implementation, prefer **XR Interaction Toolkit (XRI)** as the baseline.

---

# 6. UI Rules

UI must follow the architecture defined in:

`docs/UI_ARCHITECTURE.md`

Key constraints:

- UI built entirely with **UI Toolkit**
- `.uxml` + `.uss` layout system where useful
- presenters translate runtime data into UI
- panels must not modify runtime state directly

Runtime → Presenter → UI

Never:

UI → modify runtime truth.

---

# 7. Data Driven Machine Packages

Machine content is defined using structured packages described in:

`docs/DATA_SCHEMA.md`

These packages define:

- machine metadata
- assemblies
- subassemblies
- parts
- tools
- steps
- validation rules
- hints
- effects

Packages must remain:

- deterministic
- versioned
- schema validated

Content must not embed runtime-only state.

Use `docs/CONTENT_MODEL.md` for the conceptual hierarchy and `docs/DATA_SCHEMA.md` for the canonical package structure.

---

# 8. Event Architecture

Runtime systems communicate through the event system defined in:

`docs/RUNTIME_EVENT_MODEL.md`

Example flow:

```text
User grabs part
→ hover over target
→ placement attempt
→ validation
→ step completion
→ UI update
→ effects triggered
```

Events prevent direct subsystem coupling.

---

# 9. Performance Discipline

Performance rules are defined in:

`docs/PERFORMANCE_ARCHITECTURE.md`

Key constraints:

- minimize main thread stalls
- use GPU-friendly assets
- limit UI rebuild cost
- maintain XR frame stability
- stream assets where appropriate

Profiling may use:

- Unity Profiler
- Web performance tools
- **Perfetto MCP** for deep CPU/GPU analysis

Agents must avoid introducing:

- unnecessary allocations
- heavy UI rebuild loops
- expensive shader passes

---

# 10. Current MCP Tooling Context

The Unity assistant workflow may currently use local MCP servers in Unity's `mcp.json`.

The currently approved direction includes:

- **Perfetto MCP** for CPU/GPU/trace analysis
- existing assistant/model tooling such as Codex

These are **development tools**, not runtime dependencies.

Do not make architecture decisions that require these MCP servers to exist at runtime.

Do not assume every machine has identical local MCP installation paths unless documented by the project.

Do **not** assume Meta MCP as part of the standard repo baseline.

---

# 11. Testing Requirements

All major systems must support testing defined in:

`docs/TEST_STRATEGY.md`

Required validation includes:

- unit tests
- runtime integration tests
- schema validation
- cross-platform interaction tests
- performance regression checks
- **a practical visual validation path for visible behavior**

Agents must not merge changes that break tests.

---

# 12. Visual Validation Requirement

Whenever a task affects anything visible to the user, you must provide a concrete way for the user to test and validate the result visually.

This includes changes involving:

- UI panels
- world-space UI
- interactions
- selection/highlighting
- placement previews
- validation feedback
- effects
- step progression displays
- physical substitution visuals
- challenge UI
- visible platform-specific behavior

Acceptable ways to satisfy this requirement include:

- a sandbox scene
- a testbed scene
- a sample content package
- a temporary debug panel
- a Play Mode reproduction path
- a directly triggerable effect harness
- an inspector/debug overlay when full runtime visibility is not yet available

When reporting results, include:

- where to run it
- what to click/do
- what should appear/change
- any limitations

Do not leave visible changes without a way for the user to see and validate them.

---

# 13. Task Execution Rules

Agents must follow:

`TASK_EXECUTION_PROTOCOL.md`

Key principles:

- enter **Plan Mode** if architectural decisions are required
- prefer small safe changes
- respect module boundaries
- update documentation if architecture changes
- provide visual validation for visible work

Never silently rewrite system ownership rules.

---

# 14. Implementation Workflow

Before starting a task:

1. Identify affected subsystem
2. Consult relevant architecture docs
3. Verify source-of-truth ownership
4. Confirm module boundaries
5. Define technical validation
6. Define visual validation if the result is visible
7. Implement change
8. update tests if necessary
9. update documentation if ownership or behavior changed

---

# 15. Prohibited Actions

Agents must **not**:

- introduce new runtime truth into UI
- bypass validation systems
- alter data schema without updating validators
- break module boundaries
- introduce platform-specific interaction logic into runtime systems
- ship user-visible changes with no clear visual validation path

---

# 16. Final Principle

Before implementing any feature, always answer:

1. **Which system owns this truth?**
2. **Which document defines the behavior?**
3. **Which module should be modified?**
4. **How will the user visually validate the result if it is visible?**

If any answer is unclear, stop and enter **Plan Mode**.

This discipline keeps the architecture stable while the platform grows.