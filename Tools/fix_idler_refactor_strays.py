"""
fix_idler_refactor_strays.py — Clean up 3 follow-on issues from the
batch_idler_build refactor:

  1. preview_config.json still references the old per-axis idler partGroup
     IDs (partGroup_y_left_idler_build, etc.) — point them at the new
     batch partGroup IDs.
  2. x_axis_bench has duplicate idler002 / idler002_half_b part definitions
     (cloner leak from an earlier session) — remove (canonical defs now
     live in batch_idler_build).
  3. x_axis_bench's partGroup_x_axis_belt_threading.partIds erroneously
     lists idler002 / idler002_half_b — strip them.
"""

from __future__ import annotations
import json
import os

PKG_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10"
)

PG_RENAME = {
    "partGroup_y_left_idler_build":  "partGroup_idler_y_left",
    "partGroup_y_right_idler_build": "partGroup_idler_y_right",
    "partGroup_z_back_idler_build":  "partGroup_idler_z_back",
    "partGroup_z_front_idler_build": "partGroup_idler_z_front",
}

LEAKED_PART_IDS = {"idler002", "idler002_half_b"}


def fix_preview_config():
    path = os.path.join(PKG_DIR, "preview_config.json")
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    changed = 0
    for entry in d.get("partGroupPlacements", []) or []:
        old = entry.get("partGroupId", "")
        if old in PG_RENAME:
            entry["partGroupId"] = PG_RENAME[old]
            changed += 1
            print(f"  ~ preview_config.partGroupPlacements: {old} -> {PG_RENAME[old]}")
    if changed:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(d, f, indent=2)
    print(f"  preview_config: {changed} partGroupId refs updated")


def fix_x_axis_bench():
    path = os.path.join(PKG_DIR, "assemblies", "assembly_d3d_x_axis_bench.json")
    with open(path, encoding="utf-8") as f:
        d = json.load(f)

    # 1. Remove leaked part definitions
    before_parts = len(d["parts"])
    d["parts"] = [pp for pp in d["parts"] if pp["id"] not in LEAKED_PART_IDS]
    removed_parts = before_parts - len(d["parts"])
    print(f"  x_axis_bench: removed {removed_parts} leaked part defs ({LEAKED_PART_IDS})")

    # 2. Strip leaked IDs from partGroup.partIds
    for pg in d.get("partGroups", []) or []:
        if "partIds" not in pg:
            continue
        before = list(pg["partIds"])
        pg["partIds"] = [p for p in pg["partIds"] if p not in LEAKED_PART_IDS]
        if before != pg["partIds"]:
            print(f"  x_axis_bench: stripped from {pg['id']}.partIds: "
                  f"{set(before) - set(pg['partIds'])}")

    with open(path, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)


def main():
    print("=== preview_config.json ===")
    fix_preview_config()
    print("\n=== x_axis_bench ===")
    fix_x_axis_bench()
    print("\nVerify: python tools/package_health.py d3d_v18_10")


if __name__ == "__main__":
    main()
