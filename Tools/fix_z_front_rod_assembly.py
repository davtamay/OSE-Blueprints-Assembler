"""
fix_z_front_rod_assembly.py — Z-Front rod assembly: same broken pattern as
Z-Back was (parts referenced spacers/motor_piece instead of rods/carriage).

Fix pattern mirrors fix_z_back_rod_assembly.py — adapted for z_front IDs.
"""

from __future__ import annotations
import json
import os

ASM_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    "assemblies", "assembly_d3d_z_front_bench.json"
)


NEW_PARTS = [
    {
        "id": "z_front_rod_a",
        "displayName": "Z-Front Lower Guide Rod",
        "name": "Z-Front Lower Guide Rod",
        "assetRef": "rod_005_approved.glb",
        "category": "shaft",
        "material": "Polished steel guide rod (8mm)",
        "function": "One of two guide rods that the Z-front carriage slides along.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": -0.85, "y": 0.55, "z": 1.7},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_z_front_rod_assembly"],
    },
    {
        "id": "z_front_rod_b",
        "displayName": "Z-Front Upper Guide Rod",
        "name": "Z-Front Upper Guide Rod",
        "assetRef": "rod_006_approved.glb",
        "category": "shaft",
        "material": "Polished steel guide rod (8mm)",
        "function": "Second of two guide rods that the Z-front carriage slides along.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": -0.7, "y": 0.55, "z": 1.7},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_z_front_rod_assembly"],
    },
]

ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


STEP_FIXES = {
    "step_z_front_rods_into_idler": {
        "requiredPartIds": ["z_front_rod_a", "z_front_rod_b"],
        "taskOrder":       [task("z_front_rod_a"), task("z_front_rod_b")],
        "instructionText": (
            "Insert both Z-front guide rods into the completed idler piece. "
            "Rod ends must be flush against the bottom of the idler. If a rod "
            "won't slide in, loosen the idler bolts slightly until it does, "
            "then re-tighten."
        ),
    },
    "step_z_front_carriage_onto_rods": {
        "requiredPartIds": [],
        "taskOrder":       [],
        "instructionText": (
            "Slide the already-assembled Z-front carriage onto the rods with "
            "the long bolt ends closer to the idler. The carriage was built "
            "earlier in the batch carriage stage — no new parts to place."
        ),
    },
    "step_z_front_motor_onto_rods": {
        "requiredPartIds": ["motor_piece"],
        "taskOrder":       [task("motor_piece")],
        "instructionText": (
            "Push the motor holder piece onto the rods so the motor faces the "
            "carriage bolt heads. Loosen bolts if needed to slide on."
        ),
    },
}


def main():
    with open(ASM_PATH, encoding="utf-8") as f:
        d = json.load(f)

    existing = {p["id"] for p in d["parts"]}
    for np in NEW_PARTS:
        if np["id"] in existing:
            print(f"  skip part (exists): {np['id']}")
            continue
        d["parts"].append(np)
        print(f"  + part: {np['id']}")

    for s in d["steps"]:
        if s["id"] in STEP_FIXES:
            recipe = STEP_FIXES[s["id"]]
            old_parts = s.get("requiredPartIds", [])
            for k, v in recipe.items():
                s[k] = v
            print(f"  ~ step: {s['id']}")
            print(f"      parts: {old_parts} → {recipe['requiredPartIds']}")

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")


if __name__ == "__main__":
    main()
