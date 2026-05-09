"""
fix_x_axis_carriage_build.py — Author X-Axis carriage_build (7 steps)
with correct parts. Mirrors the Y-Left carriage pattern from
batch_carriage_build c1, but lives in x_axis_bench (X-Axis carriage is
NOT part of batch_carriage_build, only Y/Z carriages are).

Defines 12 new parts:
  • 4 LM8UU linear bearings (x_axis_lm8uu_a/b/c/d)
  • 2 carriage M6x18 top bolts (x_axis_m6x18_a, x_axis_carriage_m6x18_b)
  • 2 carriage M6x30 bottom bolts (x_axis_m6x30_a, x_axis_m6x30_b)
  • 4 M6 hex nuts (x_axis_carriage_m6_nut_a/b/c/d)

Reuses the existing X-axis carriage halves:
  • d3d_x_axis_half_carriage  (bearing-side half)
  • d3d_x_axis_carriage_side  (clamping-side half)

Wires the 7 skeleton steps (seq 158-164) with the correct part lists.
Per Axes - D3D v18.10.pdf §13.4 / 14.x — same mechanical procedure as
Y/Z carriage build (place bearings, shake test, rod test, bolt halves
with M6x18 top + M6x30 bottom, drill-tighten with nuts).
"""

from __future__ import annotations
import json
import os

ASM_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10",
    "assemblies", "assembly_d3d_x_axis_bench.json"
)

# Stage parts above the X-axis area (between Y axes, frame center)
def stage(x, y, z):
    return {
        "position": {"x": x, "y": y, "z": z},
        "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
        "scale":    {"x": 1.0, "y": 1.0, "z": 1.0},
        "color":    {"r": 0.7, "g": 0.7, "b": 0.7, "a": 1.0},
    }


def lm8uu_part(letter, x_off):
    return {
        "id": f"x_axis_lm8uu_{letter}",
        "displayName": f"X-Axis LM8UU Bearing {letter.upper()}",
        "name": f"X-Axis Linear Bearing {letter.upper()}",
        "assetRef": "d3d_axis_lm8uu_bearing.glb",
        "category": "fastener",
        "material": "Linear ball bearing (8mm shaft)",
        "function": "One of four LM8UU bearings that ride the X-axis guide rods inside the carriage.",
        "quantity": 1,
        "stagingPose": stage(0.05 + x_off, 0.55, 1.85),
        "partGroupIds": ["partGroup_x_axis_carriage_build"],
    }


def bolt_part(pid, name, fn, x, y=0.6, z=1.85, a18=True):
    return {
        "id": pid,
        "displayName": name,
        "name": name,
        "assetRef": "d3d_axis_m6x18_bolt.glb",
        "category": "fastener",
        "material": "Steel socket-head bolt",
        "function": fn,
        "quantity": 1,
        "stagingPose": stage(x, y, z),
        "partGroupIds": ["partGroup_x_axis_carriage_build"],
    }


def nut_part(letter, x_off):
    return {
        "id": f"x_axis_carriage_m6_nut_{letter}",
        "displayName": f"X-Axis Carriage M6 Nut {letter.upper()}",
        "name": f"X-Axis Carriage Hex Nut {letter.upper()}",
        "assetRef": "d3d_axis_m6_nut.glb",
        "category": "fastener",
        "material": "Steel hex nut",
        "function": "Threads onto carriage bolt to clamp the carriage halves together.",
        "quantity": 1,
        "stagingPose": stage(0.40 + x_off, 0.55, 1.85),
        "partGroupIds": ["partGroup_x_axis_carriage_build"],
    }


NEW_PARTS = [
    lm8uu_part("a", 0.00),
    lm8uu_part("b", 0.06),
    lm8uu_part("c", 0.12),
    lm8uu_part("d", 0.18),
    bolt_part("x_axis_m6x18_a",          "X-Axis M6x18 Carriage Top Bolt A",
              "First top bolt clamping the carriage halves.", 0.25),
    bolt_part("x_axis_carriage_m6x18_b", "X-Axis Carriage M6x18 Top Bolt B",
              "Second top bolt clamping the carriage halves.", 0.30),
    bolt_part("x_axis_m6x30_a",          "X-Axis M6x30 Carriage Bottom Bolt A",
              "First bottom bolt (M6x30) clamping carriage halves through bearings.", 0.35),
    bolt_part("x_axis_m6x30_b",          "X-Axis M6x30 Carriage Bottom Bolt B",
              "Second bottom bolt (M6x30) clamping carriage halves through bearings.", 0.38),
    nut_part("a", 0.00),
    nut_part("b", 0.05),
    nut_part("c", 0.10),
    nut_part("d", 0.15),
]


ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


