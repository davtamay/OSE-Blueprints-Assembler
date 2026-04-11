# CLAUDE.md — Agent Instructions for OSE Blueprints Assembler

## Auto-Run Rules for Assembly Construction

When the user asks you to author, generate, or build assembly steps, do this automatically — no prompting needed:

1. **Before any bulk edit:** `python tools/package_health.py d3d_v18_10` — confirms clean baseline
2. **To generate steps from a description:** Write a Translator Input YAML to `AgentAssistant/inputs/<name>.yaml`, then run `python tools/generate_steps.py AgentAssistant/inputs/<name>.yaml`
3. **After merging generated steps:** `python tools/package_health.py d3d_v18_10 --fix-seqindex`
4. **Sync to StreamingAssets** after any authoring change (copy from `Assets/_Project/Data/Packages/` to `Assets/StreamingAssets/MachinePackages/`)

**Do not author steps by hand if a template exists.** Check `AgentAssistant/ConstructionThroughPrompts.md` for the current template catalog. If the user describes an assembly operation matching a known template (BearingCarriage, IdlerHalves, MotorHolder, RodAssembly, BeltThread), generate the YAML and run the script — do not write step JSON manually.

**Reference document for assembly authoring:** `AgentAssistant/ConstructionThroughPrompts.md`
**Model-agnostic vocabulary (for GPT-4o / Gemini):** `AgentAssistant/assembly_vocabulary.yaml`

## Machine Package Content (machine.json)

**CRITICAL**: In the Unity Editor, machine.json is loaded from the **authoring folder**, NOT StreamingAssets:

- **Authoring (editor reads this):** `Assets/_Project/Data/Packages/<packageId>/machine.json`
- **StreamingAssets (runtime/build only):** `Assets/StreamingAssets/MachinePackages/<packageId>/machine.json`

When editing machine.json content (steps, parts, tools, targets, etc.), **always edit the authoring copy** under `Assets/_Project/Data/Packages/`. The StreamingAssets copy is for builds only and is synced during the build process. See `MachinePackageLoader.BuildMachineJsonPath()` for the resolution logic.

**Do NOT manually copy or sync to StreamingAssets.** Only edit the authoring copy. The build pipeline handles the rest.

## machine.json Authoring Conventions

**Float precision**: Position, rotation, and scale values are stored at 4 decimal places maximum (0.1 mm / 0.01° — sufficient for assembly training). The `PackageJsonUtils.WritePreviewConfig()` and `TryInjectBlock()` save paths enforce this automatically. After any bulk hand-edit, run `OSE / Package Builder / Normalize Float Precision (All Packages)` to normalize the entire file.

**Hint reuse**: Define one `HintDefinition` per *type of action*, not one per step instance. Hints that reference a specific `targetId` or `partId` for runtime highlighting must stay unique. Hints with no such context (generic reminders, safety notes) should be defined once and referenced by multiple steps via `hintIds`. Duplicate hint definitions with identical messages and no unique `targetId`/`partId` are a smell — consolidate them.

## Package Health — Maintenance Script

**`tools/package_health.py`** — run this after any bulk edit to catch all issues before Unity validation:

```bash
python tools/package_health.py d3d_v18_10                  # audit only
python tools/package_health.py d3d_v18_10 --fix-seqindex   # audit + collapse seqIndex gaps
python tools/package_health.py                              # audit all packages
```

Checks: seqIndex continuity, orphan parts (all 5 reference locations), orphan targets, broken part refs, broken target refs.

## Agent Rules for machine.json Edits

After ANY machine.json edit, the agent MUST follow these rules:

**No orphans:** Every new part, target, or hint must be referenced by at least one step. Do not leave dead definitions. If a step is removed, remove any parts, targets, and hints that become unreferenced.

**Part references exist in 5 places — check ALL when detecting orphans or broken refs:**
1. `steps[].requiredPartIds` / `optionalPartIds` / `targetPartIds`
2. `subassemblies[].partIds`
3. `targets[].associatedPartId`
4. `previewConfig.constrainedSubassemblyFitPlacements[].drivenPartIds`
5. `parts[]` definition itself

**seqIndex is globally contiguous 1..N** across ALL merged assembly files. After any step removal or insertion:
- Run `python tools/package_health.py <id> --fix-seqindex` to collapse gaps
- Never manually assign seqIndices into a range already occupied by another assembly's steps — check the global map first (`python tools/package_health.py <id>` shows the full range)

