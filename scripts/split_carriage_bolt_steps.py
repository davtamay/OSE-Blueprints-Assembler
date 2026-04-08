"""
Split step 93 into 3 proper steps per PDF Step 2.1/2.2:
  93 → Place 4 bolts (2x M6x18 top, 2x M6x30 bottom)
  94 → Place 4 nuts
  95 → Tighten all 4 bolts with power drill

Bump existing steps 94+ in y_left_bench and y_right_bench by 2
to make room without renumbering the whole file.

Also fix bolt/nut rotation: mesh long axis is Z, bolts go vertically
through the carriage (world Y) → rotate -90° around X.
"""
import json, math, sys, os
sys.stdout.reconfigure(encoding="utf-8")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MJ = os.path.join(REPO, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json")

s45 = round(math.sin(math.radians(45)), 4)
c45 = round(math.cos(math.radians(45)), 4)

# Bolt/nut long axis = Z. Carriage lies flat, bolts go down through it (world -Y).
# Rotate -90° around X: mesh Z → world Y (bolt points up, head on top)
BOLT_ROT = {"x": -s45, "y": 0.0, "z": 0.0, "w": c45}
NUT_ROT  = {"x": -s45, "y": 0.0, "z": 0.0, "w": c45}
ID_ROT   = {"x": 0.0,  "y": 0.0, "z": 0.0, "w": 1.0}


def upsert_pp(data, pid, play_pos, play_rot, start_pos, start_rot=None):
    sr = start_rot or ID_ROT.copy()
    for pp in data["previewConfig"]["partPlacements"]:
        if pp["partId"] == pid:
            pp["assembledPosition"]  = play_pos
            pp["assembledRotation"]  = play_rot
            pp["startPosition"] = start_pos
            pp["startRotation"] = sr
            return
    data["previewConfig"]["partPlacements"].append({
        "partId": pid,
        "startPosition": start_pos, "startRotation": sr,
        "startScale": {"x": 1.0, "y": 1.0, "z": 1.0},
        "color": {"r": 0.8, "g": 0.8, "b": 0.8, "a": 1.0},
        "assembledPosition": play_pos, "assembledRotation": play_rot,
        "assembledScale": {"x": 1.0, "y": 1.0, "z": 1.0},
        "splinePath": {"radius": 0.0, "segments": 8, "metallic": 0.0,
                       "smoothness": 0.0,
                       "color": {"r": 0.0, "g": 0.0, "b": 0.0, "a": 0.0},
                       "knots": []},
    })


def setup_side(data, side, cx, cy, cz):
    """
    cx,cy,cz = carriage bench play position
    Carriage rotation Y=90, Z=-90:
      - mesh Y (74mm, rod dir) → world Z
      - mesh Z (52mm, width)   → world X (mirrored for right side)
    Bolt holes at 4 corners: ±Z_world=±0.018, Y slightly above/below carriage
    """
    # Assembled carriage total height = 24mm. Bolt head sits on top (Y+0.024),
    # nut catches on bottom (Y+0.0).
    bolt_head_y = round(cy + 0.026, 4)  # slightly above assembled top face
    nut_y       = round(cy - 0.002, 4)  # flush with bottom face

    # 4 hole positions along rod direction (world Z)
    holes = [
        ("top_front", +0.018),
        ("top_rear",  -0.018),
        ("bot_front", +0.018),
        ("bot_rear",  -0.018),
    ]

    bolts = [
        (f"y_{side}_m6x18_a", f"y_{side}_m6_nut_a", holes[0]),  # top-front
        (f"y_{side}_m6x18_b", f"y_{side}_m6_nut_b", holes[1]),  # top-rear
        (f"y_{side}_m6x30_a", f"y_{side}_m6_nut_c", holes[2]),  # bot-front
        (f"y_{side}_m6x30_b", f"y_{side}_m6_nut_d", holes[3]),  # bot-rear
    ]

    all_bolt_ids = [b for b, n, _ in bolts]
    all_nut_ids  = [n for b, n, _ in bolts]

    # Start positions: bolts hovering just above the carriage, spread along Z
    # Nuts sitting beside carriage at same Z, slightly to the side
    for i, (bolt_id, nut_id, (label, dz)) in enumerate(bolts):
        # Play position: bolt head above the hole, nut below
        bolt_play = {"x": round(cx, 4), "y": bolt_head_y, "z": round(cz + dz, 4)}
        nut_play  = {"x": round(cx, 4), "y": nut_y,       "z": round(cz + dz, 4)}

        # Start: bolts in a row 8cm to one side of carriage, same Z as their hole
        sign = 1 if side == "left" else -1
        bolt_start = {"x": round(cx + sign * 0.10, 4), "y": round(cy + 0.05, 4), "z": round(cz + dz, 4)}
        nut_start  = {"x": round(cx + sign * 0.14, 4), "y": cy,                   "z": round(cz + dz, 4)}

        upsert_pp(data, bolt_id, bolt_play, BOLT_ROT, bolt_start)
        upsert_pp(data, nut_id,  nut_play,  NUT_ROT,  nut_start)

        print(f"  {bolt_id}: start={bolt_start} play={bolt_play}")
        print(f"  {nut_id}:  start={nut_start}  play={nut_play}")

    # ── Step 93: Place bolts ──
    bolt_step_id = f"step_y_{side}_carriage_place_bolts"
    nut_step_id  = f"step_y_{side}_carriage_place_nuts"
    drill_step_id = f"step_y_{side}_carriage_tighten"
    orig_step_id  = f"step_y_{side}_carriage_bolt"

    # Find the original bolt step seq
    orig_seq = next(s["sequenceIndex"] for s in data["steps"]
                    if s["id"] == orig_step_id)
    assembly = f"assembly_d3d_y_{side}_bench"

    # Bump all steps in this assembly with seq >= orig_seq+1 by 2
    for s in data["steps"]:
        if s.get("assemblyId") == assembly and s["sequenceIndex"] >= orig_seq + 1:
            s["sequenceIndex"] += 2
    print(f"  Bumped {assembly} steps >= {orig_seq+1} by 2")

    # Repurpose original step → place bolts
    for s in data["steps"]:
        if s["id"] == orig_step_id:
            s["id"]              = bolt_step_id
            s["name"]            = "Place bolts into carriage holes"
            s["family"]          = "Place"
            s["completionType"]  = "placement"
            s["instructionText"] = (
                "Insert 2x M6x18 bolts into the two top holes and 2x M6x30 bolts "
                "into the two bottom holes. Ensure the small ribbed belt hole sits "
                "directly beside the large smooth belt hole."
            )
            s["requiredPartIds"]    = [f"y_{side}_carriage_half_a", f"y_{side}_carriage_half_b"] + all_bolt_ids
            s["targetIds"]          = []
            s["requiredToolActions"] = []
            s.pop("relevantToolIds", None)
            print(f"  REPURPOSE [{orig_seq}] -> {bolt_step_id}")

    # New step: place nuts (orig_seq + 1)
    data["steps"].append({
        "id": nut_step_id,
        "name": "Thread nuts onto bolts",
        "assemblyId": assembly,
        "subassemblyId": f"subassembly_y_{side}_carriage_build",
        "sequenceIndex": orig_seq + 1,
        "family": "Place",
        "completionType": "placement",
        "instructionText": "Thread one M6 nut onto each bolt finger-tight. Do not tighten yet.",
        "requiredPartIds": all_nut_ids,
        "targetIds": [],
        "hintIds": [],
    })
    print(f"  ADD [{orig_seq+1}] {nut_step_id}")

    # New step: tighten with drill (orig_seq + 2)
    data["steps"].append({
        "id": drill_step_id,
        "name": "Tighten bolts with power drill",
        "assemblyId": assembly,
        "subassemblyId": f"subassembly_y_{side}_carriage_build",
        "sequenceIndex": orig_seq + 2,
        "family": "Use",
        "completionType": "tool_action",
        "instructionText": (
            "Use the power drill on the lowest torque setting to tighten all 4 bolts. "
            "You've completed an axis carriage!"
        ),
        "requiredPartIds": [],
        "targetIds": [],
        "requiredToolActions": [{
            "id": f"action_y_{side}_carriage_drill",
            "toolId": "tool_power_drill",
            "actionType": "tighten",
            "targetId": "",
            "requiredCount": 4,
            "successMessage": "Carriage bolted together successfully!",
            "failureMessage": "Equip the power drill and tighten each of the 4 bolts.",
        }],
        "relevantToolIds": ["tool_power_drill"],
        "hintIds": [],
    })
    print(f"  ADD [{orig_seq+2}] {drill_step_id}")


def main():
    with open(MJ, "r", encoding="utf-8") as f:
        data = json.load(f)

    # Check no bolt-place steps already exist
    existing_ids = {s["id"] for s in data["steps"]}

    print("=== Y-LEFT ===")
    if "step_y_left_carriage_place_bolts" not in existing_ids:
        setup_side(data, "left",  0.82, 0.55, 0.0)
    else:
        print("  Already split — skipping Y-left")

    print("\n=== Y-RIGHT ===")
    if "step_y_right_carriage_place_bolts" not in existing_ids:
        setup_side(data, "right", -0.82, 0.55, 0.0)
    else:
        print("  Already split — skipping Y-right")

    # Sort
    data["steps"].sort(key=lambda s: s["sequenceIndex"])

    with open(MJ, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")

    # Verify
    print("\n=== FINAL Y-LEFT CARRIAGE BOLT STEPS ===")
    for s in data["steps"]:
        if "y_left" in s.get("id","") and ("bolt" in s.get("id","") or "nut" in s.get("id","") or "tighten" in s.get("id","")):
            print(f"  [{s['sequenceIndex']}] {s['id']}: {s['name']}")
            print(f"    parts: {s.get('requiredPartIds',[])}")

    print("\nDone.")


if __name__ == "__main__":
    main()