# Per-step recipe: id → (instructionText, requiredPartIds)
STEP_RECIPES = {
    "step_x_axis_carriage_layout": {
        # seq 158: Lay out
        "instructionText":
            "Gather the X-axis carriage pieces: two carriage halves "
            "(d3d_x_axis_half_carriage and d3d_x_axis_carriage_side), "
            "4 LM8UU bearings, 4 carriage bolts (2 M6x18 top, 2 M6x30 "
            "bottom), and 4 M6 nuts. Lay them out on your bench.",
        "requiredPartIds": [
            "d3d_x_axis_half_carriage",
            "d3d_x_axis_carriage_side",
        ],
    },
    "step_x_axis_carriage_clean_holes": {
        # seq 159: Clean
        "instructionText":
            "Use a drill bit (or box cutter) to thresh out any plastic "
            "covering the bearing pockets and bolt holes on both carriage "
            "halves. Holes should be smooth — rods need to insert "
            "cleanly.",
        "requiredPartIds": [],
    },
    "step_x_axis_carriage_qc_clean": {
        # seq 160: QC
        "instructionText":
            "Verify all carriage holes are clean: 4 clean bearing "
            "semi-circles per half, 4 clear bolt holes (mid hole stays "
            "unused), no plastic flash in any pocket.",
        "requiredPartIds": [],
    },
    "step_x_axis_carriage_place_bearings": {
        # seq 161: Place bearings
        "instructionText":
            "Place the 4 LM8UU linear bearings into the bearing-side "
            "carriage half (d3d_x_axis_half_carriage). Each bearing "
            "seats into a semi-circle pocket — orient the smooth ends "
            "outward.",
        "requiredPartIds": [
            "x_axis_lm8uu_a",
            "x_axis_lm8uu_b",
            "x_axis_lm8uu_c",
            "x_axis_lm8uu_d",
        ],
    },
    "step_x_axis_carriage_shake_test": {
        # seq 162: Shake test
        "instructionText":
            "Close the carriage by holding the second half "
            "(d3d_x_axis_carriage_side) on top. Compress tightly and "
            "shake. If the bearings rattle, the fit is too loose — "
            "wrap each bearing in a single layer of electrical tape "
            "and re-test.",
        "requiredPartIds": [],
    },
    "step_x_axis_carriage_rod_slide_test": {
        # seq 163: Rod slide test
        "instructionText":
            "With the carriage closed, slide an X-axis rod (rod_009) "
            "through. Hold the carriage vertical — the rod should slide "
            "through with slight resistance (not slip freely, but not "
            "stick). Adjust tape if needed.",
        "requiredPartIds": [],
    },
    "step_x_axis_carriage_bolt_close": {
        # seq 164: Bolt halves
        "instructionText":
            "Insert the 2 M6x18 bolts in the top holes and 2 M6x30 "
            "bolts in the bottom holes. Thread an M6 nut on each. "
            "Drill-tighten on the lowest torque setting.",
        "requiredPartIds": [
            "x_axis_m6x18_a",
            "x_axis_carriage_m6x18_b",
            "x_axis_m6x30_a",
            "x_axis_m6x30_b",
            "x_axis_carriage_m6_nut_a",
            "x_axis_carriage_m6_nut_b",
            "x_axis_carriage_m6_nut_c",
            "x_axis_carriage_m6_nut_d",
        ],
    },
}

# Map current x_axis carriage step IDs to recipe keys (by name pattern)
STEP_NAME_TO_RECIPE = {
    "Lay out axis pieces":                                 "step_x_axis_carriage_layout",
    "Clean excess plastic from holes":                     "step_x_axis_carriage_clean_holes",
    "QC: verify all plastic pieces are clean":             "step_x_axis_carriage_qc_clean",
    "Place 4 bearings in carriage half":                   "step_x_axis_carriage_place_bearings",
    "Shake test: bearings must not rattle":                "step_x_axis_carriage_shake_test",
    "Rod slide test: slight resistance, not too free":     "step_x_axis_carriage_rod_slide_test",
    "Bolt carriage halves together":                       "step_x_axis_carriage_bolt_close",
}


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

    # 2. Wire skeleton steps with parts via name-match (step IDs may be auto-generated)
    patched = 0
    for s in d["steps"]:
        name = s.get("name", "")
        recipe_key = STEP_NAME_TO_RECIPE.get(name)
        if not recipe_key:
            continue
        # Only patch steps in the carriage_build partGroup
        if s.get("partGroupId") != "partGroup_x_axis_carriage_build":
            continue
        recipe = STEP_RECIPES[recipe_key]
        s["instructionText"] = recipe["instructionText"]
        s["requiredPartIds"] = list(recipe["requiredPartIds"])
        s["taskOrder"] = [task(pid) for pid in recipe["requiredPartIds"]]
        patched += 1
        print(f"  ~ step: {s['id']} ('{name[:35]}...') parts={len(s['requiredPartIds'])}")

    print(f"\nPatched {patched}/{len(STEP_RECIPES)} steps")

    with open(ASM_PATH, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    print(f"Wrote {ASM_PATH}")


if __name__ == "__main__":
    main()
