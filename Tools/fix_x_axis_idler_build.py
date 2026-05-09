"""
fix_x_axis_idler_build.py — Author X-Axis idler_build per manual §15.

X-axis idler is structurally different from Y/Z idler:
  • 5 M6x18 bolts (Y/Z has 3)
  • 2 *half-bearings* (Y/Z has 2 flanged 625ZZ)
  • Nuts placed *inside* idler half-piece slot — these later attach X
    to Y-Axes (stay LOOSE)
  • Different geometry: shorter idler with rod-pass-through cutouts

Existing skeleton: 4 steps (seq 166-169) wrongly cloned from Y/Z idler.
Replace with 6 steps following manual pages 71-72 §15.1-15.4.

Parts (10 new):
  x_axis_idler_m6x18_inner         M6x18 inner bolt (holds bearings)
  x_axis_idler_half_bearing_a      half-bearing A
  x_axis_idler_half_bearing_b      half-bearing B
  x_axis_idler_m6_nut_y_attach_a   nut for frame-mount bolt A (stays loose)
  x_axis_idler_m6_nut_y_attach_b   nut for frame-mount bolt B (stays loose)
  x_axis_idler_m6x18_y_attach_a    frame-mount bolt A (X-to-Y, loose)
  x_axis_idler_m6x18_y_attach_b    frame-mount bolt B (X-to-Y, loose)
  x_axis_idler_m6x18_clamp_a       clamp bolt A (drill-tightened)
  x_axis_idler_m6x18_clamp_b       clamp bolt B (drill-tightened)
  x_axis_idler_m6_nut_clamp        clamp nut (for the inner bolt)
"""

from __future__ import annotations
import json
import os

ASM_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    "assemblies", "assembly_d3d_x_axis_bench.json"
)


def stage(x, y=0.55, z=1.85, color=(0.7,0.7,0.7)):
    return {
        "position": {"x": x, "y": y, "z": z},
        "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
        "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
        "color":    {"r": color[0], "g": color[1], "b": color[2], "a": 1.0},
    }


