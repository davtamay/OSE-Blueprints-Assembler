
# MACHINE_SELECTION_RESEARCH.md

## Purpose

This document identifies and evaluates candidate machines and subassemblies from the **Open Source Ecology (OSE)** ecosystem that could serve as the **first authentic build target** for the XR assembly training application.

The goal is not to pick the most impressive machine.

The goal is to pick the **best first machine slice** that allows the architecture, runtime systems, and teaching methodology to succeed.

This document helps prevent the team or coding agents from choosing a target that is too complex for the early stages of the project.

It should be used together with:

- `VERTICAL_SLICE_SPEC.md`
- `TECH_STACK.md`
- `ASSEMBLY_RUNTIME.md`
- `CONTENT_MODEL.md`
- `IMPLEMENTATION_CHECKLIST.md`

---

# 1. Selection Criteria

The first authentic machine slice must satisfy several constraints.

## 1.1 Complexity

The machine slice must be:

- small
- understandable
- teachable in a short session
- limited to a manageable number of parts

Target range:

**5 – 15 parts maximum** for the first authentic slice.

Anything larger risks overwhelming the architecture and the learner.

---

## 1.2 Documentation Availability

The machine must have:

- clear blueprint documentation
- understandable diagrams
- identifiable parts
- tool references where possible

Open Source Ecology machines vary widely in documentation quality.

We must choose one that has **clear source material**.

---

## 1.3 Mechanical Clarity

The assembly must teach something intuitive, such as:

- structural alignment
- bracket mounting
- frame construction
- bolt tightening order
- simple component integration

Avoid machines where the function is hard to understand visually.

---

## 1.4 Modular Subassemblies

The machine should naturally break into **subassemblies**.

Examples:

- frame sections
- mounting brackets
- small tool holders
- basic mechanical linkages

This supports the long‑term architecture where machines are built from **nested subassemblies**.

---

## 1.5 Educational Value

The machine should help users understand:

- how parts relate structurally
- how tools are used
- why order matters
- how components integrate

The goal is **practical comprehension**, not just virtual placement.

---

## 1.6 XR Interaction Suitability

The assembly should work well with:

- hand placement
- rotation
- snapping
- alignment

Assemblies that require extremely precise internal geometry should be avoided early.

---

# 2. Strong Candidate: Power Cube

## Overview

The **Power Cube** is one of the most iconic machines in the Open Source Ecology project.

It is essentially a modular hydraulic power unit used across multiple machines.

It is a strong candidate because:

- it is modular
- it has recognizable components
- it appears across many OSE machines
- it has extensive documentation
- it teaches meaningful mechanical concepts

---

# 3. Why the Power Cube Is Ideal

## 3.1 Central System

The Power Cube is the **heart of many OSE machines**, meaning learning it gives users foundational knowledge.

---

## 3.2 Modular Design

It contains many natural subassemblies:

- frame
- engine mount
- hydraulic pump mount
- reservoir components
- bracket systems

This makes it perfect for incremental XR lessons.

---

## 3.3 Scalable Complexity

We can begin with:

- a **tiny bracket assembly**
- then expand to **frame construction**
- then move toward **larger cube assembly**

This supports long‑term content growth.

---

# 4. Recommended First Authentic Slice

The first slice should **not** be the entire Power Cube.

Instead choose a **small structural subassembly**.

## Recommended Candidate

**Power Cube Frame Corner Assembly**

Possible components:

- structural plate
- support bracket
- bolt set
- reinforcement bar

Estimated parts:

**5 – 8 parts**

This size fits the architecture perfectly.

---

# 5. Why Frame Corner Assembly Works

### Clear Structure

Users can visually understand:

- how plates connect
- how brackets reinforce structure
- how bolts secure components

### Clear Tool Use

This assembly introduces:

- bolt placement
- tightening concept
- alignment

### Clear Step Order

Steps are naturally sequential:

1. position plate
2. attach bracket
3. insert bolts
4. tighten structure

Perfect for the step runtime system.

---

# 6. Alternative Candidate Machines

If Power Cube documentation proves difficult to adapt, these alternatives may work.

## 6.1 CEB Press Subassembly

The **Compressed Earth Brick (CEB) Press** is another well‑documented OSE machine.

Possible slice:

- small lever linkage assembly
- press frame bracket assembly

Pros:

- visually understandable
- mechanical motion demonstration possible

Cons:

- some parts may be complex.

---

## 6.2 MicroHouse Structural Joint

OSE’s MicroHouse project contains structural joinery concepts.

Possible slice:

- beam corner bracket
- fastener system

Pros:

- strong educational value
- easy to understand structure

Cons:

- may resemble construction rather than machinery.

---

# 7. Decision Matrix

| Candidate | Complexity | Documentation | Educational Value | XR Suitability |
|----------|-----------|--------------|------------------|---------------|
| Power Cube Frame Corner | Low | High | High | High |
| Power Cube Bracket Mount | Low | High | High | High |
| CEB Press Linkage | Medium | Medium | Medium | Medium |
| MicroHouse Joint | Low | Medium | Medium | High |

Best initial candidate:

**Power Cube Frame Corner Assembly**

---

# 8. Data Needed Before Modeling

Before creating XR content, gather:

- blueprint diagrams
- part list
- approximate dimensions
- assembly order
- tool references

This information feeds the **CONTENT_MODEL** schema.

---

# 9. Asset Creation Plan

Assets may come from:

- Blender reconstruction from blueprints
- simplified CAD exports if available
- Hyperhuman generated meshes
- manually authored primitives for early testing

Meshes should be exported as:

**GLB with KTX2 textures where possible.**

---

# 10. First Machine Content Package

The first authentic content package should contain:

```
machine/
    power_cube_frame_corner/
        machine_manifest.json
        parts/
        tools/
        steps/
        metadata/
```

The runtime should load this package dynamically.

---

# 11. Learning Reinforcement Opportunities

For this slice we can introduce:

- part explanation overlays
- tool explanation overlays
- structural explanation text
- reinforcement cues when correct assembly is achieved

The goal is to create **aha moments** where users understand *why* the structure matters.

---

# 12. Optional Challenge Hooks

Even in the first authentic slice we can track:

- completion time
- step retries
- hint usage

These metrics can support **future leaderboard or speed‑run challenges**.

Competition should remain optional and supportive of learning.

---

# 13. Future Expansion Path

Once the first slice works, the Power Cube content can expand to:

- additional frame segments
- hydraulic pump mount
- engine mount
- reservoir components
- full cube assembly

Each becomes a **separate XR lesson module**.

---

# 14. Definition of Success

This research phase succeeds when:

- a specific subassembly is chosen
- blueprint data is collected
- part list is defined
- steps are documented
- content package schema is prepared

Once complete, modeling and content authoring can begin.

---

# 15. Final Recommendation

The best starting point for the first authentic XR assembly lesson is:

**Power Cube Frame Corner Assembly**

Reasons:

- small and manageable
- mechanically meaningful
- strong documentation
- scalable toward larger machines
- ideal for step‑based XR instruction

This choice provides the strongest foundation for the platform.
