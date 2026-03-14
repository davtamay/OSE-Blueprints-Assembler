# CONTENT_AUTHORING_PIPELINE.md

## Purpose

This document defines how content should be authored for the XR assembly training application.

Its goal is to turn raw source material into clean, data-driven machine packages without letting the workflow become chaotic, inconsistent, or tightly coupled to scene-specific logic.

This pipeline covers how to move from:

- Open Source Ecology blueprint material
- reference images and documentation
- part list definition
- tool list definition
- asset creation
- metadata authoring
- step authoring
- validation setup
- effects tagging
- challenge hooks
- machine package export

into a runtime-loadable machine experience.

This file should be used together with:

- `TECH_STACK.md`
- `docs/ARCHITECTURE.md`
- `docs/CONTENT_MODEL.md`
- `docs/ASSEMBLY_RUNTIME.md`
- `docs/UNITY_PROJECT_STRUCTURE.md`
- `docs/IMPLEMENTATION_CHECKLIST.md`
- `docs/VERTICAL_SLICE_SPEC.md`
- `docs/MACHINE_SELECTION_RESEARCH.md`
- `UI_ARCHITECTURE.md`
- `TASK_EXECUTION_PROTOCOL.md`

---

# 1. Core Principle

Content must be **data-driven**.

The runtime should interpret structured machine packages.

The runtime should not depend on hand-built scene logic for every machine.

That means content creation must produce:

- structured definitions
- reusable assets
- explicit metadata
- explicit steps
- explicit validation rules
- explicit optional effects
- explicit optional challenge metadata

not just a scene that “looks right.”

---

# 2. Authoring Philosophy

The content pipeline must prioritize:

- clarity
- repeatability
- modularity
- educational usefulness
- stable runtime integration
- scalability to many machines later

The first goal is not to perfectly recreate every engineering detail.

The first goal is to create **clear, teachable, interactive assembly content** that is structurally faithful enough to be educational and practically useful.

---

# 3. High-Level Pipeline Overview

The content pipeline should follow this sequence:

1. choose machine scope
2. gather and organize source material
3. define assembly teaching scope
4. extract part and tool lists
5. define subassemblies
6. create or acquire assets
7. author metadata
8. author step flow
9. define validation rules
10. tag optional effects and process cues
11. add challenge and multiplayer-ready metadata where useful
12. build machine package
13. validate in runtime
14. iterate based on usability and teaching quality

Do not skip directly from blueprints to scene assembly.

---

# 4. Content Scope Selection

Before making assets or writing steps, define the scope.

## 4.1 Scope Rule

Always choose the smallest meaningful scope first.

For early work, that means:

- tutorial micro-build first
- then one authentic OSE-aligned subassembly
- not a full machine
- not a giant system
- not a house-scale workflow

## 4.2 Scope Questions

Before authoring starts, answer:

- What exact assembly are we teaching?
- What is the learner supposed to understand by the end?
- What is the smallest complete teachable unit?
- How many parts are truly needed?
- Which tools matter for this slice?
- Which steps are essential?
- Does this slice support aha moments?

If those are unclear, the scope is still too vague.

---

# 5. Source Material Gathering

Content authoring should begin by collecting and organizing source material.

## 5.1 Source Types

Possible sources:

- OSE blueprint drawings
- OSE build docs
- OSE part lists
- photos of completed assemblies
- sketches or diagrams
- engineering approximations where source material is incomplete
- educator notes or simplifications

## 5.2 Source Package Folder

Each machine/subassembly should have a source folder or research record containing:

- reference images
- blueprint captures
- notes about unclear geometry
- notes about inferred dimensions
- notes about assembly order assumptions
- notes about missing information
- tool assumptions
- safety notes if relevant later

## 5.3 Source Interpretation Rule

Be honest about uncertainty.

If source material is incomplete:

- document assumptions
- simplify carefully
- do not silently invent critical mechanical relationships without noting them

---

# 6. Educational Framing Before Modeling

Before modeling, define what the learner should learn.

## 6.1 Learning Goal Template

For each authored assembly, define:

- what the user is building
- why it matters structurally
- what parts they should understand
- what tool knowledge they should gain
- what order or constraint matters
- what the aha moment is

## 6.2 Why This Matters

Without educational framing, authors often produce pretty assets but weak learning sequences.

The content pipeline must produce teachable content, not just geometry.

---

# 7. Part and Tool Extraction

After scope and source material are clear, extract the part and tool list.

## 7.1 Part Extraction

For each part, identify:

- unique id
- display name
- category
- material
- structural purpose
- geometry requirements
- quantity
- whether it can be physically substituted
- any required orientation knowledge
- search terms if practical

## 7.2 Tool Extraction

For each tool, identify:

- unique id
- name
- category
- purpose
- whether it is required or optional
- search terms
- optional safety note
- whether tool use is only informational or actually part of a process step

## 7.3 Extraction Rule

If two parts look similar but serve different roles, do not collapse them without good reason.

Part identity matters for instruction and validation.

