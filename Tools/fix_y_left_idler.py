"""
fix_y_left_idler.py — One-off: bring Y-Left idler procedure to manual parity.

Per Axes - D3D v18.10.pdf p.12-14, the IdlerHalves procedure is 6 steps:
  3.1 Insert M6x18 inner bolt           ✓ existing seq 118
  3.2 Stack two bearings, flanges out    ✓ existing seq 119
  3.3 Align rod holes (place half_b)     ✓ existing seq 120
  3.4 Tighten M6x18 with nut + drill     ✗ MISSING — adds new step
  3.5 Insert M6x30 frame-mount LOOSE      ⚠ existing seq 121 says "tighten" — fix
  3.6 Insert M6x18 in last hole, LOOSE    ✗ MISSING — adds new step

What this does:
  - Defines 4 new parts: 1 second M6x18 (loose) + 3 M6 nuts (inner, frame, loose)
  - Adds 2 new steps: tighten_inner (Use+Torque), last_bolt_loose (Place)
  - Modifies step_y_left_idler_bolt: rename + retext to "frame-mount LOOSE"
  - Updates partGroup.stepIds and assembly.stepIds in correct logical order
  - Uses fractional seqIndex (120.5, 121.5) so a follow-up
    `package_health.py --fix-seqindex` collapses to integer 1..N globally.
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


NEW_PARTS = [
    {
        "id": "y_left_idler_m6_nut_inner",
        "displayName": "Y-Left Idler M6 Nut (Inner Bolt)",
        "name": "Y-Left Idler M6 Hex Nut — Inner",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads onto the inner M6x18 (back side) so the bearings can be tightened against the idler halves.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": 1.05, "y": 0.55, "z": -0.05},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_y_left_idler_build"],
    },
    {
        "id": "y_left_idler_m6_nut_frame",
        "displayName": "Y-Left Idler M6 Nut (Frame Bolt)",
        "name": "Y-Left Idler M6 Hex Nut — Frame Mount",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads loosely onto the M6x30 frame-mount bolt — held loose so the idler can be repositioned during frame mounting.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": 1.13, "y": 0.55, "z": -0.05},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_y_left_idler_build"],
    },
    {
        "id": "y_left_idler_m6x18_loose",
        "displayName": "Y-Left Idler M6x18 Bolt (Last Hole, Loose)",
        "name": "Y-Left Idler M6x18 Loose Mount Bolt",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Inserted loosely through the last idler hole — kept loose with a nut for later frame attachment.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": 0.97, "y": 0.6, "z": -0.018},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_y_left_idler_build"],
    },
    {
        "id": "y_left_idler_m6_nut_loose",
        "displayName": "Y-Left Idler M6 Nut (Loose Bolt)",
        "name": "Y-Left Idler M6 Hex Nut — Loose Mount",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads loosely onto the second M6x18 in the last idler hole — kept loose for frame attachment.",
        "quantity": 1,
        "stagingPose": {
            "position": {"x": 0.97, "y": 0.55, "z": -0.05},
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
            "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
        },
        "partGroupIds": ["partGroup_y_left_idler_build"],
    },
]

ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}

NEW_STEP_TIGHTEN_INNER = {
    "id": "step_y_left_idler_tighten_inner",
    "name": "Tighten M6x18 inner bolt against bearings",
    "assemblyId": "assembly_d3d_y_left_bench",
    "partGroupId": "partGroup_y_left_idler_build",
    "sequenceIndex": 120.5,  # collapsed by --fix-seqindex
    "family": "Use",
    "profile": "Torque",
    "instructionText": (
        "Thread an M6 nut onto the back of the inner M6x18 bolt and tighten "
        "fully with the electric drill on its lowest torque setting. The "
        "bearings should be firmly clamped between the two idler halves."
    ),
    "removePersistentToolIds": [],
    "targetIds": [],
    "requiredPartIds": ["y_left_idler_m6_nut_inner"],
    "relevantToolIds": ["tool_power_drill"],
    "requiredToolActions": [],
    "taskOrder": [
        {
            "kind": "part",
            "id": "y_left_idler_m6_nut_inner",
            "endTransform": dict(ZERO_TRANSFORM),
        }
    ],
}

# Modified original step 121 — was "Install M6x30 belt-side bolt and tighten idler"
MODIFIED_STEP_FRAME_LOOSE = {
    "id": "step_y_left_idler_bolt",
    "name": "Insert M6x30 frame-mount bolt loosely",
    "assemblyId": "assembly_d3d_y_left_bench",
    "partGroupId": "partGroup_y_left_idler_build",
    "sequenceIndex": 121,
    "family": "Place",
    "instructionText": (
        "With the idler flat and the bearing hole pointing away from you, "
        "insert the M6x30 bolt through the top-right hole in the same "
        "direction as the inner bolt. Thread the M6 nut on the opposite "
        "side and keep it LOOSE — the idler will be tightened down later "
        "during frame mounting."
    ),
    "removePersistentToolIds": [],
    "targetIds": [],
    "requiredPartIds": [
        "y_left_idler_m6x30_a",
        "y_left_idler_m6_nut_frame",
    ],
    "requiredToolActions": [],
    "taskOrder": [
        {
            "kind": "part",
            "id": "y_left_idler_m6x30_a",
            "endTransform": dict(ZERO_TRANSFORM),
        },
        {
            "kind": "part",
            "id": "y_left_idler_m6_nut_frame",
            "endTransform": dict(ZERO_TRANSFORM),
        },
    ],
}

NEW_STEP_LAST_LOOSE = {
    "id": "step_y_left_idler_last_bolt_loose",
    "name": "Insert M6x18 in last idler hole loosely",
    "assemblyId": "assembly_d3d_y_left_bench",
    "partGroupId": "partGroup_y_left_idler_build",
    "sequenceIndex": 121.5,  # collapsed by --fix-seqindex
    "family": "Place",
    "instructionText": (
        "Insert the second M6x18 bolt through the last idler hole and thread "
        "an M6 nut on the back. Run the drill briefly so the nut catches the "
        "threads, but keep it visibly loose — like the M6x30, this stays "
        "loose for frame attachment."
    ),
    "removePersistentToolIds": [],
    "targetIds": [],
    "requiredPartIds": [
        "y_left_idler_m6x18_loose",
        "y_left_idler_m6_nut_loose",
    ],
    "relevantToolIds": ["tool_power_drill"],
    "requiredToolActions": [],
    "taskOrder": [
        {
            "kind": "part",
            "id": "y_left_idler_m6x18_loose",
            "endTransform": dict(ZERO_TRANSFORM),
        },
        {
            "kind": "part",
            "id": "y_left_idler_m6_nut_loose",
            "endTransform": dict(ZERO_TRANSFORM),
        },
    ],
}


def main():
    with open(ASM_PATH, encoding="utf-8") as f:
        d = json.load(f)

    # 1. Add 4 new parts
    existing_part_ids = {p["id"] for p in d["parts"]}
    for np in NEW_PARTS:
        if np["id"] in existing_part_ids:
            print(f"  skip part (exists): {np['id']}")
            continue
        d["parts"].append(np)
        print(f"  + part: {np['id']}")

    # 2. Replace step_y_left_idler_bolt (seq 121) with modified version
    found = False
    for i, s in enumerate(d["steps"]):
        if s["id"] == "step_y_left_idler_bolt":
            d["steps"][i] = MODIFIED_STEP_FRAME_LOOSE
            found = True
            print(f"  ~ step (modified): step_y_left_idler_bolt")
            break
    if not found:
        print("ERROR: step_y_left_idler_bolt not found")
        sys.exit(1)

    # 3. Add 2 new steps
    existing_step_ids = {s["id"] for s in d["steps"]}
    for ns in [NEW_STEP_TIGHTEN_INNER, NEW_STEP_LAST_LOOSE]:
        if ns["id"] in existing_step_ids:
            print(f"  skip step (exists): {ns['id']}")
            continue
        d["steps"].append(ns)
        print(f"  + step: {ns['id']} (seq {ns['sequenceIndex']})")

    # 4. Update partGroup.stepIds — insert tighten_inner after align_halves,
    #    and last_bolt_loose after bolt.
    for pg in d.get("partGroups", []):
        if pg["id"] == "partGroup_y_left_idler_build":
            ids = list(pg["stepIds"])
            for new_id, anchor in [
                ("step_y_left_idler_tighten_inner",   "step_y_left_idler_align_halves"),
                ("step_y_left_idler_last_bolt_loose", "step_y_left_idler_bolt"),
            ]:
                if new_id in ids:
                    continue
                idx = ids.index(anchor) + 1
                ids.insert(idx, new_id)
            pg["stepIds"] = ids
            print(f"  ~ partGroup.stepIds: {pg['stepIds']}")
            break

    # 5. Update assembly.stepIds — same insertions
    for a in d.get("assemblies", []):
        if a["id"] == "assembly_d3d_y_left_bench":
            ids = list(a["stepIds"])
            for new_id, anchor in [
                ("step_y_left_idler_tighten_inner",   "step_y_left_idler_align_halves"),
                ("step_y_left_idler_last_bolt_loose", "step_y_left_idler_bolt"),
            ]:
                if new_id in ids:
                    continue
                idx = ids.index(anchor) + 1
                ids.insert(idx, new_id)
            a["stepIds"] = ids
            print(f"  ~ assembly.stepIds count: {len(a['stepIds'])}")
            break

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")
    print("Now run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
