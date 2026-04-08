"""
Inject partPlacements for the 18 Y-left hardware parts into machine.json.

Layout:
- Existing Y-left printed parts are in row 1 at z=2.0, y=0.55
- Hardware parts go in row 2 at z=2.3 (behind row 1, same bench zone)
- Grouped by subassembly: carriage hardware | idler hardware | motor hardware
- assembledPositions cluster around the parent part's assembledPosition (approximate
  assembled positions on the axis unit)

The scale on hardware is 1.0 since the GLBs are already in meters at real size.
"""

import json
import os

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MACHINE_JSON = os.path.join(
    REPO_ROOT, "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "machine.json"
)

# Existing parent part assembledPositions (from machine.json):
# full_carriage:  playPos (0.165, 0.8215, 0.0069)
# idler002:       playPos ~ (0.165, 0.78, -0.18)  (estimate from axis layout)
# motor002:       playPos ~ (0.165, 0.78,  0.18)  (estimate)

# Hardware placement entries: (partId, startX, startZ, playX, playY, playZ, color_r, color_g, color_b)
# startY = 0.55 for all (bench surface), startZ = 2.3 (row 2)
# Hardware is small, so pack tighter (0.08m spacing)

PLACEMENTS = [
    # ── Carriage hardware (x = -1.2 to -0.88) ──
    # 4x LM8UU bearings — sit inside carriage
    ("y_left_lm8uu_a", -1.20, 0.165, 0.82, 0.01, 0.6, 0.6, 0.65),
    ("y_left_lm8uu_b", -1.12, 0.165, 0.82, -0.01, 0.6, 0.6, 0.65),
    ("y_left_lm8uu_c", -1.04, 0.165, 0.82, 0.03, 0.6, 0.6, 0.65),
    ("y_left_lm8uu_d", -0.96, 0.165, 0.82, -0.03, 0.6, 0.6, 0.65),
    # 1x M6x18 bolt — carriage top bolt
    ("y_left_m6x18_a", -0.88, 0.165, 0.84, 0.0, 0.6, 0.6, 0.65),

    # ── Idler hardware (x = -0.72 to -0.48) ──
    # 2x 625ZZ flanged bearings
    ("y_left_625zz_a", -0.72, 0.165, 0.78, -0.17, 0.6, 0.6, 0.65),
    ("y_left_625zz_b", -0.64, 0.165, 0.78, -0.19, 0.6, 0.6, 0.65),
    # 1x M6x18 bolt — idler bolt
    ("y_left_m6x18_b", -0.56, 0.165, 0.80, -0.18, 0.6, 0.6, 0.65),

    # ── Motor hardware (x = -0.40 to 0.24) ──
    # 1x GT2 pulley
    ("y_left_gt2_pulley", -0.40, 0.165, 0.78, 0.18, 0.75, 0.75, 0.78),
    # 1x GT2 belt
    ("y_left_gt2_belt",   -0.32, 0.165, 0.78, 0.10, 0.12, 0.12, 0.12),
    # 3x M6 nuts
    ("y_left_m6_nut_a",  -0.24, 0.165, 0.80, 0.16, 0.6, 0.6, 0.65),
    ("y_left_m6_nut_b",  -0.16, 0.165, 0.80, 0.20, 0.6, 0.6, 0.65),
    ("y_left_m6_nut_c",  -0.08, 0.165, 0.80, 0.22, 0.6, 0.6, 0.65),
    # 4x M3x25 SHCS
    ("y_left_m3x25_a",    0.00, 0.165, 0.79, 0.17, 0.35, 0.35, 0.38),
    ("y_left_m3x25_b",    0.08, 0.165, 0.79, 0.19, 0.35, 0.35, 0.38),
    ("y_left_m3x25_c",    0.16, 0.165, 0.79, 0.21, 0.35, 0.35, 0.38),
    ("y_left_m3x25_d",    0.24, 0.165, 0.79, 0.23, 0.35, 0.35, 0.38),
    # 1x M6x18 bolt — motor holder
    ("y_left_m6x18_c",    0.32, 0.165, 0.80, 0.20, 0.6, 0.6, 0.65),
]

START_Y = 0.55
START_Z = 2.3  # Row 2, behind the main printed parts at z=2.0


def make_placement(part_id, start_x, play_x, play_y, play_z, cr, cg, cb):
    return {
        "partId": part_id,
        "startPosition": {"x": round(start_x, 4), "y": START_Y, "z": START_Z},
        "startRotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
        "startScale": {"x": 1.0, "y": 1.0, "z": 1.0},
        "color": {"r": cr, "g": cg, "b": cb, "a": 1.0},
        "assembledPosition": {"x": round(play_x, 4), "y": round(play_y, 4), "z": round(play_z, 4)},
        "assembledRotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
        "assembledScale": {"x": 1.0, "y": 1.0, "z": 1.0},
    }


def main():
    print(f"Reading {MACHINE_JSON}")
    with open(MACHINE_JSON, "r", encoding="utf-8") as f:
        data = json.load(f)

    preview = data.get("previewConfig", {})
    placements = preview.get("partPlacements", [])
    existing_ids = {p["partId"] for p in placements}

    added = 0
    for entry in PLACEMENTS:
        pid, sx, px, py, pz, cr, cg, cb = entry
        if pid in existing_ids:
            print(f"  SKIP (exists): {pid}")
            continue
        placements.append(make_placement(pid, sx, px, py, pz, cr, cg, cb))
        added += 1
        print(f"  ADD: {pid}  start=({sx}, {START_Y}, {START_Z})  play=({px}, {py}, {pz})")

    preview["partPlacements"] = placements
    data["previewConfig"] = preview

    print(f"\nAdded {added} partPlacements.")
    print(f"Total partPlacements: {len(placements)}")

    with open(MACHINE_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("Done.")


if __name__ == "__main__":
    main()
