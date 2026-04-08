"""
Add splinePath data to the 6 hose/cable partPlacements in machine.json.

Spline knots are in PreviewRoot local space (same coordinate system as assembledPosition).
Routes are based on real Power Cube component positions and frame geometry.

Frame reference:
  - Long tubes (X-axis) at Z = ±0.3558, from X = -0.5592 to +0.5592
  - Short tubes (Z-axis) at X = ±0.5592, from Z = -0.3558 to +0.3558
  - FLOOR = 0.1016 (top of base tubes)
  - PLATE_TOP = 0.7172 (engine mount plate top)

Component positions (assembledPosition):
  - Engine: (0.0, 0.7872, 0.0)
  - Hydraulic pump: (-0.35, 0.1716, 0.0)
  - Reservoir: (0.45, 0.2266, 0.0)
  - Fuel tank: (-0.50, 0.2266, 0.15)
  - Fuel shutoff valve: (-0.50, 0.225, 0.0)
  - Battery: (0.40, 0.1981, -0.20)
"""

import json
import os

BASE = r"c:\Users\davta\Repos\OSE Blueprints Assembler"
JSON_PATH = os.path.join(BASE, "Assets", "_Project", "Data", "Packages",
                         "power_cube_frame", "machine.json")

# ── spline definitions ──
# Each entry: { radius, segments, metallic, smoothness, knots: [{x,y,z}, ...] }
# assembledPosition/assembledScale for spline parts are set to identity (0,0,0) / (1,1,1)

SPLINE_PARTS = {
    "pressure_hose": {
        # SAE 100R2 steel-braided, 3/4" ID → ~19mm OD → radius 0.0095m
        # Route: pump pressure outlet → along base → to quick-connect coupler on front-right
        "radius": 0.0095,
        "segments": 8,
        "metallic": 0.7,
        "smoothness": 0.45,
        "knots": [
            {"x": -0.35, "y": 0.24, "z": 0.06},    # pump pressure port
            {"x": -0.42, "y": 0.16, "z": 0.15},     # down and toward front-left
            {"x": -0.40, "y": 0.13, "z": 0.30},     # along front base tube
            {"x": -0.10, "y": 0.13, "z": 0.33},     # along front tube toward center
            {"x": 0.25, "y": 0.15, "z": 0.33},      # continue to front-right
            {"x": 0.45, "y": 0.20, "z": 0.30},      # up to coupler on front-right
        ],
    },
    "return_hose": {
        # SAE 100R1 rubber, 1" ID → ~32mm OD → radius 0.016m
        # Route: return coupler on front-right → along frame → back to reservoir
        "radius": 0.013,
        "segments": 8,
        "metallic": 0.0,
        "smoothness": 0.2,
        "knots": [
            {"x": 0.45, "y": 0.24, "z": 0.28},     # return coupler near front-right
            {"x": 0.50, "y": 0.16, "z": 0.20},      # down right side
            {"x": 0.52, "y": 0.13, "z": 0.05},      # along right base tube
            {"x": 0.50, "y": 0.15, "z": -0.05},     # curve toward reservoir
            {"x": 0.45, "y": 0.22, "z": 0.00},      # up to reservoir return port
        ],
    },
    "fuel_line": {
        # 3/8" fuel-rated rubber → ~10mm OD → radius 0.005m
        # Route: fuel tank outlet → through shutoff valve → up to engine carburetor
        "radius": 0.005,
        "segments": 8,
        "metallic": 0.0,
        "smoothness": 0.25,
        "knots": [
            {"x": -0.50, "y": 0.17, "z": 0.10},    # fuel tank bottom outlet
            {"x": -0.50, "y": 0.15, "z": 0.05},     # down toward valve
            {"x": -0.50, "y": 0.22, "z": 0.00},     # through shutoff valve
            {"x": -0.48, "y": 0.35, "z": -0.05},    # up along left side
            {"x": -0.30, "y": 0.55, "z": -0.03},    # angle up toward engine
            {"x": -0.10, "y": 0.72, "z": 0.08},     # to carburetor fuel inlet
        ],
    },
    "battery_cables": {
        # 2-gauge copper → ~8mm OD per cable → radius 0.006m for bundle
        # Route: battery terminals → along frame base → up to engine starter
        "radius": 0.006,
        "segments": 8,
        "metallic": 0.0,
        "smoothness": 0.3,
        "knots": [
            {"x": 0.40, "y": 0.28, "z": -0.20},    # battery terminal
            {"x": 0.38, "y": 0.14, "z": -0.18},     # down from battery
            {"x": 0.20, "y": 0.12, "z": -0.10},     # along rear base tube
            {"x": 0.00, "y": 0.12, "z": -0.05},     # center bottom
            {"x": -0.10, "y": 0.35, "z": 0.00},     # up toward engine
            {"x": -0.08, "y": 0.65, "z": -0.05},    # along engine side
            {"x": -0.05, "y": 0.72, "z": -0.10},    # to starter solenoid
        ],
    },
    "choke_cable": {
        # Steel inner / plastic sheath → ~8mm OD → radius 0.004m
        # Route: T-handle on rear tube face → up along post → to engine choke lever
        "radius": 0.004,
        "segments": 6,
        "metallic": 0.0,
        "smoothness": 0.3,
        "knots": [
            {"x": -0.05, "y": 0.20, "z": -0.31},   # T-handle on rear tube face
            {"x": -0.05, "y": 0.25, "z": -0.27},    # just behind rear tube
            {"x": -0.08, "y": 0.42, "z": -0.15},    # up inside frame
            {"x": -0.10, "y": 0.60, "z": -0.05},    # continue upward
            {"x": -0.12, "y": 0.72, "z": 0.08},     # to choke lever on engine
        ],
    },
    "throttle_cable": {
        # Steel inner / plastic sheath → ~8mm OD → radius 0.004m
        # Route: lever handle on rear tube face → up along post → to engine throttle
        "radius": 0.004,
        "segments": 6,
        "metallic": 0.0,
        "smoothness": 0.3,
        "knots": [
            {"x": 0.05, "y": 0.20, "z": -0.31},    # lever handle on rear tube face
            {"x": 0.05, "y": 0.25, "z": -0.27},     # just behind rear tube
            {"x": 0.08, "y": 0.42, "z": -0.15},     # up inside frame
            {"x": 0.10, "y": 0.60, "z": -0.05},     # continue upward
            {"x": 0.12, "y": 0.72, "z": 0.08},      # to throttle lever on engine
        ],
    },
}

