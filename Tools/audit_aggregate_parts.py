"""
audit_aggregate_parts.py — Reference Graph for Aggregate Part IDs
==================================================================
Surveys every reference site for a list of part IDs that are conceptually
aggregates / groupings rather than physical parts (e.g.
`d3d_y_left_axis_unit`, `d3d_extruder_nozzle_assembly`,
`d3d_x_axis_motor_holder_unit`).

Used before migrating these entries from `parts[]` to `partGroups[]`
with `isAggregate=true` — the migration must update every reference
site or the validator will scream and the runtime will skip-render
those steps. Run this BEFORE the data migration to know exactly which
files / arrays / steps need patches.

Usage:
    python Tools/audit_aggregate_parts.py [packageId]

Reference sites checked (matches CLAUDE.md "5 reference locations"):
  a. steps[].requiredPartIds / optionalPartIds / visualPartIds /
     targetPartIds
  b. partGroups[].partIds
  c. targets[].associatedPartId
  d. previewConfig.constrainedPartGroupFitPlacements[].drivenPartIds
  e. previewConfig.partPlacements[].partId
  f. parts[] definition itself (the source of truth)
"""

import json, os, sys
from collections import defaultdict

BASE_DIR = os.path.join(os.path.dirname(__file__), "..", "Assets", "_Project", "Data", "Packages")

# Heuristic — a part is a likely aggregate when its id ends in one of
# these suffixes OR when no GLB exists for it at the canonical paths.
AGGREGATE_SUFFIXES = ("_unit", "_pair", "_assembly", "_core", "_holder")


def find_aggregate_parts(parts, glb_files):
    """Return part ids that look like conceptual aggregates."""
    suspects = []
    for p in parts:
        pid = p.get("id", "")
        ref = (p.get("assetRef") or "").lower()
        # Skip parts that already have a GLB on disk.
        if ref and any(g.endswith(ref.lower()) for g in glb_files):
            continue
        # Match by suffix OR by missing-asset.
        if any(pid.endswith(suf) for suf in AGGREGATE_SUFFIXES):
            suspects.append(pid)
    return suspects


def collect_references(pkg_dir, target_ids):
    """Walk every assembly file + previewConfig and report references."""
    refs = defaultdict(list)  # target_id → [(site_kind, location, owner_id), ...]

    asm_dir = os.path.join(pkg_dir, "assemblies")
    for fname in sorted(os.listdir(asm_dir)):
        if not fname.endswith(".json"): continue
        fpath = os.path.join(asm_dir, fname)
        with open(fpath, encoding="utf-8") as f:
            data = json.load(f)

        for p in data.get("parts", []):
            if p.get("id") in target_ids:
                refs[p["id"]].append(("part-definition", fname, p["id"]))

        for s in data.get("steps", []):
            sid = s.get("id", "?")
            for kind in ("requiredPartIds", "optionalPartIds", "visualPartIds", "targetPartIds"):
                for pid in (s.get(kind) or []):
                    if pid in target_ids:
                        refs[pid].append((f"step.{kind}", fname, sid))

        for sa in data.get("partGroups", []):
            sid = sa.get("id", "?")
            for pid in (sa.get("partIds") or []):
                if pid in target_ids:
                    refs[pid].append(("partGroup.partIds", fname, sid))
            for pid in (sa.get("memberPartGroupIds") or []):
                if pid in target_ids:
                    refs[pid].append(("partGroup.memberPartGroupIds", fname, sid))

        for t in data.get("targets", []):
            tid = t.get("id", "?")
            if t.get("associatedPartId") in target_ids:
                refs[t["associatedPartId"]].append(("target.associatedPartId", fname, tid))

    pc_path = os.path.join(pkg_dir, "preview_config.json")
    if os.path.exists(pc_path):
        with open(pc_path, encoding="utf-8") as f:
            pc = json.load(f)
        cfg = pc.get("previewConfig", pc)
        for placement in cfg.get("partPlacements", []):
            pid = placement.get("partId")
            if pid in target_ids:
                refs[pid].append(("previewConfig.partPlacements", "preview_config.json", pid))
        for placement in cfg.get("constrainedPartGroupFitPlacements", []):
            for pid in (placement.get("drivenPartIds") or []):
                if pid in target_ids:
                    refs[pid].append(("previewConfig.constrainedPartGroupFit.drivenPartIds",
                                      "preview_config.json", placement.get("partGroupId", "?")))

    return refs


def run(package_id):
    pkg_dir = os.path.join(BASE_DIR, package_id)
    if not os.path.isdir(pkg_dir):
        print(f"ERROR: package not found: {pkg_dir}")
        sys.exit(1)

    # Load parts so we can list candidates by suffix.
    asm_dir = os.path.join(pkg_dir, "assemblies")
    all_parts = []
    for fname in sorted(os.listdir(asm_dir)):
        if not fname.endswith(".json"): continue
        with open(os.path.join(asm_dir, fname), encoding="utf-8") as f:
            all_parts.extend(json.load(f).get("parts", []))

    glb_files = []
    parts_dir = os.path.join(pkg_dir, "assets", "parts")
    if os.path.isdir(parts_dir):
        glb_files = [f.lower() for f in os.listdir(parts_dir)
                     if f.lower().endswith((".glb", ".gltf", ".fbx"))]

    aggregates = sorted(find_aggregate_parts(all_parts, glb_files))
    print(f"=== {package_id}: {len(aggregates)} aggregate-suspect part(s) ===\n")
    if not aggregates:
        print("  (none — heuristic found no likely aggregates)")
        return

    refs = collect_references(pkg_dir, set(aggregates))
    for pid in aggregates:
        sites = refs.get(pid, [])
        # Classify per migration safety.
        kinds = {kind for kind, _, _ in sites}
        binds_as_part = bool(
            kinds & {"step.requiredPartIds", "step.optionalPartIds",
                     "step.visualPartIds", "step.targetPartIds",
                     "target.associatedPartId"})
        only_membership = sites and not binds_as_part
        verdict = (
            "DRAGGABLE — needs a real mesh, NOT a partGroup migration"
            if binds_as_part else
            ("MEMBERSHIP-ONLY — safe to delete + rely on partGroup.partIds"
             if only_membership else
             "ORPHAN — safe to delete entirely"))
        print(f"  {pid}  ({len(sites)} site(s) — {verdict})")
        if not sites:
            continue
        # Group by (kind, fname) for a compact summary.
        by_site = defaultdict(list)
        for kind, fname, owner in sites:
            by_site[(kind, fname)].append(owner)
        for (kind, fname), owners in sorted(by_site.items()):
            sample = ", ".join(owners[:5])
            more   = f" (+{len(owners)-5} more)" if len(owners) > 5 else ""
            print(f"    +-{kind}  in {fname}: {sample}{more}")
        print()


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        for pkg in os.listdir(BASE_DIR):
            if os.path.isdir(os.path.join(BASE_DIR, pkg)):
                run(pkg)
                print()
    else:
        run(args[0])
