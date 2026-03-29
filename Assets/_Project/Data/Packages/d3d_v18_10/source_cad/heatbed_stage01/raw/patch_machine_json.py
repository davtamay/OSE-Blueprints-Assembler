"""
patch_machine_json.py
Patches machine.json previewConfig.partPlacements with FreeCAD-accurate positions.

- Frame bars: repositioned from flat panels to assembled 3D cube
- 9 non-frame parts: positioned using FreeCAD extraction + coordinate offset

FreeCAD frame: 304.8mm outer cube from (0,0,0) to (304.8, 304.8, 304.8) mm
Coordinate conversion: Unity(X,Y,Z) = FreeCAD(X/1000, Z/1000, Y/1000)
Offset: FreeCAD origin -> machine.json (-0.1524, +0.552, -0.1524)
  - Centers the frame at X=0, Z=0
  - Places frame bottom at Y=0.552 (worktable height)

Run: python patch_machine_json.py
"""
import json, os, copy

# --- Paths ---
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MACHINE_JSON = os.path.join(SCRIPT_DIR, "..", "..", "..", "machine.json")
PLACEMENTS_JSON = os.path.join(SCRIPT_DIR, "placements.json")
LABEL_MAP_JSON = os.path.join(SCRIPT_DIR, "label_map.json")

# --- Constants ---
FRAME_OUTER_M = 0.3048       # 304.8mm = 12 inches
BAR_HALF_WIDTH = 0.0127      # 12.7mm = half of 25.4mm (1 inch)
WORKTABLE_Y = 0.552          # existing worktable height from machine.json

# Offset: FreeCAD Unity origin -> machine.json world coords
OFFSET_X = -FRAME_OUTER_M / 2   # -0.1524, center frame at X=0
OFFSET_Y = WORKTABLE_Y           # +0.552, frame bottom at worktable
OFFSET_Z = -FRAME_OUTER_M / 2   # -0.1524, center frame at Z=0

# Derived cube edge positions (bar centers)
Y_BOT = WORKTABLE_Y + BAR_HALF_WIDTH          # 0.5647
Y_TOP = WORKTABLE_Y + FRAME_OUTER_M - BAR_HALF_WIDTH  # 0.8441
Y_MID = WORKTABLE_Y + FRAME_OUTER_M / 2       # 0.7044
X_LEFT = -FRAME_OUTER_M / 2 + BAR_HALF_WIDTH  # -0.1397
X_RIGHT = FRAME_OUTER_M / 2 - BAR_HALF_WIDTH  #  0.1397
Z_FRONT = -FRAME_OUTER_M / 2 + BAR_HALF_WIDTH # -0.1397
Z_REAR = FRAME_OUTER_M / 2 - BAR_HALF_WIDTH   #  0.1397

# Bar rotations (quaternion x, y, z, w)
ROT_ALONG_X = (0.0, 0.0, 0.0, 1.0)           # identity - bar runs along X
ROT_ALONG_Z = (0.0, 0.7071068, 0.0, 0.7071068)  # 90 deg around Y
ROT_ALONG_Y = (0.0, 0.0, 0.7071068, 0.7071068)  # 90 deg around Z (vertical)

# --- 24 Frame Bar Assembled Positions ---
# Format: partId -> (position, rotation)
# Each face has: top_bar, bottom_bar, right_bar, left_bar
# Naming convention (from flat panel layout): top=+Z, bottom=-Z, right=+X, left=-X

