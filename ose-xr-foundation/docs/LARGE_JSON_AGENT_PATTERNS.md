# Large JSON + AI Agent Patterns

## Why This Doc Exists

`machine.json` for D3D v18.10 is **~1 MB / ~34,000 lines** (of which ~52% is
TTAW/Blender-generated `previewConfig` data that agents never need to touch).
Early versions of `ose-procedure-audit` instructed the agent to read the entire
file, then mentally perform set-intersection across 313 steps × 150 parts to find
missing placements.

**This approach fails in two ways:**
1. The file consumes ~half the agent's context window, leaving little room for
   wiki content, foundation docs, and the report output.
2. LLMs are unreliable at exact set-membership checks over thousands of lines —
   they miss IDs, producing false-negative audits that silently hide broken steps.

The patterns below describe how we solved this and generalize to any large JSON
in this project.

---

## Core Principle

> **LLMs are excellent at qualitative synthesis. They are poor at exact
> set-intersection over large data sets.**

Split the work accordingly:
- **Script does:** counting, ID membership checks, boolean flags, cross-section joins
- **Agent does:** qualitative judgment, wiki cross-reference, priority ranking, wording

---

## Pattern 1 — Extract-then-Read (Primary Pattern)

**Problem:** The agent needs data scattered across a large JSON (e.g., steps in
section A, placements in section B) and must cross-reference them.

**Solution:** Run a lightweight script at the start of the skill that reads the
JSON once, does all mechanical checks, and writes a compact index file. The agent
reads the index, not the raw JSON.

**When to use:** Any time the agent needs to compare IDs across two or more
sections of a large JSON, or compute counts/flags that span the whole file.

**Structure:**
```
Step 0: cache check (grep one field, compare versions)
  → CACHE_VALID: skip extraction
  → CACHE_STALE: run extraction script
Step 1: read compact index (not raw JSON)
Steps N+: agent works from index only
```

**Cache invalidation key:** Use a version field (`packageVersion`, `schemaVersion`)
that is incremented whenever the source file changes. If no such field exists,
use a hash of the file's line count as a secondary check.

**Index file location:** Same directory as the source JSON, named `<source>-index.json`.
For `machine.json` → `audit-index.json`.

**Index file format:**
```json
{
  "generatedAt": "2026-04-03",
  "packageVersion": "0.6.0",
  "meta": {
    "stepCount": 94,
    "stepsWithIssues": 12,
    "lastCleanSeq": 23
  },
  "items": [
    { "id": "...", "issues": ["FLAG:detail"], ... }
  ]
}
```

**Index is a cache, not source of truth.** Never commit it as authoritative data.
It is regenerated from the source JSON whenever the version changes.

---

## Pattern 2 — Targeted Section Read (Fallback)

**Problem:** Script is unavailable (Python/Node missing), but the agent still
needs to audit the file.

**Solution:** Read only the line ranges containing the relevant sections, not the
full file.

**For machine.json specifically (d3d_v18_10, single-file layout):**
| Section | Approximate line range |
|---------|----------------------|
| `parts[]` | 2–1,400 |
| `steps[]` | 1,400–8,800 |
| `previewConfig.partPlacements[]` | 8,800–14,000 |
| `previewConfig.targetPlacements[]` | 14,000–34,000 |

> Note: Once the Phase 2 assembly-file split is applied, agents read `assemblies/{id}.json`
> (~2,500 lines) + `shared.json` (~1,000 lines) instead of the 34,000-line monolith.
> After Phase 2 is complete, Pattern 2 line-range reads of the full file are obsolete.

**Always note in the report** when using this fallback — set-intersection accuracy
is reduced because the agent may miss IDs when scanning manually across thousands of lines.

**When NOT to use:** Do not use this as the primary strategy. It still requires the
agent to do set-membership mentally. Use Pattern 1 whenever possible.

---

## Pattern 3 — Targeted Grep + Drill-Down (Supplement)

**Problem:** After reading the index, the agent needs the full text of one specific
field (e.g., `instructionText` for wiki cross-reference).

**Solution:** Grep for the item's ID, get the line number, read ±30 lines.

```bash
grep -n "\"id\": \"step_bottom_place_bars\"" \
  "Assets/_Project/Data/Packages/d3d_v18_10/machine.json"
# Returns: 2089:    "id": "step_bottom_place_bars",
# Then Read lines 2059–2149
```

