"""
Inject hardware part definitions into machine.json for Y-left bench assembly.
Adds part entries for the 7 new GLB types and wires them into subassembly partIds
and step requiredPartIds.
"""

import json
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MACHINE_JSON = os.path.join(
    REPO_ROOT, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json"
)

# ── New hardware part definitions ──────────────────────────────
# Each entry: (id, displayName, name, assetRef, category, material, function, quantity)
NEW_PARTS = [
    # LM8UU linear bearings — 4 for Y-left carriage
    ("y_left_lm8uu_a", "Y-Left LM8UU A", "Y-Left Linear Bearing A",
     "d3d_axis_lm8uu_bearing.glb", "bearing",
     "LM8UU linear ball bearing",
     "Linear bearing seated in Y-left carriage half for smooth rod travel.", 1),
    ("y_left_lm8uu_b", "Y-Left LM8UU B", "Y-Left Linear Bearing B",
     "d3d_axis_lm8uu_bearing.glb", "bearing",
     "LM8UU linear ball bearing",
     "Linear bearing seated in Y-left carriage half for smooth rod travel.", 1),
    ("y_left_lm8uu_c", "Y-Left LM8UU C", "Y-Left Linear Bearing C",
     "d3d_axis_lm8uu_bearing.glb", "bearing",
     "LM8UU linear ball bearing",
     "Linear bearing seated in Y-left carriage half for smooth rod travel.", 1),
    ("y_left_lm8uu_d", "Y-Left LM8UU D", "Y-Left Linear Bearing D",
     "d3d_axis_lm8uu_bearing.glb", "bearing",
     "LM8UU linear ball bearing",
     "Linear bearing seated in Y-left carriage half for smooth rod travel.", 1),

    # 625ZZ flanged bearings — 2 for Y-left idler
    ("y_left_625zz_a", "Y-Left 625ZZ A", "Y-Left Idler Bearing A",
     "d3d_axis_625zz_bearing.glb", "bearing",
     "625ZZ flanged ball bearing",
     "Flanged bearing stacked on idler bolt, flanges facing outward.", 1),
    ("y_left_625zz_b", "Y-Left 625ZZ B", "Y-Left Idler Bearing B",
     "d3d_axis_625zz_bearing.glb", "bearing",
     "625ZZ flanged ball bearing",
     "Flanged bearing stacked on idler bolt, flanges facing outward.", 1),

    # GT2 pulley — 1 for Y-left motor
    ("y_left_gt2_pulley", "Y-Left GT2 Pulley", "Y-Left Motor Pulley",
     "d3d_axis_gt2_pulley_19t.glb", "drive",
     "GT2 19-tooth aluminum timing pulley",
     "Press-fit onto motor shaft with spacer; drives GT2 timing belt.", 1),

    # GT2 belt — 1 for Y-left belt threading
    ("y_left_gt2_belt", "Y-Left GT2 Belt", "Y-Left Timing Belt",
     "d3d_axis_gt2_belt.glb", "drive",
     "GT2 timing belt, closed loop",
     "Routed through carriage belt holes, around idler bearing, and pegged.", 1),

    # M6x18 bolts — 3 for Y-left (carriage top, idler, motor holder)
    ("y_left_m6x18_a", "Y-Left M6x18 A", "Y-Left M6x18 Bolt A",
     "d3d_axis_m6x18_bolt.glb", "fastener",
     "M6x18 hex bolt, zinc-plated",
     "Secures carriage halves at top position.", 1),
    ("y_left_m6x18_b", "Y-Left M6x18 B", "Y-Left M6x18 Bolt B",
     "d3d_axis_m6x18_bolt.glb", "fastener",
     "M6x18 hex bolt, zinc-plated",
     "Secures idler halves together.", 1),
    ("y_left_m6x18_c", "Y-Left M6x18 C", "Y-Left M6x18 Bolt C",
     "d3d_axis_m6x18_bolt.glb", "fastener",
     "M6x18 hex bolt, zinc-plated",
     "Secures motor holder halves.", 1),

    # M3x25 SHCS — 4 for Y-left motor mount
    ("y_left_m3x25_a", "Y-Left M3x25 A", "Y-Left Motor Screw A",
     "d3d_axis_m3x25_shcs.glb", "fastener",
     "M3x25 socket head cap screw",
     "Mounts NEMA17 motor to motor holder.", 1),
    ("y_left_m3x25_b", "Y-Left M3x25 B", "Y-Left Motor Screw B",
     "d3d_axis_m3x25_shcs.glb", "fastener",
     "M3x25 socket head cap screw",
     "Mounts NEMA17 motor to motor holder.", 1),
    ("y_left_m3x25_c", "Y-Left M3x25 C", "Y-Left Motor Screw C",
     "d3d_axis_m3x25_shcs.glb", "fastener",
     "M3x25 socket head cap screw",
     "Mounts NEMA17 motor to motor holder.", 1),
    ("y_left_m3x25_d", "Y-Left M3x25 D", "Y-Left Motor Screw D",
     "d3d_axis_m3x25_shcs.glb", "fastener",
     "M3x25 socket head cap screw",
     "Mounts NEMA17 motor to motor holder.", 1),

    # M6 nuts — 3 for Y-left motor holder
    ("y_left_m6_nut_a", "Y-Left M6 Nut A", "Y-Left Hex Nut A",
     "d3d_axis_m6_nut.glb", "fastener",
     "M6 hex nut, zinc-plated",
     "Pre-loaded into motor holder before motor blocks access.", 1),
    ("y_left_m6_nut_b", "Y-Left M6 Nut B", "Y-Left Hex Nut B",
     "d3d_axis_m6_nut.glb", "fastener",
     "M6 hex nut, zinc-plated",
     "Pre-loaded into motor holder before motor blocks access.", 1),
    ("y_left_m6_nut_c", "Y-Left M6 Nut C", "Y-Left Hex Nut C",
     "d3d_axis_m6_nut.glb", "fastener",
     "M6 hex nut, zinc-plated",
     "Pre-loaded into motor holder before motor blocks access.", 1),
]

