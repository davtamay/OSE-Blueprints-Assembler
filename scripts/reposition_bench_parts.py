"""
Reposition all Y-left bench-build parts to a tight workbench layout
to the RIGHT of the frame (positive X).

Layout:
- Workbench origin at x=1.0, z=0.0 (right of frame)
- Parts on a ~0.8m x 0.6m table area, y=0.55 (bench height)
- Row 1 (z=0.0): Main printed parts (carriage, idler, motor holder, rods, etc.)
- Row 2 (z=0.15): Hardware for carriage subassembly (bearings, bolts)
- Row 3 (z=0.30): Hardware for idler + motor subassembly (bearings, pulley, belt, nuts, screws)
- Tight 0.1m spacing so it looks like parts laid out on a table
"""

import json
import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MACHINE_JSON = os.path.join(
    REPO_ROOT, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json"
)

# Bench origin — to the right of frame
BX = 1.0   # bench center X
BY = 0.55  # bench surface height
BZ = 0.0   # bench center Z

# Part layout: (partId, dx, dz) — offset from bench origin
# dx = left-right on bench, dz = front-back
# Grouped by subassembly for visual clarity

BENCH_LAYOUT = {
    # ── Row 0 (dz=0.0): Main printed parts ──
    "full_carriage":  (-0.30, 0.0),    # carriage
    "idler002":       (-0.15, 0.0),    # idler
    "motor002":       ( 0.00, 0.0),    # motor + holder
    "rod_005":        ( 0.15, 0.0),    # guide rod A
    "rod_006":        ( 0.30, 0.0),    # guide rod B
    "y1_bracket":     ( 0.45, 0.0),    # mounting bracket
    "pocket039":      ( 0.55, 0.0),    # bearing block A
    "pocket040":      ( 0.65, 0.0),    # bearing block B
    "y_endstop":      ( 0.75, 0.0),    # endstop

    # ── Row 1 (dz=0.15): Carriage hardware ──
    "y_left_lm8uu_a": (-0.35, 0.15),  # LM8UU bearing 1
    "y_left_lm8uu_b": (-0.27, 0.15),  # LM8UU bearing 2
    "y_left_lm8uu_c": (-0.19, 0.15),  # LM8UU bearing 3
    "y_left_lm8uu_d": (-0.11, 0.15),  # LM8UU bearing 4
    "y_left_m6x18_a": (-0.03, 0.15),  # M6x18 carriage bolt

    # ── Row 1 cont (dz=0.15): Idler hardware ──
    "y_left_625zz_a": ( 0.08, 0.15),  # 625ZZ bearing 1
    "y_left_625zz_b": ( 0.16, 0.15),  # 625ZZ bearing 2
    "y_left_m6x18_b": ( 0.24, 0.15),  # M6x18 idler bolt

    # ── Row 2 (dz=0.30): Motor hardware ──
    "y_left_gt2_pulley": (-0.35, 0.30),  # GT2 pulley
    "y_left_gt2_belt":   (-0.22, 0.30),  # GT2 belt
    "y_left_m6_nut_a":   (-0.10, 0.30),  # M6 nut 1
    "y_left_m6_nut_b":   (-0.02, 0.30),  # M6 nut 2
    "y_left_m6_nut_c":   ( 0.06, 0.30),  # M6 nut 3
    "y_left_m3x25_a":    ( 0.16, 0.30),  # M3x25 screw 1
    "y_left_m3x25_b":    ( 0.24, 0.30),  # M3x25 screw 2
    "y_left_m3x25_c":    ( 0.32, 0.30),  # M3x25 screw 3
    "y_left_m3x25_d":    ( 0.40, 0.30),  # M3x25 screw 4
    "y_left_m6x18_c":    ( 0.50, 0.30),  # M6x18 motor bolt
}


def main():
    print(f"Reading {MACHINE_JSON}")
    with open(MACHINE_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    placements = data.get("previewConfig", {}).get("partPlacements", [])
    placement_map = {p["partId"]: p for p in placements}

    updated = 0
    for part_id, (dx, dz) in BENCH_LAYOUT.items():
        pp = placement_map.get(part_id)
        if pp is None:
            print(f"  WARNING: no placement for {part_id}")
            continue

        x = round(BX + dx, 4)
        z = round(BZ + dz, 4)
        old_x = pp["startPosition"]["x"]
        old_z = pp["startPosition"]["z"]

        pp["startPosition"]["x"] = x
        pp["startPosition"]["y"] = BY
        pp["startPosition"]["z"] = z

        print(f"  {part_id}: ({old_x}, {old_z}) -> ({x}, {z})")
        updated += 1

    print(f"\nUpdated {updated} placements.")

    with open(MACHINE_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("Done.")


if __name__ == "__main__":
    main()