**This is the only legitimate reason to touch the raw JSON during an audit.**
Everything else comes from the index.

---

## Pattern 4 — Script-First Flag Precomputation

**Problem:** A set of boolean checks must be run across every item in a large
collection (e.g., does every step have non-empty instructionText? does every
target have a placement?).

**Solution:** The extraction script precomputes all flags and stores them in
`issues[]` per item. The agent reads pre-computed flags — it never re-derives them.

**Flag format:** `"SNAKE_CASE_FLAG"` for binary flags, `"FLAG:id1,id2"` when the
flag needs to carry the specific offending IDs (so the agent can name them in
action items without re-reading the source).

**Example issue flags for machine.json steps:**
```
NO_PLACEMENT:target_cube_tack_front_left_edge,target_cube_tack_front_right_edge
MISSING_PART_PLACEMENT:d3d_frame_tube_bottom_front
MISMATCHED_TARGET:target_square_check_A
NO_TOOL_ACTION
EMPTY_INSTRUCTION
NO_HINTS
```

---

## Applying These Patterns to New Files

When a new large JSON needs to be processed by an agent skill, ask:

1. **What data does the agent need?** List the fields explicitly.
2. **Does the agent need to join across sections?** → Use Pattern 1.
3. **Are there boolean checks across all items?** → Use Pattern 4 (precompute flags).
4. **Does the agent need specific text from one or two items?** → Pattern 3 (grep + drill-down).
5. **Is the whole file needed qualitatively?** → The file is probably too large; consider splitting it.

---

## Implementation Reference

The canonical implementation of these patterns for this project is in:
- `.claude/skills/ose-procedure-audit/SKILL.md` — Step 0 (cache check + extraction script)
- `Assets/_Project/Data/Packages/d3d_v18_10/audit-index.json` — generated output (gitignored)

The extraction script in SKILL.md is the reference implementation. Copy and adapt
it when building audit or analysis skills for other machine packages.

---

## Pattern 5 — Assembly-File Routing (Phase 2 Architecture)

Once the per-assembly file split is in place (`assemblies/{id}.json` + `shared.json`),
agents should use this routing table instead of reading the monolithic file:

| Task | File(s) to read | File(s) to write |
|------|----------------|-----------------|
| Author or edit steps for an assembly | `assemblies/{assemblyId}.json` | Same file |
| Define a new part | `assemblies/{firstUseAssemblyId}.json` | Same file |
| Set staging position (`stagingPose`) | `assemblies/{assemblyId}.json` on the part | Same file |
| Define a tool or partTemplate | `shared.json` | `shared.json` |
| Edit global hints (no specific targetId/partId) | `shared.json` | `shared.json` |
| Cross-reference a part from another assembly | `assemblies/{definingAssembly}.json` (read only) | Do not re-define |
| Read machine metadata (name, version) | `machine.json` | `machine.json` |
| Read assembled/step poses (TTAW-generated) | `preview_config.json` | Never — TTAW-only |

**Agent routing rule:** Determine the assembly for a step or part using `assemblyId`.
Map `assemblyId` → `assemblies/{assemblyId}.json`. All IDs are globally unique across files;
the loader merges all files into one `MachinePackageDefinition` before normalization.

**Never read `preview_config.json` during content authoring tasks.** It contains only
TTAW/Blender-generated spatial data (assembled poses, step poses, spline paths). The agent
authoring portion (staging start positions) lives in `parts[].stagingPose` in the assembly file.

---

## What NOT to Do

| Anti-pattern | Why it fails |
|---|---|
| `"Read machine.json fully — do not sample"` | Consumes context window; forces LLM set-intersection |
| Reading both steps and previewConfig sections separately | Still tens of thousands of lines; same problem |
| Grepping for all `targetId` occurrences | Returns hundreds of matches across both sections; agent can't reliably separate them |
| Caching the full audit report as a file | Report includes wiki content which changes independently of the JSON |
| Using line-range reads as the primary strategy | Fragile if file is edited; line numbers shift |
| Reading `preview_config.json` to find staging positions | Stale data — normalizer bakes from `parts[].stagingPose` at load; edit the assembly file instead |