# Color overrides for spline parts (RGBA)
SPLINE_COLORS = {
    "pressure_hose":  {"r": 0.65, "g": 0.65, "b": 0.68, "a": 1.0},   # silver steel braid
    "return_hose":    {"r": 0.12, "g": 0.12, "b": 0.12, "a": 1.0},   # black rubber
    "fuel_line":      {"r": 0.35, "g": 0.25, "b": 0.10, "a": 1.0},   # dark amber rubber
    "battery_cables": {"r": 0.45, "g": 0.08, "b": 0.08, "a": 1.0},   # dark red (bundle)
    "choke_cable":    {"r": 0.15, "g": 0.15, "b": 0.15, "a": 1.0},   # dark gray plastic
    "throttle_cable": {"r": 0.15, "g": 0.15, "b": 0.15, "a": 1.0},   # dark gray plastic
}

def main():
    with open(JSON_PATH, "r", encoding="utf-8") as f:
        data = json.load(f)

    placements = data.get("previewConfig", {}).get("partPlacements", [])
    modified = 0

    for pp in placements:
        pid = pp.get("partId", "")
        if pid not in SPLINE_PARTS:
            continue

        sp = SPLINE_PARTS[pid]

        # Set splinePath
        pp["splinePath"] = {
            "radius": sp["radius"],
            "segments": sp["segments"],
            "metallic": sp["metallic"],
            "smoothness": sp["smoothness"],
            "knots": sp["knots"],
        }

        # Set assembledPosition to origin (spline knots define geometry)
        pp["assembledPosition"] = {"x": 0.0, "y": 0.0, "z": 0.0}
        pp["assembledRotation"] = {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}
        pp["assembledScale"] = {"x": 1.0, "y": 1.0, "z": 1.0}

        # Update color
        if pid in SPLINE_COLORS:
            pp["color"] = SPLINE_COLORS[pid]

        modified += 1
        print(f"  ✓ {pid}: {len(sp['knots'])} knots, r={sp['radius']}m")

    with open(JSON_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

    print(f"\nUpdated {modified} partPlacements with splinePath data")
    print(f"Wrote: {JSON_PATH}")


if __name__ == "__main__":
    main()
