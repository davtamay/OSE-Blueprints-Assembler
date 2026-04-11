"""
package_health.py — Machine Package Integrity Checker
Usage: python tools/package_health.py [packageId]
       python tools/package_health.py d3d_v18_10
       python tools/package_health.py d3d_v18_10 --fix-seqindex

Checks:
  1. seqIndex continuity: global steps sorted 1..N, no gaps, no duplicates
  2. Orphan parts: defined but not referenced in ANY of the 5 reference locations
  3. Orphan targets: defined but not referenced by any step
  4. Broken part references: referenced part ID that is not defined in parts[]
  5. Broken target references: step references a target ID not defined in targets[]
  6. Invalid part category: category not in the Unity validator's allowed list
  7. Part placed by multiple Place steps: same partId in requiredPartIds of >1 Place-family step

Reference locations for parts (all 5 must be checked):
  a. steps[].requiredPartIds / optionalPartIds
  b. subassemblies[].partIds
  c. targets[].associatedPartId
  d. previewConfig.constrainedSubassemblyFitPlacements[].drivenPartIds
  e. parts[] definition itself (the source of truth)

With --fix-seqindex: renumbers all steps globally (preserving order) to fill gaps.
"""

import json, os, sys
from collections import Counter, defaultdict

# Valid part categories — must match Unity MachineJsonPrePlayValidator
VALID_PART_CATEGORIES = {
    "plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom"
}

BASE_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "_Project", "Data", "Packages")


def load_package(package_dir):
    """Load and merge all assembly files into one flat dict."""
    parts = []
    targets = []
    steps = []
    subassemblies = []
    preview_driven = []

    asm_dir = os.path.join(package_dir, "assemblies")
    if not os.path.isdir(asm_dir):
        print("ERROR: no assemblies/ folder found")
        sys.exit(1)

    for fname in sorted(os.listdir(asm_dir)):
        if not fname.endswith(".json"):
            continue
        fpath = os.path.join(asm_dir, fname)
        with open(fpath, encoding="utf-8") as f:
            data = json.load(f)
        parts.extend(data.get("parts", []))
        targets.extend(data.get("targets", []))
        steps.extend(data.get("steps", []))
        subassemblies.extend(data.get("subassemblies", []))

    # preview_config drivenPartIds
    pc_path = os.path.join(package_dir, "preview_config.json")
    if os.path.exists(pc_path):
        with open(pc_path, encoding="utf-8") as f:
            pc = json.load(f)
        for placement in pc.get("previewConfig", {}).get("constrainedSubassemblyFitPlacements", []):
            preview_driven.extend(placement.get("drivenPartIds", []))
        # also top-level if not wrapped
        for placement in pc.get("constrainedSubassemblyFitPlacements", []):
            preview_driven.extend(placement.get("drivenPartIds", []))

    return parts, targets, steps, subassemblies, preview_driven


def collect_referenced_part_ids(steps, subassemblies, targets, preview_driven):
    """Collect all partIds referenced in any of the 5 reference locations."""
    refs = set()
    for s in steps:
        refs.update(s.get("requiredPartIds", []))
        refs.update(s.get("optionalPartIds", []))
        refs.update(s.get("targetPartIds", []))
    for sa in subassemblies:
        refs.update(sa.get("partIds", []))
    for t in targets:
        if t.get("associatedPartId"):
            refs.add(t["associatedPartId"])
    refs.update(preview_driven)
    return refs


def collect_referenced_target_ids(steps):
    refs = set()
    for s in steps:
        refs.update(s.get("targetIds", []))
        refs.update(s.get("guidance", {}).get("targetIds", []))
        refs.update(s.get("validation", {}).get("targetIds", []))
        for rta in s.get("requiredToolActions", []):
            if rta.get("targetId"):
                refs.add(rta["targetId"])
    return refs


def check_seqindex(steps):
    """Check for duplicates and gaps in global seqIndex."""
    seqs = sorted(s["sequenceIndex"] for s in steps)
    issues = []
    counts = Counter(seqs)
    dupes = {s: c for s, c in counts.items() if c > 1}
    if dupes:
        for seq, count in sorted(dupes.items()):
            step_ids = [s["id"] for s in steps if s["sequenceIndex"] == seq]
            issues.append(f"DUPLICATE seqIndex {seq} ({count}x): {step_ids}")
    expected = list(range(1, len(seqs) + 1))
    if seqs != expected:
        gaps = [i + 1 for i in range(len(seqs)) if seqs[i] != i + 1]
        if gaps:
            issues.append(f"GAPS at positions {gaps[:10]}{'...' if len(gaps) > 10 else ''} "
                          f"(run with --fix-seqindex to collapse)")
    return issues


