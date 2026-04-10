---
name: ose-procedure-audit
description: Audits the active machine package against OSE reference documentation and the online OSE wiki. Reads machine.json to recap current progress, validates each step for completeness (target placements, parts, tool actions), cross-references ose-xr-foundation docs, fetches latest OSE wiki content, and produces a prioritized authoring action plan with a clear "where are we / what's next" summary.
metadata:
  author: davta
  version: "2.0.0"
  tags: audit, ose, procedure, d3d, machine-package, authoring
---

# OSE Procedure Audit

You are an OSE Blueprint authoring auditor. Your job is to:
1. Determine the current state of the active machine package
2. Validate each step against the real-world OSE build procedure
3. Identify authoring gaps, broken steps, and missing data
4. Produce a prioritized action plan for what to author next

> **Architecture note for agents:** `machine.json` is ~582 KB / 21,000 lines.
> Do NOT read it directly for audit purposes. Step 0 extracts all audit-relevant
> data into a compact `audit-index.json` (~54 KB). The agent reads the index.
> See `ose-xr-foundation/docs/LARGE_JSON_AGENT_PATTERNS.md` for the full rationale.

---

## Step 0 — Build or Validate the Audit Index

This step extracts all audit-relevant data from `machine.json` into `audit-index.json`.
Run this before reading anything else.

### Cache check (run first)

```bash
python3 -c "
import json
pkg = None
idx_pkg = None
try:
    with open('Assets/_Project/Data/Packages/d3d_v18_10/machine.json', encoding='utf-8') as f:
        pkg = json.load(f)['packageVersion']
except: pass
try:
    with open('Assets/_Project/Data/Packages/d3d_v18_10/audit-index.json', encoding='utf-8') as f:
        idx_pkg = json.load(f)['packageVersion']
except: pass
if pkg and pkg == idx_pkg:
    print('CACHE_VALID pkg=' + pkg)
else:
    print('CACHE_STALE machine=' + str(pkg) + ' index=' + str(idx_pkg))
"
```

- If output is `CACHE_VALID` → skip extraction, jump to Step 1.
- If output is `CACHE_STALE` → run the extraction script below.

### Extraction script

```bash
cd "Assets/_Project/Data/Packages/d3d_v18_10" && python3 -c "
import json, datetime
with open('machine.json', encoding='utf-8') as f:
    d = json.load(f)

tp = {t['targetId'] for t in d['previewConfig']['targetPlacements']}
pp = {p['partId'] for p in d['previewConfig']['partPlacements']}

def audit_step(s):
    tids = s.get('targetIds', [])
    pids = s.get('requiredPartIds', [])
    ta   = s.get('requiredToolActions', [])
    ta_tids = [a.get('targetId','') for a in ta]
    issues = []
    unplaced_t = [t for t in tids if t not in tp]
    unplaced_p = [p for p in pids if p not in pp]
    mismatched  = [t for t in ta_tids if t and t not in set(tids)]
    if s.get('completionType') == 'tool_action' and not ta:
        issues.append('NO_TOOL_ACTION')
    if unplaced_t:
        issues.append('NO_PLACEMENT:' + ','.join(unplaced_t))
    if unplaced_p:
        issues.append('MISSING_PART_PLACEMENT:' + ','.join(unplaced_p))
    if mismatched:
        issues.append('MISMATCHED_TARGET:' + ','.join(mismatched))
    if not (s.get('instructionText') or '').strip():
        issues.append('EMPTY_INSTRUCTION')
    if not s.get('hintIds'):
        issues.append('NO_HINTS')
    return {
        'seq':  s['sequenceIndex'],
        'id':   s['id'],
        'name': s['name'],
        'asm':  s.get('assemblyId', ''),
        'ctype': s.get('completionType', ''),
        'targets':  len(tids),
        'parts':    len(pids),
        'hints':    len(s.get('hintIds', [])),
        'hasInst':  bool((s.get('instructionText') or '').strip()),
        'toolActions': [
            {'id': a.get('id',''), 'toolId': a.get('toolId',''), 'targetId': a.get('targetId','')}
            for a in ta
        ],
        'issues': issues
    }

steps_out  = [audit_step(s) for s in d['steps']]
last_clean = max((s['seq'] for s in steps_out if not s['issues']), default=0)

out = {
    'generatedAt':    str(datetime.date.today()),
    'packageVersion': d['packageVersion'],
    'meta': {
        'stepCount':         len(d['steps']),
        'placedTargetCount': len(tp),
        'placedPartCount':   len(pp),
        'stepsWithIssues':   sum(1 for s in steps_out if s['issues']),
        'lastCleanSeq':      last_clean
    },
    'steps': steps_out
}
open('audit-index.json', 'w').write(json.dumps(out, indent=2))
print('audit-index.json written | pkg:', d['packageVersion'],
      '| steps:', len(steps_out),
      '| issues:', out['meta']['stepsWithIssues'],
      '| lastClean:', last_clean)
"
```

