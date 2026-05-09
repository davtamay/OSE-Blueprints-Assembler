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
  9. Tool-action target placements: every target referenced by a requiredToolActions[]
                                     entry has a matching previewConfig.targetPlacements
                                     entry (else runtime falls to identity rotation)
 10. Mixed anchor mechanism: target sets useLocalOffsetFromPart=true AND anchorRef
                              (logically ambiguous — pick one)
 11. Sibling-target rotation divergence: parallel-named targets across c1/c2/c3...
                                          clones have identical toolActionRotation
 12. Sibling-pair geometry divergence: parallel-named part pairs (e.g. *_half_a and
                                        *_half_b across carriage clones) must share
                                        the same intra-pair delta vector — clones
                                        producing different inter-half spacing
                                        misalign close-halves ghosts
 13. Clone translation consistency: within each clone family (y_left/y_right/z_back/...),
                                     ALL parts must share the same translation delta
                                     from the canonical clone. A clone with halves
                                     translated +0.77X but bolts translated +0.005X
                                     is structurally split — close-halves ghost lands
                                     in the wrong place, shake-test centroid is bogus
 14. Sibling-part rotation parity: parallel-named parts across clones (e.g.
                                    *_carriage_half_a in c1/c2/c3/c4) must share
                                    identical assembledRotation. A clone whose halves
                                    don't rotate-to-clamshell will visually overlay
                                    instead of forming a closed carriage
 15. GLB shell audit: flag *_approved.glb files containing 2+ significant connected
                       components whose AABBs overlap >30%, or that share a near-
                       coincident face. Catches the Brackettop regression where two
                       physically distinct parts (top plate + carriage) shipped
                       fused into one mesh. See tools/audit_part_glbs.py
 16. GLB SHA dedup: flag *_approved.glb files with identical SHA256 hashes.
                     Catches the idler×4 regression where the same mesh was copied
                     to 4 filenames and referenced separately, bloating the package
                     and creating multiple Unity GUIDs to maintain.
 17. Use-family parts pre-placed: family=Use steps cannot have requiredPartIds for
                     parts not yet placed by a prior family=Place step. Use routes
                     through UseStepHandler which doesn't place parts. Ports the
                     C# normalizer's ValidateUseFamilyPartsArePrePlaced check so
                     this class of bug fails the health check at author time
                     instead of waiting for TTAW Validation Dashboard / Play.

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