**Removing an assembly subassembly — full checklist:**
1. Remove from `assembly.subassemblyIds`
2. Remove step IDs from `assembly.stepIds`
3. Remove the step definitions from `steps[]`
4. Remove the `subassemblies[]` block definition
5. Remove targets whose `associatedPartId` only served those steps AND that are not referenced by any remaining step
6. Do NOT remove parts — they may be in another subassembly's `partIds` or a target's `associatedPartId`
7. Run `python tools/package_health.py <id> --fix-seqindex` to close the seqIndex gap

**No empty arrays or nulls:** Omit optional fields (`requiredPartIds`, `hintIds`, `targetIds`, `optionalPartIds`, `relevantToolIds`, `effectTriggerIds`, `validationRuleIds`, `requiredToolActions`, `eventTags`) entirely rather than writing `[]` or `null`. The runtime defaults are safe.

**No template duplication:** If a part uses `templateId`, do not repeat `category`, `material`, or `assetRef` when the value matches the template. The runtime inherits these from the template.

**Use `family`, not `completionType`:** Always set `family` on new steps. Do not add `completionType` — it is a legacy fallback only. Omit `profile` rather than writing `""` (absence = family default).

**Reuse hints:** Before creating a new hint, search existing hints for one with the same message or intent. Only create a unique hint when it carries a specific `targetId` or `partId` for runtime highlighting.

**Payload-first for new steps:** New steps should use `guidance`, `validation`, `feedback`, `reinforcement`, and `difficulty` payloads rather than flat fields (`instructionText`, `hintIds`, `validationRuleIds`, etc.). Flat fields are legacy — never add them to new content.

**Validate after edits:** After bulk edits, tell the user to run `OSE > Validate All Packages` in Unity. The pre-play validator blocks Play mode on errors automatically.

**Staging positions go in `parts[].stagingPose`, not in `previewConfig`:** The agent-authored source of truth for where a part floats before the trainee places it is `parts[].stagingPose` (position, rotation, scale, color). `previewConfig.partPlacements[].startPosition` is derived — baked by `MachinePackageNormalizer.BakeStagingPoses()` at load time. Never edit `startPosition` in previewConfig directly.

## Machine Package File Architecture (Phase 2)

When the per-assembly file split is in place, each machine package has this layout:

```
Assets/_Project/Data/Packages/{packageId}/
  machine.json          ← metadata only: name, version, entryAssemblyIds, schemaVersion
  shared.json           ← tools, partTemplates, global hints (no targetId/partId), global validationRules
  assemblies/           ← one JSON per assembly, self-contained
    frame.json
    y_left.json
    x_axis.json
    ...
  preview_config.json   ← TTAW/Blender-generated ONLY — agents never open this
  assets/
    parts/*.glb
    tools/*.glb
```

**Agent routing rules:**

| What you need to do | File to read | File to write |
|---------------------|-------------|--------------|
| Edit steps for an assembly | `assemblies/{assemblyId}.json` | Same file |
| Define a new part (or set `stagingPose`) | `assemblies/{firstUseAssemblyId}.json` | Same file |
| Define a tool or partTemplate | `shared.json` | `shared.json` |
| Edit a global hint (no `targetId`/`partId`) | `shared.json` | `shared.json` |
| Read machine name/version | `machine.json` | `machine.json` |
| Read assembled/step poses | `preview_config.json` (read-only) | **Never write** |

**Never re-define a part in a second assembly file.** A part is defined once in the assembly where it is first physically assembled; later assemblies reference it by `id` only. The loader merges all files — all IDs are globally visible.

**`preview_config.json` is 100% TTAW/Blender-generated.** If a part's start position needs changing, edit `parts[].stagingPose` in the assembly file. The normalizer bakes it into previewConfig at load time.

**Backward-compatible:** If `assemblies/` is absent, the loader falls back to reading a single monolithic `machine.json`. No other system needs to change.

## Assembly Procedure Authoring — Grammar and Templates

### The Assembly Grammar

Every step maps to a small vocabulary. Use these exact terms — they have direct JSON equivalents.

**Verbs → step family:**
| Action verb | Step family | Notes |
|-------------|-------------|-------|
| place, seat, insert, lay, set | Place | Physical placement of part into/onto target |
| align, confirm, check, verify, test, inspect | Confirm | No movement — observation or measurement |
| tighten, drill, cut, weld, press, route | Use | Requires a tool; add `requiredToolActions` |
| gather, stage, lay out | Confirm | Pre-work layout step |

