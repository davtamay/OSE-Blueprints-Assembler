# OSE XR Assembly Authoring — Mission & Strategy

## Mission Goal

Turn asset files + a short description into perfectly structured step JSON, deterministically, with no guessing.

**Why it matters:** Authoring the batch carriage build took ~45 min because the agent had to:
- Trace part IDs across 4 files without a catalog
- Reconstruct physical procedure from existing data (reverse-engineer intent)
- Carry spatial knowledge in training only — nothing in the data enforced it
- Infer animation/validation schemas from examples instead of querying them

**Target state:** 15-line description in → valid step JSON out, regardless of which model is running. Same input, same output, every time.

---

## The 5-Layer Stack

### Layer 1 — Assembly Grammar ✅ DONE
*Formal verbs, spatial modifiers, and test keywords that map directly to step fields*

Lives in: `CLAUDE.md` (Grammar & Templates section), `AgentAssistant/assembly_vocabulary.yaml` (machine-readable, for GPT-4o / Gemini)

**What it solves:** Without this, agents infer "flanges-outward" from examples and get it wrong ~30% of the time. With it, every term has an exact JSON translation.

| Human term | Family | Key JSON |
|---|---|---|
| place / seat / insert | `Place` | `requiredPartIds`, `targetIds` |
| confirm / check / shake-test | `Confirm` | `guidance`, `validation`, `feedback` |
| tighten / drill at torque | `Use` | `requiredToolActions`, `profile: Torque` |
| finger-tight | `Place`, no tool | omit `requiredToolActions` |

---

### Layer 2 — Procedure Templates ✅ DONE
*Named step sequences: part IDs in → complete step array out*

Lives in: `tools/generate_steps.py` (code = canonical definition), `AgentAssistant/ConstructionThroughPrompts.md` (catalog)

| Template | Steps | Status |
|---|---|---|
| `BearingCarriage` | 6 | ✅ Implemented |
| `IdlerHalves` | 4 | ✅ Implemented |
| `MotorHolder` | 7 | ✅ Implemented |
| `RodAssembly` | 5 | ✅ Implemented |
| `BeltThread` | 7 | ✅ Implemented |

**What it solves:** The agent never needs to know what a "shake-test step" looks like internally. It describes the operation type, the code expands it. Adding a new carriage = 15 lines of YAML, not 200 lines of JSON.

---

### Layer 3 — Spatial Contract ✅ DONE (Phase 1)
*Physical validity enforced at validation time, not just in CLAUDE.md prose*

Lives in: `Assets/_Project/Scripts/Content/Validation/BuiltIn/SpatialContractPass.cs`
Registered as built-in pass 10 in `MachinePackageValidator._builtInPasses`.

**Phase 1 checks (implemented):**
- **Staging pose collision** — two authored parts within 0.05 m → Warning (CLAUDE.md rule 7)
- **Subassembly ordering** — Confirm-family step is the first step in its subassembly (nothing placed yet) → Warning
- **Use step tool coverage** — Use-family step with neither `requiredToolActions` nor `relevantToolIds` → Warning

**Phase 2 (planned — requires GLB loading at validation time):**
- `targets[].acceptedDimensions` vs part bounding box from loaded GLB
- Fastener hole diameter vs bolt spec in part definition

**What it solves:** Staging collisions, procedure sequencing errors, and headless Use steps are now caught before Play mode — the same way category errors and double-placement were added to `package_health.py`.

---

### Layer 4 — MCP Server 🔲 PLANNED
*Agent-queryable endpoints: query before writing, never guess*

This is what makes Layer 1–3 agent-accessible without reading example files. Currently an agent must read existing assembly JSON to learn what a shake cue looks like. An MCP endpoint means it queries the schema directly.

**Planned endpoints:**
```
GET /parts/{partId}         → id, name, assetRef, category, boundingBox, fastenerSpec
GET /targets/{targetId}     → position, orientation, acceptedPartIds[], toolActionType
GET /templates              → template names, parameter signatures, step count
GET /schema/step-families   → required/optional fields + behavioral contract per family
GET /schema/animation-cues  → cue types (shake, pulse, poseTransition, orientSubassembly)
                              + parameters (amplitude, frequency, axis, loop, trigger)
GET /schema/part-categories → valid category values (currently: plate, bracket, fastener,
                              shaft, panel, housing, pipe, custom)
POST /validate/step         → given step JSON → returns field errors before file write
POST /generate/steps        → template name + partId map → step JSON array
```

