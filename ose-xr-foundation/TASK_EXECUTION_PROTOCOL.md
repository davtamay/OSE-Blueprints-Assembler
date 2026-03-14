# TASK_EXECUTION_PROTOCOL.md
## OSE XR Foundation – Agent Task Execution Protocol

This document defines the operational workflow for AI agents contributing to the OSE XR Foundation project.
It establishes the canonical rules for planning, task sizing, validation, and commits.

Works together with:
- CLAUDE_TASK_PROMPT.md
- TEST_STRATEGY.md
- IMPLEMENTATION_CHECKLIST.md
- SOURCE_OF_TRUTH_MAP.md

Agents must follow these rules before modifying code or documentation.

---

# 1. Plan Mode

Agents must enter Plan Mode before making changes.

Plan Mode requires:
- reading architecture docs
- inspecting the Unity project structure
- identifying the smallest valid implementation step
- identifying risks and dependencies

Agents must not modify files during Plan Mode.

Plan Mode must return:
1. architecture summary
2. proposed next step
3. exact files to modify
4. risks
5. technical validation plan
6. visual validation plan
7. proposed commit message

---

# 2. Task Sizing Rules

Tasks must remain small and atomic.

Guidelines:

- prefer 1–3 files changed per commit
- avoid multi‑system modifications
- avoid architectural refactors without explicit approval
- avoid mixing runtime code and documentation unless tightly related

If a task becomes too large:

1. stop
2. split the task
3. return a revised plan

---

# 3. Documentation First

If architecture is unclear:

1. stop implementation
2. identify documentation drift
3. propose documentation corrections
4. resolve docs before writing runtime code

Agents must never guess architecture behavior.

---

# 4. Technical Validation

All code changes must include technical validation.

Examples:

- project compiles
- no new Unity console errors
- module boundaries preserved
- Input System and XRI compatibility maintained

Agents must report what was validated and how.

---

# 5. Visual Validation

Every user‑visible change must include visual validation steps.

Examples:

- scene setup instructions
- expected Game View results
- interaction behavior
- UI results

Validation steps should allow confirmation in less than 2 minutes.

---

# 6. Commit Discipline

Rules:

- one logical change per commit
- clear commit message
- no unrelated file edits
- respect module boundaries

Commit message example:

runtime: add assembly session state manager skeleton

---

# 7. Architectural Boundaries

Agents must respect boundaries defined in:

- SOURCE_OF_TRUTH_MAP.md
- SYSTEM_BOUNDARIES.md
- UNITY_PROJECT_STRUCTURE.md

Core rules:

- runtime owns state
- UI is presentation only
- systems communicate through events
- content is data‑driven

---

# 8. Escalation Rules

Agents must stop and ask for clarification when:

- architecture docs conflict
- required files are missing
- ownership is unclear
- changes affect multiple subsystems

---

# 9. Regression Prevention

Before finishing a task:

- verify existing functionality still works
- ensure architecture rules were not violated
- confirm documentation remains accurate

---

# 10. Goal

This protocol ensures the OSE XR Foundation project remains:

- modular
- scalable
- predictable
- safe for agent‑assisted development