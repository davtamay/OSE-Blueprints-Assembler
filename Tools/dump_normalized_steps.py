"""
dump_normalized_steps.py — Predict the runtime-normalized taskOrder for each
step and flag suspicious cases. Static-analysis equivalent of what
MachinePackageNormalizer does at load time.

Usage:
    python tools/dump_normalized_steps.py d3d_v18_10
    python tools/dump_normalized_steps.py d3d_v18_10 --only-issues

Mirrors these C# normalizer passes (Assets/_Project/Scripts/Content/Loading/
MachinePackageNormalizer.cs):
  - EnsureConfirmActionForConfirmSteps: Confirm-family steps get a
    confirm_action entry if none present.
  - NormalizeTaskOrderToolActionKinds: target-kind entries with a backing
    toolAction become toolAction kind. (We only flag, don't rewrite.)
  - EnsureTaskOrderCoversRequirements: every requiredPartId and
    requiredToolAction must have a covering taskOrder entry.
  - MarkVisualOnlyTaskOrderEntriesOptional: visualPartIds → optional.
    (We just note them.)

We DON'T port the full C# (no DropEmptyTaskOrderTransformPayloads, etc.) —
this is a fast lint-style auditor for authoring time.

What it flags:
  ⚠ Use family with no taskOrder + no relevantToolIds — likely deadlock-prone
  ⚠ Confirm family + no confirm_action authored — relies on normalizer
  ⚠ Place family with requiredPartIds not in taskOrder — runtime adds
    silently, but author should know
  ⚠ kind="target" with toolAction backing — normalizer rewrites to toolAction

Output: AgentAssistant/manual_audits/<package>_normalized_steps.md
"""

from __future__ import annotations
import json
import os
import sys
import glob
from collections import Counter

PKG_ROOT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages"
)


def task_covers_part(task_order, pid):
    return any(e.get("kind") in ("part", "preexistingPart")
               and e.get("id") == pid for e in task_order or [])


def task_covers_toolaction(task_order, taid):
    return any(e.get("kind") in ("toolAction", "target")
               and e.get("id") == taid for e in task_order or [])


def task_has_confirm_action(task_order):
    return any(e.get("kind") == "confirm_action"
               for e in task_order or [])


def predict_normalized(step):
    """Return (predicted_task_entries, flags[])."""
    family = step.get("family", "")
    profile = step.get("profile", "")
    authored = step.get("taskOrder", []) or []
    required_parts = step.get("requiredPartIds", []) or []
    required_tas = step.get("requiredToolActions", []) or []
    relevant_tools = step.get("relevantToolIds", []) or []
    flags = []

    predicted = list(authored)

    # Pass 1: EnsureConfirmActionForConfirmSteps
    if family == "Confirm" and not task_has_confirm_action(predicted):
        predicted.append({"kind": "confirm_action", "id": "confirm",
                          "_added_by": "normalizer"})
        if not authored:
            flags.append("Confirm step had no taskOrder; normalizer adds confirm_action")

    # Pass 2: EnsureTaskOrderCoversRequirements (parts)
    for pid in required_parts:
        if not task_covers_part(predicted, pid):
            predicted.append({"kind": "part", "id": pid,
                              "_added_by": "normalizer"})
            flags.append(f"requiredPart '{pid}' not in taskOrder; normalizer adds")

    # Pass 3: EnsureTaskOrderCoversRequirements (tool actions)
    for ta in required_tas:
        taid = ta.get("id") if isinstance(ta, dict) else str(ta)
        if not task_covers_toolaction(predicted, taid):
            predicted.append({"kind": "toolAction", "id": taid,
                              "_added_by": "normalizer"})
            flags.append(f"requiredToolAction '{taid}' not in taskOrder; normalizer adds")

    # Pass 4 (manual lint): kind='target' with potential toolAction backing
    for e in authored:
        if e.get("kind") == "target":
            flags.append(f"taskOrder kind='target' on '{e.get('id')}' — "
                         f"normalizer may rewrite to 'toolAction'")

    # Lint: Use family with NOTHING actionable
    if family == "Use" and not predicted and not relevant_tools:
        flags.append("Use family with empty taskOrder AND no relevantToolIds — "
                     "possible cursor stall")

    # Lint: Use family with relevant tools but no toolAction in taskOrder
    if (family == "Use" and relevant_tools and
            not any(e.get("kind") in ("toolAction", "target") for e in predicted)):
        flags.append(f"Use family with relevantToolIds={relevant_tools} but "
                     f"no toolAction taskOrder; may stall unless requiredToolActions also set")

    # Lint: Place family with requiredPartIds but EMPTY authored taskOrder
    if family == "Place" and required_parts and not authored:
        flags.append(f"Place family with {len(required_parts)} part(s) but "
                     f"empty authored taskOrder; relies entirely on normalizer")

    return predicted, flags


