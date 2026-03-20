# PERFORMANCE_ARCHITECTURE.md

## Purpose

This document defines the performance architecture for the XR assembly training application.

Its goal is to ensure the project remains performant and scalable across:

- desktop browsers
- mobile browsers
- XR headsets
- future multiplayer and challenge modes

This project targets web delivery and must therefore treat performance as a **first-class architectural concern**, not a late optimization pass.

This file defines the performance strategy for:

- CPU work
- GPU work
- rendering paths
- asset loading
- texture delivery
- memory usage
- UI cost
- event cost
- interaction cost
- validation cost
- test and profiling workflows

This file should be used together with:

- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/INTERACTION_MODEL.md`
- `docs/UI_ARCHITECTURE.md`
- `docs/PART_AUTHORING_PIPELINE.md`
- `docs/TEST_STRATEGY.md`
- `TASK_EXECUTION_PROTOCOL.md`

---

# 1. Core Performance Principle

Performance is part of architecture.

The project must not assume that performance problems can always be solved later with isolated optimization.

Instead, systems must be designed from the beginning to:

- avoid unnecessary work
- control memory growth
- keep the main thread responsive
- minimize unnecessary rendering cost
- stream content intelligently
- degrade gracefully across hardware tiers
- remain measurable and testable

---

# 2. Performance Goals

The performance strategy must optimize for:

- responsive user interaction
- smooth assembly manipulation
- readable UI
- stable frame pacing
- low hitching during step transitions
- efficient asset loading
- controlled memory use
- scalable content packages
- safe growth toward larger machines later

The goal is not maximum visual complexity.

The goal is a **stable, teachable, web-delivered assembly experience**.

---

# 3. Platform Performance Reality

The app must support multiple platform tiers with different capabilities.

## 3.1 Platform Tiers

Suggested tiers:

- **Tier A**: high-capability desktop
- **Tier B**: standalone XR headset
- **Tier C**: modern mobile device
- **Tier D**: constrained fallback path

## 3.2 Architectural Rule

Do not design the app as if all targets behave like a desktop GPU.

Standalone XR and mobile constraints must influence architecture from the beginning.

---

# 4. Main Thread Budget Philosophy

The main thread should remain focused on the work that must happen immediately.

## 4.1 Main Thread Should Prioritize

- rendering submission
- immediate interaction
- runtime state progression
- small validation decisions
- UI updates
- lightweight orchestration

## 4.2 Main Thread Should Avoid

- heavy parsing
- large synchronous content processing
- unnecessary hierarchy churn
- avoidable allocations in hot loops
- rebuilding entire UI trees unnecessarily
- heavy per-frame scans of all runtime entities

## 4.3 Rule

If work is heavy, repeated, and separable, it should be considered for:

- staged execution
- background preprocessing
- cached results
- worker-based processing where practical

---

# 5. CPU Performance Architecture

CPU performance should be managed through structure, not only micro-optimizations.

## 5.1 CPU Cost Sources

Likely CPU cost sources include:

- runtime state progression
- validation checks
- interaction routing
- event dispatch
- UI binding updates
- content parsing
- asset bookkeeping
- challenge/timer tracking
- future multiplayer state prep

## 5.2 CPU Rules

- keep runtime systems focused and low-responsibility
- avoid giant update loops that touch unrelated systems
- use event-driven updates where appropriate
- avoid polling when a state transition is enough
- cache derived data when recomputation is unnecessary
- prefer explicit state changes over broad scans

## 5.3 Validation Performance Rule

Validation should be:

- scoped to the current active step
- scoped to currently relevant parts/targets
- mode-aware
- event-triggered where possible

Do not validate the whole machine every frame.

---

# 6. GPU Performance Architecture

GPU cost must be treated as a core delivery constraint.

## 6.1 GPU Cost Sources

Likely GPU cost sources include:

- model complexity
- material count
- overdraw
- effect complexity
- UI rendering
- dynamic transparency
- lighting complexity
- shadow usage
- multiple visible parts and ghost previews
- XR stereo rendering costs

## 6.2 GPU Rules

- keep material variety under control
- reduce unnecessary transparency
- avoid excessive full-screen effects
- keep shader complexity intentional
- use lightweight feedback cues where possible
- control the number of simultaneously emphasized objects

## 6.3 XR GPU Rule

XR is especially sensitive to:

- fill rate
- transparency
- multiple layered UI surfaces
- expensive post effects
- shader-heavy effects

Treat XR as a stricter rendering target.

---

# 7. Render Path Strategy

## 7.1 Current Baseline

The dependable baseline is:

- **WebGL-safe deployment**

## 7.2 Preferred Direction

Where stable and justified:

- **WebGPU** may be used or evaluated as a preferred future path

## 7.3 Architectural Rule

The codebase must not depend exclusively on WebGPU assumptions.

Rendering choices should remain compatible with:

- stable fallback behavior
- platform capability checks
- quality tiers

## 7.4 Render Path Policy

Feature assumptions related to the render path should be isolated behind capability logic.

Do not let random runtime systems assume a specific GPU path.

---

# 8. Asset Streaming Architecture

Content must be delivered incrementally.

## 8.1 Streaming Goals

The app should load:

- enough to begin interaction quickly
- current-step-critical assets first
- adjacent-step assets next
- optional or richer assets later

## 8.2 Streaming Stages

Suggested staged loading:

1. machine/package metadata
2. first assembly/subassembly metadata
3. first required part models
4. current UI data and hints
5. next-step likely assets
6. optional richer content
7. optional challenge and debug content

## 8.3 Streaming Rule

Never block the initial experience on unnecessary later-step content if it can be deferred.

---

# 9. Texture and Model Delivery Strategy

## 9.1 Model Formats

Preferred:

- GLB
- glTF

## 9.2 Texture Formats

Preferred:

- KTX2
- Basis Universal

These support better download and runtime efficiency for web delivery.

## 9.3 Asset Efficiency Rules

Assets should be:

- instructionally readable
- not overbuilt
- appropriate to platform tier
- named clearly
- reusable where possible

Do not model detail that the user never benefits from in the current slice.

---

# 10. Memory Architecture

Memory problems are often architectural, not accidental.

## 10.1 Main Memory Risks

Likely risks include:

- loading too many assets up front
- keeping unused machine data alive
- excessive duplicate materials
- unnecessary runtime copies of data
- UI document duplication
- effect instances left alive too long

## 10.2 Memory Rules

- load only what is needed
- release or pool what is no longer needed
- share immutable data where possible
- prefer references over duplicated large payloads
- control effect instance lifetimes
- keep runtime state compact and explicit

## 10.3 Package Scaling Rule

As machine content grows, packages must remain bounded and streamable.

Do not assume future larger machines can all be fully resident at once.

---

# 11. UI Performance Architecture

UI Toolkit is the primary UI framework, so UI cost must be managed deliberately.

## 11.1 UI Cost Sources

Likely UI costs include:

- frequent tree updates
- excessive layout rebuilding
- many simultaneous panels
- large world-space UI surfaces in XR
- frequent text churn
- debug overlays left active

## 11.2 UI Rules

- keep UI presentation reactive to meaningful state changes
- do not rebuild entire documents for small text changes
- keep panel count controlled
- keep XR UI simple and readable
- minimize unnecessary per-frame UI work
- separate debug UI from player UI

## 11.3 Platform Adaptation Rule

UI density and complexity may need to vary by platform.

Desktop can afford more density than mobile or XR.

---

# 12. Effects Performance Architecture

Effects must support teaching without destabilizing performance.

## 12.1 Effect Types

Potential effects include:

- placement confirmation pulse
- highlight feedback
- error cue
- welding sparks
- heat glow
- torch/fire
- dust
- milestone celebration

## 12.2 Effects Rules

- choose the cheapest effect that communicates the idea
- keep expensive effects short-lived
- use lower-end fallbacks
- do not let many effects stack unnecessarily
- treat effect cost as part of quality tier control

## 12.3 Shader Rule

HLSL shader effects should be:

- intentional
- measurable
- fallback-aware
- not blindly layered into every interaction

---

# 13. Interaction and Validation Performance

Interaction should feel immediate.

## 13.1 Interaction Rules

- keep active interaction scope narrow
- evaluate only relevant candidates
- avoid broad object searches in hot interaction paths
- reduce unnecessary collision/target checks where possible
- keep placement evaluation clear and bounded

## 13.2 Validation Rules

Validation should scale by:

- active step
- active target
- active part
- active mode

Do not allow the validation system to become a global expensive query every frame.

---

# 14. Event System Performance

The runtime event model is valuable, but events still have cost.

## 14.1 Event Rules

- emit semantic events, not noise
- do not emit events every frame for no reason
- keep payloads small
- avoid duplicate event dispatch
- avoid broad subscription patterns that cause unrelated systems to wake up constantly

## 14.2 Logging Rule

Event logging in development is valuable, but logging should be controllable so it does not distort runtime behavior unnecessarily.

---

# 15. Background Work Strategy

## 15.1 WebWorkers

Where practical for the web delivery path, WebWorkers may be used for separable heavy work such as:

- package preprocessing
- large metadata parsing
- indexing/search preparation
- challenge replay processing
- other non-main-thread-friendly web tasks

## 15.2 WebAssembly

WASM should be considered only where it adds real value.

Potential candidates:

- heavy geometric processing
- heavy parsing or transformation
- complex indexing/search
- specialized validation helpers if justified

## 15.3 Rule

Do not add workers or WASM just because they sound advanced.

They should be introduced when:

- the workload is clearly separable
- the cost is measurable
- the integration complexity is justified

---

# 16. Performance Tiering Strategy

The app should adapt quality and complexity by capability tier.

## 16.1 Tier Controls

Potential tier controls include:

- texture resolution
- visible object count
- model complexity
- effect quality
- UI density
- number of simultaneous ghost/highlight cues
- animation richness
- debug feature availability

## 16.2 Rule

Tiering should be centralized, not scattered across random scripts.

Use explicit platform/capability services.

---

# 17. Profiling and Measurement Strategy

Performance decisions must be evidence-driven.

## 17.1 Core Measurement Areas

Measure at minimum:

- frame time
- frame pacing stability
- CPU hotspots
- GPU hotspots
- memory growth
- asset load time
- UI cost
- effect cost
- interaction hitching
- step transition hitching

## 17.2 Measurement Rule

Do not optimize blindly.

Profile, identify, prioritize, then fix.

---

# 18. Porfeto MCP Consideration

The project should remain open to using **Porfeto MCP** as part of the profiling and performance workflow when available and useful.

## 18.1 Intended Use

Porfeto MCP may be used to assist with:

- CPU profiling review
- GPU profiling review
- hotspot identification
- regression comparison between changesets
- frame-time investigation
- performance bottleneck analysis

## 18.2 Architectural Rule

Porfeto MCP is a **development acceleration and analysis tool**, not a runtime dependency.

The app architecture must remain valid and testable even without Porfeto MCP access.

## 18.3 Workflow Rule

When Porfeto MCP becomes available, it should be integrated into the development and profiling workflow as a trusted measurement aid, especially for:

- Unity CPU/GPU testing
- XR performance investigation
- Web build regression checks
- content package complexity audits

This should improve decision quality, not replace architectural discipline.

---

# 19. Performance Testing Strategy

Performance should be validated continuously, not only at the end.

## 19.1 Performance Test Targets

Test:

- tutorial vertical slice
- first authentic OSE slice
- worst-case current-step content load
- effect-heavy step
- XR UI-heavy scenario
- repeated step transitions
- challenge-mode timing path

## 19.2 Regression Rule

When a meaningful feature lands, verify that it did not introduce obvious regressions in:

- frame time
- memory
- load hitches
- UI responsiveness
- interaction responsiveness

---

# 20. Content Authoring Performance Rules

Content creation must respect runtime budgets.

## 20.1 Authoring Rules

Authors should:

- simplify aggressively where detail does not help learning
- keep materials intentional
- keep texture usage controlled
- avoid unnecessary duplicates
- think in subassemblies and streaming chunks
- consider low-end readability, not just beauty

## 20.2 Pipeline Rule

Performance constraints should be part of content review, not only engineering review.

---

# 21. Multiplayer and Challenge Performance Readiness

Future systems must not destabilize baseline performance.

## 21.1 Multiplayer Readiness Rule

Keep sync-relevant state explicit and compact.

Do not make future synchronization depend on huge transient visual state.

## 21.2 Challenge Rule

Challenge systems should track:

- timers
- retries
- hint counts
- scoring inputs

without creating heavy per-frame logic that scales badly.

---

# 22. Recommended Early Performance Focus

Early development should focus on preventing the biggest architectural mistakes.

Prioritize:

1. low-overhead runtime state flow
2. scoped validation
3. controlled UI Toolkit updates
4. careful event usage
5. staged asset loading
6. KTX2/GLB-friendly content
7. effect fallback design
8. capability tiering seams
9. measurable profiling workflow

---

# 23. Anti-Patterns to Avoid

Avoid:

- loading whole machines when only one subassembly is needed
- large per-frame searches of scene objects
- rebuilding UI constantly
- effect spam for basic interactions
- assuming desktop-only budgets
- adding WebGPU-only assumptions everywhere
- introducing WASM or workers without evidence
- over-modeling content beyond instructional need
- treating profiling as optional

---

# 24. Validation Questions

Before approving a performance-sensitive feature, ask:

- Does this add work every frame?
- Can this be event-driven instead?
- Is the main thread doing unnecessary heavy work?
- Is this render-path-safe?
- Does this behave acceptably on constrained tiers?
- Does this need a fallback?
- Does this increase memory residency unnecessarily?
- Can this be streamed or deferred?
- Has this been measured or is it only assumed?
- Would Porfeto MCP or another profiling pass likely be useful here?

If these questions are unclear, performance architecture is at risk.

---

# 25. Final Guidance

The correct performance strategy is not:

“build everything first and optimize later.”

The correct strategy is:

- design for bounded work
- keep the main thread lean
- stream content intentionally
- keep assets web-friendly
- control UI and effect cost
- tier the experience by capability
- measure real hotspots
- use profiling tools, including Porfeto MCP when available, to guide decisions
- preserve a stable, teachable experience across desktop, mobile, and XR

That is how the project stays performant enough to scale.