FRAME_BAR_POSITIONS = {
    # Bottom face (horizontal, Y = Y_BOT)
    "d3d_frame_bottom_top_bar":    ((0.0, Y_BOT, Z_REAR),  ROT_ALONG_X),
    "d3d_frame_bottom_bottom_bar": ((0.0, Y_BOT, Z_FRONT), ROT_ALONG_X),
    "d3d_frame_bottom_right_bar":  ((X_RIGHT, Y_BOT, 0.0), ROT_ALONG_Z),
    "d3d_frame_bottom_left_bar":   ((X_LEFT, Y_BOT, 0.0),  ROT_ALONG_Z),

    # Top face (horizontal, Y = Y_TOP)
    "d3d_frame_top_top_bar":    ((0.0, Y_TOP, Z_REAR),  ROT_ALONG_X),
    "d3d_frame_top_bottom_bar": ((0.0, Y_TOP, Z_FRONT), ROT_ALONG_X),
    "d3d_frame_top_right_bar":  ((X_RIGHT, Y_TOP, 0.0), ROT_ALONG_Z),
    "d3d_frame_top_left_bar":   ((X_LEFT, Y_TOP, 0.0),  ROT_ALONG_Z),

    # Left face (vertical, X = X_LEFT)
    "d3d_frame_left_top_bar":    ((X_LEFT, Y_TOP, 0.0),    ROT_ALONG_Z),
    "d3d_frame_left_bottom_bar": ((X_LEFT, Y_BOT, 0.0),    ROT_ALONG_Z),
    "d3d_frame_left_right_bar":  ((X_LEFT, Y_MID, Z_FRONT), ROT_ALONG_Y),
    "d3d_frame_left_left_bar":   ((X_LEFT, Y_MID, Z_REAR),  ROT_ALONG_Y),

    # Right face (vertical, X = X_RIGHT)
    "d3d_frame_right_top_bar":    ((X_RIGHT, Y_TOP, 0.0),    ROT_ALONG_Z),
    "d3d_frame_right_bottom_bar": ((X_RIGHT, Y_BOT, 0.0),    ROT_ALONG_Z),
    "d3d_frame_right_right_bar":  ((X_RIGHT, Y_MID, Z_REAR),  ROT_ALONG_Y),
    "d3d_frame_right_left_bar":   ((X_RIGHT, Y_MID, Z_FRONT), ROT_ALONG_Y),

    # Front face (vertical, Z = Z_FRONT)
    "d3d_frame_front_top_bar":    ((0.0, Y_TOP, Z_FRONT),    ROT_ALONG_X),
    "d3d_frame_front_bottom_bar": ((0.0, Y_BOT, Z_FRONT),    ROT_ALONG_X),
    "d3d_frame_front_right_bar":  ((X_RIGHT, Y_MID, Z_FRONT), ROT_ALONG_Y),
    "d3d_frame_front_left_bar":   ((X_LEFT, Y_MID, Z_FRONT),  ROT_ALONG_Y),

    # Rear face (vertical, Z = Z_REAR)
    "d3d_frame_rear_top_bar":    ((0.0, Y_TOP, Z_REAR),    ROT_ALONG_X),
    "d3d_frame_rear_bottom_bar": ((0.0, Y_BOT, Z_REAR),    ROT_ALONG_X),
    "d3d_frame_rear_right_bar":  ((X_LEFT, Y_MID, Z_REAR),  ROT_ALONG_Y),  # right when facing -Z = -X
    "d3d_frame_rear_left_bar":   ((X_RIGHT, Y_MID, Z_REAR), ROT_ALONG_Y),  # left when facing -Z = +X
}


def r4(v):
    """Round to 4 decimal places."""
    return round(v, 4)


def load_part_positions():
    """Load FreeCAD-extracted positions and compute machine.json positions."""
    with open(PLACEMENTS_JSON) as f:
        placements = json.load(f)
    with open(LABEL_MAP_JSON) as f:
        label_map = json.load(f)

    # Remove metadata key
    label_map = {k: v for k, v in label_map.items() if k != "_notes"}

    parts = {}
    for fc_label, part_id in label_map.items():
        if fc_label not in placements:
            print(f"  WARNING: {fc_label} not in placements.json, skipping")
            continue

        p = placements[fc_label]
        pos = p["position"]
        rot = p["rotation"]

        # Apply offset
        machine_pos = (
            r4(pos["x"] + OFFSET_X),
            r4(pos["y"] + OFFSET_Y),
            r4(pos["z"] + OFFSET_Z),
        )
        machine_rot = (
            r4(rot["x"]),
            r4(rot["y"]),
            r4(rot["z"]),
            r4(rot["w"]),
        )

        parts[part_id] = (machine_pos, machine_rot)
        print(f"  {part_id:45s} pos=({machine_pos[0]:8.4f}, {machine_pos[1]:8.4f}, {machine_pos[2]:8.4f})  "
              f"rot=({machine_rot[0]:.4f}, {machine_rot[1]:.4f}, {machine_rot[2]:.4f}, {machine_rot[3]:.4f})")

    return parts


