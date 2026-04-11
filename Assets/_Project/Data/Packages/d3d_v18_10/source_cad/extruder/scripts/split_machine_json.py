#!/usr/bin/env python3
"""
split_machine_json.py
=====================
Phase 2 migration: split the monolithic machine.json into the A+++ per-assembly
file layout:

    {packageDir}/
      machine.json          ← metadata only (machine, version, challengeConfig, assetManifest)
      shared.json           ← tools, partTemplates, global hints, global validationRules, effects
      assemblies/
        {assemblyId}.json   ← one file per assembly (assembly def, subassemblies, steps,
                               parts, targets, local hints)
      preview_config.json   ← previewConfig object extracted verbatim (TTAW-generated)

The loader (MachinePackageLoader.LoadSplitLayoutAsync) merges all files at load time
into one MachinePackageDefinition. Normalizer and validator are file-agnostic.

Backward-compat: non-migrated packages (no assemblies/ folder) continue using the
single-file machine.json as before.

Usage:
    python split_machine_json.py [--dry-run]

Run from any directory; paths are resolved relative to this script's location.
"""

import json
import sys
import os
import shutil
import datetime
from collections import defaultdict

SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
PACKAGE_DIR  = os.path.normpath(os.path.join(SCRIPT_DIR, "../../../"))
MACHINE_JSON = os.path.join(PACKAGE_DIR, "machine.json")


# ── helpers ───────────────────────────────────────────────────────────────────

def safe_list(value):
    return value if isinstance(value, list) else []


def omit_none_and_empty(d: dict) -> dict:
    """Return a copy of d with None values and empty lists removed."""
    out = {}
    for k, v in d.items():
        if v is None:
            continue
        if isinstance(v, list) and len(v) == 0:
            continue
        if isinstance(v, dict) and len(v) == 0:
            continue
        out[k] = v
    return out


def write_json(path: str, data: dict, dry_run: bool):
    if dry_run:
        print(f"  [DRY] would write {os.path.relpath(path, PACKAGE_DIR)}")
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"  Wrote {os.path.relpath(path, PACKAGE_DIR)}")


# ── main ──────────────────────────────────────────────────────────────────────

