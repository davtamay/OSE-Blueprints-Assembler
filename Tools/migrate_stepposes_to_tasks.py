"""
migrate_stepposes_to_tasks.py — Phase G.2.4 content migration

Bakes every authored PartPreviewPlacement.stepPoses[] entry into the
TaskOrderEntry.endTransform of the task that owns it, so the pose data moves
from "shared, step-keyed, potentially cross-referenced" storage to
"per-task, inline, task-owned" storage. After migration, the pose-chain
linearity invariant holds by construction — no task can read or mutate
pose data belonging to another task.

Usage:
    python tools/migrate_stepposes_to_tasks.py <packageId>           # write + backup
    python tools/migrate_stepposes_to_tasks.py <packageId> --dry-run # report only

Ownership rule:
    For a stepPose (partId P, stepId S):
    1. Skip synthetic NO-TASK waypoints (label starts with "__notask_auto").
    2. Find step S in the package.
    3. Walk step.taskOrder in order, record every task that touches P:
         - Part task: kind == "part" AND partId-from-entry-id == P
         - Tool task: kind == "toolAction" AND the matching
           requiredToolActions[].targetId's target has
           associatedPartId == P
    4. The LAST such task is the owner (its completion commits the part
       to this pose at step end).
    5. If owner.endTransform is null, bake the stepPose's transform.
       If already set, log a conflict and leave existing value alone.
    6. If no owner is found, log the stepPose as an orphan and keep it.

toPose token cleanup:
    After endTransform is populated, any requiredToolActions[].interaction.toPose
    of the form "step:<currentStepId>" becomes redundant and is cleared to
    null. Cross-task refs "step:<otherStepId>" (which G.1 disallows) are also
    cleared with a log.

Backup policy:
    Before any write, every file that will change is copied to
        <pkg>/.migration_backups/<filename>_<timestamp>_pre_g224.json
    On --dry-run nothing is written.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

BASE_DIR = os.path.join(
    os.path.dirname(__file__), "..", "Assets", "_Project", "Data", "Packages"
)
AUTO_NO_TASK_LABEL = "__notask_auto"


# ── Helpers ────────────────────────────────────────────────────────────────

def to_part_id(entry_id: str | None) -> str:
    """Mirror of C# TaskInstanceId.ToPartId — strip #N suffix."""
    if not entry_id:
        return entry_id or ""
    i = entry_id.find("#")
    return entry_id if i < 0 else entry_id[:i]


def load_json(path: str) -> dict[str, Any]:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def save_json(path: str, data: dict[str, Any]) -> None:
    """Pretty-write preserving the project's 4-space indent + \\n endings."""
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def backup(pkg_dir: str, src_path: str, timestamp: str) -> str:
    backup_dir = os.path.join(pkg_dir, ".migration_backups")
    os.makedirs(backup_dir, exist_ok=True)
    base = os.path.basename(src_path)
    name, ext = os.path.splitext(base)
    dest = os.path.join(backup_dir, f"{name}_{timestamp}_pre_g224{ext}")
    shutil.copy2(src_path, dest)
    return dest


# ── Package load / merge ───────────────────────────────────────────────────

@dataclass
class LoadedFile:
    path: str
    data: dict[str, Any]
    is_split_assembly: bool = False  # an assembly file under assemblies/
    dirty: bool = False


@dataclass
class LoadedPackage:
    pkg_id: str
    pkg_dir: str
    preview: LoadedFile
    files: list[LoadedFile]   # every file that holds merged data (assemblies + shared)
    steps_by_id: dict[str, tuple[LoadedFile, dict[str, Any]]]
    targets_by_id: dict[str, dict[str, Any]]
    actions_by_id: dict[str, dict[str, Any]]