def fix_seqindex(package_dir, steps_with_files):
    """Renumber all steps globally (preserving order) to fill gaps."""
    steps_with_files.sort(key=lambda x: x[0]["sequenceIndex"])
    new_seq_map = {s["id"]: i + 1 for i, (s, _) in enumerate(steps_with_files)}

    asm_dir = os.path.join(package_dir, "assemblies")
    for fname in sorted(os.listdir(asm_dir)):
        if not fname.endswith(".json"):
            continue
        fpath = os.path.join(asm_dir, fname)
        with open(fpath, encoding="utf-8") as f:
            data = json.load(f)
        changed = 0
        for s in data.get("steps", []):
            if s["id"] in new_seq_map and s["sequenceIndex"] != new_seq_map[s["id"]]:
                s["sequenceIndex"] = new_seq_map[s["id"]]
                changed += 1
        if changed:
            with open(fpath, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2)
            print(f"  Fixed {changed} seqIndices in {fname}")


def run(package_id, fix_seqindex_flag=False):
    package_dir = os.path.join(BASE_DIR, package_id)
    if not os.path.isdir(package_dir):
        print(f"ERROR: package not found: {package_dir}")
        sys.exit(1)

    parts, targets, steps, subassemblies, preview_driven = load_package(package_dir)

    all_part_ids = {p["id"] for p in parts}
    all_target_ids = {t["id"] for t in targets}
    referenced_parts = collect_referenced_part_ids(steps, subassemblies, targets, preview_driven)
    referenced_targets = collect_referenced_target_ids(steps)

    errors = []
    warnings = []

    # 1. seqIndex
    seq_issues = check_seqindex(steps)
    for issue in seq_issues:
        errors.append(f"seqIndex: {issue}")

    # 2. Orphan parts (defined but never referenced anywhere)
    orphan_parts = all_part_ids - referenced_parts
    for pid in sorted(orphan_parts):
        warnings.append(f"Orphan part: '{pid}' defined but not referenced in steps, subassemblies, targets, or previewConfig")

    # 3. Orphan targets
    orphan_targets = all_target_ids - referenced_targets
    for tid in sorted(orphan_targets):
        warnings.append(f"Orphan target: '{tid}' defined but not referenced by any step")

    # 4. Broken part references
    broken_parts = referenced_parts - all_part_ids
    for pid in sorted(broken_parts):
        errors.append(f"Broken part ref: '{pid}' is referenced but not defined in parts[]")

    # 5. Broken target references
    broken_targets = referenced_targets - all_target_ids
    for tid in sorted(broken_targets):
        errors.append(f"Broken target ref: '{tid}' is referenced but not defined in targets[]")

    # 6. Invalid part category
    for p in parts:
        cat = p.get("category", "")
        if cat and cat not in VALID_PART_CATEGORIES:
            errors.append(
                f"Invalid category '{cat}' on part '{p['id']}'. "
                f"Valid values: {', '.join(sorted(VALID_PART_CATEGORIES))}"
            )

    # 7. Part placed by multiple Place-family steps
    place_steps_by_part = defaultdict(list)
    for s in steps:
        if s.get("family") == "Place":
            for pid in s.get("requiredPartIds", []):
                place_steps_by_part[pid].append(s["id"])
    for pid, step_ids in sorted(place_steps_by_part.items()):
        if len(step_ids) > 1:
            errors.append(
                f"Part '{pid}' is in requiredPartIds of multiple Place steps: "
                f"{', '.join(step_ids)} — each part can only be placed once"
            )

    # Report
    print(f"\n=== {package_id} ===")
    print(f"  Parts: {len(parts)}, Targets: {len(targets)}, Steps: {len(steps)}, Subassemblies: {len(subassemblies)}")
    seqs = sorted(s["sequenceIndex"] for s in steps)
    print(f"  seqIndex range: {seqs[0] if seqs else '-'} to {seqs[-1] if seqs else '-'}")

    if errors:
        print(f"\nERRORS ({len(errors)}):")
        for e in errors:
            print(f"  ✗ {e}")
    if warnings:
        print(f"\nWARNINGS ({len(warnings)}):")
        for w in warnings:
            print(f"  ⚠ {w}")
    if not errors and not warnings:
        print("\n  All checks passed.")

    # Fix seqIndex if requested and there are gaps/dupes
    if fix_seqindex_flag and any("seqIndex" in e for e in errors):
        print("\nFixing seqIndex...")
        asm_dir = os.path.join(package_dir, "assemblies")
        steps_with_files = []
        for fname in sorted(os.listdir(asm_dir)):
            if not fname.endswith(".json"):
                continue
            with open(os.path.join(asm_dir, fname), encoding="utf-8") as f:
                data = json.load(f)
            for s in data.get("steps", []):
                steps_with_files.append((s, fname))
        fix_seqindex(package_dir, steps_with_files)
        print("Done. Run again without --fix-seqindex to verify.")

    return len(errors)


if __name__ == "__main__":
    args = sys.argv[1:]
    do_fix = "--fix-seqindex" in args
    args = [a for a in args if not a.startswith("--")]

    if not args:
        # Run all packages
        for pkg in os.listdir(BASE_DIR):
            if os.path.isdir(os.path.join(BASE_DIR, pkg)):
                run(pkg, do_fix)
    else:
        sys.exit(run(args[0], do_fix))