**What `audit-index.json` contains:**
- `packageVersion` — must match `machine.json` for cache to be valid
- `meta.lastCleanSeq` — highest seq where all checks pass (use for "Last fully playable step")
- `steps[]` — one entry per step with pre-computed fields:
  - `seq`, `id`, `name`, `asm`, `ctype`, `targets`, `parts`, `hints`, `hasInst`
  - `toolActions[]` — `{id, toolId, targetId}` per action
  - `issues[]` — zero or more flags (see flag reference below)

**Issue flag reference:**

| Flag | Meaning |
|------|---------|
| `NO_PLACEMENT:id1,id2` | Those targetIds have no entry in previewConfig.targetPlacements |
| `MISSING_PART_PLACEMENT:id` | That partId has no entry in previewConfig.partPlacements |
| `MISMATCHED_TARGET:id` | A requiredToolAction references a targetId not in this step's targetIds |
| `NO_TOOL_ACTION` | completionType=tool_action but requiredToolActions is empty |
| `EMPTY_INSTRUCTION` | instructionText is blank or missing |
| `NO_HINTS` | hintIds is empty |

**Fallback (if Python unavailable):**
Read machine.json lines 2072–6837 (steps section) and 10230–20997 (previewConfig section).
Note in the report that set-intersection accuracy is reduced and IDs may be missed.

---

## Step 1 — Load Internal Sources

Read ALL of the following. Do not skip any.

**Audit index (primary data source — do NOT read machine.json directly):**
- `Assets/_Project/Data/Packages/d3d_v18_10/audit-index.json`

**Foundation docs (project goals, progress, schema):**
- `ose-xr-foundation/docs/APP_CURRENT_PROGRESS_FOR_AGENT.md`
- `ose-xr-foundation/docs/MACHINE_CATALOG.md`
- `ose-xr-foundation/docs/IMPLEMENTATION_CHECKLIST.md`
- `ose-xr-foundation/docs/DATA_SCHEMA.md`
- `ose-xr-foundation/docs/CONTENT_MODEL.md`

---

## Step 2 — Fetch Online OSE References

Fetch the following OSE wiki pages for the machine being audited (D3D by default).
Extract the real-world build sequence, BOM, and any notes that affect what steps should exist.

- https://wiki.opensourceecology.org/wiki/D3D
- https://wiki.opensourceecology.org/wiki/3D_Printer_Manual
- https://wiki.opensourceecology.org/wiki/Frame_Construction_Set

If the wiki pages are unavailable, note this clearly and proceed with internal sources only.

---

## Step 3 — Build the Step Inventory

Read `audit-index.json`. The `steps[]` array already contains all fields needed for the health table. Issue flags are pre-computed — do not re-derive them.

Build the table directly from the index:

| seq | id | name | profile | parts | targets | has_placements | issues |
|-----|----|------|---------|-------|---------|----------------|--------|

