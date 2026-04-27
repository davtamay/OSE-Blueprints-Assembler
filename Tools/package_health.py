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
  8. Prefab instances: prefab YAML exists, role bindings cover declared roles,
                       override paths reference real step id_suffixes

Reference locations for parts (all 5 must be checked):
  a. steps[].requiredPartIds / optionalPartIds
  b. partGroups[].partIds
  c. targets[].associatedPartId
  d. previewConfig.constrainedPartGroupFitPlacements[].drivenPartIds
  e. parts[] definition itself (the source of truth)

With --fix-seqindex: renumbers all steps globally (preserving order) to fill gaps.

Slice 0 alias note: legacy machine.json files using `subassemblies`,
`subassemblyId`, or `constrainedSubassemblyFitPlacements` are still read
for backwards compatibility — the loader and this script accept both names.
New content uses the partGroup vocabulary.
"""

import json, os, sys
from collections import Counter, defaultdict

# Valid part categories — must match Unity MachineJsonPrePlayValidator
VALID_PART_CATEGORIES = {
    "plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom"
}

BASE_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "_Project", "Data", "Packages")


def load_package(package_dir):
    """Load and merge all assembly files into one flat dict.

    Returns
    -------
    parts, targets, steps, part_groups, preview_driven, prefab_instances
        prefab_instances is a list of (instance_dict, source_filename) so
        the prefab validator can report which file each issue lives in.
    """
    parts = []
    targets = []
    steps = []
    part_groups = []
    preview_driven = []
    prefab_instances = []

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
        # Slice 0 alias — accept both names so legacy and new content load.
        part_groups.extend(data.get("partGroups", []))
        part_groups.extend(data.get("subassemblies", []))
        for inst in data.get("prefabInstances", []):
            prefab_instances.append((inst, fname))

    # preview_config drivenPartIds
    pc_path = os.path.join(package_dir, "preview_config.json")
    if os.path.exists(pc_path):
        with open(pc_path, encoding="utf-8") as f:
            pc = json.load(f)
        cfg = pc.get("previewConfig", pc)
        for placement in cfg.get("constrainedPartGroupFitPlacements", []):
            preview_driven.extend(placement.get("drivenPartIds", []))
        for placement in cfg.get("constrainedSubassemblyFitPlacements", []):
            preview_driven.extend(placement.get("drivenPartIds", []))

    return parts, targets, steps, part_groups, preview_driven, prefab_instances


def collect_referenced_part_ids(steps, part_groups, targets, preview_driven):
    """Collect all partIds referenced in any of the 5 reference locations."""
    refs = set()
    for s in steps:
        refs.update(s.get("requiredPartIds", []))
        refs.update(s.get("optionalPartIds", []))
        refs.update(s.get("targetPartIds", []))
    for sa in part_groups:
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


PREFABS_DIR = os.path.join(os.path.dirname(__file__), "..", "AgentAssistant", "prefabs")


def _load_prefab_yaml(prefab_id):
    """Best-effort YAML load for a Step Configuration Prefab. Returns None if
    PyYAML isn't installed or the file is missing — the caller degrades to a
    presence-only check so the audit still runs without yaml installed."""
    if not prefab_id:
        return None
    path = os.path.join(PREFABS_DIR, f"{prefab_id}.yaml")
    if not os.path.exists(path):
        path = os.path.join(PREFABS_DIR, f"{prefab_id}.yml")
        if not os.path.exists(path):
            return None
    try:
        import yaml  # type: ignore
        with open(path, encoding="utf-8") as f:
            return yaml.safe_load(f)
    except ImportError:
        return {"_path_only": path}  # signal: file exists, content unparsed
    except Exception:
        return None


def check_prefab_instances(prefab_instances):
    """Validate every PrefabInstance entry across all assembly files.

    Checks:
      - prefabId references a YAML in AgentAssistant/prefabs/
      - bindings cover every role declared by the prefab (and don't add extras)
      - list-role bindings respect the declared count when present
      - override paths target step:<id_suffix>.<...> for an id_suffix that
        actually appears in the prefab's steps[]
    """
    errors = []
    warnings = []

    for inst, fname in prefab_instances:
        instance_id = inst.get("instanceId") or "(unnamed)"
        prefab_id   = inst.get("prefabId")   or ""
        ctx = f"prefab instance '{instance_id}' in {fname}"

        if not prefab_id:
            errors.append(f"{ctx}: empty prefabId.")
            continue

        prefab = _load_prefab_yaml(prefab_id)
        if prefab is None:
            errors.append(f"{ctx}: source YAML '{prefab_id}.yaml' not found in AgentAssistant/prefabs/.")
            continue
        if "_path_only" in prefab:
            warnings.append(f"{ctx}: source YAML found but PyYAML is not installed — skipping content checks.")
            continue

        # Roles
        declared_roles = (prefab.get("roles") or {}) or {}
        bound = {b.get("role"): b for b in (inst.get("bindings") or []) if b.get("role")}

        for role_name, role_decl in declared_roles.items():
            kind = (role_decl or {}).get("kind", "part")
            if role_name not in bound:
                errors.append(f"{ctx}: missing role binding '{role_name}' (kind={kind}).")
                continue
            binding = bound[role_name]
            if kind == "part_list":
                ids = binding.get("partIds") or []
                expected = role_decl.get("count")
                if expected is not None and len(ids) != expected:
                    errors.append(f"{ctx}: role '{role_name}' expects {expected} entries, got {len(ids)}.")
                if not isinstance(ids, list):
                    errors.append(f"{ctx}: role '{role_name}' (part_list) has non-list binding.")
            else:
                if not binding.get("partId"):
                    errors.append(f"{ctx}: role '{role_name}' (part) has empty partId binding.")

        extras = set(bound.keys()) - set(declared_roles.keys())
        for extra in sorted(extras):
            warnings.append(f"{ctx}: binding for role '{extra}' has no matching role: declaration in '{prefab_id}.yaml'.")

        # Overrides
        step_suffixes = set()
        for st in (prefab.get("steps") or []):
            sfx = (st or {}).get("id_suffix")
            if sfx:
                step_suffixes.add(sfx)
        for ov in (inst.get("overrides") or []):
            path = (ov or {}).get("path")
            if not path:
                warnings.append(f"{ctx}: override entry has empty path.")
                continue
            if path.startswith("step:"):
                rest = path[len("step:"):]
                dot  = rest.find(".")
                suffix = rest if dot < 0 else rest[:dot]
                if suffix not in step_suffixes:
                    errors.append(
                        f"{ctx}: override path '{path}' targets step '{suffix}' "
                        f"but the prefab defines no step with that id_suffix.")
            elif path.startswith("part:") or path.startswith("partGroup:") or path.startswith("partGroup."):
                warnings.append(f"{ctx}: override path '{path}' uses a prefix not yet supported by the expander (steps only as of Slice 3a).")
            else:
                warnings.append(f"{ctx}: override path '{path}' has no recognised entity prefix (expected 'step:').")

    return errors, warnings


def run(package_id, fix_seqindex_flag=False):
    package_dir = os.path.join(BASE_DIR, package_id)
    if not os.path.isdir(package_dir):
        print(f"ERROR: package not found: {package_dir}")
        sys.exit(1)

    parts, targets, steps, part_groups, preview_driven, prefab_instances = load_package(package_dir)

    all_part_ids = {p["id"] for p in parts}
    all_target_ids = {t["id"] for t in targets}
    referenced_parts = collect_referenced_part_ids(steps, part_groups, targets, preview_driven)
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
        warnings.append(f"Orphan part: '{pid}' defined but not referenced in steps, partGroups, targets, or previewConfig")

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

    # 8. Prefab instances — Slice 2 / 3.
    prefab_errors, prefab_warnings = check_prefab_instances(prefab_instances)
    errors.extend(prefab_errors)
    warnings.extend(prefab_warnings)

    # Report
    print(f"\n=== {package_id} ===")
    print(f"  Parts: {len(parts)}, Targets: {len(targets)}, Steps: {len(steps)}, "
          f"PartGroups: {len(part_groups)}, PrefabInstances: {len(prefab_instances)}")
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
