
# TEST_STRATEGY.md

## Purpose

This document defines the testing strategy for the XR assembly training application.

The goal is to ensure the system remains:

- stable
- predictable
- scalable
- cross‑platform
- safe for agent‑driven development

Testing must verify:

- runtime behavior
- interaction correctness
- content package validity
- UI integration
- cross‑platform input
- performance stability
- regression safety

This project is designed to grow into a **large content platform**, so testing must validate both **engine systems** and **machine content packages**.

---

# 1. Testing Philosophy

The testing strategy follows these principles:

1. **Test architecture early**
2. **Test content automatically**
3. **Test interactions deterministically**
4. **Prevent regression through automation**
5. **Validate cross‑platform behavior**
6. **Allow agents to safely modify code**

The goal is to make it **very difficult to accidentally break the application**.

---

# 2. Testing Layers

Testing is divided into several layers.

```
Unit Tests
↓
System Tests
↓
Runtime Integration Tests
↓
Content Validation Tests
↓
Cross‑Platform Interaction Tests
↓
Performance Tests
```

Each layer validates different aspects of the system.

---

# 3. Unit Tests

Unit tests validate **small isolated systems**.

These tests should run quickly and not require scene loading.

### Examples

Test:

- validation rule calculations
- tolerance logic
- step progression logic
- event bus dispatch
- data schema parsing
- metadata loading
- content id lookup
- challenge scoring math

### Example Targets

```
AssemblyValidator
StepProgressionController
EventBus
MachinePackageLoader
ScoreCalculator
```

---

# 4. System Tests

System tests validate **individual runtime systems**.

These tests may involve small runtime contexts but should still remain mostly isolated.

### Systems To Test

Interaction System

- grab detection
- hover detection
- release events

Assembly Runtime

- placement attempt detection
- validation triggers

Validation System

- tolerance checks
- dependency checks

Step System

- step ordering
- completion logic

Effects System

- effect dispatch
- fallback effects

UI System

- UI update events
- step instruction updates

---

# 5. Runtime Integration Tests

These tests validate **multiple systems working together**.

Example test scenario:

```
User grabs part
→ hover over placement target
→ attempt placement
→ validation runs
→ placement accepted
→ step completes
→ UI updates
→ effect triggers
```

Expected outcome:

- correct events fired
- correct step progression
- correct UI update
- correct effect triggered

---

# 6. Content Package Validation Tests

Every machine package must be validated automatically.

### Required Checks

- schema version valid
- required fields exist
- all ids unique
- references resolve
- parts referenced by steps exist
- tools referenced exist
- validation rules exist
- target anchors exist
- effect ids resolve
- hint ids resolve

### Failure Policy

If a machine package fails validation:

The build should fail or flag the content immediately.

---

# 7. Interaction Tests

Interaction tests verify that input systems behave correctly across platforms.

Supported inputs:

- XR controllers
- XR hands
- desktop mouse
- desktop keyboard
- mobile touch gestures

### Interaction Tests

Test:

Grab interaction
Release interaction
Placement attempt
UI interaction
Gesture detection
Camera navigation

---

# 8. Cross‑Platform Input Tests

The input system must behave consistently across:

| Platform | Input Type |
|--------|--------|
XR Headset | hands / controllers |
Desktop | mouse + keyboard |
Mobile | touch gestures |

### Test Goals

Ensure:

- actions map correctly
- gestures translate to interactions
- no input device causes logic breakage

---

# 9. Content Flow Tests

These tests validate the **user learning flow**.

### Validate

- instructions appear correctly
- hints trigger when needed
- part information displays correctly
- step transitions feel logical
- assembly completion triggers correctly

---

# 10. Multiplayer Readiness Tests (Future)

Even before multiplayer exists, tests should confirm that runtime state can be synchronized.

Test:

- step state
- part state
- assembly state
- challenge timers
- player actions

These states must be serializable.

---

# 11. Performance Tests

Because this application targets:

- mobile
- desktop
- XR headsets

performance must be monitored.

### Performance Metrics

Track:

- frame time
- memory usage
- asset load time
- event dispatch cost
- WebGL runtime behavior

### Test Targets

Ensure:

- stable 72‑90 FPS on XR devices where possible
- no frame spikes during assembly actions

---

# 12. Asset Pipeline Tests

Assets must also be validated.

### Validate

- model loads successfully
- KTX2 textures decode correctly
- missing asset references are detected
- asset scale is correct
- orientation metadata correct

---

# 13. Regression Testing

Every meaningful code change should trigger automated tests.

Agent‑driven workflows must:

- stage changes
- run tests
- commit only if tests pass

This protects the architecture as the system grows.

---

# 14. Agent‑Safe Development Rules

Because this project uses AI agents:

Agents must:

1. run tests before committing
2. never bypass failing tests
3. never modify schema without updating validators
4. never merge failing builds

Agents must follow `TASK_EXECUTION_PROTOCOL.md`.

---

# 15. Manual Testing Scenarios

Certain experiences require manual validation.

### Example Scenarios

User learns assembly for first time

User performs physical substitution

User requests hints repeatedly

User attempts incorrect placement repeatedly

User completes assembly successfully

Challenge mode run

Speed run scenario

These tests confirm the **learning experience quality**.

---

# 16. Continuous Integration

Eventually CI should automatically run:

- unit tests
- schema validation
- package validation
- runtime tests

Every push should confirm the system still works.

---

# 17. Testing Tools

Potential testing tools:

Unity Test Framework
Play Mode Tests
Edit Mode Tests
Custom content validators
CI pipeline scripts

---

# 18. Recommended Early Test Focus

In early development prioritize testing:

1. Event system
2. Step progression
3. Validation system
4. Data schema loading
5. Input routing
6. Content package validation

These are the **highest‑risk systems**.

---

# 19. Anti‑Patterns

Avoid:

- relying only on manual testing
- letting content bypass schema validation
- skipping regression tests
- coupling tests tightly to scene layouts
- ignoring cross‑platform interaction differences

---

# 20. Final Guidance

A strong testing strategy allows:

- safe architecture evolution
- safe agent‑assisted development
- scalable machine content growth
- stable runtime behavior

Testing should evolve with the system and remain a **first‑class engineering discipline**.
