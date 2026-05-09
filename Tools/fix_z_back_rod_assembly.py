"""
fix_z_back_rod_assembly.py — Z-Back rod assembly: parts are wrong/missing.

Per Axes - D3D v18.10.pdf §6.1-6.5:
  6.1 Insert rods into idler, ends flush
  6.2 Tighten short idler bolts onto rods
  6.3 Slide carriage onto rods (long bolt ends near idler)
  6.4 Push motor holder onto rods
  6.5 QC: rods flush, carriage slides freely

Currently broken:
  step_z_back_rods_into_idler:    parts=['z1_spacer_1','z1_spacer_002']  ← spacers, not rods!
  step_z_back_carriage_onto_rods: parts=['motor_piece001']                ← motor, not carriage!
  step_z_back_motor_onto_rods:    parts=[]                                ← empty
  step_z_back_idler_tighten_rods: parts=[]                                ← OK (Use, no parts)
  step_z_back_rods_qc:            parts=[]                                ← OK (Confirm)

Fix:
  1. Define 2 new rod parts: rod_009, rod_010 (rod GLBs already exist).
  2. Re-point step parts to correct entities:
     - rods_into_idler  → [rod_009, rod_010]  (drop spacers)
     - carriage_onto_rods → [z_back_carriage_half_a]  (per single-half-place rule)
     - motor_onto_rods  → [motor_piece001]
  3. Spacers remain staged (z_back-specific; not in Y-Left manual procedure
     and not part of base rod assembly).
"""

from __future__ import annotations
import json
import os
import sys

ASM_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    "assemblies", "assembly_d3d_z_back_bench.json"
)

# Z-back rod staging position — back-of-frame area, slightly offset
ROD_STAGE_BASE = {"x": -0.6, "y": 0.55, "z": 3.6}

NEW_PARTS = [
    {
        "id": "z_back_rod_a",
        "displayName": "Z-Back Lower Guide Rod",
        "name": "Z-Back Lower Guide Rod",
        "assetRef": "rod_005_approved.glb",
        "category": "shaft",
        "material": "Polished steel guide rod (8mm)",
        "function": "One of two guide rods that the Z-back carriage slides along.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": -0.6, "y": 0.55, "z": 3.5},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_z_back_rod_assembly"],
    },
    {
        "id": "z_back_rod_b",
        "displayName": "Z-Back Upper Guide Rod",
        "name": "Z-Back Upper Guide Rod",
        "assetRef": "rod_006_approved.glb",
        "category": "shaft",
        "material": "Polished steel guide rod (8mm)",
        "function": "Second of two guide rods that the Z-back carriage slides along.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": -0.45, "y": 0.55, "z": 3.5},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_z_back_rod_assembly"],
    },
]


ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


# Step ID → fix recipe
STEP_FIXES = {
    "step_z_back_rods_into_idler": {
        "requiredPartIds": ["z_back_rod_a", "z_back_rod_b"],
        "taskOrder":       [task("z_back_rod_a"), task("z_back_rod_b")],
        "instructionText": (
            "Insert both Z-back guide rods into the completed idler piece. "
            "Rod ends must be flush against the bottom of the idler. If a "
            "rod won't slide in, loosen the idler bolts slightly until it does, "
            "then re-tighten."
        ),
    },
    "step_z_back_carriage_onto_rods": {
        "requiredPartIds": [],
        "taskOrder":       [],
        "instructionText": (
            "Slide the already-assembled Z-back carriage onto the rods with "
            "the long bolt ends closer to the idler. This orientation "
            "maximizes the Z-axis travel. The carriage was built earlier in "
            "the batch carriage stage — no new parts to place."
        ),
    },
    "step_z_back_motor_onto_rods": {
        "requiredPartIds": ["motor_piece001"],
        "taskOrder":       [task("motor_piece001")],
        "instructionText": (
            "Push the motor holder piece onto the rods so the motor faces the "
            "carriage bolt heads. Loosen bolts if needed to slide on."
        ),
    },
}


def main():
    with open(ASM_PATH, encoding="utf-8") as f:
        d = json.load(f)

    # 1. Add new rod parts
    existing = {p["id"] for p in d["parts"]}
    for np in NEW_PARTS:
        if np["id"] in existing:
            print(f"  skip part (exists): {np['id']}")
            continue
        d["parts"].append(np)
        print(f"  + part: {np['id']}")

    # 2. Apply fixes to each step
    fixes_applied = 0
    for s in d["steps"]:
        if s["id"] in STEP_FIXES:
            recipe = STEP_FIXES[s["id"]]
            old_parts = s.get("requiredPartIds", [])
            for k, v in recipe.items():
                s[k] = v
            fixes_applied += 1
            print(f"  ~ step: {s['id']}")
            print(f"      parts: {old_parts} → {recipe['requiredPartIds']}")

    if fixes_applied != len(STEP_FIXES):
        print(f"WARNING: applied {fixes_applied}/{len(STEP_FIXES)} fixes — some step IDs not found")

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")
    print("Now run: python tools/package_health.py d3d_v18_10")


if __name__ == "__main__":
    main()
