# Construction Through Prompts
## OSE XR Assembly Instruction Authoring — Agent Reference

This document is the **single source of truth** for any agent (Claude, GPT-4o, Gemini, or future)
working on assembly instruction construction in this project. Read this before authoring any steps.

---

## Agent Bootstrap — Read This First

When you arrive in this project to author assembly steps, do this immediately:

```bash
# 1. Check package health (catches seqIndex gaps, orphan parts, broken refs)
python tools/package_health.py d3d_v18_10

# 2. If YAML files exist in inputs/, generate steps for them
python tools/generate_steps.py AgentAssistant/inputs/<file>.yaml

# 3. After merging generated steps into an assembly file, validate
python tools/package_health.py d3d_v18_10 --fix-seqindex
```

If the user says "build the X carriage" or "add steps for Y assembly":
1. Ask for or assemble the Translator Input (see Layer 5 below)
2. Write it to `AgentAssistant/inputs/<name>.yaml`
3. Run `generate_steps.py` — it outputs correct step JSON
4. Merge the output into the target assembly file
5. Run `package_health.py` to verify

**Do not author steps by hand unless the template doesn't exist yet.**

---

## The 5-Layer Stack

The goal: give a 15-line description of an assembly operation → get perfectly structured
step JSON deterministically, with no guessing about spatial rules, field shapes, or vocabulary.

Current status of each layer is marked below.

---

### Layer 1 — Assembly Grammar ✅ DONE
*Formal vocabulary: verbs, spatial modifiers, test keywords → JSON field mappings*

Documented in [`CLAUDE.md`](../CLAUDE.md) under "Assembly Procedure Authoring — Grammar and Templates."

**What it solves:** Without this, agents infer what "flanges-outward" means from examples and get
it wrong ~30% of the time. With it, every term has an exact JSON translation.

**Key mappings (summary):**

| Human term | step family | Key JSON |
|---|---|---|
| place / seat / insert | `Place` | `requiredPartIds`, `targetIds` |
| confirm / check / verify | `Confirm` | `guidance`, `validation`, `feedback` |
| tighten / drill | `Use` | `requiredToolActions` |
| shake-test | `Confirm` | `validation.successCriteria`: no rattle |
| rod-slide-test | `Confirm` | `validation.successCriteria`: slight resistance |
| flanges-outward | instructionText qualifier | no bearing migration |
| finger-tight | `Place`, no tool | do not add `requiredToolActions` |
| lowest torque | `Use` + `profile: Torque` | power drill, cross pattern |

See `AgentAssistant/assembly_vocabulary.yaml` for the machine-readable version (use for GPT/Gemini).

---

### Layer 2 — Procedure Templates ✅ DONE
*Named step sequences: description + part IDs → complete step array*

Templates live as **code** in `tools/generate_steps.py`. Currently implemented:

| Template | Steps | Status |
|---|---|---|
| `BearingCarriage` | 6 | ✅ Implemented |
| `IdlerHalves` | 4 | 🔲 Planned |
| `MotorHolder` | 7 | 🔲 Planned |
| `RodAssembly` | 5 | 🔲 Planned |
| `BeltThread` | 7 | 🔲 Planned |

**What it solves:** The agent no longer needs to know what a "shake-test step" looks like internally.
It describes the operation, the code expands it. Same input → same output every time, regardless
of which model runs the generation.

**Adding a new template:** Add a function `template_<name>(params, ...) -> List[dict]` in
`generate_steps.py`, then register it in `TEMPLATES`. The function is the canonical definition
of that procedure type.

---

### Layer 3 — Spatial Contract 🔲 PLANNED (Unity handles bounds)
*Physical validity: does this part fit that target? Are the bolt holes the right diameter?*

**Current state:** Spatial rules are prose in `CLAUDE.md` (7 absolute rules). Agents read them
and apply them, but there's no runtime enforcement.

**Planned:** Unity Editor validator extension that checks:
- `targets[].acceptedDimensions` vs part bounding box from loaded GLB
- Fastener hole diameter vs bolt spec in part definition
- Sequence dependencies (bearings must be placed before halves can close)

**What it solves:** Eliminates the class of errors where an agent places a part in the wrong
target or bolts before seating bearings. Unity loads the GLB anyway — bounds are free data.

**For now:** The 7 spatial rules in `CLAUDE.md` are enforced by the `BearingCarriage` template
itself (step ordering is baked in as code, not inferred).

---

### Layer 4 — MCP Server 🔲 PLANNED (deferred)
*Agent-queryable endpoints: part catalog, target positions, step schema, POST validate/generate*

**Planned endpoints:**
```
GET /parts/{partId}      → id, assetRef, boundingBox, fastenerSpec
GET /targets/{targetId}  → position, orientation, acceptedPartIds[]
GET /templates           → template names + parameter signatures
GET /schema/step-families → required/optional fields per family
POST /validate/step      → returns field errors before writing
POST /generate/steps     → template + partId map → step array
```

**Why deferred:** `generate_steps.py` achieves the same determinism for 10% of the effort.
An MCP server becomes valuable when:
1. Multiple agents (Claude + Gemini + GPT-4o) are running simultaneously
2. The part catalog is too large to fit in a context window
3. POST /validate/step can block bad steps before they touch files