---

# 8. Subassembly Definition

Before step authoring, group the content into meaningful subassemblies.

## 8.1 Why Subassemblies Matter

Subassemblies help:

- reduce cognitive load
- create milestones
- support reuse
- support scalable machine growth
- create clearer aha moments

## 8.2 Good Subassembly Characteristics

A good subassembly is:

- structurally meaningful
- teachable in a short sequence
- small enough to validate
- large enough to feel like progress

## 8.3 Rule

Do not define subassemblies purely by file organization.

Define them by instructional and structural meaning.

---

# 9. Asset Creation Pipeline

Assets should be created only after the instructional structure is understood.

## 9.1 Asset Sources

Possible sources include:

- Blender-authored geometry
- simplified CAD conversions
- hand-authored meshes
- Hyperhuman-generated assets where appropriate
- primitives for early vertical-slice testing

## 9.2 Asset Authoring Rules

Assets should be:

- lightweight
- web-friendly
- readable in instructional contexts
- simplified where necessary for clarity
- separate from step logic
- reusable across steps and packages where possible

## 9.3 Fidelity Rule

Do not over-model details that do not help the learner.

Instructional clarity is more important than maximum realism in the first slices.

## 9.4 Recommended Export Formats

Preferred model formats:

- GLB
- glTF

Preferred texture compression path:

- KTX2
- Basis Universal

## 9.5 Asset Naming Rule

Asset names should clearly expose what they are.

Examples:

- `powercube_frame_plate_a.glb`
- `powercube_corner_bracket_b.glb`
- `m8_hex_bolt.glb`

Avoid generic names like:

- `part1`
- `newmesh`
- `final_final`

---

# 10. Asset Simplification and Variants

Some parts may need multiple visual forms.

## 10.1 Useful Variants

Possible variants:

- normal assembly view
- ghost placement preview
- highlighted inspection view
- low-end simplified version if needed later

## 10.2 Rule

Visual variants should still map back to the same part identity in content.

Do not let multiple visual representations create identity confusion in the runtime.

---

# 11. Metadata Authoring

Once parts and tools are defined, author metadata.

## 11.1 Part Metadata

Each part should support:

- name
- category
- material
- function
- structural role
- search terms
- associated tool(s)
- quantity
- educational note
- physical substitution allowed flag

## 11.2 Tool Metadata

Each tool should support:

- name
- purpose
- usage notes
- optional search terms
- optional safety notes

## 11.3 Metadata Rule

Metadata should be concise, useful, and practical.

Avoid empty filler text.

If the user reads it, it should help them understand the build.

---

# 12. Step Authoring Pipeline

Step authoring is one of the most important parts of the pipeline.

## 12.1 Step Design Questions

For each step, define:

- what is the action
- what part(s) are involved
- what tool(s) are relevant
- what target(s) matter
- what order dependency exists
- what hint should appear if the user struggles
- what completion mode is used
- what the learner should understand after this step

## 12.2 Step Granularity Rule

Steps should not be:

- so large that the user is confused
- so tiny that the flow becomes tedious

A step should represent one coherent learner action.

## 12.3 Step Completion Modes

Use content-driven completion modes such as:

- virtual-only
- physical-only
- virtual-or-physical
- confirmation-only
- multi-part-required

These should come from the content model, not scene hacks.

---

# 13. Validation Authoring

Validation rules must be authored deliberately.

## 13.1 Validation Inputs

For each relevant step, define:

- target anchor
- position tolerance
- rotation tolerance
- correct part identity
- dependency requirements
- whether auto-snap is allowed
- whether leniency changes by mode

## 13.2 Validation Rule

Validation should reinforce learning.

It should not feel random or unfair.

If a part is rejected, the runtime should be able to explain why.

That requires good authored validation data.

---

# 14. Hint Authoring

Hints are part of content authoring, not an afterthought.

## 14.1 Hint Types

Possible hint types:

- highlight target
- ghost placement
- explanatory note
- directional cue
- “why this matters” reinforcement
- tool reminder

## 14.2 Hint Rule

Hints should reduce confusion without solving the entire problem automatically unless the mode allows it.

---

# 15. Effects Authoring

Some steps may benefit from process or feedback effects.

## 15.1 Effect Types

Possible effects include:

- placement confirmation glow
- success pulse
- welding sparks
- heat glow
- torch/fire cues
- dust
- process transition cues
- milestone celebration

## 15.2 Effects Rule

Effects should be tagged in content only when they improve instruction or clarity.

Do not add effects just because they are visually impressive.

## 15.3 Authoring Rule

Effects should be referenced by effect definitions or ids, not embedded as scene-only assumptions.

---

# 16. Challenge Metadata Authoring

The first content slices do not need full challenge systems, but authoring should stay open for them.

## 16.1 Useful Challenge Metadata

Possible fields:

- timer enabled
- retries counted
- hint usage counted
- strict validation mode support
- score payload compatibility

## 16.2 Rule

Challenge metadata should remain optional and should not pollute the core educational content.