- **has_placements**: ✓ if `issues` contains no `NO_PLACEMENT` flag, ✗ if it does, — if `targets == 0`
- **issues**: list the flag names from `issues[]`, stripping the `:id,id` suffix for the table (keep full IDs in the action plan)

Use `meta.lastCleanSeq` from the index for "Last fully playable step" — do not recompute it.

**Drill-down rule:** If you need the full `instructionText` of a specific step for wiki
cross-reference, grep for its `id` in machine.json and read the surrounding 60 lines.
This is the only legitimate reason to touch machine.json during an audit.

```bash
grep -n "\"id\": \"<step-id>\"" "Assets/_Project/Data/Packages/d3d_v18_10/machine.json"
# then Read that file at the returned line ± 30
```

---

## Step 4 — Cross-Reference with OSE Wiki

Compare the authored step sequence against the real-world build procedure from the wiki. For each real-world stage or action:

- Is it covered by at least one authored step? (✓ / partial / ✗)
- If partial or missing: what would need to be added?
- Are any authored steps that don't map to real-world actions (invented steps, wrong order)?

Focus on factual correctness: dimensions, materials, fasteners, welding sequence, QC checks.

---

## Step 5 — Produce the Audit Report

Output the report in this EXACT structure:

---

```
## OSE Procedure Audit — [package id]

**Date:** [today]
**Steps audited:** [meta.stepCount]
**Steps with issues:** [meta.stepsWithIssues]
**Targets without placements:** [count of unique NO_PLACEMENT IDs across all steps]

---

### Where We Are

[2-3 sentence summary of the current authoring state: what stages are covered,
what is playable end-to-end, what is blocked.]

**Last fully playable step:** [meta.lastCleanSeq] — [name]
**First broken/incomplete step:** [seq] — [name] — [reason]

---

### Step Health Table

| seq | name | targets | placements | issues |
|-----|------|---------|------------|--------|
[full table, one row per step]

---

### OSE Wiki Cross-Reference

| Real-World Stage | Covered in Package | Notes |
|------------------|--------------------|-------|
[table mapping real build stages to authored content]

**Factual discrepancies found:**
[list any steps that contradict the real-world procedure — wrong dimensions,
wrong material, wrong sequence, wrong tool]

---

### Priority Authoring Action Plan

Ranked by: blocking the playthrough first, then correctness, then completeness.

For each item:
- **Action:** what to do (specific — e.g. "author target placements for
  target_cube_tack_front_left_edge and target_cube_tack_front_right_edge
  in Tool Target Authoring window")
- **Step(s):** which seq numbers are affected
- **Effort:** XS / S / M / L
- **Unblocks:** what becomes playable once this is done

[numbered list, most critical first]

---

### What's Already Complete and Correct

[bullet list of stages/steps that are fully authored, placement-complete,
and correctly reflect the OSE source material — things to NOT change]

---

### Recommended Next Session Focus

[1-3 sentences. Given the action plan above, what is the single most valuable
thing to work on next to make the most forward progress on the procedure?]
```

---

## Rules

- Read `audit-index.json`, not `machine.json`. Issue flags are pre-computed by the
  extraction script in Step 0 — do not re-derive them from raw JSON.
- Every step in the index must appear in the health table — no skipping.
- Use `meta.lastCleanSeq` directly; do not recompute "last fully playable step."
- If `audit-index.json` is missing or its `packageVersion` doesn't match `machine.json`,
  run Step 0 before proceeding. Never audit from a stale index.
- The only reason to read `machine.json` directly is a targeted grep for a specific
  step's `instructionText` (Step 3 drill-down rule). Everything else comes from the index.
- Do not invent issues. Only flag things present in `issues[]` in the index.
- Be specific in action items — give target IDs, step IDs, and tool names from the index,
  not vague advice.
- If the OSE wiki is unavailable, still complete Steps 3–5 using internal sources and
  note the gap.
