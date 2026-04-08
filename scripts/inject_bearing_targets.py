"""
Add target definitions and target placements for the 4 LM8UU bearings
at step_y_left_carriage_place_bearings (step 90).

Carriage assembledPosition: (0.165, 0.8215, 0.0069)
Carriage extents: ~74mm x 52mm x 24mm
Bearings sit in 4 pockets: 2 per rod channel, spaced along carriage length.
"""

import json
import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MACHINE_JSON = os.path.join(
    REPO_ROOT, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json"
)

# Carriage play position (center of carriage in assembled state)
CX, CY, CZ = 0.165, 0.8215, 0.0069

# Bearing pocket offsets from carriage center (approximate)
# Two rod channels separated ~30mm apart (Z), bearings spaced ~20mm along rod (X)
BEARING_OFFSETS = [
    (-0.010, 0.0,  0.015),   # rod A, front bearing
    ( 0.010, 0.0,  0.015),   # rod A, rear bearing
    (-0.010, 0.0, -0.015),   # rod B, front bearing
    ( 0.010, 0.0, -0.015),   # rod B, rear bearing
]

BEARING_PARTS = ["y_left_lm8uu_a", "y_left_lm8uu_b", "y_left_lm8uu_c", "y_left_lm8uu_d"]
BEARING_TARGETS = [
    "target_y_left_bearing_a",
    "target_y_left_bearing_b",
    "target_y_left_bearing_c",
    "target_y_left_bearing_d",
]


def main():
    print(f"Reading {MACHINE_JSON}")
    with open(MACHINE_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    # 1. Add target definitions
    existing_target_ids = {t["id"] for t in data["targets"]}
    for i, (tid, pid) in enumerate(zip(BEARING_TARGETS, BEARING_PARTS)):
        if tid in existing_target_ids:
            print(f"  SKIP target (exists): {tid}")
            continue
        data["targets"].append({
            "id": tid,
            "name": f"Y-Left Bearing Pocket {chr(65+i)}",
            "description": f"Bearing pocket for {pid} inside Y-left carriage half.",
            "associatedPartId": pid,
            "tags": ["d3d", "y_left", "carriage", "bearing"]
        })
        print(f"  ADD target: {tid} -> {pid}")

    # 2. Add target placements
    existing_tp_ids = {tp["targetId"] for tp in data["previewConfig"]["targetPlacements"]}
    for i, tid in enumerate(BEARING_TARGETS):
        if tid in existing_tp_ids:
            print(f"  SKIP placement (exists): {tid}")
            continue
        dx, dy, dz = BEARING_OFFSETS[i]
        data["previewConfig"]["targetPlacements"].append({
            "targetId": tid,
            "position": {
                "x": round(CX + dx, 4),
                "y": round(CY + dy, 4),
                "z": round(CZ + dz, 4)
            },
            "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
            "scale": {"x": 0.015, "y": 0.015, "z": 0.024},
            "color": {"r": 0.3, "g": 0.85, "b": 0.95, "a": 1.0},
            "portA": {"x": 0.0, "y": 0.0, "z": 0.0},
            "portB": {"x": 0.0, "y": 0.0, "z": 0.0}
        })
        print(f"  ADD placement: {tid} at ({CX+dx:.4f}, {CY+dy:.4f}, {CZ+dz:.4f})")

    # 3. Wire targets into step_y_left_carriage_place_bearings
    for step in data["steps"]:
        if step["id"] == "step_y_left_carriage_place_bearings":
            existing = set(step.get("targetIds", []))
            for tid in BEARING_TARGETS:
                if tid not in existing:
                    step.setdefault("targetIds", []).append(tid)
                    print(f"  WIRE step: {step['id']} += {tid}")
            break

    # 4. Also update bearing part assembledPositions to match target positions
    placements = data["previewConfig"]["partPlacements"]
    for i, pid in enumerate(BEARING_PARTS):
        dx, dy, dz = BEARING_OFFSETS[i]
        for pp in placements:
            if pp["partId"] == pid:
                pp["assembledPosition"]["x"] = round(CX + dx, 4)
                pp["assembledPosition"]["y"] = round(CY + dy, 4)
                pp["assembledPosition"]["z"] = round(CZ + dz, 4)
                print(f"  UPDATE playPos: {pid} -> ({CX+dx:.4f}, {CY+dy:.4f}, {CZ+dz:.4f})")
                break

    print(f"\nWriting {MACHINE_JSON}")
    with open(MACHINE_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("Done.")


if __name__ == "__main__":
    main()