def run(package_id: str, only_issues: bool = False):
    asm_dir = os.path.join(PKG_ROOT, package_id, "assemblies")
    all_steps = []
    for path in sorted(glob.glob(os.path.join(asm_dir, "*.json"))):
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        for s in data.get("steps", []):
            s["_assembly_file"] = os.path.basename(path)
            all_steps.append(s)

    all_steps.sort(key=lambda s: s.get("sequenceIndex", 0))

    out_lines = []
    out_lines.append(f"# Normalized Step Audit — {package_id}\n")
    out_lines.append(f"Total steps: {len(all_steps)}\n")

    counters = Counter()
    flagged_steps = []

    for s in all_steps:
        predicted, flags = predict_normalized(s)
        authored_count = len(s.get("taskOrder", []) or [])
        predicted_count = len(predicted)
        added = predicted_count - authored_count
        counters["total"] += 1
        if flags:
            counters["flagged"] += 1
            flagged_steps.append((s, predicted, flags))
        if added:
            counters["normalizer_added_entries"] += added

    # Summary
    out_lines.append("## Summary\n")
    out_lines.append(f"- Steps analyzed: {counters['total']}")
    out_lines.append(f"- Steps with lint flags: {counters['flagged']}")
    out_lines.append(f"- TaskOrder entries normalizer would add: "
                     f"{counters['normalizer_added_entries']}")

    # Family breakdown
    fam_authored = Counter()
    fam_predicted = Counter()
    for s in all_steps:
        f = s.get("family", "?")
        fam_authored[f] += len(s.get("taskOrder", []) or [])
        pred, _ = predict_normalized(s)
        fam_predicted[f] += len(pred)
    out_lines.append("\n### TaskOrder count: authored vs normalized (by family)\n")
    out_lines.append("| Family | Authored | After normalize | Δ |")
    out_lines.append("|---|---:|---:|---:|")
    for fam in sorted(set(list(fam_authored) + list(fam_predicted))):
        a = fam_authored[fam]; p = fam_predicted[fam]
        out_lines.append(f"| {fam} | {a} | {p} | +{p-a} |")

    # Flagged steps
    if flagged_steps:
        out_lines.append(f"\n## Flagged steps ({len(flagged_steps)})\n")
        for s, predicted, flags in flagged_steps:
            seq = s.get("sequenceIndex", "?")
            out_lines.append(f"\n### seq {seq}: `{s['id']}`")
            out_lines.append(f"- File: `{s['_assembly_file']}`")
            out_lines.append(f"- Family: `{s.get('family','?')}` | "
                             f"Profile: `{s.get('profile','-')}`")
            out_lines.append(f"- Name: {s.get('name','')}")
            out_lines.append(f"- Authored taskOrder: "
                             f"{len(s.get('taskOrder', []) or [])} entries")
            out_lines.append(f"- Predicted (post-normalize): "
                             f"{len(predicted)} entries")
            for f in flags:
                out_lines.append(f"  - ⚠ {f}")

    if not only_issues:
        # Per-assembly-file summary for context
        out_lines.append("\n## Per-assembly summary\n")
        by_file = Counter()
        flagged_by_file = Counter()
        for s in all_steps:
            by_file[s["_assembly_file"]] += 1
        for s, _, _ in flagged_steps:
            flagged_by_file[s["_assembly_file"]] += 1
        out_lines.append("| Assembly | Steps | Flagged |")
        out_lines.append("|---|---:|---:|")
        for f in sorted(by_file):
            out_lines.append(f"| `{f}` | {by_file[f]} | {flagged_by_file.get(f,0)} |")

    out_dir = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "AgentAssistant", "manual_audits"
    )
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, f"{package_id}_normalized_steps.md")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(out_lines))
    print(f"Wrote {out_path}")
    print(f"Steps: {counters['total']} | Flagged: {counters['flagged']} | "
          f"Normalizer would add {counters['normalizer_added_entries']} taskOrder entries")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    only_issues = "--only-issues" in sys.argv
    run(sys.argv[1], only_issues)
