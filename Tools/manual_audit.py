"""
manual_audit.py — Diff a manual index against the authored package
to produce a "what's left" gap report.

Usage:
    python tools/manual_audit.py <index.json> <packageId>

For each rollup (template × axes), it:
  1. Pulls all "Step X.Y: Title" labels from the manual pages in the rollup
  2. Finds the corresponding assembly file(s) for those axes
  3. Lists authored steps whose name fuzzy-matches a manual step (covered)
     and authored / manual steps with no match (gaps both directions)
  4. Flags assemblies with no steps at all (skeletons)

Output: AgentAssistant/manual_audits/<source>_audit.md
"""

from __future__ import annotations
import json
import os
import re
import sys
import glob
import difflib
from collections import defaultdict

ASSEMBLY_BY_AXIS = {
    "y_left":  "assembly_d3d_y_left_bench",
    "y_right": "assembly_d3d_y_right_bench",
    "z_front": "assembly_d3d_z_front_bench",
    "z_back":  "assembly_d3d_z_back_bench",
    "x_axis":  "assembly_d3d_x_axis_bench",
}

# Keyword fingerprint per template: any authored step whose lowercased name
# contains any of the patterns is considered "in the template's scope."
TEMPLATE_SCOPE = {
    "BearingCarriage": ["carriage", "bearing", "lm8uu", "shake test", "rod slide"],
    "IdlerHalves":     ["idler"],
    "MotorHolder":     ["motor", "pulley", "motor holder", "set screw", "dangle"],
    "RodAssembly":     ["rod into", "insert rod", "rods flush", "rods into idler"],
    "BeltThread":      ["belt", "peg"],
}

# Words to ignore when matching titles
STOPWORDS = {"the", "a", "an", "with", "into", "onto", "and", "of", "for",
             "to", "on", "in", "at", "as", "by", "is", "be", "this", "that"}


def tokenize(s: str) -> set[str]:
    return {w for w in re.findall(r"[a-z0-9]+", s.lower()) if w not in STOPWORDS}


def fuzzy_overlap(a: str, b: str) -> float:
    ta, tb = tokenize(a), tokenize(b)
    if not ta or not tb:
        return 0.0
    return len(ta & tb) / max(len(ta), len(tb))


def assembly_steps(asm_path: str) -> list[dict]:
    if not os.path.isfile(asm_path):
        return []
    with open(asm_path, encoding="utf-8") as f:
        data = json.load(f)
    out = []
    for s in data.get("steps", []):
        out.append({
            "seq": s.get("sequenceIndex"),
            "name": s.get("name", ""),
            "family": s.get("family", ""),
            "parts": s.get("requiredPartIds", []),
            "id": s.get("id", ""),
        })
    return out


def in_scope(step_name: str, template: str) -> bool:
    needles = TEMPLATE_SCOPE.get(template, [])
    n = step_name.lower()
    return any(k in n for k in needles)


def manual_steps_for_rollup(idx: dict, rollup: dict) -> list[dict]:
    out = []
    for p in idx["pages"]:
        if p["page"] in rollup["pages"]:
            for s in p.get("steps", []):
                out.append({"page": p["page"], "num": s["num"], "title": s["title"]})
    # de-dupe by (num, title)
    seen = set()
    uniq = []
    for s in out:
        key = (s["num"], s["title"].strip().lower())
        if key in seen:
            continue
        seen.add(key)
        uniq.append(s)
    return uniq


