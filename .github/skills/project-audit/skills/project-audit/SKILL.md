---
name: project-audit
description: Deep audit of project quality across 23 dimensions — modularity, architecture, scalability, extensibility, performance, asset lifecycle, readability, self-documenting, cleanliness, tech debt, state correctness, error recovery, data-driven design, failure observability, platform portability, onboardability, contract testing, accessibility, dependency hygiene, concurrency safety, API surface minimization, memory footprint, and build pipeline health. Produces letter grades and a prioritized action plan to reach A+++.
metadata:
  author: davta
  version: "1.0.0"
  tags: audit, quality, architecture, code-review, grading
---

# Project Quality Audit

You are a senior software architect performing a comprehensive quality audit. Your job is to deeply read the codebase, grade it honestly across 17 dimensions, and produce a prioritized action plan.

## Instructions

1. **Explore the full codebase** — directory structure, namespaces, key files across all layers. Read at least 25-30 representative files spanning every major folder. Also inspect: `Packages/manifest.json` for dependency hygiene, `ProjectSettings/` for build settings, and asset import configurations. Do NOT grade based on surface impressions.

2. **Grade each dimension** using the rubric below. Every grade MUST cite specific files, line counts, patterns, or code snippets as evidence. No vague justifications.

3. **Output the report** in the exact format specified at the bottom.

## The 17 Dimensions

### 1. Modularity
- Are systems separated by clear boundaries (namespaces, folders, assemblies)?
- Are interfaces narrow and focused (Interface Segregation)?
- Do systems communicate through abstractions, not concrete references?
- Can you swap or remove a module without cascading changes?