# ── Step → part wiring ─────────────────────────────────────────
# Map step IDs to the new hardware part IDs they should reference
STEP_PART_WIRING = {
    "step_y_left_carriage_place_bearings": [
        "y_left_lm8uu_a", "y_left_lm8uu_b", "y_left_lm8uu_c", "y_left_lm8uu_d"
    ],
    "step_y_left_carriage_bolt": [
        "y_left_m6x18_a"
    ],
    "step_y_left_idler_insert_bolt": [
        "y_left_m6x18_b"
    ],
    "step_y_left_idler_insert_bearings": [
        "y_left_625zz_a", "y_left_625zz_b"
    ],
    "step_y_left_motor_pulley": [
        "y_left_gt2_pulley"
    ],
    "step_y_left_motor_half_nuts": [
        "y_left_m6_nut_a", "y_left_m6_nut_b", "y_left_m6_nut_c"
    ],
    "step_y_left_motor_belt_channel": [
        "y_left_gt2_belt"
    ],
    "step_y_left_motor_screws": [
        "y_left_m3x25_a", "y_left_m3x25_b", "y_left_m3x25_c", "y_left_m3x25_d"
    ],
    "step_y_left_motor_m6_bolts": [
        "y_left_m6x18_c"
    ],
}

# ── Subassembly → part wiring ──────────────────────────────────
SUBASSEMBLY_PART_WIRING = {
    "subassembly_y_left_carriage_build": [
        "y_left_lm8uu_a", "y_left_lm8uu_b", "y_left_lm8uu_c", "y_left_lm8uu_d",
        "y_left_m6x18_a"
    ],
    "subassembly_y_left_idler_build": [
        "y_left_625zz_a", "y_left_625zz_b", "y_left_m6x18_b"
    ],
    "subassembly_y_left_motor_build": [
        "y_left_gt2_pulley", "y_left_gt2_belt",
        "y_left_m6_nut_a", "y_left_m6_nut_b", "y_left_m6_nut_c",
        "y_left_m3x25_a", "y_left_m3x25_b", "y_left_m3x25_c", "y_left_m3x25_d",
        "y_left_m6x18_c"
    ],
}


def main():
    print(f"Reading {MACHINE_JSON}")
    with open(MACHINE_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    # 1. Add part definitions
    existing_ids = {p["id"] for p in data["parts"]}
    added = 0
    for pid, display, name, asset, cat, mat, func, qty in NEW_PARTS:
        if pid in existing_ids:
            print(f"  SKIP (exists): {pid}")
            continue
        data["parts"].append({
            "id": pid,
            "displayName": display,
            "name": name,
            "assetRef": asset,
            "category": cat,
            "material": mat,
            "function": func,
            "quantity": qty,
        })
        added += 1
        print(f"  ADD: {pid}")
    print(f"Added {added} part definitions.\n")

    # 2. Wire into subassembly partIds
    sub_map = {s["id"]: s for s in data["subassemblies"]}
    for sub_id, part_ids in SUBASSEMBLY_PART_WIRING.items():
        sub = sub_map.get(sub_id)
        if not sub:
            print(f"  WARNING: subassembly {sub_id} not found")
            continue
        existing = set(sub.get("partIds", []))
        for pid in part_ids:
            if pid not in existing:
                sub.setdefault("partIds", []).append(pid)
                print(f"  {sub_id} += {pid}")

    # 3. Wire into step requiredPartIds
    step_map = {s["id"]: s for s in data["steps"]}
    for step_id, part_ids in STEP_PART_WIRING.items():
        step = step_map.get(step_id)
        if not step:
            print(f"  WARNING: step {step_id} not found")
            continue
        existing = set(step.get("requiredPartIds", []))
        for pid in part_ids:
            if pid not in existing:
                step.setdefault("requiredPartIds", []).append(pid)
                print(f"  {step_id} += {pid}")

    # 4. Write back
    print(f"\nWriting {MACHINE_JSON}")
    with open(MACHINE_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("Done.")


if __name__ == "__main__":
    main()
