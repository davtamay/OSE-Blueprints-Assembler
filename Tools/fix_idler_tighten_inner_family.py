"""
fix_idler_tighten_inner_family.py — Fix validator error:
  Use-family steps cannot have requiredPartIds (Use routes through
  UseStepHandler which doesn't place parts).

The 4 *_idler_tighten_inner steps (added by my prior scripts) put the
M6 nut in requiredPartIds on a Use+Torque step. Validator rightly
errored:

    [Validate.UseParts] step 'step_<axis>_idler_tighten_inner' (family=Use)
    declares requiredPartIds that no prior family=Place step placed.

Fix per axis: move the nut requirement from the tighten_inner step
(Use) to the prior align_halves step (Place). The nut becomes a part
the trainee handles during alignment; the tighten step then has no
parts and just drives the drill (Use+Torque, relevantToolIds preserved).

Procedurally close to the manual: alignment happens, then the tighten
step is purely the drill action against the already-handled nut.
"""

from __future__ import annotations
import json
import os

PKG_ASM = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets", "_Project", "Data", "Packages", "d3d_v18_10", "assemblies"
)

ZERO_TRANSFORM = {
    "position": {"x": 0.0, "y": 0.0, "z": 0.0},
    "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0},
    "scale":    {"x": 0.0, "y": 0.0, "z": 0.0},
}


def task(pid):
    return {"kind": "part", "id": pid, "endTransform": dict(ZERO_TRANSFORM)}


# Per-axis: (assembly file, align step id, tighten step id, nut part id)
TARGETS = [
    ("assembly_d3d_y_left_bench.json",
     "step_y_left_idler_align_halves",
     "step_y_left_idler_tighten_inner",
     "y_left_idler_m6_nut_inner"),
    ("assembly_d3d_z_back_bench.json",
     "step_z_back_idler_align_halves",
     "step_z_back_idler_tighten_inner",
     "z_back_idler_m6_nut_inner"),
    ("assembly_d3d_y_right_bench.json",
     "step_y_right_idler_align_halves",
     "step_y_right_idler_tighten_inner",
     "y_right_idler_m6_nut_inner"),
    ("assembly_d3d_z_front_bench.json",
     "step_z_front_idler_align_halves",
     "step_z_front_idler_tighten_inner",
     "z_front_idler_m6_nut_inner"),
]


def patch(asm_file, align_id, tighten_id, nut_id):
    path = os.path.join(PKG_ASM, asm_file)
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    align = next((s for s in d["steps"] if s["id"] == align_id), None)
    tighten = next((s for s in d["steps"] if s["id"] == tighten_id), None)
    if not align or not tighten:
        print(f"  ⚠ {asm_file}: missing {align_id} or {tighten_id}; skipping")
        return False

    # 1. Add nut to align_halves.requiredPartIds (if not already there)
    align_parts = list(align.get("requiredPartIds", []))
    if nut_id not in align_parts:
        align_parts.append(nut_id)
        align["requiredPartIds"] = align_parts
        # Mirror in taskOrder
        ato = list(align.get("taskOrder", []))
        if not any(e.get("id") == nut_id and e.get("kind") == "part" for e in ato):
            ato.append(task(nut_id))
        align["taskOrder"] = ato
        # Update instruction so the trainee knows to handle the nut here
        instr = align.get("instructionText", "") or ""
        if "nut" not in instr.lower():
            align["instructionText"] = (
                instr.rstrip() +
                " Also pick up the M6 nut now — it threads onto the back of "
                "the inner bolt before tightening in the next step."
            )
        print(f"  + {align_id}: added '{nut_id}' to requiredPartIds")
    else:
        print(f"  ✓ {align_id}: '{nut_id}' already present")

    # 2. Strip nut from tighten step's requiredPartIds + taskOrder
    tparts = [p for p in tighten.get("requiredPartIds", []) if p != nut_id]
    tighten["requiredPartIds"] = tparts
    tto = [e for e in tighten.get("taskOrder", []) if e.get("id") != nut_id]
    tighten["taskOrder"] = tto
    # Update tighten instruction to clarify the nut was placed prior
    tighten["instructionText"] = (
        "Hold the previously-threaded M6 nut on the back of the inner bolt "
        "and tighten the bolt with the electric drill on its lowest torque "
        "setting. The bearings should now be firmly clamped between the "
        "two idler halves."
    )
    print(f"  ~ {tighten_id}: requiredPartIds now {tighten['requiredPartIds']} (was [{nut_id!r}])")

    with open(path, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2)
    return True


def main():
    fixed = 0
    for asm_file, align_id, tighten_id, nut_id in TARGETS:
        print(f"\n=== {asm_file} ===")
        if patch(asm_file, align_id, tighten_id, nut_id):
            fixed += 1
    print(f"\nDone. Patched {fixed}/{len(TARGETS)} axes.")
    print("Verify: python tools/package_health.py d3d_v18_10")


if __name__ == "__main__":
    main()