**Design constraint:** The MCP server wraps `generate_steps.py` — it doesn't replace it.
The Python code is the template engine; the MCP layer is the network interface.

---

### Layer 5 — Translator Input Format ✅ DONE
*Canonical 15-line YAML description → deterministic step JSON via `generate_steps.py`*

**What it solves:** A human (or any LLM) writes a short YAML description. The code expands it.
The LLM's job is just "convert natural language to this YAML" — trivial for any capable model.

**The pipeline:**
```
Human description
    ↓  [any LLM: Claude, GPT-4o, Gemini — just extract params]
Translator Input YAML  →  AgentAssistant/inputs/<name>.yaml
    ↓  [generate_steps.py — pure code, no LLM]
Step JSON array  →  AgentAssistant/outputs/<name>.json
    ↓  [agent merges into assembly file]
assembly_d3d_*.json
    ↓  [package_health.py verifies]
Clean package ✓
```

**Template:**
```yaml
assembly: assembly_d3d_<id>
subassembly: subassembly_<id>
template: BearingCarriage           # must match a key in TEMPLATES dict
start_seq: 87                       # first sequenceIndex; use package_health.py to find safe slot
parts:
  half_a: <partId>
  half_b: <partId>
  bearings: [<partId_a>, <partId_b>, <partId_c>, <partId_d>]
  bolts_top: [<m6x18_a>, <m6x18_b>]
  bolts_bot: [<m6x30_a>, <m6x30_b>]
  nuts: [<nut_a>, <nut_b>, <nut_c>, <nut_d>]
tool: tool_power_drill              # optional, defaults to tool_power_drill
torque_setting: lowest              # optional
orientation_cue: "small ribbed belt hole beside large smooth belt hole"
milestone: "Y-Left carriage complete — 1 of 4"
```

Drop this file in `AgentAssistant/inputs/` and run `generate_steps.py`.

---

## What's Immediately Useful Right Now

These three things are live and reduce authoring time today:

### 1. `tools/generate_steps.py` — Step Generation (BearingCarriage)
```bash
python tools/generate_steps.py AgentAssistant/inputs/my_carriage.yaml
# → AgentAssistant/outputs/my_carriage.json  (6 steps, correct schema)
```

### 2. `tools/package_health.py` — Pre/Post Edit Validation
```bash
python tools/package_health.py d3d_v18_10               # find issues
python tools/package_health.py d3d_v18_10 --fix-seqindex # fix gaps
```

### 3. Grammar + Templates in CLAUDE.md
Any agent reading CLAUDE.md before authoring steps has:
- Exact verb → family mapping
- Test keyword → JSON block shapes
- 7 spatial rules (ordering, flanges, bolt lengths, tighten sequence)
- 5 named procedure templates

---

## Using This with Non-Claude Models (GPT-4o, Gemini)

Prepend `AgentAssistant/assembly_vocabulary.yaml` as the system prompt.
Then give the model this task:

> "Convert the following assembly description into a Translator Input YAML using the schema
> in assembly_vocabulary.yaml. Output only the YAML, nothing else."

The model outputs YAML. You run `generate_steps.py`. The model never touches the JSON.

This is model-agnostic because the LLM does only natural-language parsing.
All schema knowledge lives in `assembly_vocabulary.yaml`, not in the model's weights.

---

## Auto-Run Convention (How Agents Know What to Execute)

Agents that read `CLAUDE.md` get auto-run rules under "Auto-Run Rules for Assembly Construction."

For any other model, paste this at the top of your prompt:
```
You are an assembly authoring agent. Your working conventions:
1. Before authoring any steps, run: python tools/package_health.py d3d_v18_10
2. To generate steps, write a Translator Input YAML to AgentAssistant/inputs/<name>.yaml
   then run: python tools/generate_steps.py AgentAssistant/inputs/<name>.yaml
3. After merging steps, run: python tools/package_health.py d3d_v18_10 --fix-seqindex
4. Never write step JSON by hand if a template exists for the operation type.
5. Check AgentAssistant/ConstructionThroughPrompts.md for template catalog and current status.
```

---

## Template Catalog

| Template | Operation | Parts Required | Steps |
|---|---|---|---|
| `BearingCarriage` | Build a linear bearing carriage from two halves | half_a, half_b, bearings[4], bolts_top[2], bolts_bot[2], nuts[4] | 6 |
| `IdlerHalves` | Assemble idler pulley between two half-pieces | half_a, half_b, bearings[2], bolt_inner, bolt_frame_mount | 4 |
| `MotorHolder` | Assemble motor + pulley + belt into motor holder | motor, pulley, belt, half_nuts[3], belt_bolt, motor_screws[4], close_bolts[3] | 7 |
| `RodAssembly` | Thread rods through idler, carriage, motor holder | rod_a, rod_b, idler, carriage, motor_holder | 5 |
| `BeltThread` | Route and tension drive belt | belt, idler, peg_1, peg_2 | 7 |

---

*Last updated: 2026-04-10. Add new templates here as they are implemented in `generate_steps.py`.*