**Spatial modifiers (encode in `instructionText`):**
- `flanges-outward` / `flanges-facing-each-other` — bearing orientation
- `flush with [surface]` — rod end position
- `small ribbed hole beside large smooth hole` — carriage belt-hole alignment
- `cable plug facing [direction]` — motor orientation
- `toothed-side inward` — belt routing direction
- `finger-tight` — no tool, hand thread only (Place, no requiredToolActions)
- `lowest torque setting` — Use + tool_power_drill, profile: "Torque"

**Test keywords → always produce guidance + validation + feedback blocks:**
- `shake-test` → Confirm, guidance.whyItMattersText (compress hand, 2-3 sec, no rattle)
- `rod-slide-test` → Confirm, guidance.whyItMattersText (slight resistance, vertical rod)
- `pop-check` (pulley) → Confirm, audio cue on set screw
- `dangle-test` → Confirm, motor hangs freely from belt
- `travel-test` → Confirm, belt moves full rod length smoothly

**Fastener patterns:**
- `M6x18 top, M6x30 bottom` → always this way for carriage/idler builds
- `M3x25 motor screws` → 4×, ¾ tight before M6 bolts
- Tighten order: motor screws first, then M6 bolts, not reversed

### Assembly Procedure Templates

These are the canonical step sequences for recurring operation types. Reference by name when authoring:

**TEMPLATE: BearingCarriage(halfA, halfB, bearings[4], bolts_top[2], bolts_bot[2], nuts[4])**
```
seq N+0  Place    → seat 4 bearings in halfA, flanges-outward
seq N+1  Place    → close halfB onto halfA, align belt holes (small-ribbed ∥ large-smooth)
seq N+2  Confirm  → shake-test halfA+halfB hand-compressed, no rattle     ← halves MUST be closed first
seq N+3  Confirm  → rod-slide-test, slight resistance
seq N+4  Place    → insert bolts (top: short M6x18, bottom: long M6x30), thread nuts finger-tight
seq N+5  Use      → tighten all 4 bolts, power-drill lowest torque
```

**TEMPLATE: IdlerHalves(half_a, half_b, bearings[2], bolt_inner, bolt_frame_mount)**
```
seq N+0  Place    → insert bolt_inner through half_a from outside
seq N+1  Place    → stack 2 flanged bearings on bolt, flanges-outward
seq N+2  Confirm  → align rod holes on half_b before closing
seq N+3  Place    → press half_b against half_a, install bolt_frame_mount in belt-side hole (loose)
```

**TEMPLATE: MotorHolder(motor, pulley, belt, half_nuts[3], belt_bolt, motor_screws[4], close_bolts[3])**
```
seq N+0  Place    → seat pulley on shaft with spacer, tighten first set screw
seq N+1  Use      → remove spacer, tighten second set screw until pop-check (audible click)
seq N+2  Place    → prep half-piece: 1 long bolt bottom-left poking up, 3 nuts
seq N+3  Place    → lay belt in channel, toothed-side inward
seq N+4  Place    → close halves gently, insert motor cable plug facing bottom-right
seq N+5  Confirm  → dangle-test: motor hangs from belt freely
seq N+6  Use      → tighten 4× motor screws ¾ tight, then 3× M6 bolts
```

**TEMPLATE: RodAssembly(rod_a, rod_b, idler, carriage, motor_holder)**
```
seq N+0  Place    → insert both rods into completed idler (flush with idler bottom)
seq N+1  Use      → tighten idler's two shorter bolts (grip rods)
seq N+2  Place    → slide carriage onto rods (long bolt ends closer to idler)
seq N+3  Place    → push motor_holder onto rods (motor facing carriage bolt heads)
seq N+4  Confirm  → verify rods still flush with idler bottom; bolt heads same side
```

**TEMPLATE: BeltThread(belt, idler, peg_1, peg_2)**
```
seq N+0  Place    → insert belt end through large smooth belt hole in carriage
seq N+1  Place    → route around idler bearing (slight curve helps)
seq N+2  Place    → pull through small ribbed belt hole
seq N+3  Confirm  → orient peg foot away from axis center
seq N+4  Use      → insert belt end ~¾ inch into peg_1, press into ribbed hole
seq N+5  Place    → place peg_2 loosely (tensioned at frame mounting)
seq N+6  Confirm  → travel-test: belt moves full rod length, no rub
```

### Step Authoring from Description — Translator Input Format

When describing an assembly operation for an agent to convert to JSON steps, use this format:

