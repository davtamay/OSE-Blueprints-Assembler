# VERTICAL_SLICE_SPEC.md

## Purpose

This document defines the exact first vertical slices for the project so coding agents and human developers stop guessing what to build first.

It covers:

- the first tutorial slice
- the first authentic Open Source Ecology slice
- the goals of each slice
- what is in scope
- what is explicitly out of scope
- how each slice should be validated
- what "done" means before moving on

This file should be used together with:

- `AGENTS.md`
- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/CONTENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`
- `TASK_EXECUTION_PROTOCOL.md`

---

# 1. Why Vertical Slices Matter

This project is too large to build as a broad unfinished prototype.

A vertical slice is the safest strategy because it proves a complete path through the system:

- content loading
- runtime state
- input
- interaction
- validation
- feedback
- persistence
- presentation

A slice is valuable because it tests the architecture end-to-end.

The first goal is not “build many machines.”

The first goal is:

**prove that one complete teaching flow works cleanly.**

---

# 2. Slice Strategy

The project should use two early slices:

## Slice A - Tutorial Slice

A tiny, confidence-building onboarding build that teaches the interaction language of the app.

## Slice B - First Authentic OSE Slice

A small, modular, real assembly path derived from Open Source Ecology documentation, preferably aligned with the **Power Cube** path at subassembly scale.

The order must be:

1. Tutorial Slice
2. First Authentic OSE Slice

Do not reverse this order.

---

# 3. Global Rules for Both Slices

These rules apply to both slices.

1. Use the **Unity Input System** as the canonical input foundation.
2. Use the latest stable approved stack from `TECH_STACK.md`.
3. Keep all logic modular and delegated to the correct modules.
4. Keep content data-driven.
5. Keep the architecture open for future multiplayer.
6. Keep performance awareness in mind from the beginning.
7. Validate the slice honestly before claiming it is done.
8. Auto stage and commit after each meaningful validated changeset.
9. Prefer a smaller fully working slice over a larger partially working one.
10. The slice should be enjoyable and confidence-building, not only technically correct.

---

# 4. Slice A - Tutorial Slice

## 4.1 Purpose

The tutorial slice exists to teach the user how to use the app before they touch a real machine assembly.

It should:

- teach interaction language
- reduce intimidation
- create quick wins
- establish trust in the system
- make the app feel fun and welcoming

This slice is primarily about **teaching the interface and process**, not teaching a complex machine.

---

## 4.2 Tutorial Build Recommendation

The tutorial should be a **tiny mechanical assembly** made of a few parts.

Recommended shape:

- 3 to 6 parts maximum
- clearly distinct shapes
- obvious attachment order
- simple placement targets
- at least one tool concept
- at least one optional physical substitution example

Example concept directions:

- simple bracket + bolt assembly
- two-plate hinge-like assembly
- small frame corner assembly
- handle mount assembly

The tutorial build does not need to be a real OSE machine.

Its purpose is to teach the system.

---

## 4.3 Tutorial Learning Goals

The tutorial must teach:

- navigation basics
- selection
- inspection
- rotating/manipulating a part
- moving a part toward a target
- placing a part
- reading part information
- understanding required tool info
- using a hint
- confirming a placement
- optionally marking a part as physically present
- understanding progression from one step to the next

The user should finish the tutorial knowing how the app works.

---

## 4.4 Tutorial Scope

### In Scope

- one tiny assembly
- one simple machine package
- part metadata
- tool metadata
- step instructions
- ghost/preview placement
- basic validation
- basic success feedback
- at least one hint flow
- one optional physical substitution path
- step progression
- completion summary
- progress persistence at slice level

### Out of Scope

- complex tools
- advanced challenge mode
- real multiplayer
- complex process effects
- large content library
- remote assistance
- advanced scoring
- large OSE machine complexity

---

## 4.5 Tutorial Runtime Capabilities Required

The tutorial slice should prove these systems:

- machine package loading
- session creation
- step activation
- part presentation
- selection
- manipulation
- placement validation
- instruction display
- part info display
- tool info display
- hint system
- step completion
- progress save/restore at basic level

This is the minimum end-to-end proof.

---

## 4.6 Tutorial Fun and Feedback Requirements

The tutorial must feel good to use.

It should include:

- clear visual focus
- satisfying placement response
- supportive guidance
- milestone reinforcement
- short and rewarding pacing

Optional light polish:

- placement highlight
- subtle success pulse
- simple sparkle or confirmation cue
- reassuring text feedback

Avoid overproducing effects here.

The goal is clarity and confidence.

---

## 4.7 Tutorial Validation Criteria

The tutorial is validated when:

- a new user can begin without prior app knowledge
- the first step is understandable
- the user can complete the full tutorial flow
- selection and placement feel coherent
- validation feedback is clear
- step progression works
- basic persistence works
- the slice feels welcoming rather than confusing

---

## 4.8 Tutorial Definition of Done

The tutorial slice is done when:

- it teaches the app interaction language successfully
- it proves the full runtime path end-to-end
- the architecture remains clean
- no major console/runtime issues remain
- the team would trust it as the first thing a real user sees

Only after that should Slice B begin.

---

# 5. Slice B - First Authentic OSE Slice

## 5.1 Purpose

The first authentic OSE slice exists to prove that the architecture can teach a real Open Source Ecology-inspired machine assembly path.

It should:

- use actual blueprint-driven concepts
- preserve the modular content approach
- remain small enough to finish cleanly
- demonstrate real instructional value
- prepare the project for scaling into more assemblies later

This slice is about **proving realism without overreaching**.

---

## 5.2 Recommended First OSE Direction

Recommended direction:

**Power Cube path, but only at subassembly scale**

Do not attempt the full Power Cube first.

Choose one small, well-bounded subassembly that maps cleanly to:

- a few meaningful parts
- understandable assembly order
- part/tool metadata
- step-by-step instruction
- validation
- structural explanation

The Power Cube is a good early target because it is:

- modular
- documented
- mechanically meaningful
- well suited to progressive teaching

---

## 5.3 OSE Slice Selection Criteria

The chosen subassembly should be:

- relatively small
- mechanically understandable
- well documented in source material
- meaningful enough to feel authentic
- simple enough to complete in a first real slice

Avoid choosing:

- a huge hydraulic system end-to-end
- a full vehicle
- a house-scale system
- a content area with unclear or incomplete source documentation
- anything requiring too many parts or too many process types at once

---

## 5.4 OSE Slice Learning Goals

The authentic slice should teach:

- how parts relate structurally
- why a given assembly order matters
- what tools are required
- what each part does
- how a subassembly becomes a meaningful unit
- how the user transitions from guided placement toward understanding

This slice should feel more “real” than the tutorial.

---

## 5.5 OSE Slice Scope

### In Scope

- one authentic OSE-aligned subassembly
- a small but real machine package
- accurate part metadata
- accurate tool metadata
- structured step flow
- validation
- reinforcement text such as “why this matters”
- one or two process feedback cues if needed
- optional simple milestone feedback
- basic challenge hook readiness
- persistence at slice level

### Out of Scope

- full machine completion
- full challenge system
- leaderboard backend
- full multiplayer
- full remote assistance
- a giant content import pipeline
- perfect simulation of every real-world process
- every possible part/tool edge case

---

## 5.6 OSE Slice Runtime Capabilities Required

The authentic slice should prove:

- runtime can load real machine-aligned content
- step system scales beyond tutorial simplicity
- metadata can support real part explanation
- user can assemble a meaningful subassembly
- validation still feels understandable
- physical substitution path remains coherent
- architecture holds under slightly more realistic content complexity

---

## 5.7 OSE Slice Effects Policy

Effects in this slice should remain modest and instructional.

Only add process visuals if they genuinely help understanding.

Examples:

- placement confirmation cue
- subtle spark/weld cue if a process step warrants it
- ghost guidance or highlight cue
- assembly completion emphasis

Do not turn the first authentic slice into an effects showcase.

Instruction comes first.

---

## 5.8 OSE Slice Challenge Readiness

The OSE slice should be designed so that challenge and speed-run systems can be layered later without restructuring the slice.

That means the slice should be able to expose:

- completion time
- retry count
- invalid placements
- hint usage
- optional future leaderboard payload fields

Challenge features do not need full implementation yet, but the slice should not block them.

---

## 5.9 OSE Slice Multiplayer Readiness

The authentic slice must remain open for future multiplayer.

That means current truth must remain explicit for:

- current step
- part placement state
- physical substitution state
- completion events
- optional timing/challenge state

Do not embed critical truth only inside visuals or UI.

---

## 5.10 OSE Slice Validation Criteria

The slice is validated when:

- it feels like a real instructional assembly
- source material has been interpreted cleanly enough for the slice scope
- the runtime handles the added realism without architectural strain
- users can understand what they are building and why
- part/tool metadata supports practical understanding
- the slice feels like a real stepping stone toward larger OSE machine training

---

## 5.11 OSE Slice Definition of Done

The first authentic OSE slice is done when:

- the selected subassembly can be completed end-to-end
- the content is data-driven
- the runtime remains modular
- the experience feels genuinely instructional
- the slice is small enough to be stable
- the codebase is more ready for scaling after completing it

---

# 6. What the First Two Slices Must Prove Together

Once both slices are complete, the project should have proven:

- canonical input architecture works
- content packages work
- runtime step progression works
- validation works
- metadata-driven instruction works
- persistence works at a meaningful level
- physical substitution works at a meaningful level
- the architecture can support both onboarding and authentic machine teaching
- the project can scale incrementally without rewriting the core

That is a major milestone.

---

# 7. Exact Recommended Development Order for the Slices

Use this order:

## Tutorial Slice Order

1. define tutorial content package
2. load tutorial package
3. present first step
4. show part metadata
5. support inspect/select/manipulate
6. validate placement
7. show hint
8. support step progression
9. support optional physical substitution
10. show completion summary
11. validate persistence

## Authentic OSE Slice Order

1. choose one Power Cube-aligned subassembly
2. gather source notes and simplify scope
3. define content package
4. add real part metadata
5. add real tool metadata
6. implement step flow
7. validate placement and progression
8. add reinforcement text
9. add modest instructional feedback/effects if warranted
10. verify persistence and challenge readiness
11. audit multiplayer-open state boundaries

---

# 8. Non-Goals for the First Two Slices

The following are not goals yet:

- full OSE content library
- full machine simulation
- full multiplayer
- full remote assistance
- full global leaderboard system
- final production art polish
- full analytics stack
- every platform fully optimized from day one

The first two slices are about proving architecture and teaching quality.

---

# 9. Risks to Avoid

## Risk 1 - Overscoping the tutorial

If the tutorial becomes too elaborate, it stops teaching the interaction language quickly.

## Risk 2 - Picking too complex an OSE slice

If the first authentic slice is too large, the team will get lost in content complexity before the architecture is proven.

## Risk 3 - Mixing tutorial and authentic logic

The tutorial and authentic content should share runtime systems, but remain separate content slices with separate goals.

## Risk 4 - Premature challenge or multiplayer complexity

Challenge and multiplayer readiness matter, but they should not destabilize the first two slices.

## Risk 5 - Turning process effects into the main focus

Effects should support teaching, not replace it.

---

# 10. Recommended First Authentic OSE Subassembly Candidate

Current recommended candidate:

**Power Cube subassembly path**

But keep the first scope very small.

Good candidate characteristics:

- visible structural logic
- modest part count
- simple tooling requirements
- clear step order
- easy-to-explain function

The exact chosen subassembly should be documented once selected from source materials.

---

# 11. Slice Completion Review Questions

Before approving a slice, ask:

- Does the slice actually teach something clearly?
- Does the slice prove the runtime path end-to-end?
- Is the architecture still clean?
- Did we keep the code delegated to proper modules?
- Is the Unity Input System being used correctly?
- Did we avoid machine-specific hacks in generic code?
- Did we preserve multiplayer-open boundaries?
- Did we keep the experience enjoyable and not only functional?
- Would we trust this slice as the foundation for the next one?

If the answer is no to any of these, the slice is not done yet.

---

# 12. Final Slice Guidance

The correct first milestone is not a huge prototype.

The correct first milestone is:

- a tiny tutorial that teaches the interaction language cleanly
- followed by one real OSE-aligned subassembly that proves the architecture can teach authentic construction

That combination gives the project:

- a user-friendly entry point
- a real instructional proof point
- a stable technical foundation
- a scalable path forward

That is the vertical slice strategy the agent should follow.