import json, os, re, sys
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

    # preview_config drivenPartIds + targetPlacements (for safeguard checks)
    target_placements = []
    pc_path = os.path.join(package_dir, "preview_config.json")
    if os.path.exists(pc_path):
        with open(pc_path, encoding="utf-8") as f:
            pc = json.load(f)
        cfg = pc.get("previewConfig", pc)
        for placement in cfg.get("constrainedPartGroupFitPlacements", []):
            preview_driven.extend(placement.get("drivenPartIds", []))
        for placement in cfg.get("constrainedSubassemblyFitPlacements", []):
            preview_driven.extend(placement.get("drivenPartIds", []))
        target_placements = cfg.get("targetPlacements", [])

    return parts, targets, steps, part_groups, preview_driven, prefab_instances, target_placements


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

    parts, targets, steps, part_groups, preview_driven, prefab_instances, target_placements = load_package(package_dir)

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

    # 9. Tool-action targets must have a targetPlacement in preview_config.
    #    Anchor-resolved targets read rotation from the static placement
    #    (ToolTargetSpawner.TryResolveToolActionTargetPose lines 380-397);
    #    a missing placement falls through to identity rotation, which
    #    silently mis-orients the tool. Caught the c2 b/c/d regression.
    placed_target_ids = {tp.get("targetId") for tp in target_placements if tp.get("targetId")}
    targets_in_tool_actions = set()
    for s in steps:
        for rta in s.get("requiredToolActions", []):
            tid = rta.get("targetId")
            if tid:
                targets_in_tool_actions.add(tid)
    for tid in sorted(targets_in_tool_actions - placed_target_ids):
        errors.append(
            f"Missing targetPlacement: '{tid}' is used by a tool action but has no entry "
            f"in previewConfig.targetPlacements — runtime will use identity rotation"
        )

    # 10. Targets must not mix anchor mechanisms — useLocalOffsetFromPart and
    #     anchorRef select different code paths in ToolTargetSpawner; setting
    #     both is logically ambiguous (the local-offset path wins, the
    #     anchorRef rotation may not). Caught the c2 mechanism mismatch.
    for t in targets:
        has_local = bool(t.get("useLocalOffsetFromPart"))
        has_anchor = bool(t.get("anchorRef"))
        if has_local and has_anchor:
            warnings.append(
                f"Mixed anchor mechanism on target '{t.get('id')}': both "
                f"useLocalOffsetFromPart=true and anchorRef='{t.get('anchorRef')}' "
                f"are set. Pick one — useLocalOffsetFromPart wins at runtime"
            )

    # 12. Sibling part pairs across clones must share the same intra-pair
    #     delta vector. For each pair of suffixes that co-occur within a
    #     prefix family (e.g. half_a/half_b within *_carriage), check that
    #     every prefix's (suffix_x.assembledPosition - suffix_y.assembledPosition)
    #     vector matches the canonical (first/most-common) delta. Catches the
    #     c2/c3/c4 carriage half mis-spacing that misaligns close-halves
    #     ghosts even when individual placements look superficially valid.
    pair_re = re.compile(r"^(.+?)_(half_[ab])$")
    by_prefix = defaultdict(dict)  # prefix -> {suffix: assembledPosition_dict}
    for pp in target_placements:  # placeholder; we want partPlacements
        pass
    # Re-load partPlacements (load_package only returned target_placements)
    pc_path = os.path.join(package_dir, "preview_config.json")
    part_placements = []
    if os.path.exists(pc_path):
        with open(pc_path, encoding="utf-8") as f:
            pc = json.load(f)
        cfg = pc.get("previewConfig", pc)
        part_placements = cfg.get("partPlacements", [])
    for pp in part_placements:
        pid = pp.get("partId", "")
        ap = pp.get("assembledPosition")
        if not ap: continue
        m = pair_re.match(pid)
        if m:
            by_prefix[m.group(1)][m.group(2)] = (
                float(ap.get("x", 0)), float(ap.get("y", 0)), float(ap.get("z", 0))
            )
    # For each prefix that has both half_a and half_b, compute delta and
    # compare across all such prefixes that share a parent family.
    deltas = {}  # prefix -> (dx, dy, dz)
    for prefix, halves in by_prefix.items():
        if "half_a" in halves and "half_b" in halves:
            a = halves["half_a"]; b = halves["half_b"]
            deltas[prefix] = (round(b[0]-a[0], 4), round(b[1]-a[1], 4), round(b[2]-a[2], 4))
    if len(deltas) >= 2:
        # Use the most common delta as canonical.
        delta_counts = Counter(deltas.values())
        canonical, _ = delta_counts.most_common(1)[0]
        for prefix, d in sorted(deltas.items()):
            if d != canonical:
                # Tolerance: any axis deviating > 1cm is a real divergence.
                if any(abs(d[i] - canonical[i]) > 0.01 for i in range(3)):
                    errors.append(
                        f"Sibling-pair geometry divergence ({prefix}_half_a/half_b): "
                        f"intra-pair delta {d} differs from canonical {canonical}. "
                        f"Other clones use {canonical}; close-halves ghost will misalign"
                    )

    # 13. Clone-family translation consistency. For each set of partGroups
    #     named partGroup_<family>_<clone> (e.g. partGroup_carriage_y_left,
    #     _y_right, _z_back, _z_front), one clone is the canonical reference;
    #     every other clone must apply ONE translation delta to ALL its
    #     parts — bolts, halves, bearings should all move together. Catches
    #     the cloner producing structurally split clones (carriage halves
    #     ending up in the wrong location while hardware is correctly placed).
    #     Tolerance: 1cm — anything larger is a cloner bug.
    family_re = re.compile(r"^partGroup_(?P<family>[a-zA-Z]+)_(?P<clone>[a-z_]+)$")
    family_clones = defaultdict(dict)  # family -> {clone: partGroup_id}
    for pg in part_groups:
        m = family_re.match(pg.get("id", "") or "")
        if m:
            family_clones[m.group("family")][m.group("clone")] = pg["id"]
    # Build part-id -> partGroupId membership (parts can carry partGroupIds[])
    part_to_groups = defaultdict(set)
    for p_dict in [{"id": pp.get("partId")} for pp in part_placements]:
        pass  # placeholder — we want raw parts, not placements
    # Re-walk parts to get partGroupIds
    raw_parts_by_id = {p["id"]: p for p in parts}
    for p in parts:
        for pgid in (p.get("partGroupIds") or []):
            part_to_groups[p["id"]].add(pgid)
    # Index assembledPosition by partId
    asm_pos = {pp.get("partId"): pp.get("assembledPosition")
               for pp in part_placements if pp.get("partId") and pp.get("assembledPosition")}
    def _strip(prefix, pid):
        # carriage parts may have an inner "_carriage_" infix in canonical
        if pid.startswith(prefix + "_carriage_"):
            return "carriage_" + pid[len(prefix) + len("_carriage_"):]
        if pid.startswith(prefix + "_"):
            return pid[len(prefix) + 1:]
        return None
    for family, clones in family_clones.items():
        if len(clones) < 2:
            continue
        # First clone alphabetically becomes canonical for the comparison
        canonical_clone = sorted(clones)[0]
        canonical_pg = clones[canonical_clone]
        # role -> canonical partId
        canonical_role_to_pid = {}
        for pid, pgs in part_to_groups.items():
            if canonical_pg in pgs:
                r = _strip(canonical_clone, pid)
                if r:
                    canonical_role_to_pid[r] = pid
        for clone, clone_pg in clones.items():
            if clone == canonical_clone:
                continue
            # Per-role delta vs canonical
            deltas = []
            role_deltas = {}
            for pid, pgs in part_to_groups.items():
                if clone_pg not in pgs:
                    continue
                r = _strip(clone, pid)
                if not r: continue
                # alias for c1 carriage_m6x18_b vs clones m6x18_b
                cpid = canonical_role_to_pid.get(r) or canonical_role_to_pid.get(
                    "carriage_m6x18_b" if r == "m6x18_b" else
                    ("m6x18_b" if r == "carriage_m6x18_b" else r))
                if not cpid: continue
                cv = asm_pos.get(cpid); xv = asm_pos.get(pid)
                if not (cv and xv): continue
                d = (round(float(xv["x"]) - float(cv["x"]), 4),
                     round(float(xv["y"]) - float(cv["y"]), 4),
                     round(float(xv["z"]) - float(cv["z"]), 4))
                deltas.append(d)
                role_deltas[r] = d
            if not deltas:
                continue
            # Most-common delta = canonical for this clone
            common, _ = Counter(deltas).most_common(1)[0]
            outliers = {r: d for r, d in role_deltas.items()
                        if any(abs(d[i] - common[i]) > 0.01 for i in range(3))}
            if outliers:
                bullet = "; ".join(f"{r}={d}" for r, d in sorted(outliers.items()))
                errors.append(
                    f"Clone-translation split (partGroup_{family}_{clone} vs canonical "
                    f"partGroup_{family}_{canonical_clone}): most parts translate by "
                    f"{common}, but these diverge — {bullet}. Cloner produced a "
                    f"structurally split family"
                )

    # 14. Sibling-part rotation parity. For each set of parallel-named parts
    #     within a clone FAMILY (e.g. *_carriage_half_a across y_left/y_right/
    #     z_back/z_front, but NOT cross-family with idler_*_half_a), the
    #     assembledRotation must be IDENTICAL. Catches c3/c4 carriage halves
    #     that ended up with broken rotations after clone-and-retarget.
    #     Match scope: token immediately preceding the suffix is the family
    #     anchor (e.g. "carriage" in "*_carriage_half_a") — only parts that
    #     share both family + suffix are compared.
    family_re = re.compile(r"^(?P<clone>[a-z][a-z_]*?)_(?P<family>carriage|idler|motor_holder|extruder|peg)_(?P<suffix>.+)$")
    family_groups = defaultdict(dict)  # (family, suffix) -> {pid: rotation_tuple}
    for pp in part_placements:
        pid = pp.get("partId", "") or ""
        ar = pp.get("assembledRotation")
        if not ar: continue
        m = family_re.match(pid)
        if not m: continue
        key = (m.group("family"), m.group("suffix"))
        family_groups[key][pid] = (
            round(float(ar.get("x", 0)), 4),
            round(float(ar.get("y", 0)), 4),
            round(float(ar.get("z", 0)), 4),
            round(float(ar.get("w", 0)), 4),
        )
    for (family, suffix), members in family_groups.items():
        if len(members) < 2:
            continue
        rot_counts = Counter(members.values())
        if len(rot_counts) > 1:
            common, _ = rot_counts.most_common(1)[0]
            outliers = {pid: r for pid, r in members.items()
                        if any(abs(r[i] - common[i]) > 0.01 for i in range(4))}
            if outliers:
                bullet = "; ".join(f"{pid}={r}" for pid, r in sorted(outliers.items()))
                errors.append(
                    f"Sibling-part rotation divergence (family={family}, suffix={suffix}): "
                    f"most clones use {common}, these diverge — {bullet}. "
                    f"Likely cloner mishandled rotation"
                )

    # 11. Sibling targets (same id_suffix across c1/c2/c3/... clones) must
    #     have identical toolActionRotation. Catches clone-and-retarget
    #     bugs that drop axes (c2 had y=0 vs c1 y=90 for bolt_tighten).
    sibling_re = re.compile(r"^(.*?)_c\d+_(.*)$")
    by_suffix = defaultdict(list)
    for t in targets:
        tid = t.get("id", "")
        m = sibling_re.match(tid)
        if m:
            key = (m.group(1), m.group(2))  # ("target", "bolt_tighten_a")
            by_suffix[key].append(t)
    def _rot_key(t):
        r = t.get("toolActionRotation") or {}
        return (round(float(r.get("x", 0)), 4),
                round(float(r.get("y", 0)), 4),
                round(float(r.get("z", 0)), 4))
    for (prefix, suffix), siblings in by_suffix.items():
        if len(siblings) < 2:
            continue
        rots = {_rot_key(t): t.get("id") for t in siblings if t.get("useToolActionRotation")}
        if len(rots) > 1:
            details = "; ".join(f"{tid}={rk}" for rk, tid in rots.items())
            errors.append(
                f"Sibling-target rotation divergence ({prefix}_*_{suffix}): "
                f"clones should have identical toolActionRotation. {details}"
            )

    # Report
    print(f"\n=== {package_id} ===")
    print(f"  Parts: {len(parts)}, Targets: {len(targets)}, Steps: {len(steps)}, "
          f"PartGroups: {len(part_groups)}, PrefabInstances: {len(prefab_instances)}")
    seqs = sorted(s["sequenceIndex"] for s in steps)
    print(f"  seqIndex range: {seqs[0] if seqs else '-'} to {seqs[-1] if seqs else '-'}")

    # Check 15: GLB shell audit — flag fused composite parts (Brackettop regression).
    # Imports lazily so the script still works if audit_part_glbs.py is moved.
    try:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import audit_part_glbs
        parts_dir = os.path.join(package_dir, "assets", "parts")
        if os.path.isdir(parts_dir):
            for fname in sorted(os.listdir(parts_dir)):
                if not fname.endswith("_approved.glb"):
                    continue
                glb_issues = audit_part_glbs.audit(os.path.join(parts_dir, fname))
                for issue in glb_issues:
                    if "cannot audit" in issue:
                        warnings.append(f"GLB audit: {issue}")
                    else:
                        errors.append(f"GLB audit: {issue}")
    except Exception as e:
        warnings.append(f"GLB audit skipped: {e}")

    # Check 16: SHA dedup — flag *_approved.glb files with identical SHA256.
    # Catches the idler×4 regression where the same mesh was copied to 4 filenames
    # (idler_approved, idler001/002/003_approved) and referenced separately,
    # bloating the package and creating 4 Unity GUIDs to maintain.
    try:
        import hashlib
        parts_dir = os.path.join(package_dir, "assets", "parts")
        if os.path.isdir(parts_dir):
            sha_to_files = defaultdict(list)
            for fname in sorted(os.listdir(parts_dir)):
                if not fname.endswith(".glb"):
                    continue
                fpath = os.path.join(parts_dir, fname)
                with open(fpath, "rb") as f:
                    digest = hashlib.sha256(f.read()).hexdigest()
                sha_to_files[digest].append(fname)
            for digest, files in sha_to_files.items():
                if len(files) > 1:
                    canonical = min(files, key=len)
                    dups = [f for f in files if f != canonical]
                    warnings.append(
                        f"GLB dedup: {len(files)} files share SHA {digest[:12]} — "
                        f"canonical='{canonical}', duplicates={dups}. "
                        f"Repoint assetRefs to canonical and delete duplicates."
                    )
    except Exception as e:
        warnings.append(f"GLB dedup skipped: {e}")

    # Check 17: Use-family parts must be pre-placed.
    # Ports MachinePackageNormalizer.ValidateUseFamilyPartsArePrePlaced
    # (Assets/_Project/Scripts/Content/Loading/MachinePackageNormalizer.cs:1207).
    # Walks steps in seqIndex order, accumulating partIds placed by Place
    # steps. For each Use step, errors if any requiredPartId hasn't been
    # placed yet. Catches the authoring bug where "tighten X with drill"
    # is family=Use but X is a fresh nut/bolt — Use routes through
    # UseStepHandler which doesn't place parts.
    sorted_steps = sorted(
        (s for s in steps if s.get("sequenceIndex") is not None),
        key=lambda s: s["sequenceIndex"],
    )
    placed_before = set()
    for step in sorted_steps:
        family = (step.get("family") or "").strip()
        required = step.get("requiredPartIds") or []
        if family.lower() == "use" and required:
            unplaced = [pid for pid in required
                        if pid and pid not in placed_before]
            if unplaced:
                errors.append(
                    f"Use-family step '{step.get('id')}' "
                    f"(seq {step.get('sequenceIndex')}) declares "
                    f"requiredPartIds that no prior Place step placed: "
                    f"{', '.join(unplaced)}. Either move them to a prior "
                    f"Place step or split the step into Place+Use."
                )
        if family.lower() == "place":
            for pid in required:
                if pid:
                    placed_before.add(pid)

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
