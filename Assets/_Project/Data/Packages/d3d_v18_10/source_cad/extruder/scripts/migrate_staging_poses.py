#!/usr/bin/env python3
"""
migrate_staging_poses.py
========================
Phase 1 migration: move startPosition/Rotation/Scale/color from
previewConfig.partPlacements[] into parts[].stagingPose in machine.json.

After running this script:
  - parts[].stagingPose is the canonical source of truth for staging positions
  - previewConfig.partPlacements[].startPosition/Rotation/Scale/color are left in place
    (the runtime normalizer bakes stagingPose -> previewConfig at load time, so both
     are consistent; the old previewConfig values serve as a legacy fallback for any
     package that hasn't been migrated)

Usage:
    python migrate_staging_poses.py [--dry-run]

Run from the repository root or any location; it resolves machine.json relative
to the script's own location (../../machine.json).
"""

import json
import sys
import copy
import os
import datetime

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MACHINE_JSON = os.path.normpath(
    os.path.join(SCRIPT_DIR, "../../../machine.json")
)


def is_zero_float3(v: dict) -> bool:
    return v.get("x", 0) == 0 and v.get("y", 0) == 0 and v.get("z", 0) == 0


def is_zero_float4(v: dict) -> bool:
    return v.get("r", 0) == 0 and v.get("g", 0) == 0 and v.get("b", 0) == 0 and v.get("a", 0) == 0


def is_identity_quat(v: dict) -> bool:
    return (v.get("x", 0) == 0 and v.get("y", 0) == 0 and
            v.get("z", 0) == 0 and v.get("w", 0) == 0)


def round_float3(v: dict, decimals: int = 4) -> dict:
    return {k: round(float(v.get(k, 0)), decimals) for k in ("x", "y", "z")}


def round_quat(v: dict, decimals: int = 4) -> dict:
    return {k: round(float(v.get(k, 0)), decimals) for k in ("x", "y", "z", "w")}


def round_float4(v: dict, decimals: int = 4) -> dict:
    return {k: round(float(v.get(k, 0)), decimals) for k in ("r", "g", "b", "a")}


def main(dry_run: bool = False):
    if not os.path.exists(MACHINE_JSON):
        print(f"ERROR: machine.json not found at {MACHINE_JSON}")
        sys.exit(1)

    print(f"Loading: {MACHINE_JSON}")
    with open(MACHINE_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    parts = data.get("parts", [])
    part_placements = (data.get("previewConfig") or {}).get("partPlacements", [])

    # Build lookup: partId -> partPlacement
    pp_by_id = {}
    for pp in part_placements:
        if pp and pp.get("partId"):
            pp_by_id[pp["partId"]] = pp

    migrated = 0
    skipped_no_position = 0
    skipped_already_has_staging = 0

    for part in parts:
        if not part or not part.get("id"):
            continue

        part_id = part["id"]
        pp = pp_by_id.get(part_id)

        if not pp:
            continue

        # Skip if already has stagingPose
        if part.get("stagingPose"):
            skipped_already_has_staging += 1
            continue

        start_pos = pp.get("startPosition", {})
        start_rot = pp.get("startRotation", {})
        start_scl = pp.get("startScale", {})
        color     = pp.get("color", {})

        # Skip if no meaningful staging position was ever set
        if is_zero_float3(start_pos):
            skipped_no_position += 1
            continue

        staging = {
            "position": round_float3(start_pos),
            "rotation": round_quat(start_rot),
        }

        # Only include scale if non-zero (normalizer treats zero scale as "use default")
        if not is_zero_float3(start_scl):
            staging["scale"] = round_float3(start_scl)

        # Only include color if non-zero alpha
        if not is_zero_float4(color) and color.get("a", 0) > 0:
            staging["color"] = round_float4(color)

        part["stagingPose"] = staging
        migrated += 1

    print(f"\nResults:")
    print(f"  Migrated:                    {migrated}")
    print(f"  Skipped (zero startPos):     {skipped_no_position}")
    print(f"  Skipped (already has pose):  {skipped_already_has_staging}")
    print(f"  Total parts:                 {len(parts)}")

    if dry_run:
        print("\n[DRY RUN] No changes written.")
        return

    # Backup
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    backup_dir = os.path.join(os.path.dirname(MACHINE_JSON), ".migration_backups")
    os.makedirs(backup_dir, exist_ok=True)
    backup_path = os.path.join(backup_dir, f"machine_{ts}_pre_stagingpose.json")
    import shutil
    shutil.copy2(MACHINE_JSON, backup_path)
    print(f"\nBackup: {backup_path}")

    with open(MACHINE_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"Written: {MACHINE_JSON}")
    print(f"\nNext step: open Unity and run 'OSE > Validate All Packages' to confirm 0 errors.")
    print("The normalizer will bake stagingPose -> previewConfig.startPosition at load time.")


if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    main(dry_run=dry_run)