def load_package(pkg_id: str) -> LoadedPackage:
    pkg_dir = os.path.join(BASE_DIR, pkg_id)
    if not os.path.isdir(pkg_dir):
        sys.exit(f"ERROR: package '{pkg_id}' not found at {pkg_dir}")

    preview_path = os.path.join(pkg_dir, "preview_config.json")
    if not os.path.isfile(preview_path):
        sys.exit(f"ERROR: {preview_path} missing")
    preview = LoadedFile(preview_path, load_json(preview_path))

    files: list[LoadedFile] = []
    # Split-layout: assemblies/*.json + shared.json
    asm_dir = os.path.join(pkg_dir, "assemblies")
    if os.path.isdir(asm_dir):
        for fname in sorted(os.listdir(asm_dir)):
            if not fname.endswith(".json"):
                continue
            p = os.path.join(asm_dir, fname)
            files.append(LoadedFile(p, load_json(p), is_split_assembly=True))
    shared_path = os.path.join(pkg_dir, "shared.json")
    if os.path.isfile(shared_path):
        files.append(LoadedFile(shared_path, load_json(shared_path)))

    # Build merged-id indices so we can resolve steps/targets/actions that
    # live anywhere across the split layout.
    steps_by_id: dict[str, tuple[LoadedFile, dict[str, Any]]] = {}
    targets_by_id: dict[str, dict[str, Any]] = {}
    actions_by_id: dict[str, dict[str, Any]] = {}
    for lf in files:
        for s in lf.data.get("steps", []):
            if s and s.get("id"):
                steps_by_id[s["id"]] = (lf, s)
                # Index per-step requiredToolActions too.
                for a in s.get("requiredToolActions", []) or []:
                    if a and a.get("id"):
                        actions_by_id[a["id"]] = a
        for t in lf.data.get("targets", []) or []:
            if t and t.get("id"):
                targets_by_id[t["id"]] = t

    return LoadedPackage(pkg_id, pkg_dir, preview, files, steps_by_id, targets_by_id, actions_by_id)


# ── Migration core ─────────────────────────────────────────────────────────

@dataclass
class MigrationReport:
    migrated: int = 0
    conflicts: int = 0
    orphans: int = 0
    skipped_synthetic: int = 0
    topose_cleared_self: int = 0
    topose_cleared_cross: int = 0
    details: list[str] = field(default_factory=list)

    def log(self, msg: str) -> None:
        self.details.append(msg)


def find_owner_task(step: dict[str, Any], part_id: str,
                    actions_by_id: dict[str, dict[str, Any]],
                    targets_by_id: dict[str, dict[str, Any]]) -> dict[str, Any] | None:
    """Walk step.taskOrder; return the LAST task that touches part_id, else None."""
    owner: dict[str, Any] | None = None
    for entry in step.get("taskOrder", []) or []:
        if not entry:
            continue
        kind = entry.get("kind")
        if kind == "part" and to_part_id(entry.get("id", "")) == part_id:
            owner = entry
            continue
        if kind == "toolAction":
            action = actions_by_id.get(entry.get("id", ""))
            if not action:
                continue
            target = targets_by_id.get(action.get("targetId", ""))
            if target and target.get("associatedPartId") == part_id:
                owner = entry
    return owner