def audit(index_path: str, package_id: str) -> str:
    with open(index_path, encoding="utf-8") as f:
        idx = json.load(f)
    pkg_dir = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "Assets", "_Project", "Data", "Packages", package_id, "assemblies"
    )
    asm_files = {os.path.basename(p).replace(".json", ""): p
                 for p in glob.glob(os.path.join(pkg_dir, "*.json"))}

    lines = []
    lines.append(f"# Manual Audit — {idx['source']}\n")
    lines.append(f"Package: `{package_id}` | Pages: {idx['page_count']} | "
                 f"Rollups: {len(idx['rollups'])}\n")

    summary = []  # (template, axes, manual_count, authored_count, coverage_pct)
    for r in idx["rollups"]:
        tpl = r["template"]
        axes = r["axes"]
        if tpl.startswith("_"):
            continue
        man_steps = manual_steps_for_rollup(idx, r)
        page_range = (f"{min(r['pages'])}–{max(r['pages'])}"
                      if len(r['pages']) > 1 else str(r['pages'][0]))

        lines.append("\n---\n")
        lines.append(f"## {tpl} — axes: {', '.join(axes) or '(none)'} "
                     f"(manual pp. {page_range})\n")
        lines.append(f"**Manual sub-steps ({len(man_steps)}):**")
        for s in man_steps:
            lines.append(f"- p.{s['page']} Step {s['num']}: {s['title']}")
        lines.append("")

        # For each axis, find authored steps in scope and diff
        if not axes:
            lines.append("_No axis instances detected — informational page._\n")
            continue

        for axis in axes:
            asm_id = ASSEMBLY_BY_AXIS.get(axis)
            if not asm_id:
                lines.append(f"### {axis}: no assembly mapping\n")
                continue
            path = asm_files.get(asm_id)
            if not path:
                lines.append(f"### {axis}: assembly file `{asm_id}.json` MISSING\n")
                continue
            steps = assembly_steps(path)
            scoped = [s for s in steps if in_scope(s["name"], tpl)]

            if not scoped:
                lines.append(f"### {axis} ← `{asm_id}.json`\n")
                lines.append(f"**❌ NO authored steps in {tpl} scope.** "
                             f"Assembly has {len(steps)} total steps.\n")
                summary.append((tpl, axis, len(man_steps), 0, 0.0))
                continue

            # Greedy match each manual step to best authored step
            covered_manual = set()
            covered_authored = set()
            matches = []
            for mi, m in enumerate(man_steps):
                best = (None, 0.0)
                for ai, a in enumerate(scoped):
                    if ai in covered_authored:
                        continue
                    score = fuzzy_overlap(m["title"], a["name"])
                    if score > best[1]:
                        best = (ai, score)
                if best[0] is not None and best[1] >= 0.25:
                    matches.append((mi, best[0], best[1]))
                    covered_manual.add(mi)
                    covered_authored.add(best[0])

            cov_pct = 100.0 * len(covered_manual) / max(len(man_steps), 1)
            summary.append((tpl, axis, len(man_steps), len(scoped), cov_pct))

            lines.append(f"### {axis} ← `{asm_id}.json`")
            lines.append(f"Authored in scope: {len(scoped)} | "
                         f"Coverage of manual: {len(covered_manual)}/"
                         f"{len(man_steps)} ({cov_pct:.0f}%)\n")

            if matches:
                lines.append("**Matched (manual → authored):**")
                for mi, ai, sc in matches:
                    m = man_steps[mi]; a = scoped[ai]
                    lines.append(f"- Step {m['num']} `{m['title']}` "
                                 f"→ seq {a['seq']} `{a['name']}` ({sc:.0%})")
                lines.append("")

            unmatched_manual = [m for i, m in enumerate(man_steps)
                                if i not in covered_manual]
            if unmatched_manual:
                lines.append(f"**⚠ Manual steps NOT covered ({len(unmatched_manual)}):**")
                for m in unmatched_manual:
                    lines.append(f"- p.{m['page']} Step {m['num']}: {m['title']}")
                lines.append("")

            unmatched_authored = [a for i, a in enumerate(scoped)
                                  if i not in covered_authored]
            if unmatched_authored:
                lines.append(f"**ℹ Authored steps with no manual match "
                             f"({len(unmatched_authored)}):**")
                for a in unmatched_authored:
                    parts = f", parts={a['parts']}" if a['parts'] else ""
                    lines.append(f"- seq {a['seq']} `{a['name']}` "
                                 f"({a['family']}{parts})")
                lines.append("")

    # Summary table at top
    lines.insert(2, "## Coverage Summary\n")
    lines.insert(3, "| Template | Axis | Manual steps | Authored in scope | Coverage |")
    lines.insert(4, "|---|---|---:|---:|---:|")
    for tpl, axis, m, a, pct in summary:
        bar = "🔴" if pct < 50 else ("🟡" if pct < 90 else "🟢")
        lines.insert(5, f"| {tpl} | {axis} | {m} | {a} | {bar} {pct:.0f}% |")
    lines.insert(5 + len(summary), "")

    return "\n".join(lines)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    index_path, package_id = sys.argv[1], sys.argv[2]

    out_dir = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "AgentAssistant", "manual_audits"
    )
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.splitext(os.path.basename(index_path))[0].replace(".index", "")
    out_path = os.path.join(out_dir, f"{base}_audit.md")

    md = audit(index_path, package_id)
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(md)
    print(f"Wrote {out_path}")
    # Print summary table to stdout
    for line in md.split("\n"):
        if line.startswith("|") or line.startswith("## Coverage"):
            print(line)


if __name__ == "__main__":
    main()