NEW_PARTS = [
    {
        "id": "x_axis_idler_m6x18_inner",
        "displayName": "X-Axis Idler M6x18 Inner Bolt",
        "name": "X-Axis Idler M6x18 Inner Bolt",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Inner bolt holding the two half-bearings between the idler halves.",
        "quantity": 1,
        "stagingPose": stage(0.55, 0.6, 1.82),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_half_bearing_a",
        "displayName": "X-Axis Idler Half-Bearing A",
        "name": "X-Axis Idler Half-Bearing A",
        "assetRef": "d3d_axis_625zz_bearing.glb",
        "category": "fastener",
        "material": "Steel half-bearing (smaller than flanged 625ZZ)",
        "function": "Lower half-bearing on inner bolt. Flanges face outward.",
        "quantity": 1,
        "stagingPose": stage(0.62, 0.55, 1.85),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_half_bearing_b",
        "displayName": "X-Axis Idler Half-Bearing B",
        "name": "X-Axis Idler Half-Bearing B",
        "assetRef": "d3d_axis_625zz_bearing.glb",
        "category": "fastener",
        "material": "Steel half-bearing",
        "function": "Upper half-bearing on inner bolt. Flanges face outward (opposite of A).",
        "quantity": 1,
        "stagingPose": stage(0.69, 0.55, 1.85),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6_nut_y_attach_a",
        "displayName": "X-Axis Idler Y-Attach Nut A",
        "name": "X-Axis Idler M6 Nut — Y-Attach A",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Sits in idler half-piece slot. Used later to attach X-Axis to Y-Axis at frame mount. Stays LOOSE.",
        "quantity": 1,
        "stagingPose": stage(0.45, 0.5, 1.85),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6_nut_y_attach_b",
        "displayName": "X-Axis Idler Y-Attach Nut B",
        "name": "X-Axis Idler M6 Nut — Y-Attach B",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Sits in idler half-piece slot. Used later to attach X-Axis to Y-Axis at frame mount. Stays LOOSE.",
        "quantity": 1,
        "stagingPose": stage(0.50, 0.5, 1.85),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6x18_y_attach_a",
        "displayName": "X-Axis Idler Y-Attach Bolt A",
        "name": "X-Axis Idler M6x18 — Y-Attach A",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Frame-mount bolt that threads into Y-attach nut. Stays LOOSE for later X-to-Y attachment.",
        "quantity": 1,
        "stagingPose": stage(0.45, 0.6, 1.82),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6x18_y_attach_b",
        "displayName": "X-Axis Idler Y-Attach Bolt B",
        "name": "X-Axis Idler M6x18 — Y-Attach B",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Frame-mount bolt that threads into Y-attach nut. Stays LOOSE for later X-to-Y attachment.",
        "quantity": 1,
        "stagingPose": stage(0.50, 0.6, 1.82),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6x18_clamp_a",
        "displayName": "X-Axis Idler Clamp Bolt A",
        "name": "X-Axis Idler M6x18 Clamp Bolt A",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Clamp bolt closing the two idler halves. Drill-tightened.",
        "quantity": 1,
        "stagingPose": stage(0.40, 0.6, 1.82),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6x18_clamp_b",
        "displayName": "X-Axis Idler Clamp Bolt B",
        "name": "X-Axis Idler M6x18 Clamp Bolt B",
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": "Second clamp bolt closing the two idler halves. Drill-tightened.",
        "quantity": 1,
        "stagingPose": stage(0.55, 0.6, 1.82),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
    {
        "id": "x_axis_idler_m6_nut_clamp",
        "displayName": "X-Axis Idler Clamp Nut",
        "name": "X-Axis Idler M6 Nut — Inner Clamp",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads onto inner bolt to clamp the half-bearings.",
        "quantity": 1,
        "stagingPose": stage(0.55, 0.5, 1.85),
        "partGroupIds": ["partGroup_x_axis_idler_build"],
    },
]


ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


# 6 manual-correct steps — replace 4 wrong skeleton steps
NEW_STEPS = [
    {
        "id": "step_x_axis_idler_insert_inner_bolt",
        "name": "Insert inner M6x18 bolt into idler half",
        "assemblyId": "assembly_d3d_x_axis_bench",
        "partGroupId": "partGroup_x_axis_idler_build",
        "sequenceIndex": 166.0,
        "family": "Place",
        "instructionText": (
            "Begin assembling the X-axis idler: place the inner M6x18 bolt "
            "through one idler half-piece (d3d_x_axis_idler_unit), bolt head "
            "facing out."
        ),
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["d3d_x_axis_idler_unit", "x_axis_idler_m6x18_inner"],
        "requiredToolActions": [],
        "taskOrder": [task("d3d_x_axis_idler_unit"), task("x_axis_idler_m6x18_inner")],
    },
    {
        "id": "step_x_axis_idler_place_half_bearings",
        "name": "Stack two half-bearings onto inner bolt",
        "assemblyId": "assembly_d3d_x_axis_bench",
        "partGroupId": "partGroup_x_axis_idler_build",
        "sequenceIndex": 166.1,
        "family": "Place",
        "instructionText": (
            "Place the two half-bearings on the inner bolt with flanges "
            "facing outward. The X-axis idler uses smaller half-bearings, "
            "not the flanged 625ZZ used on Y/Z."
        ),
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["x_axis_idler_half_bearing_a", "x_axis_idler_half_bearing_b"],
        "requiredToolActions": [],
        "taskOrder": [task("x_axis_idler_half_bearing_a"), task("x_axis_idler_half_bearing_b")],
    },
    {
        "id": "step_x_axis_idler_place_y_attach_nuts",
        "name": "Place 2 Y-attach nuts in idler half slot",
        "assemblyId": "assembly_d3d_x_axis_bench",
        "partGroupId": "partGroup_x_axis_idler_build",
        "sequenceIndex": 166.2,
        "family": "Place",
        "instructionText": (
            "Place the 2 hex nuts in the idler half-piece slot beside the "
            "bearings as shown in the manual. These nuts will later be used "
            "to attach the X-axis assembly to the Y-axes — they STAY LOOSE "
            "in the slot for now."
        ),
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["x_axis_idler_m6_nut_y_attach_a", "x_axis_idler_m6_nut_y_attach_b"],
        "requiredToolActions": [],
        "taskOrder": [task("x_axis_idler_m6_nut_y_attach_a"), task("x_axis_idler_m6_nut_y_attach_b")],
    },
    {
        "id": "step_x_axis_idler_insert_y_attach_bolts",
        "name": "Insert 2 Y-attach M6x18 bolts through idler half",
        "assemblyId": "assembly_d3d_x_axis_bench",
        "partGroupId": "partGroup_x_axis_idler_build",
        "sequenceIndex": 166.3,
        "family": "Place",
        "instructionText": (
            "Insert the 2 frame-mount M6x18 bolts through the idler "
            "half-piece, threading each into one of the slot nuts. Keep "
            "loose — these clamp the X-axis to the Y-axes during frame "
            "mounting later."
        ),
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": ["x_axis_idler_m6x18_y_attach_a", "x_axis_idler_m6x18_y_attach_b"],
        "requiredToolActions": [],
        "taskOrder": [task("x_axis_idler_m6x18_y_attach_a"), task("x_axis_idler_m6x18_y_attach_b")],
    },
    {
        "id": "step_x_axis_idler_close_with_clamp_bolts",
        "name": "Close idler halves with 2 clamp bolts + clamp nut",
        "assemblyId": "assembly_d3d_x_axis_bench",
        "partGroupId": "partGroup_x_axis_idler_build",
        "sequenceIndex": 166.4,
        "family": "Place",
        "instructionText": (
            "Press the second idler half against the first, aligning the "
            "rod holes on both halves. Insert the 2 clamp M6x18 bolts "
            "through the closing holes and thread the M6 clamp nut on "
            "the inner bolt. Hand-snug — the next step uses the drill."
        ),
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": [
            "x_axis_idler_m6x18_clamp_a",
            "x_axis_idler_m6x18_clamp_b",
            "x_axis_idler_m6_nut_clamp",
        ],
        "requiredToolActions": [],
        "taskOrder": [
            task("x_axis_idler_m6x18_clamp_a"),
            task("x_axis_idler_m6x18_clamp_b"),
            task("x_axis_idler_m6_nut_clamp"),
        ],
    },
    {
        "id": "step_x_axis_idler_drill_tighten",
        "name": "Drill-tighten X-axis idler clamp bolts",
        "assemblyId": "assembly_d3d_x_axis_bench",
        "partGroupId": "partGroup_x_axis_idler_build",
        "sequenceIndex": 166.5,
        "family": "Use",
        "profile": "Torque",
        "instructionText": (
            "Tighten the inner bolt and the 2 clamp bolts down fully with "
            "an electric drill on the lowest setting. The 2 Y-attach bolts "
            "and their slot nuts STAY LOOSE for X-to-Y frame attachment."
        ),
        "removePersistentToolIds": [],
        "targetIds": [],
        "requiredPartIds": [],
        "relevantToolIds": ["tool_power_drill"],
        "requiredToolActions": [],
        "taskOrder": [],
    },
]


