# PERSISTENCE_MODEL.md

## Purpose

This document defines the canonical persistence architecture for the XR assembly training application.

It answers three questions:

1. What does persistence guarantee?
2. What does persistence store?
3. How does the runtime reconstruct a session from persisted data?

This document is authoritative for persistence scope and restore semantics. Implementation details (storage backend, serialization format) may evolve, but the guarantees defined here govern what persistence means architecturally.

---

# 1. Persistence Guarantee: Step-Boundary Restore

Persistence guarantees **step-boundary restore**.

This means:

- The runtime can restore a session to the **boundary of the last completed step**
- All steps before the restore point are treated as completed
- The step at the restore point activates normally, as if the learner just arrived there
- The scene is reconstructed from **authored content data + step progress**, not from persisted spatial state

Persistence does **not** guarantee:

- Mid-step restore (e.g. a part dragged halfway to a target)
- Exact reproduction of transient visual state (animations, effects, cursor position)
- Reconstruction of ephemeral interaction state (selection, grab, inspect focus)

If a learner exits mid-step, the session resumes at the **beginning of that step**, not partway through it.

---

# 2. Why Step-Boundary Restore Is Sufficient

The assembly content model is deterministic at step boundaries.

Given:

- the machine package (authored content data)
- the current step index (or equivalently, the count of completed steps)
- the session mode

...the runtime can reconstruct the correct scene state:

- **Completed parts**: all parts from steps before the restore point are marked Completed and positioned at their authored target poses
- **Current step setup**: the active step resolves its parts, tools, targets, ghosts, and interaction context from authored data
- **Visual state**: ghost targets, tool targets, highlights, and UI all derive from the active step definition

No per-part spatial data needs to be persisted because every part's final position is defined by its authored target. The runtime does not need to remember *where* a part ended up — it knows where it *should* be from the package data.

---

# 3. What Persistence Stores

## 3.1 Required Fields (Current)

These fields are persisted and required for restore:

| Field | Purpose |
|---|---|
| `MachineId` | Identifies which machine package to load |
| `MachineVersion` | Detects content version mismatch on restore |
| `Mode` | Session mode (tutorial, guided, standard, challenge) |
| `CurrentAssemblyId` | Which assembly is active |
| `CurrentSubassemblyId` | Which subassembly is active |
| `CurrentStepId` | Which step was last active |
| `CompletedStepCount` | How many steps were completed (restore cursor) |
| `Lifecycle` | Session lifecycle state |

## 3.2 Metrics Fields (Current)

These fields are persisted for session continuity but are not required for scene reconstruction:

| Field | Purpose |
|---|---|
| `ElapsedSeconds` | Total session time |
| `ChallengeActive` | Whether challenge mode is on |
| `MistakeCount` | Cumulative mistakes |
| `HintsUsed` | Cumulative hint usage |
| `CurrentStepStartSeconds` | Timer anchor for current step |
| `CurrentStepElapsedSeconds` | Time spent on current step |
| `LastStepDurationSeconds` | Duration of previous step |
| `TotalStepDurationSeconds` | Cumulative step time |

## 3.3 Fields NOT Persisted (By Design)

The following are intentionally excluded from persistence because they are either derivable from authored data or are ephemeral:

- Per-part world positions or rotations
- Per-part placement state (derived from step progress on restore)
- Physical substitution confirmations (future: see section 6)
- Ghost target visibility state
- Tool cursor state
- Selection / interaction focus
- Camera position
- Effect playback state
- UI panel visibility

---

# 4. Restore Sequence

When a session is restored, the runtime executes this sequence:

1. **Load machine package** using `MachineId`
2. **Validate version** — if `MachineVersion` mismatches the loaded package, warn or reject
3. **Advance progression cursor** — skip to `CompletedStepCount` without firing per-step events
4. **Bulk-complete parts** — mark all parts from skipped steps as Completed (state only)
5. **Publish `SessionRestored` event** — visual layer positions completed parts at authored targets
6. **Activate current step** — fires a single `StepStateChanged(Active)` so all listeners set up normally

This is a **fast-forward**, not a replay. The runtime does not re-execute each step's interaction lifecycle. It jumps directly to the step boundary and lets the normal step activation path take over.

---

# 5. Persistence Timing

The session auto-saves at step boundaries:

- **On step completion**: after a step transitions to Completed, the updated `MachineSessionState` is persisted
- **On session lifecycle changes**: pause, background, or explicit save

Persistence does **not** save continuously during step interaction. There is no mid-step checkpoint.

---

# 6. Future Extensions

The step-boundary restore model is designed to remain stable as the system grows. Future capabilities should extend the persisted state only when the authored data alone cannot reconstruct the required state.

## 6.1 Physical Substitution State

When physical substitution is implemented, the persistence model may need to store which parts were confirmed as physically present, since this is a learner decision not derivable from authored content.

Candidate addition:
- `PhysicalSubstitutionConfirmations`: array of part ids confirmed as physically placed

This remains step-boundary compatible — the restore path would mark those parts as physically present instead of virtually placed.

## 6.2 Challenge / Scoring State

Current challenge metrics (time, mistakes, hints) are already persisted. If per-step scoring is added later, it should be stored as a lightweight array alongside `CompletedStepCount`, not as a full scene snapshot.

## 6.3 Branching / Non-Linear Progression

If the content model ever supports non-linear step ordering (e.g. learner chooses assembly order), persistence may need to store the actual completion set rather than a single count. The restore model stays the same: bulk-complete the set, activate the next available step.

---

# 7. What Persistence Is NOT

Persistence is not a scene serialization system.

It does not capture Unity transforms, renderer states, material overrides, or any scene-graph data. It captures **logical progression state** that the runtime uses to reconstruct the scene from authored content.

This distinction matters because:

- It keeps the persisted payload small and version-resilient
- It avoids coupling persistence to scene structure
- It makes restore deterministic — the same progress + the same package always produces the same scene
- It simplifies multiplayer readiness — sync state is the same shape as persistence state

---

# 8. Relationship to Other Documents

| Document | Relationship |
|---|---|
| `ASSEMBLY_RUNTIME.md` | Defines the runtime lifecycle that persistence saves/restores into |
| `SOURCE_OF_TRUTH_MAP.md` | Section 2.9 identifies `SessionPersistenceService` as the persistence truth owner |
| `DATA_SCHEMA.md` | Defines the authored content that makes step-boundary restore sufficient |
| `CONTENT_MODEL.md` | Defines the deterministic content hierarchy that persistence relies on |

---

# 9. Canonical Rule

If a piece of state can be derived from authored content + step progress, do not persist it.

Persist only what the runtime cannot reconstruct from the machine package and the completed step count.
