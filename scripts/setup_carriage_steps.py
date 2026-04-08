"""
Set up carriage bearing placement steps per the D3D v18.10 PDF workflow.

For each axis (Y-left, Y-right):
1. Insert a "setup fixture" step that places the carriage half (pockets up).
   Once completed, it becomes locked/immovable.
2. The bearing placement step only has the 4 bearings as required parts.
3. Rotate carriage halves so pocket face is UP (+Y).
4. Position bearings in the rod channel pockets.
5. Add Y-right bearing parts, placements, targets (mirroring Y-left).
"""

import json
import math
import copy
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MJ = os.path.join(REPO, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json")

s45 = round(math.sin(math.radians(45)), 4)
c45 = round(math.cos(math.radians(45)), 4)


def main():
    with open(MJ, "r", encoding="utf-8") as f:
        data = json.load(f)

    existing_part_ids = {p["id"] for p in data["parts"]}
    existing_pp_ids = {pp["partId"] for pp in data["previewConfig"]["partPlacements"]}
    existing_target_ids = {t["id"] for t in data["targets"]}
    existing_tp_ids = {tp["targetId"] for tp in data["previewConfig"]["targetPlacements"]}

    # ----------------------------------------------------------------
    # 1. Insert fixture steps (carriage setup before bearing placement)
    # ----------------------------------------------------------------

    # Y-LEFT: insert fixture step before step_y_left_carriage_place_bearings
    yl_bearing_step = next(s for s in data["steps"] if s["id"] == "step_y_left_carriage_place_bearings")
    yl_seq = yl_bearing_step["sequenceIndex"]

    # Check if fixture step already exists
    yl_fixture_exists = any(s["id"] == "step_y_left_carriage_setup_fixture" for s in data["steps"])
    if not yl_fixture_exists:
        # Bump all steps >= yl_seq
        for s in data["steps"]:
            if s["sequenceIndex"] >= yl_seq:
                s["sequenceIndex"] += 1
        data["steps"].append({
            "id": "step_y_left_carriage_setup_fixture",
            "name": "Set carriage half on workbench, pocket side up",
            "assemblyId": "assembly_d3d_y_left_bench",
            "subassemblyId": "subassembly_y_left_carriage_build",
            "sequenceIndex": yl_seq,
            "family": "Place",
            "completionType": "confirmation",
            "instructionText": "Place one carriage half-piece on the workbench with the four bearing pockets facing up.",
            "requiredPartIds": ["y_left_carriage_half_a"],
            "targetIds": [],
            "hintIds": [],
        })
        print(f"ADD step [{yl_seq}]: step_y_left_carriage_setup_fixture")

    # Remove carriage half from bearing placement step (it is now Completed/locked)
    rp = yl_bearing_step.get("requiredPartIds", [])
    if "y_left_carriage_half_a" in rp:
        rp.remove("y_left_carriage_half_a")
        print("REMOVE y_left_carriage_half_a from step_y_left_carriage_place_bearings")

    # Y-RIGHT: same pattern
    yr_bearing_step = next(s for s in data["steps"] if s["id"] == "step_y_right_carriage_place_bearings")
    yr_seq = yr_bearing_step["sequenceIndex"]

    yr_fixture_exists = any(s["id"] == "step_y_right_carriage_setup_fixture" for s in data["steps"])
    if not yr_fixture_exists:
        for s in data["steps"]:
            if s["sequenceIndex"] >= yr_seq:
                s["sequenceIndex"] += 1
        data["steps"].append({
            "id": "step_y_right_carriage_setup_fixture",
            "name": "Set carriage half on workbench, pocket side up",
            "assemblyId": "assembly_d3d_y_right_bench",
            "subassemblyId": "subassembly_y_right_carriage_build",
            "sequenceIndex": yr_seq,
            "family": "Place",
            "completionType": "confirmation",
            "instructionText": "Place one carriage half-piece on the workbench with the four bearing pockets facing up.",
            "requiredPartIds": ["y_right_carriage_half_a"],
            "targetIds": [],
            "hintIds": [],
        })
        print(f"ADD step [{yr_seq}]: step_y_right_carriage_setup_fixture")

    rp_yr = yr_bearing_step.get("requiredPartIds", [])
    if "y_right_carriage_half_a" in rp_yr:
        rp_yr.remove("y_right_carriage_half_a")
        print("REMOVE y_right_carriage_half_a from step_y_right_carriage_place_bearings")

    # ----------------------------------------------------------------
    # 2. Carriage orientation: pocket face UP
    # ----------------------------------------------------------------
    # Mesh half A: pocket face at X=0, base at X=-0.012
    # Rotate -90 deg around Z so X+ -> Y+ (pockets face up)
    pockets_up = {"x": 0.0, "y": 0.0, "z": -s45, "w": c45}
    pockets_down = {"x": 0.0, "y": 0.0, "z": s45, "w": c45}

    for pp in data["previewConfig"]["partPlacements"]:
        if pp["partId"] in ("y_left_carriage_half_a", "y_right_carriage_half_a"):
            pp["playRotation"] = pockets_up.copy()
            print(f"ROTATE {pp['partId']} pockets-up")
        if pp["partId"] in ("y_left_carriage_half_b", "y_right_carriage_half_b"):
            pp["playRotation"] = pockets_down.copy()
            print(f"ROTATE {pp['partId']} pockets-down (clamp side)")

    # ----------------------------------------------------------------
    # 3. Bearing positions in rotated carriage
    # ----------------------------------------------------------------
    # After -90 Z rotation of carriage mesh:
    #   mesh Y (rod direction) -> world -X
    #   mesh Z (channel separation) -> world Z (unchanged)
    #   mesh X (pocket face) -> world +Y
    # Pockets at mesh (0, +/-13mm, +/-15mm) ->
    #   world (carriage_X -/+ 13mm, carriage_Y, carriage_Z +/- 15mm)

    # Bearing mesh: long axis = Z (24mm), OD plane = XY (15mm)
    # After carriage rotation, rods run along world X.
    # Bearing Z axis needs to align with world X -> rotate 90 deg around Y
    bearing_rot = {"x": 0.0, "y": s45, "z": 0.0, "w": c45}

    # Y-LEFT bearings
    CX_L, CY_L, CZ_L = 0.165, 0.8215, 0.0069
    BEARINGS_L = [
        ("y_left_lm8uu_a", "target_y_left_bearing_a", -0.013, +0.015),
        ("y_left_lm8uu_b", "target_y_left_bearing_b", +0.013, +0.015),
        ("y_left_lm8uu_c", "target_y_left_bearing_c", -0.013, -0.015),
        ("y_left_lm8uu_d", "target_y_left_bearing_d", +0.013, -0.015),
    ]

    for pid, tid, dx, dz in BEARINGS_L:
        pos = {
            "x": round(CX_L + dx, 4),
            "y": round(CY_L + 0.006, 4),
            "z": round(CZ_L + dz, 4),
        }
        for pp in data["previewConfig"]["partPlacements"]:
            if pp["partId"] == pid:
                pp["playPosition"] = pos.copy()
                pp["playRotation"] = bearing_rot.copy()
        for tp in data["previewConfig"]["targetPlacements"]:
            if tp["targetId"] == tid:
                tp["position"] = pos.copy()
        print(f"UPDATE {pid}/{tid} -> {pos}")

    # ----------------------------------------------------------------
    # 4. Y-RIGHT bearing parts, placements, targets
    # ----------------------------------------------------------------
    CX_R, CY_R, CZ_R = -0.1651, 0.822, 0.0047
    BEARINGS_R = [
        ("y_right_lm8uu_a", "target_y_right_bearing_a", -0.013, +0.015),
        ("y_right_lm8uu_b", "target_y_right_bearing_b", +0.013, +0.015),
        ("y_right_lm8uu_c", "target_y_right_bearing_c", -0.013, -0.015),
        ("y_right_lm8uu_d", "target_y_right_bearing_d", +0.013, -0.015),
    ]

    for i, (pid, tid, dx, dz) in enumerate(BEARINGS_R):
        # Part definition
        if pid not in existing_part_ids:
            data["parts"].append({
                "id": pid,
                "name": f"Y-Right LM8UU Bearing {chr(65+i)}",
                "function": "Linear bearing for Y-right axis carriage.",
                "category": "custom",
                "material": "Steel linear bearing",
                "quantity": 1,
                "assetRef": "d3d_axis_lm8uu_bearing.glb",
            })
            print(f"ADD part: {pid}")

        pos = {
            "x": round(CX_R + dx, 4),
            "y": round(CY_R + 0.006, 4),
            "z": round(CZ_R + dz, 4),
        }

        # Part placement
        if pid not in existing_pp_ids:
            data["previewConfig"]["partPlacements"].append({
                "partId": pid,
                "startPosition": {"x": round(-0.7 - i * 0.06, 4), "y": 0.55, "z": 0.15},
                "startRotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
                "startScale": {"x": 1.0, "y": 1.0, "z": 1.0},
                "color": {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
                "playPosition": pos.copy(),
                "playRotation": bearing_rot.copy(),
                "playScale": {"x": 1.0, "y": 1.0, "z": 1.0},
                "splinePath": {
                    "radius": 0.0, "segments": 8, "metallic": 0.0, "smoothness": 0.0,
                    "color": {"r": 0.0, "g": 0.0, "b": 0.0, "a": 0.0}, "knots": [],
                },
            })
            print(f"ADD placement: {pid}")
        else:
            for pp in data["previewConfig"]["partPlacements"]:
                if pp["partId"] == pid:
                    pp["playPosition"] = pos.copy()
                    pp["playRotation"] = bearing_rot.copy()

        # Target definition
        if tid not in existing_target_ids:
            data["targets"].append({
                "id": tid,
                "name": f"Y-Right Bearing Pocket {chr(65+i)}",
                "description": f"Bearing pocket for {pid} inside Y-right carriage half.",
                "associatedPartId": pid,
                "tags": ["d3d", "y_right", "carriage", "bearing"],
                "anchorRef": f"anchors/d3d/y_right/lm8uu_{chr(97+i)}",
            })
            print(f"ADD target: {tid}")

        # Target placement
        if tid not in existing_tp_ids:
            data["previewConfig"]["targetPlacements"].append({
                "targetId": tid,
                "position": pos.copy(),
                "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
                "scale": {"x": 0.015, "y": 0.024, "z": 0.015},
                "color": {"r": 0.3, "g": 0.85, "b": 0.95, "a": 1.0},
                "portA": {"x": 0.0, "y": 0.0, "z": 0.0},
                "portB": {"x": 0.0, "y": 0.0, "z": 0.0},
            })
            print(f"ADD target placement: {tid}")
        else:
            for tp in data["previewConfig"]["targetPlacements"]:
                if tp["targetId"] == tid:
                    tp["position"] = pos.copy()

    # Wire Y-right bearing parts into step
    yr_bp = next(s for s in data["steps"] if s["id"] == "step_y_right_carriage_place_bearings")
    yr_rp = yr_bp.get("requiredPartIds", [])
    for pid, _, _, _ in BEARINGS_R:
        if pid not in yr_rp:
            yr_rp.append(pid)
    yr_bp["requiredPartIds"] = yr_rp

    # Wire Y-right targets into step
    yr_bp["targetIds"] = [tid for _, tid, _, _ in BEARINGS_R]
    print(f"WIRE step_y_right_carriage_place_bearings: parts={yr_rp}, targets={yr_bp['targetIds']}")

    # Wire into subassembly
    for sub in data.get("subassemblies", []):
        if sub["id"] == "subassembly_y_right_carriage_build":
            pids = sub.get("partIds", [])
            for pid, _, _, _ in BEARINGS_R:
                if pid not in pids:
                    pids.append(pid)
            print(f"WIRE subassembly_y_right_carriage_build: {pids}")

    # ----------------------------------------------------------------
    # 5. Sort steps and save
    # ----------------------------------------------------------------
    data["steps"].sort(key=lambda s: s["sequenceIndex"])

    with open(MJ, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")

    # Print summary
    print("\n=== Y-LEFT CARRIAGE STEPS ===")
    for s in data["steps"]:
        if "y_left" in s.get("id", "") and "carriage" in s.get("id", ""):
            print(f"  [{s['sequenceIndex']}] {s['id']}")
            print(f"    parts: {s.get('requiredPartIds', [])}")
            print(f"    targets: {s.get('targetIds', [])}")

    print("\n=== Y-RIGHT CARRIAGE STEPS ===")
    for s in data["steps"]:
        if "y_right" in s.get("id", "") and "carriage" in s.get("id", ""):
            print(f"  [{s['sequenceIndex']}] {s['id']}")
            print(f"    parts: {s.get('requiredPartIds', [])}")
            print(f"    targets: {s.get('targetIds', [])}")

    print("\nDone.")


if __name__ == "__main__":
    main()
