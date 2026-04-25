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
| `CarriageBatchUnit` | BearingCarriage with `skip:` (suffix-matched) for batch builds | same as BearingCarriage | 6 − len(skip) |

---

## Repeater Syntax — `instances:` for Replicated Patterns

When the same template applies N times with different part-id maps (4 carriages, 6 panel sides),
use the top-level `instances:` array. Each entry overrides `prefix`, `parts`, and any of
`orientation_cue` / `tool` / `torque_setting` / `milestone` / `skip` (template-specific). The
expander assigns contiguous `sequenceIndex` across all instances starting from `start_seq`.
Step IDs must be unique across instances — enforce uniqueness via distinct `prefix:` values
(the expander aborts on collision).

```yaml
template: CarriageBatchUnit
start_seq: 200
tool: tool_power_drill           # YAML-level default — every instance inherits
orientation_cue: "small ribbed belt hole beside large smooth belt hole"

instances:
  - prefix: batch_c1
    parts: { half_a: c1_half_a, half_b: c1_half_b, bearings: [...], bolts_top: [...], bolts_bot: [...], nuts: [...] }
    milestone: "Carriage 1 of 4."

  - prefix: batch_c2
    parts: { ... }
    milestone: "Carriage 2 of 4."

  - prefix: batch_c3
    skip: [close_halves, place_bolts]   # drop the named BearingCarriage steps
    parts: { ... }

  - prefix: batch_c4
    skip: [close_halves, place_bolts]
    parts: { ... }
```

A YAML without `instances:` is unchanged from the prior single-instance behavior. See
`AgentAssistant/inputs/example_carriage_batch.yaml` for a complete worked example.

After merging the generated JSON into an assembly file, run
`python tools/package_health.py <packageId> --fix-seqindex` to collapse any seqIndex gap.

---

## Step Configuration Prefabs (YAML, data-defined)

Prefabs are the data-defined sibling of the Python templates. A prefab is a YAML file
in `AgentAssistant/prefabs/<name>.yaml` that captures a step sequence as data — no
Python required. An instantiation YAML in `AgentAssistant/inputs/` references the
prefab by name and supplies the part roles + start sequence.

The Python templates (`BearingCarriage`, `IdlerHalves`, etc.) keep working unchanged.
Prefabs are additive — new patterns can be authored by humans or LLMs as YAML without
touching code.

### Prefab schema

```yaml
prefab: CarriageBuild               # unique name (used by instantiation YAMLs)
description: "..."

roles:                              # slots the instantiator must fill
  half_a:    { kind: part }                        # single partId
  bearings:  { kind: part_list, count: 4 }         # list of partIds
  bolts_top: { kind: part_list, count: 2 }
  ...

options:                            # knobs with defaults; instantiator may override
  tool:           { type: string, default: tool_power_drill }
  torque_setting: { type: string, default: lowest }
  milestone:      { type: string, default: "Carriage assembly complete." }

derived:                            # computed roles (e.g. concat lists)
  all_bolts: { kind: part_list, combine: [bolts_top, bolts_bot] }

steps:                              # ordered step templates
  - id_suffix: place_bearings       # final id = step_<prefix>_<id_suffix>
    family: Place
    name: "Seat {bearings.count} Bearings in Carriage Half A"
    requiredPartIds:
      - "{half_a}"                  # single role
      - "*{bearings}"               # expand list role inline
    guidance:
      instructionText: "Place all {bearings.count} bearings into ({half_a}). Orient {orientation_cue}."
      whyItMattersText: "..."
    validation:
      successCriteria: "..."
      failureCriteria: "..."
    feedback:
      successMessage: "..."
      failureMessage: "..."
  - id_suffix: tighten
    family: Use
    requiredToolActions:
      - { toolId: "{tool}", actionType: Tighten, profile: Torque }
    ...
```

**Substitution rules** (any string field):
- `{role}` — single role value (or option value)
- `{role.count}` — length of a list role
- Plain text passes through

**Array-of-references rules** (`requiredPartIds`, `optionalPartIds`, etc.):
- `"{role}"` — insert single role value
- `"*{role}"` — expand list role inline
- Any other string is a literal

### Instantiation YAML

```yaml
prefab: CarriageBuild
prefix: y_left_carriage             # used in step IDs
start_seq: 87
parts:
  half_a: y_left_carriage_half_a
  half_b: y_left_carriage_half_b
  bearings: [y_left_lm8uu_a, y_left_lm8uu_b, y_left_lm8uu_c, y_left_lm8uu_d]
  bolts_top: [y_left_m6x18_a, y_left_carriage_m6x18_b]
  bolts_bot: [y_left_m6x30_a, y_left_m6x30_b]
  nuts: [y_left_m6_nut_a, y_left_m6_nut_b, y_left_m6_nut_c, y_left_m6_nut_d]
options:
  milestone: "Y-Left carriage complete — 1 of 4"
```

### CLI

```bash
python Tools/instantiate_prefab.py --list-prefabs
python Tools/instantiate_prefab.py AgentAssistant/inputs/<file>.yaml
```

Available today (P1): `CarriageBuild` (port of the Python `BearingCarriage` template).

Worked example: `AgentAssistant/inputs/example_prefab_carriage.yaml` produces output
byte-equivalent to the Python `BearingCarriage` template for the same inputs.

---

*Last updated: 2026-04-25. Add new templates here as they are implemented in `generate_steps.py`. Add new prefabs as YAML files in `AgentAssistant/prefabs/`.*