def migrate_stepposes(pkg: LoadedPackage, dry_run: bool) -> MigrationReport:
    report = MigrationReport()
    placements = pkg.preview.data.get("previewConfig", {}).get("partPlacements", [])
    if not placements:
        placements = pkg.preview.data.get("partPlacements", [])  # some packages have it at root

    for placement in placements:
        part_id = placement.get("partId")
        if not part_id:
            continue
        step_poses = placement.get("stepPoses") or []
        if not step_poses:
            continue

        survivors: list[dict[str, Any]] = []
        for sp in step_poses:
            if not sp:
                continue
            label = sp.get("label", "") or ""
            if label.startswith(AUTO_NO_TASK_LABEL):
                report.skipped_synthetic += 1
                survivors.append(sp)
                continue

            step_id = sp.get("stepId")
            step_entry = pkg.steps_by_id.get(step_id) if step_id else None
            if not step_entry:
                report.orphans += 1
                report.log(f"ORPHAN (no step): part={part_id} stepId='{step_id}'")
                survivors.append(sp)
                continue
            step_file, step = step_entry

            owner = find_owner_task(step, part_id, pkg.actions_by_id, pkg.targets_by_id)
            if owner is None:
                report.orphans += 1
                report.log(f"ORPHAN (no owning task): part={part_id} stepId='{step_id}'")
                survivors.append(sp)
                continue

            if owner.get("endTransform"):
                report.conflicts += 1
                report.log(
                    f"CONFLICT: part={part_id} stepId='{step_id}' "
                    f"owner-task='{owner.get('id')}' already has endTransform — kept existing"
                )
                # Keep stepPose so the author can reconcile manually.
                survivors.append(sp)
                continue

            # Bake the pose into the owner task's endTransform.
            owner["endTransform"] = {
                "position": sp.get("position", {}),
                "rotation": sp.get("rotation", {}),
                "scale":    sp.get("scale",    {}),
            }
            step_file.dirty = True
            pkg.preview.dirty = True  # stepPoses[] is mutated below
            report.migrated += 1
            report.log(
                f"MIGRATED: part={part_id} stepId='{step_id}' "
                f"→ owner-task='{owner.get('id')}' (kind={owner.get('kind')})"
            )
            # sp is DROPPED (not added to survivors)

        if len(survivors) != len(step_poses):
            placement["stepPoses"] = survivors
            if not survivors:
                # Keep the (empty) field to match existing serialisation — the
                # runtime normaliser handles empty arrays identically to null.
                placement["stepPoses"] = []
            pkg.preview.dirty = True

    # toPose cleanup pass.
    for step_file, step in pkg.steps_by_id.values():
        for action in step.get("requiredToolActions", []) or []:
            interaction = action.get("interaction")
            if not interaction:
                continue
            tp = interaction.get("toPose")
            if not tp or not tp.startswith("step:"):
                continue
            ref_step_id = tp[len("step:"):]
            # Find the matching taskOrder entry to see if it now has endTransform.
            task_entry = None
            for e in step.get("taskOrder", []) or []:
                if e and e.get("kind") == "toolAction" and e.get("id") == action.get("id"):
                    task_entry = e
                    break

            if ref_step_id == step.get("id"):
                if task_entry and task_entry.get("endTransform"):
                    interaction["toPose"] = None
                    step_file.dirty = True
                    report.topose_cleared_self += 1
                    report.log(
                        f"CLEARED toPose (self-ref → endTransform): action='{action.get('id')}' "
                        f"stepId='{step.get('id')}'"
                    )
            else:
                # Cross-task ref — illegal under G.1 grammar. Clear and log.
                interaction["toPose"] = None
                step_file.dirty = True
                report.topose_cleared_cross += 1
                report.log(
                    f"CLEARED toPose (cross-task): action='{action.get('id')}' "
                    f"stepId='{step.get('id')}' had toPose='{tp}'"
                )

    if dry_run:
        return report

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    if pkg.preview.dirty:
        backup(pkg.pkg_dir, pkg.preview.path, timestamp)
        save_json(pkg.preview.path, pkg.preview.data)
    for lf in pkg.files:
        if lf.dirty:
            backup(pkg.pkg_dir, lf.path, timestamp)
            save_json(lf.path, lf.data)

    return report


# ── CLI ────────────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Bake stepPoses[] entries into TaskOrderEntry.endTransform (G.2.4).",
    )
    parser.add_argument("package_id", help="Package id (e.g. d3d_v18_10)")
    parser.add_argument("--dry-run", action="store_true",
                        help="Report only; do not write.")
    args = parser.parse_args()

    pkg = load_package(args.package_id)
    report = migrate_stepposes(pkg, dry_run=args.dry_run)

    # Summary.
    print(f"=== {args.package_id} ===")
    print(f"  Migrated:              {report.migrated}")
    print(f"  Conflicts (kept):      {report.conflicts}")
    print(f"  Orphans (kept):        {report.orphans}")
    print(f"  Skipped (auto NO-TASK):{report.skipped_synthetic}")
    print(f"  toPose cleared (self): {report.topose_cleared_self}")
    print(f"  toPose cleared (cross):{report.topose_cleared_cross}")

    # Per-entry details (verbose).
    if report.details:
        print("\n--- Details ---")
        for d in report.details[:200]:
            print(f"  {d}")
        if len(report.details) > 200:
            print(f"  … and {len(report.details) - 200} more lines.")

    if args.dry_run:
        print("\n(dry-run — no files written)")
    elif report.migrated or any(lf.dirty for lf in pkg.files) or pkg.preview.dirty:
        print("\nWrote changes. Backups under .migration_backups/ with pre_g224 suffix.")
        print("Next: run `python tools/package_health.py {0}` to verify.".format(args.package_id))
    else:
        print("\n(no changes to write)")


if __name__ == "__main__":
    main()
