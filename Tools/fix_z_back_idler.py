"""
fix_z_back_idler.py — Mirror Y-Left's manual-correct 6-step idler procedure
into Z-Back. Z-Back uses the same plastic piece type as Y-Left per the manual.

Per Axes - D3D v18.10.pdf p.12-14, IdlerHalves is 6 steps:
  3.1 Insert M6x18 inner bolt
  3.2 Stack two bearings, flanges out
  3.3 Place + align second half
  3.4 Tighten M6x18 with nut + drill
  3.5 Insert M6x30 frame-mount LOOSE
  3.6 Insert M6x18 in last hole, LOOSE

Currently Z-Back has 4 skeleton steps with only `idler001` defined and
mostly empty requiredPartIds.

This script:
  1. Adds idler001_half_b + 2 bearings + 1 second M6x18 + 1 M6x30 + 3 nuts (9 new parts)
  2. Renames idler001 ("Leadscrew Idler" → "Belt Idler") per manual
  3. Replaces 4 skeleton steps with 6 correct steps mirroring Y-Left
  4. Updates partGroup.stepIds + assembly.stepIds in the new order
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

# Z-back idler staging origin (offset from Y-Left's authoring origin).
# Z-back is at the back of the frame: -X area, high Y, +Z (back face).
BASE_X = -0.85
BASE_Y = 0.55
BASE_Z = 3.6


def staged(x_off=0.0, y_off=0.0, z_off=0.0):
    return {
        "position": {"x": BASE_X + x_off, "y": BASE_Y + y_off, "z": BASE_Z + z_off},
        "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
        "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
        "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
    }


NEW_PARTS = [
    {
        "id": "idler001_half_b",
        "displayName": "Z-Back Idler Clamping Half",
        "name": "Z-Back Belt Idler Clamping Half",
        "assetRef": "idler_approved.glb",
        "category": "custom",
        "material": "Printed idler half",
        "function": "Mates with idler001 to capture the flanged bearings on the M6x18 bolt.",
        "quantity": 1,
        "stagingPose": staged(0.05, 0.0, 0.05),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_625zz_a",
        "displayName": "Z-Back Idler Bearing A",
        "name": "Z-Back Idler 625ZZ Flanged Bearing A",
        "assetRef": "d3d_axis_625zz_bearing.glb",
        "category": "fastener",
        "material": "Steel flanged ball bearing",
        "function": "Lower idler bearing — flanges face outward to keep the belt centered.",
        "quantity": 1,
        "stagingPose": staged(0.25, 0.0, 0.05),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_625zz_b",
        "displayName": "Z-Back Idler Bearing B",
        "name": "Z-Back Idler 625ZZ Flanged Bearing B",
        "assetRef": "d3d_axis_625zz_bearing.glb",
        "category": "fastener",
        "material": "Steel flanged ball bearing",
        "function": "Upper idler bearing — flanges face outward (opposite of bearing A).",
        "quantity": 1,
        "stagingPose": staged(0.33, 0.0, 0.05),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_idler_m6x18_inner",
        "displayName": "Z-Back Idler M6x18 Inner Bolt",
        "name": "Z-Back Idler M6x18 Inner Bolt",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Inner bolt that captures the two flanged bearings between the idler halves.",
        "quantity": 1,
        "stagingPose": staged(0.09, 0.05, -0.118),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_idler_m6_nut_inner",
        "displayName": "Z-Back Idler M6 Nut (Inner Bolt)",
        "name": "Z-Back Idler M6 Hex Nut — Inner",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads onto the inner M6x18 (back side) so bearings can be tightened against the halves.",
        "quantity": 1,
        "stagingPose": staged(0.22, 0.0, -0.15),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_idler_m6x30_frame",
        "displayName": "Z-Back Idler Frame-Mount M6x30 Bolt",
        "name": "Z-Back Idler M6x30 Frame-Mount Bolt",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Long bolt installed loosely in the top-right idler hole — used later for frame mounting.",
        "quantity": 1,
        "stagingPose": staged(0.27, 0.05, -0.118),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_idler_m6_nut_frame",
        "displayName": "Z-Back Idler M6 Nut (Frame Bolt)",
        "name": "Z-Back Idler M6 Hex Nut — Frame Mount",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads loosely onto the M6x30 frame-mount bolt — held loose for frame mounting.",
        "quantity": 1,
        "stagingPose": staged(0.30, 0.0, -0.15),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_idler_m6x18_loose",
        "displayName": "Z-Back Idler M6x18 (Last Hole, Loose)",
        "name": "Z-Back Idler M6x18 Loose Mount Bolt",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Inserted loosely through the last idler hole — kept loose with a nut for frame attachment.",
        "quantity": 1,
        "stagingPose": staged(0.13, 0.05, -0.118),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
    {
        "id": "z_back_idler_m6_nut_loose",
        "displayName": "Z-Back Idler M6 Nut (Loose Bolt)",
        "name": "Z-Back Idler M6 Hex Nut — Loose Mount",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads loosely onto the second M6x18 in the last idler hole — kept loose for frame attachment.",
        "quantity": 1,
        "stagingPose": staged(0.13, 0.0, -0.15),
        "partGroupIds": ["partGroup_z_back_idler_build"],
    },
]


ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def _to(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


# 6 manual-correct steps. seqIndex floats are collapsed by --fix-seqindex.
NEW_STEPS = [
    {
        "id": "step_z_back_idler_insert_bolt",
        "name": "Insert M6x18 bolt into idler half",
        "assemblyId": "assembly_d3d_z_back_bench",
        "partGroupId": "partGroup_z_back_idler_build",
        "sequenceIndex": 244.0,  # placeholder; renumbered globally
        "family": "Place",
        "instructionText": "Begin assembling the idler: place an M6x18 bolt through one idler half-piece from the outside.",
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["idler001", "z_back_idler_m6x18_inner"],
        "requiredToolActions": [],
        "taskOrder": [_to("idler001"), _to("z_back_idler_m6x18_inner")],
    },
    {
        "id": "step_z_back_idler_insert_bearings",
        "name": "Stack two flanged bearings onto bolt",
        "assemblyId": "assembly_d3d_z_back_bench",
        "partGroupId": "partGroup_z_back_idler_build",
        "sequenceIndex": 244.1,
        "family": "Place",
        "instructionText": "Insert two bearing pieces one on top of the other onto the bolt, flanges facing outward (away from each other).",
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["z_back_625zz_a", "z_back_625zz_b"],
        "requiredToolActions": [],
        "taskOrder": [_to("z_back_625zz_a"), _to("z_back_625zz_b")],
    },
    {
        "id": "step_z_back_idler_align_halves",
        "name": "Place second idler half and align rod holes",
        "assemblyId": "assembly_d3d_z_back_bench",
        "partGroupId": "partGroup_z_back_idler_build",
        "sequenceIndex": 244.2,
        "family": "Place",
        "instructionText": "Place the second idler half against the first. Confirm the circular rod holes on both halves line up exactly so the rods will pass through cleanly.",
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["idler001_half_b"],
        "requiredToolActions": [],
        "taskOrder": [_to("idler001_half_b")],
    },
    {
        "id": "step_z_back_idler_tighten_inner",
        "name": "Tighten M6x18 inner bolt against bearings",
        "assemblyId": "assembly_d3d_z_back_bench",
        "partGroupId": "partGroup_z_back_idler_build",
        "sequenceIndex": 244.3,
        "family": "Use",
        "profile": "Torque",
        "instructionText": "Thread an M6 nut onto the back of the inner M6x18 bolt and tighten fully with the electric drill on its lowest torque setting.",
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["z_back_idler_m6_nut_inner"],
        "relevantToolIds": ["tool_power_drill"],
        "requiredToolActions": [],
        "taskOrder": [_to("z_back_idler_m6_nut_inner")],
    },
    {
        "id": "step_z_back_idler_bolt",
        "name": "Insert M6x30 frame-mount bolt loosely",
        "assemblyId": "assembly_d3d_z_back_bench",
        "partGroupId": "partGroup_z_back_idler_build",
        "sequenceIndex": 244.4,
        "family": "Place",
        "instructionText": "With the idler flat and the bearing hole pointing away, insert the M6x30 bolt through the top-right hole. Thread the M6 nut on the opposite side and keep it LOOSE — the idler will be tightened during frame mounting.",
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["z_back_idler_m6x30_frame", "z_back_idler_m6_nut_frame"],
        "requiredToolActions": [],
        "taskOrder": [_to("z_back_idler_m6x30_frame"), _to("z_back_idler_m6_nut_frame")],
    },
    {
        "id": "step_z_back_idler_last_bolt_loose",
        "name": "Insert M6x18 in last idler hole loosely",
        "assemblyId": "assembly_d3d_z_back_bench",
        "partGroupId": "partGroup_z_back_idler_build",
        "sequenceIndex": 244.5,
        "family": "Place",
        "instructionText": "Insert the second M6x18 bolt through the last idler hole and thread an M6 nut on the back. Run the drill briefly so the nut catches, but keep it visibly loose.",
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["z_back_idler_m6x18_loose", "z_back_idler_m6_nut_loose"],
        "relevantToolIds": ["tool_power_drill"],
        "requiredToolActions": [],
        "taskOrder": [_to("z_back_idler_m6x18_loose"), _to("z_back_idler_m6_nut_loose")],
    },
]

# Old skeleton step IDs to remove (4 stubs being replaced)
OLD_STEP_IDS = {
    "step_z_back_idler_insert_bolt",
    "step_z_back_idler_insert_bearings",
    "step_z_back_idler_align_halves",
    "step_z_back_idler_bolt",
}

# New canonical stepId order in the partGroup + assembly stepIds
NEW_STEP_ORDER = [s["id"] for s in NEW_STEPS]


def main():
    with open(ASM_PATH, encoding="utf-8") as f:
        d = json.load(f)

    # 0. Fix idler001's misleading name (manual confirms it's belt-driven)
    for p in d["parts"]:
        if p["id"] == "idler001":
            old_name = p.get("name", "")
            if "Leadscrew" in old_name:
                p["name"] = "Z-Back Belt Idler"
                p["material"] = "Belt idler pulley with bearing"
                p["function"] = "Redirects the Z-back axis drive belt at the bottom-back of the frame."
                print(f"  ~ idler001: renamed '{old_name}' → 'Z-Back Belt Idler'")
            break

    # 1. Add new parts
    existing = {p["id"] for p in d["parts"]}
    for np in NEW_PARTS:
        if np["id"] in existing:
            print(f"  skip part (exists): {np['id']}")
            continue
        d["parts"].append(np)
        print(f"  + part: {np['id']}")

    # 2. Replace old skeleton steps with new ones
    d["steps"] = [s for s in d["steps"] if s["id"] not in OLD_STEP_IDS]
    for ns in NEW_STEPS:
        d["steps"].append(ns)
        print(f"  + step: {ns['id']} (seq {ns['sequenceIndex']})")

    # 3. Update partGroup.stepIds
    for pg in d.get("partGroups", []):
        if pg["id"] == "partGroup_z_back_idler_build":
            pg["stepIds"] = list(NEW_STEP_ORDER)
            print(f"  ~ partGroup.stepIds: {pg['stepIds']}")
            break

    # 4. Update assembly.stepIds — replace the old 4 with the new 6 in place
    for a in d.get("assemblies", []):
        if a["id"] == "assembly_d3d_z_back_bench":
            ids = list(a["stepIds"])
            # Find the position of the first old idler step
            try:
                start = ids.index("step_z_back_idler_insert_bolt")
            except ValueError:
                start = 0
            # Drop any old idler step IDs and splice in the new order
            ids = [i for i in ids if i not in OLD_STEP_IDS]
            ids = ids[:start] + list(NEW_STEP_ORDER) + ids[start:]
            a["stepIds"] = ids
            print(f"  ~ assembly.stepIds count: {len(a['stepIds'])}")
            break

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")
    print("Now run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