# Old skeleton step IDs to remove
OLD_STEP_IDS = {
    "step_x_axis_idler_insert_bolt",
    "step_x_axis_idler_insert_bearings",
    "step_x_axis_idler_align_halves",
    "step_x_axis_idler_bolt",
}

NEW_STEP_ORDER = [s["id"] for s in NEW_STEPS]


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

    # 2. Replace skeleton steps
    d["steps"] = [s for s in d["steps"] if s["id"] not in OLD_STEP_IDS]
    for ns in NEW_STEPS:
        d["steps"].append(ns)
        print(f"  + step: {ns['id']} (seq {ns['sequenceIndex']})")

    # 3. Update partGroup.stepIds
    for pg in d["partGroups"]:
        if pg["id"] == "partGroup_x_axis_idler_build":
            pg["stepIds"] = list(NEW_STEP_ORDER)
            break

    # 4. Update assembly.stepIds — replace old idler step IDs with new in same place
    asm = d["assemblies"][0]
    ids = list(asm.get("stepIds", []))
    # Find the position of any old idler step
    insert_at = None
    for old_id in OLD_STEP_IDS:
        if old_id in ids:
            insert_at = ids.index(old_id) if insert_at is None else min(insert_at, ids.index(old_id))
    ids = [i for i in ids if i not in OLD_STEP_IDS]
    if insert_at is None:
        insert_at = len(ids)
    asm["stepIds"] = ids[:insert_at] + list(NEW_STEP_ORDER) + ids[insert_at:]

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"\nWrote {ASM_PATH}")
    print("Run: python tools/package_health.py d3d_v18_10 --fix-seqindex")


if __name__ == "__main__":
    main()