def set_pos_rot(entry, pos, rot):
    """Update position and rotation fields on a placement entry."""
    entry["playPosition"] = {"x": r4(pos[0]), "y": r4(pos[1]), "z": r4(pos[2])}
    entry["playRotation"] = {"x": r4(rot[0]), "y": r4(rot[1]), "z": r4(rot[2]), "w": r4(rot[3])}


def set_target_pos_rot(entry, pos, rot):
    """Update position and rotation on a target placement entry."""
    entry["position"] = {"x": r4(pos[0]), "y": r4(pos[1]), "z": r4(pos[2])}
    entry["rotation"] = {"x": r4(rot[0]), "y": r4(rot[1]), "z": r4(rot[2]), "w": r4(rot[3])}


def main():
    print(f"=== FreeCAD-to-Unity Position Patcher ===")
    print(f"Frame outer: {FRAME_OUTER_M*1000:.1f}mm, Bar width: {BAR_HALF_WIDTH*2*1000:.1f}mm")
    print(f"Offset: ({OFFSET_X:.4f}, {OFFSET_Y:.4f}, {OFFSET_Z:.4f})")
    print(f"Cube Y range: [{WORKTABLE_Y:.4f}, {WORKTABLE_Y + FRAME_OUTER_M:.4f}]")
    print(f"Cube X range: [{-FRAME_OUTER_M/2:.4f}, {FRAME_OUTER_M/2:.4f}]")
    print(f"Cube Z range: [{-FRAME_OUTER_M/2:.4f}, {FRAME_OUTER_M/2:.4f}]")

    # Load part positions from FreeCAD extraction
    print(f"\n--- Non-frame part positions (FreeCAD + offset) ---")
    part_positions = load_part_positions()

    # Load machine.json
    print(f"\n--- Loading machine.json ---")
    with open(MACHINE_JSON) as f:
        machine = json.load(f)

    preview = machine.get("previewConfig", {})
    part_placements = preview.get("partPlacements", [])
    target_placements = preview.get("targetPlacements", [])

    # Build lookup
    part_placement_map = {p["partId"]: p for p in part_placements}
    target_placement_map = {t["targetId"]: t for t in target_placements}

    # --- Update frame bar playPositions ---
    bars_updated = 0
    bars_missing = []
    for part_id, (pos, rot) in FRAME_BAR_POSITIONS.items():
        if part_id in part_placement_map:
            set_pos_rot(part_placement_map[part_id], pos, rot)
            bars_updated += 1
        else:
            bars_missing.append(part_id)

    print(f"\n--- Frame bars: {bars_updated} updated, {len(bars_missing)} missing ---")
    if bars_missing:
        for m in bars_missing:
            print(f"  MISSING: {m}")

    # Also update corresponding target placements for frame bars
    bar_targets_updated = 0
    for part_id, (pos, rot) in FRAME_BAR_POSITIONS.items():
        # Target IDs follow pattern: target_{face}_{position}_bar
        target_id = "target_" + part_id.replace("d3d_frame_", "")
        if target_id in target_placement_map:
            set_target_pos_rot(target_placement_map[target_id], pos, rot)
            bar_targets_updated += 1

    print(f"  Bar target placements updated: {bar_targets_updated}")

    # --- Update 9 non-frame part playPositions ---
    parts_updated = 0
    parts_missing = []
    for part_id, (pos, rot) in part_positions.items():
        if part_id in part_placement_map:
            set_pos_rot(part_placement_map[part_id], pos, rot)
            parts_updated += 1
        else:
            parts_missing.append(part_id)

    print(f"\n--- Non-frame parts: {parts_updated} updated, {len(parts_missing)} missing ---")
    if parts_missing:
        for m in parts_missing:
            print(f"  MISSING: {m}")

    # --- Write back ---
    with open(MACHINE_JSON, "w") as f:
        json.dump(machine, f, indent=4)

    print(f"\n[DONE] machine.json updated successfully.")
    print(f"  Frame bars updated: {bars_updated}")
    print(f"  Bar targets updated: {bar_targets_updated}")
    print(f"  Parts updated: {parts_updated}")


if __name__ == "__main__":
    main()