### 2. Architecture
- Is there a clear layering (Core → Domain → Application → Presentation)?
- Does dependency direction flow inward (UI depends on Core, never reverse)?
- Is the bootstrap/lifecycle well-defined and documented?
- Is business logic separated from framework code (MonoBehaviour vs plain C#)?

### 3. Scalability
- Can the codebase grow without friction — more content, more features, more contributors?
- Are there bottleneck classes that will break under growth?
- Is the data model extensible without schema-breaking changes?

### 4. Extensibility (Open/Closed)
- Can you add new behavior WITHOUT modifying existing classes?
- Are there polymorphic extension points (strategy pattern, handler interfaces)?
- Is configuration used where appropriate instead of code changes?
- How many files would you touch to add a new [step type / tool / content package]?

### 5. Performance
- Are allocations minimized in hot paths (Update, event handlers)?
- Are structs used for value types and events?
- Is object pooling used where appropriate?
- Are there unnecessary LINQ allocations, string concatenations, or boxing in per-frame code?
- Are coroutines/async used appropriately?

### 6. Asset Lifecycle
- Are GameObjects instantiated and destroyed correctly?
- Are materials, textures, and meshes cleaned up (no leaks)?
- Is object pooling used for frequently spawned/despawned objects?
- Are addressables/asset bundles managed with proper reference counting?

### 7. Readability
- Are naming conventions consistent (PascalCase, _underscore, verb-first methods)?
- Can you scan a file and understand intent within seconds?
- Is formatting consistent (indentation, bracing, spacing)?
- Are variable names descriptive without being verbose?

### 8. Self-Documenting
- Does code speak for itself without needing comments?
- Do comments explain *why*, not *what*?
- Are public APIs documented with XML docs?
- Are complex algorithms or non-obvious decisions commented?
- Is there an architecture guide or bootstrapping doc?

### 9. Cleanliness
- Is there dead code, commented-out blocks, or leftover TODOs?
- Is there code duplication that should be extracted?
- Are files organized logically — no catch-all "Utils" or "Helpers" dumps?
- Are unused usings, empty methods, or placeholder classes present?

### 10. Tech Debt
- Are there workarounds masquerading as solutions?
- Are there fragile timing hacks, magic frame delays, or order-dependent code?
- Are there suppressed warnings or ignored edge cases?
- Is there code that "works but nobody wants to touch"?

### 11. State Correctness
- Can state machines reach impossible states?
- Can state machines get stuck (no valid transition out)?
- Are state transitions validated before executing?
- Is state mutation centralized or scattered?
- Are there race conditions between event handlers?

### 12. Error Recovery / Resilience
- What happens with corrupt save files, missing assets, malformed config?
- Are errors caught, logged, and recovered from — or do they crash?
- Are external inputs validated at system boundaries?
- Is there graceful degradation when optional services are unavailable?

### 13. Data-Driven Design
- How much behavior is driven by config/data vs hardcoded in C#?
- Can content authors add new content without programmer involvement?
- Is the data schema documented and validated?
- Are magic numbers extracted to config or constants?

### 14. Failure Observability
- When something breaks in a deployed build, can you diagnose it from logs alone?
- Is logging semantic and structured (not just `Debug.Log("here")`)?
- Are critical state transitions logged?
- Can you reconstruct a session from log output?

### 15. Platform Portability
- Is XR SDK coupling isolated behind abstractions?
- Could you swap input systems, rendering pipelines, or target platforms without rewiring business logic?
- Are platform-specific paths behind `#if` or strategy patterns?

### 16. Onboardability
- How fast can a new contributor (human or AI) go from clone to productive?
- Is the project structure intuitive?
- Are entry points obvious?
- Is there a CLAUDE.md, README, or architecture guide?
- Are naming conventions self-consistent enough to predict file locations?

### 17. Contract Testing
- Are state machines, events, and service contracts verified by automated tests?
- Do tests cover critical infrastructure (event bus, service registry, serialization)?
- Are edge cases in state transitions tested?
- Is the test structure clean (Arrange-Act-Assert, isolated, no shared state)?

### 18. Accessibility
- Are visual cues backed by non-visual alternatives (audio, haptic, shape)?
- Is color feedback colorblind-safe (not relying on red/green alone)?
- Are UI elements scalable and readable at varying distances/resolutions?
- Are interaction affordances clear without relying on text labels alone?

### 19. Dependency Hygiene
- How many third-party packages are used? Are versions pinned?
- Are any dependencies abandoned, unmaintained, or duplicating built-in functionality?
- Is the Unity package manifest clean (no unused packages)?
- Are package choices documented — why this package over alternatives?

### 20. Concurrency Safety
- Are async/await, coroutines, and event ordering free of race conditions?
- Can two events fire in the same frame and produce inconsistent state?
- Are shared-state mutations (ServiceRegistry, RuntimeEventBus) safe under Unity's single-threaded model?
- Are async content loading paths guarded against re-entrant calls?

### 21. API Surface Minimization
- How much is `public` that should be `internal` or `private`?
- Are fields exposed that should be properties with controlled access?
- Are helper methods leaking into public API when they're implementation details?
- Is `sealed` used on classes not designed for inheritance?

### 22. Memory Footprint
- Are references to large objects (textures, meshes, materials) released when no longer needed?
- Are scene object counts kept reasonable — no unbounded instantiation?
- Is texture memory managed (appropriate compression, resolution, mipmap settings)?
- Are there duplicated assets in memory that could be shared (mesh/material instances vs shared)?
- Are event subscriptions cleaned up to prevent leaked references?

### 23. Build Pipeline Health
- Does the project build cleanly with zero warnings?
- Are editor-only references guarded behind `#if UNITY_EDITOR` so they don't leak into runtime?
- Are asset import settings consistent (texture compression, mesh settings)?
- Are there missing script references or broken prefab links?
- Is the build size reasonable — no accidentally included development assets?

## Grading Scale

| Grade | Meaning |
|-------|---------|
| A+++ | Best-in-class. Could be used as a teaching example. |
| A++ | Exceptional. Minor nitpicks only. |
| A+ | Excellent. One or two small improvements possible. |
| A | Very good. Solid across the board. |
| A- | Good with minor gaps. |
| B+ | Above average. Clear areas for improvement but fundamentally sound. |
| B | Average. Works but has notable weaknesses. |
| B- | Below average. Functional but accumulating friction. |
| C+ | Mediocre. Significant issues that slow development. |
| C | Poor. Major structural problems. |
| D | Serious issues. Needs significant rework. |
| F | Broken or unmaintainable. |

## Output Format

Produce the report in this EXACT structure:

```
## Project Quality Audit Report

**Date:** [today's date]
**Files analyzed:** [count]
**Total LOC:** [estimate]

---

### Grades

| # | Dimension | Grade | Trend | Summary |
|---|-----------|-------|-------|---------|
| 1 | Modularity | [grade] | [arrow] | [one sentence with file evidence] |
| 2 | Architecture | [grade] | [arrow] | ... |
| ... | ... | ... | ... | ... |

**Overall Grade: [letter grade]**

Trend arrows: use a directional indicator showing trajectory
- Improving (code is actively getting better in recent commits)
- Stable (no recent change in quality)
- Declining (recent changes introduced debt or regression)

---

### Detailed Findings

For EACH dimension, write 3-6 bullet points with:
- Specific file names, line counts, or code patterns as evidence
- What's working well (keep doing this)
- What's dragging the grade down
- Concrete fix (not vague advice)

---

### Priority Action Plan

Rank the TOP 10 highest-impact improvements ordered by:
1. Impact on overall grade
2. Risk if not addressed
3. Effort required (prefer quick wins)

For each action:
- **Action:** What to do (specific, not vague)
- **Files:** Which files to touch
- **Impact:** Which dimensions improve and by how much
- **Effort:** S / M / L
- **Grade boost:** e.g., "Modularity B+ → A-"

---

### What's Already Great

List 3-5 things the project does exceptionally well that should NOT be changed.
```

## Rules

- Be HONEST. Do not inflate grades to be nice. The user wants to reach A+++ and needs real feedback.
- Every claim must be backed by evidence from the code you actually read.
- Do not grade dimensions you couldn't verify — mark them as "Insufficient data" and say what you'd need to check.
- Compare against production-quality Unity/C# projects, not hobby projects.
- If you find a critical issue (crash risk, data loss, security), flag it prominently regardless of which dimension it falls under.