def main(dry_run: bool = False):
    if not os.path.exists(MACHINE_JSON):
        print(f"ERROR: machine.json not found at {MACHINE_JSON}")
        sys.exit(1)

    # Abort if already split (assemblies/ folder exists)
    assemblies_folder = os.path.join(PACKAGE_DIR, "assemblies")
    if os.path.isdir(assemblies_folder):
        print("ERROR: assemblies/ folder already exists — package is already split.")
        print("Delete the assemblies/ folder first if you want to re-run this script.")
        sys.exit(1)

    print(f"Loading: {MACHINE_JSON}")
    with open(MACHINE_JSON, encoding="utf-8") as f:
        data = json.load(f)

    # ── index source arrays ────────────────────────────────────────────────────
    assemblies    = safe_list(data.get("assemblies"))
    subassemblies = safe_list(data.get("subassemblies"))
    parts         = safe_list(data.get("parts"))
    tools         = safe_list(data.get("tools"))
    part_templates= safe_list(data.get("partTemplates"))
    steps         = safe_list(data.get("steps"))
    validation_rules = safe_list(data.get("validationRules"))
    effects       = safe_list(data.get("effects"))
    hints         = safe_list(data.get("hints"))
    targets       = safe_list(data.get("targets"))
    preview_config = data.get("previewConfig")
    machine       = data.get("machine")
    schema_version = data.get("schemaVersion")
    package_version = data.get("packageVersion")
    challenge_config = data.get("challengeConfig")
    asset_manifest = data.get("assetManifest")

    assembly_ids = {a["id"] for a in assemblies if a and a.get("id")}
    print(f"\nFound {len(assemblies)} assemblies, {len(steps)} steps, "
          f"{len(parts)} parts, {len(targets)} targets, {len(hints)} hints")

    # ── build lookup maps ──────────────────────────────────────────────────────

    steps_by_assembly  = defaultdict(list)  # assemblyId → [step]
    for step in steps:
        if not step: continue
        asm_id = step.get("assemblyId", "")
        if asm_id:
            steps_by_assembly[asm_id].append(step)

    # subassembly → assemblyId (from its own assemblyId field)
    subassembly_to_assembly = {}
    for sub in subassemblies:
        if sub and sub.get("id") and sub.get("assemblyId"):
            subassembly_to_assembly[sub["id"]] = sub["assemblyId"]

    # targetId → assemblyId (from first step that references it)
    target_to_assembly = {}
    for asm_id, asm_steps in steps_by_assembly.items():
        for step in asm_steps:
            for tid in safe_list(step.get("targetIds")):
                if tid not in target_to_assembly:
                    target_to_assembly[tid] = asm_id
            # Also requiredToolActions.targetId
            for ta in safe_list(step.get("requiredToolActions")):
                if ta and ta.get("targetId") and ta["targetId"] not in target_to_assembly:
                    target_to_assembly[ta["targetId"]] = asm_id

    # partId → assemblyId (from Place-family steps; fall back to any step that references it)
    part_to_assembly_place = {}  # from Place steps
    part_to_assembly_any   = {}  # fallback
    for asm_id, asm_steps in steps_by_assembly.items():
        for step in asm_steps:
            is_place = (step.get("family", "").lower() == "place" or
                        step.get("completionType", "").lower() == "placement")
            for pid in safe_list(step.get("requiredPartIds")):
                if pid:
                    part_to_assembly_any.setdefault(pid, asm_id)
                    if is_place:
                        part_to_assembly_place.setdefault(pid, asm_id)
            for pid in safe_list(step.get("optionalPartIds")):
                if pid:
                    part_to_assembly_any.setdefault(pid, asm_id)

    def get_part_assembly(part_id: str) -> str | None:
        return (part_to_assembly_place.get(part_id)
                or part_to_assembly_any.get(part_id))

    # hintId → set of assemblyIds that reference it
    hint_referenced_by = defaultdict(set)  # hintId → {assemblyId}
    for asm_id, asm_steps in steps_by_assembly.items():
        for step in asm_steps:
            for hid in safe_list(step.get("hintIds")):
                hint_referenced_by[hid].add(asm_id)
            # Also guidance.hintIds
            guidance = step.get("guidance") or {}
            for hid in safe_list(guidance.get("hintIds")):
                hint_referenced_by[hid].add(asm_id)

    # Build hint lookup by id
    hints_by_id = {h["id"]: h for h in hints if h and h.get("id")}

    def get_hint_file(hint: dict) -> str:
        """
        Returns the assemblyId the hint belongs to, or 'shared' for global hints.

        Rules (in priority order):
          1. Has a targetId → assembly owning that target
          2. Has a partId → assembly owning that part
          3. Referenced by exactly one assembly's steps → that assembly
          4. Otherwise → shared.json
        """
        hid = hint.get("id", "")
        # 1. targetId
        tid = hint.get("targetId", "")
        if tid and tid in target_to_assembly:
            return target_to_assembly[tid]
        # 2. partId
        pid = hint.get("partId", "")
        if pid:
            pa = get_part_assembly(pid)
            if pa:
                return pa
        # 3. Referenced by exactly one assembly
        refs = hint_referenced_by.get(hid, set())
        if len(refs) == 1:
            return next(iter(refs))
        # 4. Global (referenced by multiple, or unreferenced, or no targetId/partId)
        return "shared"

    # ── collect per-assembly content ───────────────────────────────────────────

    asm_by_id = {}  # assemblyId → dict with collected entities

    for asm in assemblies:
        if not asm or not asm.get("id"):
            continue
        asm_by_id[asm["id"]] = {
            "_def":         asm,
            "subassemblies":[],
            "parts":        [],
            "steps":        [],
            "targets":      [],
            "hints":        [],
        }

    # distribute subassemblies
    for sub in subassemblies:
        if not sub: continue
        asm_id = sub.get("assemblyId", "")
        if asm_id in asm_by_id:
            asm_by_id[asm_id]["subassemblies"].append(sub)
        else:
            print(f"  WARN: subassembly '{sub.get('id')}' has unknown assemblyId '{asm_id}' — skipping")

    # distribute steps
    for step in steps:
        if not step: continue
        asm_id = step.get("assemblyId", "")
        if asm_id in asm_by_id:
            asm_by_id[asm_id]["steps"].append(step)
        else:
            print(f"  WARN: step '{step.get('id')}' has unknown assemblyId '{asm_id}' — skipping")

    # distribute parts
    targets_by_id = {t["id"]: t for t in targets if t and t.get("id")}
    assigned_parts = set()
    for part in parts:
        if not part: continue
        pid = part.get("id", "")
        asm_id = get_part_assembly(pid)
        if asm_id and asm_id in asm_by_id:
            asm_by_id[asm_id]["parts"].append(part)
            assigned_parts.add(pid)
        else:
            # No step references this part — put in the first assembly as fallback
            first_asm = assemblies[0]["id"] if assemblies else None
            if first_asm and first_asm in asm_by_id:
                asm_by_id[first_asm]["parts"].append(part)
                assigned_parts.add(pid)
                print(f"  WARN: part '{pid}' not referenced by any step — placed in '{first_asm}'")

    # distribute targets
    assigned_targets = set()
    for target in targets:
        if not target: continue
        tid = target.get("id", "")
        asm_id = target_to_assembly.get(tid)
        if asm_id and asm_id in asm_by_id:
            asm_by_id[asm_id]["targets"].append(target)
            assigned_targets.add(tid)
        else:
            print(f"  WARN: target '{tid}' not referenced by any step — omitting from assembly files (may be orphan)")

    # distribute hints
    global_hints = []
    assigned_hints = set()
    for hint in hints:
        if not hint: continue
        hid = hint.get("id", "")
        dest = get_hint_file(hint)
        if dest == "shared":
            global_hints.append(hint)
        elif dest in asm_by_id:
            asm_by_id[dest]["hints"].append(hint)
        else:
            global_hints.append(hint)
        assigned_hints.add(hid)

    # ── summary ────────────────────────────────────────────────────────────────

    print(f"\nDistribution summary:")
    total_asm_steps = total_asm_parts = total_asm_targets = total_asm_hints = 0
    for asm_id, bucket in sorted(asm_by_id.items()):
        ns = len(bucket["steps"])
        np = len(bucket["parts"])
        nt = len(bucket["targets"])
        nh = len(bucket["hints"])
        total_asm_steps   += ns
        total_asm_parts   += np
        total_asm_targets += nt
        total_asm_hints   += nh
        print(f"  {asm_id}: {ns} steps, {np} parts, {nt} targets, {nh} hints")
    print(f"  shared.json: {len(tools)} tools, {len(part_templates)} templates, "
          f"{len(global_hints)} hints, {len(validation_rules)} rules, {len(effects)} effects")
    print(f"  Unassigned targets: {len(targets) - total_asm_targets}")
    print(f"  Total steps distributed: {total_asm_steps}/{len(steps)}")
    print(f"  Total parts distributed: {total_asm_parts}/{len(parts)}")

    if dry_run:
        print("\n[DRY RUN] No files written.")
        return

    # ── backup ────────────────────────────────────────────────────────────────
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_dir  = os.path.join(PACKAGE_DIR, ".migration_backups")
    backup_path = os.path.join(backup_dir, f"machine_{ts}_pre_split.json")
    os.makedirs(backup_dir, exist_ok=True)
    shutil.copy2(MACHINE_JSON, backup_path)
    print(f"\nBackup: {backup_path}")

    print(f"\nWriting files to: {PACKAGE_DIR}")

    # ── write preview_config.json ─────────────────────────────────────────────
    if preview_config is not None:
        # Wrapped so the loader can use JsonUtility.FromJson<MachinePackageDefinition>
        write_json(os.path.join(PACKAGE_DIR, "preview_config.json"),
                   {"previewConfig": preview_config}, dry_run)

    # ── write shared.json ─────────────────────────────────────────────────────
    shared_content = omit_none_and_empty({
        "tools":           tools if tools else None,
        "partTemplates":   part_templates if part_templates else None,
        "hints":           global_hints if global_hints else None,
        "validationRules": validation_rules if validation_rules else None,
        "effects":         effects if effects else None,
    })
    write_json(os.path.join(PACKAGE_DIR, "shared.json"), shared_content, dry_run)

    # ── write assemblies/*.json ───────────────────────────────────────────────
    os.makedirs(assemblies_folder, exist_ok=True)
    for asm_id, bucket in sorted(asm_by_id.items()):
        asm_content = omit_none_and_empty({
            "assemblies":    [bucket["_def"]],
            "subassemblies": bucket["subassemblies"] if bucket["subassemblies"] else None,
            "parts":         bucket["parts"]         if bucket["parts"]         else None,
            "steps":         bucket["steps"]         if bucket["steps"]         else None,
            "targets":       bucket["targets"]       if bucket["targets"]       else None,
            "hints":         bucket["hints"]         if bucket["hints"]         else None,
        })
        write_json(os.path.join(assemblies_folder, f"{asm_id}.json"), asm_content, dry_run)

    # ── write new machine.json (metadata only) ────────────────────────────────
    new_machine = omit_none_and_empty({
        "schemaVersion":  schema_version,
        "packageVersion": package_version,
        "machine":        machine,
        "challengeConfig":challenge_config,
        "assetManifest":  asset_manifest,
    })
    write_json(MACHINE_JSON, new_machine, dry_run)

    print(f"\nDone. {len(asm_by_id)} assembly files written to assemblies/")
    print("Next steps:")
    print("  1. Open Unity — the loader will detect assemblies/ and use split-layout loading")
    print("  2. Run 'OSE > Validate All Packages' to confirm 0 errors")
    print("  3. If errors, inspect the console for which assembly file needs correction")


if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    main(dry_run)