---

# 17. Multiplayer-Ready Content Rules

Content should remain open for future multiplayer.

## 17.1 What This Means

Author content so important actions can be represented explicitly, such as:

- which step is active
- which part is being manipulated
- which placement is complete
- which hint was requested
- which physical substitution was confirmed

## 17.2 Rule

Do not hide critical authored content assumptions only inside UI text or only inside scene arrangement.

---

# 18. Machine Package Assembly

After parts, tools, metadata, steps, validation, and effects are authored, assemble the runtime package.

## 18.1 Package Contents

A machine package should include structured data such as:

- machine manifest
- assemblies
- subassemblies
- parts
- tools
- steps
- validation data
- optional effects data
- optional challenge metadata
- asset references

## 18.2 Package Rule

The package should be portable and loadable without machine-specific code changes.

That is one of the core goals of the architecture.

---

# 19. Recommended Folder Structure for Source Authoring

A source-authoring structure might look like this:

```text
ContentAuthoring/
  power_cube_frame_corner/
    Sources/
    Notes/
    Parts/
    Tools/
    Steps/
    Effects/
    Exports/
```

Inside Unity, the project-facing structure should still respect the official project layout, but the authoring mindset should remain organized like this.

---

# 20. Runtime Validation Pass

Before a machine package is accepted, validate it in runtime.

## 20.1 Required Checks

Check at minimum:

- package loads
- assets resolve
- metadata displays correctly
- step flow works
- validation behaves correctly
- hints appear correctly
- optional physical substitution path works
- completion summary works
- no content-specific hacks were required in generic runtime code

## 20.2 Teaching Check

Also ask:

- does the learner understand what they are doing?
- does the sequence create aha moments?
- is the part/tool information useful?
- is the assembly sequence too long or too granular?
- does the content feel satisfying to complete?

---

# 21. Iteration Loop

Content authoring should be iterative.

Recommended loop:

1. rough scope
2. rough content package
3. runtime test
4. simplify confusing areas
5. refine assets or metadata
6. refine step flow
7. refine hints and effects
8. validate again

Do not assume the first authored version is the best teaching version.

---

# 22. Recommended First Pipeline Target

For the first authentic slice, the recommended target remains:

- a small **Power Cube-aligned subassembly**
- likely a **frame corner or bracket-style subassembly**
- modest part count
- clear bolts / brackets / plate relationships
- simple tool story
- strong instructional clarity

This is the right place to prove the pipeline.

---

# 23. Authoring Roles and Responsibilities

Even if one person handles multiple roles, the pipeline should conceptually separate them.

## 23.1 Research Role

Owns:

- source gathering
- blueprint interpretation
- assumption notes

## 23.2 Content Design Role

Owns:

- educational framing
- subassembly definition
- step design
- hint design

## 23.3 Asset Role

Owns:

- mesh creation
- simplification
- export quality
- naming quality

## 23.4 Data Authoring Role

Owns:

- metadata
- validation definitions
- effect tags
- challenge metadata
- package assembly

Keeping these roles conceptually separate improves quality even for solo development.

---

# 24. Anti-Patterns to Avoid

Avoid these mistakes:

- building the machine only as a Unity scene with no package structure
- writing machine-specific logic into generic runtime systems
- modeling before understanding teaching goals
- overcomplicating the first authentic slice
- skipping metadata because “the model already shows it”
- authoring validation only by feel with no explicit data
- using effects without instructional purpose
- creating assets with messy naming and no identity mapping
- making the tutorial and authentic slices share content in confusing ways

---

# 25. Recommended First Implementation Order for the Pipeline

Use this order:

1. define machine scope
2. gather sources
3. write educational goals
4. extract parts and tools
5. define subassemblies
6. author rough content schema
7. create rough placeholder assets
8. author step flow
9. author validation
10. test in runtime
11. refine metadata and hints
12. refine assets
13. tag effects if justified
14. finalize machine package
15. validate and commit

This keeps the pipeline grounded in teaching and architecture, not only art production.

---

# 26. Validation Questions

Before approving authored content, ask:

- Is the scope small and teachable enough?
- Are the source assumptions documented?
- Are the parts and tools explicit?
- Are the steps clear and appropriately sized?
- Does validation reinforce learning?
- Are hints and effects justified?
- Is the package data-driven?
- Does it run without custom hacks?
- Does it remain open for future multiplayer and challenge features?
- Does it create practical understanding and aha moments?

If any of these are unclear, the content is not ready yet.

---

# 27. Final Guidance

The correct content authoring strategy is not:

“model something cool, drop it in Unity, and make it interactive somehow.”

The correct strategy is:

- start from scope and learning goals
- organize source material honestly
- define parts, tools, subassemblies, and steps clearly
- keep assets separate from instructional logic
- author metadata, validation, hints, and effects deliberately
- package content in a reusable data-driven way
- validate in runtime
- iterate until the experience is both teachable and stable

That is how the content pipeline becomes scalable, reliable, and useful for the long-term vision of the project.
