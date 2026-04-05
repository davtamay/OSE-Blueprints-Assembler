---
name: ose-procedure-audit
description: Audits the active machine package against OSE reference documentation and the online OSE wiki. Reads machine.json to recap current progress, validates each step for completeness (target placements, parts, tool actions), cross-references ose-xr-foundation docs, fetches latest OSE wiki content, and produces a prioritized authoring action plan with a clear "where are we / what's next" summary.
metadata:
  author: davta
  version: "1.0.0"
  tags: audit, ose, procedure, d3d, machine-package, authoring
---

# OSE Procedure Audit

You are an OSE Blueprint authoring auditor. Your job is to:
1. Determine the current state of the active machine package
2. Validate each step against the real-world OSE build procedure
3. Identify authoring gaps, broken steps, and missing data
4. Produce a prioritized action plan for what to author next

---

## Step 1 — Load Internal Sources

Read ALL of the following. Do not skip any.

**Machine package (source of truth for current authored state):**
- `Assets/_Project/Data/Packages/d3d_v18_10/machine.json`

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

From machine.json, extract every step and for each record:

| seq | id | name | profile | parts | targets | has_placements | issues |
|-----|----|------|---------|-------|---------|----------------|--------|

- **parts**: count of `requiredPartIds`
- **targets**: count of `targetIds`
- **has_placements**: for each target in `targetIds`, check whether a matching entry exists in `previewConfig.targetPlacements`. Mark ✓ if all placed, ✗ if any missing, — if no targets.
- **issues**: any of the following flags:
  - `NO_PLACEMENT` — target in targetIds has no entry in previewConfig.targetPlacements
  - `NO_TOOL_ACTION` — step has completionType=tool_action but requiredToolActions is empty
  - `MISMATCHED_TARGET` — requiredToolAction references a targetId not in targetIds
  - `MISSING_PART_PLACEMENT` — requiredPartId has no entry in previewConfig.partPlacements
  - `EMPTY_INSTRUCTION` — instructionText is blank or a placeholder
  - `NO_HINTS` — step has hintIds empty and no inline hints

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
**Steps audited:** [count]
**Steps with issues:** [count]
**Targets without placements:** [count]

---

### Where We Are

[2-3 sentence summary of the current authoring state: what stages are covered,
what is playable end-to-end, what is blocked.]

**Last fully playable step:** [seq] — [name]
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
- **Action:** what to do (specific — e.g. "author target placements for target_cube_tack_front_left_edge and target_cube_tack_front_right_edge in Tool Target Authoring window")
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

- Read machine.json fully — do not sample. Every step and every target must be checked.
- Cross-reference target IDs between `steps[].targetIds` and `previewConfig.targetPlacements[].targetId` explicitly. Do not assume a placement exists.
- Cross-reference part IDs between `steps[].requiredPartIds` and `previewConfig.partPlacements[].partId` explicitly.
- If the OSE wiki is unavailable, still complete Steps 3–5 using internal sources and note the gap.
- Do not invent issues. Only flag things that are actually broken or missing in the data.
- The "Last fully playable step" is the highest seq where all required targets have placements, all parts have placements, and all tool actions have valid targets.
- Be specific in action items — give target IDs, step IDs, and tool names, not vague advice.
