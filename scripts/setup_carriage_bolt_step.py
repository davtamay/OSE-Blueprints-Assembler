"""
Wire up step 93 (bolt carriage halves together) per PDF Step 2.1:
- 2x M6x18 bolts (top holes)
- 2x M6x30 bolts (bottom holes)
- 4x M6 nuts
- Power drill tool action to tighten
Start positions clustered right next to carriage on bench.
"""
import json, math, sys, os
sys.stdout.reconfigure(encoding="utf-8")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MJ = os.path.join(REPO, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json")

s45 = round(math.sin(math.radians(45)), 4)
c45 = round(math.cos(math.radians(45)), 4)
BOLT_ROT = {"x": 0.0, "y": s45, "z": 0.0, "w": c45}
ID_ROT   = {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}

# Carriage on bench: position (0.82, 0.55, 0), rot Y=90 Z=-90
# Bolt holes at 4 corners (world-space offsets from carriage center)
# Top = M6x18, Bottom = M6x30
HOLES = [
    ("top_l", +0.020, +0.018),
    ("top_r", +0.020, -0.018),
    ("bot_l", -0.020, +0.018),
    ("bot_r", -0.020, -0.018),
]

def upsert_pp(data, pid, play_pos, play_rot, start_pos):
    for pp in data["previewConfig"]["partPlacements"]:
        if pp["partId"] == pid:
            pp["playPosition"]  = play_pos
            pp["playRotation"]  = play_rot
            pp["startPosition"] = start_pos
            pp["startRotation"] = ID_ROT.copy()
            print(f"  UPDATE {pid}: start={start_pos} play={play_pos}")
            return
    data["previewConfig"]["partPlacements"].append({
        "partId": pid,
        "startPosition": start_pos, "startRotation": ID_ROT.copy(),
        "startScale": {"x": 1.0, "y": 1.0, "z": 1.0},
        "color": {"r": 0.8, "g": 0.8, "b": 0.8, "a": 1.0},
        "playPosition": play_pos, "playRotation": play_rot.copy(),
        "playScale": {"x": 1.0, "y": 1.0, "z": 1.0},
        "splinePath": {"radius": 0.0, "segments": 8, "metallic": 0.0, "smoothness": 0.0,
                       "color": {"r": 0.0, "g": 0.0, "b": 0.0, "a": 0.0}, "knots": []},
    })
    print(f"  ADD {pid}: start={start_pos} play={play_pos}")


def add_part(data, pid, name, fn, asset):
    if any(p["id"] == pid for p in data["parts"]):
        return
    data["parts"].append({
        "id": pid, "name": name, "function": fn,
        "category": "fastener", "material": "Steel",
        "quantity": 1, "assetRef": asset,
    })
    print(f"  ADD part: {pid}")


def setup_side(data, side, cx, cy, cz, x_sign):
    bolts_nuts = [
        (f"y_{side}_m6x18_a", f"y_{side}_m6_nut_a", HOLES[0]),
        (f"y_{side}_m6x18_b", f"y_{side}_m6_nut_b", HOLES[1]),
        (f"y_{side}_m6x30_a", f"y_{side}_m6_nut_c", HOLES[2]),
        (f"y_{side}_m6x30_b", f"y_{side}_m6_nut_d", HOLES[3]),
    ]

    # Add missing parts
    for i, (bolt_id, nut_id, _) in enumerate(bolts_nuts):
        is_30 = "m6x30" in bolt_id
        add_part(data, bolt_id,
                 f"Y-{side.title()} M6x{'30' if is_30 else '18'} Bolt {bolt_id[-1].upper()}",
                 f"Secures carriage halves ({'bottom' if is_30 else 'top'} holes).",
                 "d3d_axis_m6x18_bolt.glb")
        add_part(data, nut_id,
                 f"Y-{side.title()} Hex Nut {nut_id[-1].upper()}",
                 "Nut for carriage bolt.",
                 "d3d_axis_m6_nut.glb")

    # Add placements
    for i, (bolt_id, nut_id, (label, dy, dz)) in enumerate(bolts_nuts):
        bolt_play = {"x": round(cx + x_sign * 0.013, 4), "y": round(cy + dy, 4), "z": round(cz + dz, 4)}
        nut_play  = {"x": round(cx - x_sign * 0.013, 4), "y": round(cy + dy, 4), "z": round(cz + dz, 4)}
        # Start: tight cluster right beside carriage (offset in X, spaced in Z)
        bolt_start = {"x": round(cx + x_sign * (0.10 + i * 0.03), 4), "y": cy, "z": round(cz + dz, 4)}
        nut_start  = {"x": round(cx + x_sign * (0.10 + i * 0.03), 4), "y": cy, "z": round(cz + dy, 4)}
        upsert_pp(data, bolt_id, bolt_play, BOLT_ROT, bolt_start)
        upsert_pp(data, nut_id,  nut_play,  BOLT_ROT, nut_start)

    # Update step
    step_id = f"step_y_{side}_carriage_bolt"
    all_bolts = [b for b, n, _ in bolts_nuts]
    all_nuts  = [n for b, n, _ in bolts_nuts]
    for s in data["steps"]:
        if s["id"] == step_id:
            s["completionType"] = "tool_action"
            s["family"] = "Use"
            s["requiredPartIds"] = (
                [f"y_{side}_carriage_half_a", f"y_{side}_carriage_half_b"]
                + all_bolts + all_nuts
            )
            s["requiredToolActions"] = [{
                "id": f"action_y_{side}_carriage_tighten",
                "toolId": "tool_power_drill",
                "actionType": "tighten",
                "targetId": "",
                "requiredCount": 4,
                "successMessage": "All 4 bolts tightened — carriage assembled.",
                "failureMessage": "Equip the power drill and tighten each of the 4 bolts.",
            }]
            s["relevantToolIds"] = ["tool_power_drill"]
            s["instructionText"] = (
                "Close the two carriage halves together. Insert 2x M6x18 bolts in the top holes "
                "and 2x M6x30 bolts in the bottom holes. Fit a nut on each bolt. "
                "Align the small ribbed belt hole beside the large smooth hole. "
                "Tighten all 4 with the power drill on lowest torque setting."
            )
            print(f"  UPDATE {step_id}: {len(all_bolts)+len(all_nuts)} fasteners + drill")

    # Subassembly
    sub_id = f"subassembly_y_{side}_carriage_build"
    for sub in data.get("subassemblies", []):
        if sub["id"] == sub_id:
            pids = sub["partIds"]
            for pid in all_bolts + all_nuts:
                if pid not in pids:
                    pids.append(pid)


def main():
    with open(MJ, "r", encoding="utf-8") as f:
        data = json.load(f)

    print("=== Y-LEFT ===")
    setup_side(data, "left",  0.82, 0.55, 0.0, +1)
    print("\n=== Y-RIGHT ===")
    setup_side(data, "right", -0.82, 0.55, 0.0, -1)

    with open(MJ, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("\nDone.")


if __name__ == "__main__":
    main()
