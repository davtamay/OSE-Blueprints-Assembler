"""
fix_y_left_motor_holder_bolts.py — One-off: Y-Left motor holder M6x18 parity.

Per Axes - D3D v18.10.pdf p.21 (§5.4 continued):
  "Insert M6x18 bolts into the three remaining holes.
   Tighten each into the nut using an electric drill."

Currently:
  step_y_left_motor_m6_bolts (seq 131): family=Place, requiredPartIds=[y_left_m6x18_c]
    → only 1 of the 3 bolts is referenced
    → no follow-up Use+Torque tighten step

Fix:
  1. Add 2 new parts: y_left_motor_holder_m6x18_b, _c
  2. Update existing step to include all 3 bolts in requiredPartIds + taskOrder
  3. Add new Use+Torque step after it: tighten motor holder M6x18 bolts
  4. Update partGroup.stepIds and assembly.stepIds
"""

from __future__ import annotations
import json
import os
import sys

ASM_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    "assemblies", "assembly_d3d_y_left_bench.json"
)


def m6x18_part(suffix_letter: str, x: float) -> dict:
    return {
        "id": f"y_left_motor_holder_m6x18_{suffix_letter}",
        "displayName": f"Y-Left Motor Holder M6x18 Bolt {suffix_letter.upper()}",
        "name": f"Y-Left Motor Holder M6x18 Bolt {suffix_letter.upper()}",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Secures motor holder half-pieces. Tightened with electric drill into the embedded M6 nut.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": x, "y": 0.6, "z": -0.018},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_y_left_motor_build"],
    }


NEW_PARTS = [
    m6x18_part("b", 0.94),
    m6x18_part("c", 0.96),
]

ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}

ALL_THREE_M6X18 = [
    "y_left_m6x18_c",                      # existing
    "y_left_motor_holder_m6x18_b",         # new
    "y_left_motor_holder_m6x18_c",         # new
]


NEW_STEP_TIGHTEN = {
    "id": "step_y_left_motor_m6_bolts_tighten",
    "name": "Tighten motor holder M6x18 bolts with drill",
    "assemblyId": "assembly_d3d_y_left_bench",
    "partGroupId": "partGroup_y_left_motor_build",
    "sequenceIndex": 131.5,  # collapsed by --fix-seqindex
    "family": "Use",
    "profile": "Torque",
    "instructionText": (
        "Tighten each of the 3 motor-holder M6x18 bolts into its embedded "
        "nut using the electric drill. The motor holder is now fully "
        "clamped to the holder pieces."
    ),
    "removePersistentToolIds": [],
    "targetIds": [],
    "requiredPartIds": [],
    "relevantToolIds": ["tool_power_drill"],
    "requiredToolActions": [],
    "taskOrder": [],
}


def main():
    with open(ASM_PATH, encoding="utf-8") as f:
        d = json.load(f)

    # 1. Add new parts
    existing = {p["id"] for p in d["parts"]}
    for np in NEW_PARTS:
        if np["id"] in existing:
            print(f"  skip part (exists): {np['id']}")
            continue
        d["parts"].append(np)
        print(f"  + part: {np['id']}")

    # 2. Update existing motor M6x18 step to reference all 3 + taskOrder
    found = False
    for s in d["steps"]:
        if s["id"] == "step_y_left_motor_m6_bolts":
            s["requiredPartIds"] = list(ALL_THREE_M6X18)
            s["instructionText"] = (
                "Insert M6x18 bolts into the three remaining holes of the "
                "motor holder, threading each into its embedded nut. Hand-"
                "snug for now — the next step uses the drill."
            )
            s["taskOrder"] = [
                {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}
                for pid in ALL_THREE_M6X18
            ]
            found = True
            print(f"  ~ step (now references 3 bolts): step_y_left_motor_m6_bolts")
            break
    if not found:
        print("ERROR: step_y_left_motor_m6_bolts not found")
        sys.exit(1)

    # 3. Add new tighten step
    if any(s["id"] == NEW_STEP_TIGHTEN["id"] for s in d["steps"]):
        print(f"  skip step (exists): {NEW_STEP_TIGHTEN['id']}")
    else:
        d["steps"].append(NEW_STEP_TIGHTEN)
        print(f"  + step: {NEW_STEP_TIGHTEN['id']} (seq 131.5)")

    # 4. Update partGroup.stepIds — insert tighten after motor_m6_bolts
    for pg in d.get("partGroups", []):
        if pg["id"] == "partGroup_y_left_motor_build":
            ids = list(pg["stepIds"])
            if NEW_STEP_TIGHTEN["id"] not in ids:
                idx = ids.index("step_y_left_motor_m6_bolts") + 1
                ids.insert(idx, NEW_STEP_TIGHTEN["id"])
                pg["stepIds"] = ids
                print(f"  ~ partGroup.stepIds (motor): +tighten step")
            break

    # 5. Update assembly.stepIds — same insertion
    for a in d.get("assemblies", []):
        if a["id"] == "assembly_d3d_y_left_bench":
            ids = list(a["stepIds"])
            if NEW_STEP_TIGHTEN["id"] not in ids:
                idx = ids.index("step_y_left_motor_m6_bolts") + 1
                ids.insert(idx, NEW_STEP_TIGHTEN["id"])
                a["stepIds"] = ids
                print(f"  ~ assembly.stepIds count: {len(a['stepIds'])}")
            break

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")
    print("Now run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