```
ASSEMBLY: [assembly_id]
SUBASSEMBLY: [subassembly_id]
TEMPLATE: [template name OR "custom"]
PARTS:
  [role]: [partId]   # e.g. half_a: y_left_carriage_half_a
HARDWARE:
  [role]: [partId]   # e.g. bolt_top_1: y_left_m6x18_a
TOOL: [toolId] at [setting]  # e.g. tool_power_drill at lowest torque
ORIENTATION_CUE: "[text]"    # spatial alignment cue for instructionText
START_SEQ: [integer]
LABEL: "[milestone message]"
```

The agent expands this into the full step JSON array using the template above. Each field maps
deterministically to JSON fields — no spatial guessing required.

### Spatial Reasoning Rules

These physical rules are absolute — enforce them in every step:

1. **Bearings always seat before closing halves.** A close-halves step cannot precede a place-bearings step.
2. **Bolt holes must match bolt diameter.** M6 bolt → M6 hole. Never use M3 hardware in M6 holes.
3. **Rod-flush constraint.** After rod threading, always verify rods flush at idler bottom (rod-slide into idler from top, not bottom).
4. **Belt-hole alignment is required before bolting.** If small-ribbed ≠ beside large-smooth, the peg holes mismatch. This must be in the close-halves instructionText, not assumed.
5. **Tighten sequence.** Motor screws before M6 bolts on motor holder. Carriage bolts before rod assembly. Idler frame-mount bolt stays loose until frame mounting.
6. **Shake test must pass before rod-slide test.** If bearings rattle, rod test is meaningless.
7. **Staging positions must be unique.** No two parts can share the same `stagingPose.position`. Offset at minimum 0.05m between adjacent parts.

### MCP Server — Planned Capabilities

A planned MCP server will expose the assembly procedure schema so agents can query before writing:

```
GET /parts/{partId}         → metadata, assetRef, category, fastenerSpec, boundingBox
GET /templates              → procedure templates with parameter signatures
GET /schema/step-families   → required fields, behavioral contract per family
GET /schema/animation-cues  → cue types, parameters, when appropriate
GET /targets/{targetId}     → position, acceptedPartIds[], toolActionType, orientation
POST /validate/step         → validate step JSON before writing, returns field errors
POST /generate/steps        → template name + partId map → step JSON array
```

Until the MCP server is live, agents must read existing assembly files for examples and verify
part IDs exist in the merged package before referencing them in steps.

### Step Quality Checklist

Before finalizing any authored step block, verify:
- [ ] Every `requiredPartId` exists in the merged part catalog
- [ ] Every `targetId` is defined in `targets[]`
- [ ] Confirm steps have both `validation.successCriteria` AND `validation.failureCriteria`
- [ ] Confirm steps have both `feedback.successMessage` AND `feedback.failureMessage`
- [ ] Use steps have `requiredToolActions` or at minimum `relevantToolIds`
- [ ] Place steps that close a half-piece reference the second half in `requiredPartIds`
- [ ] Tighten steps include `profile: "Torque"` and a `workingOrientation` if the part must be flipped
- [ ] Layout steps reference all parts the trainee must gather (`requiredPartIds`)
- [ ] No step has `sequenceIndex` that duplicates another within the same assembly

## Agent Rules for Bug Fixes

When fixing any bug, the agent MUST also prevent that class of bug from recurring:

**Eliminate the root cause, not just the symptom.** If the same mistake could be made in another call site, file, or future feature, fix the architecture so it can't happen. Examples:
- If multiple callers need the same derived data → bake it at load time in `MachinePackageNormalizer` so every caller gets the right answer automatically.
- If cleanup is missed during a lifecycle transition → add a centralized cleanup hook (e.g. `playModeStateChanged`) rather than patching individual call sites.
- If a visual state isn't applied because a code path skips a step → ensure the authoritative method always runs, not just in some paths.

**Never scatter the same derivation logic across callers.** If you find yourself writing the same resolution/fallback in multiple places, centralize it into a single method or normalizer pass. Then update all callers to use the single source of truth.

**Add structural prevention, not just a point fix.** After fixing a bug, ask: "Could a developer adding a new feature hit this same issue?" If yes, make the fix structural — change the API, add a normalizer pass, or update a base method so the new feature would work correctly by default.

**Learn from every error.** After fixing a bug, save the lesson to memory so the same mistake is never repeated in future conversations. Record what went wrong, why it was hard to find, and what pattern to follow going forward. Each bug is a signal — use it to permanently improve how you approach this codebase.