**Why it helps:**
- Agent calls `GET /schema/animation-cues` → knows shake amplitude/frequency/axis fields exactly → no guessing from examples
- Agent calls `GET /schema/part-categories` → knows "hardware" is invalid → catches error before Play mode
- Agent calls `POST /validate/step` → catches double-placement, invalid category, missing fields → zero Unity validator surprises
- Multiple models (Claude + Gemini + GPT-4o) can all query the same schema → consistent output regardless of which model authors steps

**Design:** MCP server wraps `generate_steps.py` — not a replacement. Python code is the template engine; MCP is the network interface. Can be a lightweight FastAPI or stdio MCP server running locally during authoring sessions.

**What it solves:** The entire class of Unity validator errors that occur because agents had to infer schema from examples. Every error we've hit (invalid category, double-placed parts, missing requiredPartIds, wrong animation cue fields) would be caught at the query stage, not at Play mode.

---

### Layer 5 — Translator Input Format ✅ DONE
*Canonical 15-line YAML: any model writes it, `generate_steps.py` expands it deterministically*

Lives in: `AgentAssistant/inputs/` (YAML files), `AgentAssistant/ConstructionThroughPrompts.md` (format spec)

```yaml
assembly: assembly_d3d_y_left_bench
subassembly: subassembly_y_left_carriage_build
template: BearingCarriage
start_seq: 87
parts:
  half_a: y_left_carriage_half_a
  half_b: y_left_carriage_half_b
  bearings: [y_left_lm8uu_a, y_left_lm8uu_b, y_left_lm8uu_c, y_left_lm8uu_d]
  bolts_top: [y_left_m6x18_a, y_left_carriage_m6x18_b]
  bolts_bot: [y_left_m6x30_a, y_left_m6x30_b]
  nuts: [y_left_m6_nut_a, y_left_m6_nut_b, y_left_m6_nut_c, y_left_m6_nut_d]
tool: tool_power_drill
torque_setting: lowest
orientation_cue: "small ribbed belt hole beside large smooth belt hole"
milestone: "Y-Left carriage complete — 1 of 4"
```

---

## Validation Lessons — Every Unity Error Gets Back-ported

Every class of Unity validator error is now caught by `tools/package_health.py` before Play mode:

| Unity error | Cause | `package_health.py` check |
|---|---|---|
| Part placed by multiple Place steps | Same partId in requiredPartIds of >1 Place step | Check 7 — double-placement detector |
| Invalid `category` value | Used "hardware" instead of "shaft" | Check 6 — allowlist validator |
| seqIndex gaps / duplicates | Steps removed without renumbering | Check 1 + `--fix-seqindex` |
| Orphan parts | Parts left defined after step removal | Check 2 |
| Broken part refs | Typo or wrong file | Check 4 |

**Rule:** Run `python tools/package_health.py d3d_v18_10` before AND after every bulk edit.
Add new checks to the script every time Unity surfaces a new error class.

---

## Complexity Trajectory

| Task | Before layers 1–2–5 | After |
|---|---|---|
| Author BearingCarriage (6 steps) | ~45 min (archaeology + authoring) | ~5 min (YAML → generate) |
| Author new carriage variant | ~30 min | ~2 min |
| Author IdlerHalves (4 steps) | ~20 min | ~2 min (once template exists) |
| Debug Unity validator error | ~15 min | <1 min (package_health.py catches it) |

After Layer 4 (MCP):
- Zero guessing about animation cue schemas → shake cue correct on first write
- Zero Unity validator surprises → POST /validate/step catches everything before file write
- Any model (Claude, Gemini, GPT-5o) produces identical output → no model-specific tuning

---

## Document Map (all in `AgentAssistant/`)

| File | Purpose |
|---|---|
| `MissionAndStrategy.md` | This file — strategic vision, 5-layer stack, complexity targets |
| `ConstructionThroughPrompts.md` | Agent bootstrap, template catalog, how-to-use for all models |
| `assembly_vocabulary.yaml` | Machine-readable grammar — prepend as system prompt for non-Claude models |
| `inputs/*.yaml` | Translator Input files — describe an assembly operation in 15 lines |
| `outputs/*.json` | Generated step arrays — merge into assembly JSON |

*Last updated: 2026-04-11*
